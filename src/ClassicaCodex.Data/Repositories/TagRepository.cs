using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class TagRepository
{
    public async Task<int> GetOrCreateAsync(string name, string? category, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // ON CONFLICT DO NOTHING with no WHEN MATCHED branch mirrors the old
        // MERGE exactly: RETURNING only produces a row when the INSERT
        // actually happened, so an existing tag still needs the fallback
        // SELECT below, same as before.
        const string sql = @"
            INSERT INTO Tags (Name, Category) VALUES (@Name, @Category)
            ON CONFLICT(Name) DO NOTHING
            RETURNING TagId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@Category", (object?)category ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result != null) return Convert.ToInt32(result);

        const string selectSql = "SELECT TagId FROM Tags WHERE Name = @Name;";
        await using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = selectSql;
        selectCmd.Parameters.AddWithValue("@Name", name);
        return Convert.ToInt32(await selectCmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// Tags many lines with one tag at once - what auto-tagging needs, since
    /// applying a tag to hundreds or thousands of matches one row at a time
    /// would mean that many round-trips. Batched to stay well under
    /// SQLite's per-statement variable limit, and skips anything already
    /// tagged (INSERT OR IGNORE, backed by the primary key on
    /// (EditionId, CitationRef, TagId)) rather than erroring on the duplicate.
    /// </summary>
    public async Task<int> BulkTagTextNodesAsync(
        int tagId, IReadOnlyList<long> textNodeIds, CancellationToken cancellationToken = default)
    {
        if (textNodeIds.Count == 0) return 0;

        const int batchSize = 500;
        var totalTagged = 0;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        for (var offset = 0; offset < textNodeIds.Count; offset += batchSize)
        {
            // Indexed rather than Skip().Take(): Skip() on an IReadOnlyList
            // restarts from element zero on every batch, making the loop
            // quadratic in the row count.
            var thisBatch = Math.Min(batchSize, textNodeIds.Count - offset);

            // SQLite doesn't support aliasing VALUES-constructor columns the
            // way SQL Server does, so the id list is built as a UNION ALL of
            // single-value SELECTs instead - the first SELECT's alias names
            // the column for every row that follows.
            var selectRows = new List<string>();
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;

            for (var i = 0; i < thisBatch; i++)
            {
                selectRows.Add(i == 0 ? $"SELECT @id{i} AS TextNodeId" : $"SELECT @id{i}");
                cmd.Parameters.AddWithValue($"@id{i}", textNodeIds[offset + i]);
            }
            cmd.Parameters.AddWithValue("@TagId", tagId);

            cmd.CommandText = $@"
                INSERT OR IGNORE INTO PassageTags (EditionId, CitationRef, TagId)
                SELECT DISTINCT tn.EditionId, tn.CitationRef, @TagId
                FROM ({string.Join(" UNION ALL ", selectRows)}) AS s
                JOIN TextNodes tn ON tn.TextNodeId = s.TextNodeId;";

            totalTagged += await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        return totalTagged;
    }

    /// <summary>Deletes one tag and every line-association it has - the tag stops existing entirely.</summary>
    public async Task DeleteTagAsync(int tagId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // Two separate statements rather than one semicolon-joined command
        // text - safer than relying on multi-statement batching support.
        await using (var cmd1 = conn.CreateCommand())
        {
            cmd1.CommandText = "DELETE FROM PassageTags WHERE TagId = @TagId;";
            cmd1.Parameters.AddWithValue("@TagId", tagId);
            await cmd1.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd2 = conn.CreateCommand())
        {
            cmd2.CommandText = "DELETE FROM Tags WHERE TagId = @TagId;";
            cmd2.Parameters.AddWithValue("@TagId", tagId);
            await cmd2.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Wipes every tag and every tag-to-line association - a full reset.
    /// Doesn't touch bookmarks or anything else, only tags.
    /// </summary>
    public async Task ClearAllTagsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        await using (var cmd1 = conn.CreateCommand())
        {
            cmd1.CommandText = "DELETE FROM PassageTags;";
            cmd1.CommandTimeout = 120;
            await cmd1.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cmd2 = conn.CreateCommand())
        {
            cmd2.CommandText = "DELETE FROM Tags;";
            cmd2.CommandTimeout = 120;
            await cmd2.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task TagTextNodeAsync(long textNodeId, int tagId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // Stored against the passage, not the node id the caller happens to
        // be holding - see SchemaInitializer's PassageTags comment.
        const string sql = @"
            INSERT OR IGNORE INTO PassageTags (EditionId, CitationRef, TagId)
            SELECT DISTINCT tn.EditionId, tn.CitationRef, @TagId
            FROM TextNodes tn
            WHERE tn.TextNodeId = @TextNodeId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@TextNodeId", textNodeId);
        cmd.Parameters.AddWithValue("@TagId", tagId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<Tag>> GetAllTagsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Tag>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT t.TagId, t.Name, t.Category, COUNT(tnt.TagId) AS UsageCount
            FROM Tags t
            LEFT JOIN PassageTags tnt ON t.TagId = tnt.TagId
            GROUP BY t.TagId, t.Name, t.Category
            ORDER BY t.Name;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new Tag
            {
                TagId = reader.GetInt32(0),
                Name = reader.GetString(1),
                Category = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }

        return results;
    }

    /// <summary>
    /// Which of these passages carry any tag, and what those tags are.
    ///
    /// For marking a list assembled some other way - a place's search results,
    /// say - where the tags aren't what produced the list but are worth
    /// noticing when they coincide with it.
    ///
    /// PassageTags is keyed on edition and citation reference, not on a text
    /// node id, so the ids have to be resolved back through TextNodes. Tagging
    /// a passage that way survives a re-ingest: node ids are reassigned when a
    /// corpus is rebuilt, but "Iliad 1.1 in this edition" is still the same
    /// line afterwards.
    /// </summary>
    public async Task<Dictionary<long, List<string>>> GetTagNamesForNodesAsync(
        IReadOnlyCollection<long> textNodeIds, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<long, List<string>>();
        if (textNodeIds.Count == 0) return results;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // Inlined rather than parameterised because the list is a set of
        // integers this code produced itself, never user text, and a
        // parameter per id would run into SQLite's variable limit on a long
        // result list.
        var ids = string.Join(",", textNodeIds.Distinct());

        var sql = $@"
            SELECT tn.TextNodeId, t.Name
            FROM TextNodes tn
            JOIN PassageTags pt ON pt.EditionId = tn.EditionId AND pt.CitationRef = tn.CitationRef
            JOIN Tags t ON t.TagId = pt.TagId
            WHERE tn.TextNodeId IN ({ids})
            ORDER BY t.Name;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var nodeId = reader.GetInt64(0);
            if (!results.TryGetValue(nodeId, out var names))
            {
                names = new List<string>();
                results[nodeId] = names;
            }

            names.Add(reader.GetString(1));
        }

        return results;
    }

    public async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)>> GetByTagAsync(
        string tagName, CancellationToken cancellationToken = default)
    {
        var results = new List<(int, long, string, string, string, string)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM PassageTags tnt
            JOIN Tags t ON tnt.TagId = t.TagId
            JOIN TextNodes tn ON tnt.EditionId = tn.EditionId AND tnt.CitationRef = tn.CitationRef
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE t.Name = @TagName
            ORDER BY a.Name, w.Title, tn.SortOrder;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@TagName", tagName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return results;
    }

    /// <summary>
    /// Builds the myth network: one node per tag (sized by how many lines
    /// it's applied to), and an edge between any two tags that both appear
    /// somewhere within the same work - weighted by how many works they
    /// share. Two tags never applied to the same exact line can still be
    /// linked here if they both show up anywhere in, say, the same play;
    /// that's deliberate - it surfaces "these two things appear together in
    /// this author's treatment" rather than requiring word-for-word overlap.
    /// </summary>
    public async Task<(List<(int TagId, string Name, string? Category, int UsageCount)> Nodes,
        List<(int TagId1, int TagId2, int SharedWorkCount)> Edges)> GetCoOccurrenceGraphAsync(
        CancellationToken cancellationToken = default)
    {
        var nodes = new List<(int, string, string?, int)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string nodesSql = @"
            SELECT t.TagId, t.Name, t.Category, COUNT(tnt.TagId) AS UsageCount
            FROM Tags t
            LEFT JOIN PassageTags tnt ON t.TagId = tnt.TagId
            GROUP BY t.TagId, t.Name, t.Category
            ORDER BY t.Name;";

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = nodesSql;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                nodes.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3)));
            }
        }

        // Which works each tag "touches" (appears anywhere in). Computed
        // in-memory from here since a SQL self-join for pairwise
        // intersection counts gets unwieldy - tag counts are small enough
        // (dozens to low hundreds) that this is cheap either way.
        var tagToWorks = new Dictionary<int, HashSet<int>>();

        const string touchesSql = @"
            SELECT DISTINCT tnt.TagId, w.WorkId
            FROM PassageTags tnt
            JOIN TextNodes tn ON tnt.EditionId = tn.EditionId AND tnt.CitationRef = tn.CitationRef
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId;";

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = touchesSql;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tagId = reader.GetInt32(0);
                var workId = reader.GetInt32(1);

                if (!tagToWorks.TryGetValue(tagId, out var set))
                {
                    set = new HashSet<int>();
                    tagToWorks[tagId] = set;
                }
                set.Add(workId);
            }
        }

        var edges = new List<(int, int, int)>();
        var tagIds = tagToWorks.Keys.OrderBy(id => id).ToList();

        for (var i = 0; i < tagIds.Count; i++)
        {
            for (var j = i + 1; j < tagIds.Count; j++)
            {
                var sharedCount = tagToWorks[tagIds[i]].Intersect(tagToWorks[tagIds[j]]).Count();
                if (sharedCount > 0)
                {
                    edges.Add((tagIds[i], tagIds[j], sharedCount));
                }
            }
        }

        return (nodes, edges);
    }

    /// <summary>
    /// Same graph shape as GetCoOccurrenceGraphAsync, but a much stronger
    /// notion of "shared": two tags only get an edge if they actually occur
    /// near each other in the text - within windowLines of each other in the
    /// same edition - rather than merely appearing somewhere in the same
    /// work. That distinction matters once encyclopedic sources are in the
    /// corpus (Apollodorus's Library, Ovid's Metamorphoses): virtually every
    /// pair of gods "shares" those works under the whole-work definition,
    /// which is true but not informative. Requiring actual proximity
    /// filters that out and should leave only genuinely connected figures.
    /// </summary>
    public async Task<(List<(int TagId, string Name, string? Category, int UsageCount)> Nodes,
        List<(int TagId1, int TagId2, int SharedWorkCount)> Edges)> GetProximityCoOccurrenceGraphAsync(
        int windowLines, CancellationToken cancellationToken = default)
    {
        var nodes = new List<(int, string, string?, int)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string nodesSql = @"
            SELECT t.TagId, t.Name, t.Category, COUNT(tnt.TagId) AS UsageCount
            FROM Tags t
            LEFT JOIN PassageTags tnt ON t.TagId = tnt.TagId
            GROUP BY t.TagId, t.Name, t.Category
            ORDER BY t.Name;";

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = nodesSql;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                nodes.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3)));
            }
        }

        // Every occurrence of every tag, as (EditionId, SortOrder) - enough
        // to measure "how close together" two occurrences are without
        // pulling the actual text back.
        var occurrencesByTag = new Dictionary<int, List<(int EditionId, int SortOrder)>>();

        const string occurrencesSql = @"
            SELECT tnt.TagId, tn.EditionId, tn.SortOrder
            FROM PassageTags tnt
            JOIN TextNodes tn ON tnt.EditionId = tn.EditionId AND tnt.CitationRef = tn.CitationRef;";

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = occurrencesSql;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tagId = reader.GetInt32(0);
                var editionId = reader.GetInt32(1);
                var sortOrder = reader.GetInt32(2);

                if (!occurrencesByTag.TryGetValue(tagId, out var list))
                {
                    list = new List<(int, int)>();
                    occurrencesByTag[tagId] = list;
                }
                list.Add((editionId, sortOrder));
            }
        }

        // Group each tag's occurrences by edition and sort by position, so
        // "closest pair" within a shared edition is a single sorted-merge
        // pass rather than a full cross-product.
        var byTagAndEdition = occurrencesByTag.ToDictionary(
            kv => kv.Key,
            kv => kv.Value
                .GroupBy(o => o.EditionId)
                .ToDictionary(g => g.Key, g => g.Select(o => o.SortOrder).OrderBy(x => x).ToList()));

        var edges = new List<(int, int, int)>();
        var tagIds = byTagAndEdition.Keys.OrderBy(id => id).ToList();

        for (var i = 0; i < tagIds.Count; i++)
        {
            var editionsA = byTagAndEdition[tagIds[i]];

            for (var j = i + 1; j < tagIds.Count; j++)
            {
                var editionsB = byTagAndEdition[tagIds[j]];
                var closeEditionCount = 0;

                foreach (var editionId in editionsA.Keys)
                {
                    if (!editionsB.TryGetValue(editionId, out var positionsB)) continue;

                    if (HasNearbyPair(editionsA[editionId], positionsB, windowLines))
                    {
                        closeEditionCount++;
                    }
                }

                if (closeEditionCount > 0)
                {
                    edges.Add((tagIds[i], tagIds[j], closeEditionCount));
                }
            }
        }

        return (nodes, edges);
    }

    /// <summary>
    /// Every passage tagged with either of two tags, restricted to the works
    /// (whole-work mode) or editions (proximity mode) that actually
    /// qualified them for an edge in the first place - i.e. this answers
    /// "what specifically connects these two tags", for whichever mode
    /// produced the graph currently on screen.
    /// </summary>
    public async Task<List<(string TagName, int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)>> GetEdgePassagesAsync(
        string tagNameA, string tagNameB, bool useProximity, int windowLines, CancellationToken cancellationToken = default)
    {
        var results = new List<(string, int, long, string, string, string, string)>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string rowsSql = @"
            SELECT t.Name, tn.TextNodeId, e.EditionId, w.WorkId, tn.SortOrder, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM PassageTags tnt
            JOIN Tags t ON tnt.TagId = t.TagId
            JOIN TextNodes tn ON tnt.EditionId = tn.EditionId AND tnt.CitationRef = tn.CitationRef
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE t.Name IN (@NameA, @NameB)
            ORDER BY a.Name, w.Title, tn.SortOrder;";

        var rowsA = new List<(long TextNodeId, int EditionId, int WorkId, int SortOrder, string AuthorName, string WorkTitle, string CitationRef, string Text)>();
        var rowsB = new List<(long TextNodeId, int EditionId, int WorkId, int SortOrder, string AuthorName, string WorkTitle, string CitationRef, string Text)>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = rowsSql;
            cmd.Parameters.AddWithValue("@NameA", tagNameA);
            cmd.Parameters.AddWithValue("@NameB", tagNameB);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tagName = reader.GetString(0);
                var row = (
                    reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
                    reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8));

                if (string.Equals(tagName, tagNameA, StringComparison.Ordinal)) rowsA.Add(row);
                else rowsB.Add(row);
            }
        }

        if (useProximity)
        {
            // Only the passages that are actually near a passage of the
            // other tag, in the same edition - the same test the edge
            // weight itself was computed from.
            foreach (var rowA in rowsA)
            {
                var isNear = rowsB.Any(rowB =>
                    rowB.EditionId == rowA.EditionId && Math.Abs(rowB.SortOrder - rowA.SortOrder) <= windowLines);
                if (isNear) results.Add((tagNameA, rowA.WorkId, rowA.TextNodeId, rowA.AuthorName, rowA.WorkTitle, rowA.CitationRef, rowA.Text));
            }
            foreach (var rowB in rowsB)
            {
                var isNear = rowsA.Any(rowA =>
                    rowA.EditionId == rowB.EditionId && Math.Abs(rowA.SortOrder - rowB.SortOrder) <= windowLines);
                if (isNear) results.Add((tagNameB, rowB.WorkId, rowB.TextNodeId, rowB.AuthorName, rowB.WorkTitle, rowB.CitationRef, rowB.Text));
            }
        }
        else
        {
            // Whole-work mode: any passage belonging to a work that has at
            // least one occurrence of the other tag somewhere in it.
            var worksA = rowsA.Select(r => r.WorkId).ToHashSet();
            var worksB = rowsB.Select(r => r.WorkId).ToHashSet();
            var sharedWorks = worksA.Intersect(worksB).ToHashSet();

            foreach (var rowA in rowsA.Where(r => sharedWorks.Contains(r.WorkId)))
            {
                results.Add((tagNameA, rowA.WorkId, rowA.TextNodeId, rowA.AuthorName, rowA.WorkTitle, rowA.CitationRef, rowA.Text));
            }
            foreach (var rowB in rowsB.Where(r => sharedWorks.Contains(r.WorkId)))
            {
                results.Add((tagNameB, rowB.WorkId, rowB.TextNodeId, rowB.AuthorName, rowB.WorkTitle, rowB.CitationRef, rowB.Text));
            }
        }

        return results
            .OrderBy(r => r.Item4).ThenBy(r => r.Item5).ThenBy(r => r.Item7)
            .ToList();
    }

    /// <summary>
    /// True if any position in one sorted list falls within windowLines of
    /// any position in the other - a two-pointer merge over both sorted
    /// lists rather than an O(n*m) comparison of every pair.
    /// </summary>
    private static bool HasNearbyPair(List<int> sortedA, List<int> sortedB, int windowLines)
    {
        var i = 0;
        var j = 0;

        while (i < sortedA.Count && j < sortedB.Count)
        {
            var diff = sortedA[i] - sortedB[j];
            if (Math.Abs(diff) <= windowLines) return true;

            if (diff < 0) i++;
            else j++;
        }

        return false;
    }
}
