using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class TextNodeRepository
{
    /// <summary>
    /// How many matching lines a search returns before it stops and says so.
    /// Not a correctness limit - it's there so a two-letter query against a
    /// multi-million-line corpus doesn't try to materialise the whole thing
    /// into a List and take the app down with it.
    /// </summary>
    public const int DefaultMaxResults = 5000;

    /// <summary>
    /// How many inflected forms get expanded into a single query. A large
    /// Greek paradigm can run past this; when it does the search is still
    /// correct as far as it goes, but incomplete, and SearchHits.Truncated
    /// says so rather than letting it pass for a full answer.
    /// </summary>
    private const int MaxFormsPerQuery = 200;

    /// <summary>
    /// The same cap for the no-index fallback path, which builds one LIKE
    /// clause per form rather than an IN list - far more expensive per form,
    /// hence the much lower ceiling.
    /// </summary>
    private const int MaxFormsPerLikeQuery = 60;

    /// <summary>
    /// Inserts text nodes for one edition. Given "ingest everything" scope,
    /// this runs for every edition in every work - potentially low millions
    /// of rows across a full corpus - so it batches multiple rows into each
    /// INSERT statement rather than one row per statement. See
    /// WordIndexRepository's remarks for why that matters more than just
    /// wrapping everything in a transaction. 200 rows per statement here
    /// (smaller than the other tables) since a line's Text value can
    /// occasionally run long, and this keeps each statement's total size
    /// reasonable regardless.
    /// </summary>
    public async Task BulkInsertAsync(IReadOnlyList<TextNode> nodes, CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0) return;

        const int rowsPerStatement = 200;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        for (var offset = 0; offset < nodes.Count; offset += rowsPerStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Indexed rather than Skip().Take() - see WordIndexRepository's
            // note; Skip() restarts from element zero on every batch.
            var batchSize = Math.Min(rowsPerStatement, nodes.Count - offset);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)transaction;

            var valueRows = new List<string>(batchSize);
            for (var i = 0; i < batchSize; i++)
            {
                var node = nodes[offset + i];
                valueRows.Add($"(@e{i},@c{i},@s{i},@t{i})");
                cmd.Parameters.AddWithValue($"@e{i}", node.EditionId);
                cmd.Parameters.AddWithValue($"@c{i}", node.CitationRef);
                cmd.Parameters.AddWithValue($"@s{i}", node.SortOrder);
                cmd.Parameters.AddWithValue($"@t{i}", node.Text);
            }

            cmd.CommandText =
                $"INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text) VALUES {string.Join(",", valueRows)};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<List<TextNode>> GetByEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        var results = new List<TextNode>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"SELECT TextNodeId, EditionId, CitationRef, SortOrder, Text
                             FROM TextNodes WHERE EditionId = @EditionId ORDER BY SortOrder;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@EditionId", editionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TextNode
            {
                TextNodeId = reader.GetInt64(0),
                EditionId = reader.GetInt32(1),
                CitationRef = reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                Text = reader.GetString(4)
            });
        }

        return results;
    }

    /// <summary>
    /// Plain substring search across every line. SQL Server's version of
    /// this tried Full-Text Search first for word-stem matching - that's a
    /// SQL-Server-only feature with no SQLite equivalent worth building
    /// (FTS5 would mostly replicate light English stemming that nothing
    /// else in the app depends on; the lemma system + WordIndex already
    /// carry the real Greek/Latin search workload). Plain LIKE is the whole
    /// path now.
    /// </summary>
    public async Task<SearchHits> SearchAsync(
        string query, int maxResults = DefaultMaxResults, CancellationToken cancellationToken = default)
    {
        var results = new List<(int, long, string, string, string, string)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // LIMIT one more than we intend to keep: if that extra row comes
        // back, there was at least one more match than we're showing, which
        // is exactly what Truncated needs to know. Cheaper and more honest
        // than a second COUNT(*) over the same predicate.
        const string sql = @"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE tn.Text LIKE @Query ESCAPE '\'
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @Limit;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 180;
        cmd.Parameters.AddWithValue("@Query", $"%{EscapeLikeWildcards(query)}%");
        cmd.Parameters.AddWithValue("@Limit", maxResults + 1);

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

        var truncated = results.Count > maxResults;
        if (truncated) results.RemoveRange(maxResults, results.Count - maxResults);

        return new SearchHits(results, truncated);
    }

    /// <summary>
    /// Search with filters, for the search window.
    ///
    /// Built as one SQL statement rather than by filtering in memory: the
    /// narrowing is exactly what keeps a broad query from hitting the result
    /// cap, so it has to happen before the LIMIT, not after. Filtering
    /// afterwards would mean "the first 5000 matches anywhere, of which
    /// three happen to be Aeschylus" instead of "the first 5000 in
    /// Aeschylus".
    ///
    /// Every clause is optional and every value is parameterised. The only
    /// SQL assembled from a variable is the count of placeholders in the IN
    /// lists, which is derived from collection sizes rather than content.
    /// </summary>
    public async Task<SearchHits> SearchFilteredAsync(
        SearchFilters filters, CancellationToken cancellationToken = default)
    {
        var query = filters.Query.Trim();
        if (query.Length == 0) return SearchHits.Empty;

        // An era that matched no authors is a real result - no passages can
        // qualify - and must not be confused with "no era filter".
        if (filters.EraAuthorIds is { Count: 0 }) return SearchHits.Empty;

        var results = new List<(int, long, string, string, string, string)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 180;

        var where = new List<string>();

        // Whole-word matching wants the word index, and asks the connection
        // that's already open rather than opening a second one.
        var indexAvailable = filters.MatchMode == SearchMatchMode.WholeWord
                             && await WordIndexRepository.HasDataAsync(conn, cancellationToken);

        AppendTextPredicate(cmd, where, query, filters.MatchMode, indexAvailable);

        if (filters.Languages.Count > 0)
        {
            where.Add($"e.Language IN ({AddParameters(cmd, "lang", filters.Languages)})");
        }

        if (filters.Corpora.Count > 0)
        {
            where.Add($"a.Namespace IN ({AddParameters(cmd, "ns", filters.Corpora)})");
        }

        if (filters.OriginalsOnly != null)
        {
            where.Add("e.Kind = @Kind");
            cmd.Parameters.AddWithValue("@Kind", filters.OriginalsOnly.Value ? "Original" : "Translation");
        }

        if (filters.AuthorId != null)
        {
            where.Add("a.AuthorId = @AuthorId");
            cmd.Parameters.AddWithValue("@AuthorId", filters.AuthorId.Value);
        }

        if (filters.WorkId != null)
        {
            where.Add("w.WorkId = @WorkId");
            cmd.Parameters.AddWithValue("@WorkId", filters.WorkId.Value);
        }

        if (filters.EraAuthorIds is { Count: > 0 })
        {
            where.Add($"a.AuthorId IN ({AddParameters(cmd, "era", filters.EraAuthorIds.Select(id => id.ToString()))})");
        }

        // Tags and bookmarks hang off (EditionId, CitationRef), not off the
        // text node - see SchemaInitializer's PassageTags comment - so these
        // join on the passage rather than the row.
        if (!string.IsNullOrWhiteSpace(filters.TagName))
        {
            where.Add(@"EXISTS (
                SELECT 1 FROM PassageTags pt
                JOIN Tags t ON pt.TagId = t.TagId
                WHERE pt.EditionId = tn.EditionId AND pt.CitationRef = tn.CitationRef
                  AND t.Name = @TagName)");
            cmd.Parameters.AddWithValue("@TagName", filters.TagName);
        }

        if (filters.BookmarkedOnly)
        {
            where.Add(@"EXISTS (
                SELECT 1 FROM Bookmarks b
                WHERE b.EditionId = tn.EditionId AND b.CitationRef = tn.CitationRef)");
        }

        cmd.Parameters.AddWithValue("@Limit", filters.MaxResults + 1);

        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE {string.Join(" AND ", where)}
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @Limit;";

        // Only for the LIKE fallback. The index path has already matched on
        // whole normalized words, and re-checking the raw text against an
        // unaccented query here would throw away exactly the rows the index
        // was able to find.
        var wholeWordTargets = filters.MatchMode == SearchMatchMode.WholeWord && !indexAvailable
            ? query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(WordNormalizer.Normalize)
                .Where(w => w.Length > 0)
                .ToHashSet(StringComparer.Ordinal)
            : null;

        var rowsRead = 0;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rowsRead++;
            var text = reader.GetString(5);

            if (wholeWordTargets != null)
            {
                var isWholeWordHit = text
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(WordNormalizer.Normalize)
                    .Any(w => w.Length > 0 && wholeWordTargets.Contains(w));

                if (!isWholeWordHit) continue;
            }

            results.Add((
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), text));
        }

        // Measured against rows read rather than rows kept: the whole-word
        // confirmation runs after the LIMIT, so this can return fewer than
        // the cap and still have been clipped.
        var truncated = rowsRead > filters.MaxResults;
        if (truncated && results.Count > filters.MaxResults)
        {
            results.RemoveRange(filters.MaxResults, results.Count - filters.MaxResults);
        }

        return new SearchHits(results, truncated);
    }

    /// <summary>
    /// The text half of the WHERE clause.
    ///
    /// WholeWord is done with GLOB rather than a regular expression, since
    /// SQLite ships no regex by default: "[^a-z]" style character classes
    /// are the one pattern facility available, and bounding the term with
    /// non-letters on each side is what "whole word" means here. It's
    /// case-sensitive where LIKE isn't, so both cases of the first letter
    /// are tried.
    /// </summary>
    private static void AppendTextPredicate(
        SqliteCommand cmd, List<string> where, string query, SearchMatchMode mode, bool indexAvailable)
    {
        switch (mode)
        {
            case SearchMatchMode.AllWords:
                var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < words.Length; i++)
                {
                    where.Add($"tn.Text LIKE @w{i} ESCAPE '\\'");
                    cmd.Parameters.AddWithValue($"@w{i}", $"%{EscapeLikeWildcards(words[i])}%");
                }

                // A query of only punctuation splits to nothing, which would
                // leave the WHERE clause empty and return the whole corpus.
                if (words.Length == 0) where.Add("1 = 0");
                break;

            case SearchMatchMode.WholeWord when indexAvailable:
                // Against the word index, which stores one normalized word
                // per line - accents stripped, final sigma folded. That
                // makes this both genuinely whole-word (the index holds
                // words, not substrings) and accent-insensitive, so a Greek
                // word matches however the edition accents it.
                //
                // It has to come from the index rather than from LIKE: a
                // LIKE pattern is compared against the raw text, so a query
                // typed without accents contains characters the text simply
                // doesn't have, and no amount of confirming afterwards can
                // recover a row the prefilter already excluded.
                var indexTargets = query
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(WordNormalizer.Normalize)
                    .Where(w => w.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (indexTargets.Count == 0)
                {
                    where.Add("1 = 0");
                    break;
                }

                where.Add($@"EXISTS (
                    SELECT 1 FROM WordIndex wi
                    WHERE wi.TextNodeId = tn.TextNodeId
                      AND wi.NormalizedWord IN ({AddParameters(cmd, "ww", indexTargets)}))");
                break;

            case SearchMatchMode.WholeWord:
                // No word index built yet. Falls back to a LIKE prefilter
                // plus the normalized confirmation below, which still
                // rejects substrings correctly but can only find a word
                // spelled as typed - accent-insensitivity is exactly the
                // part the index was providing. Building the index from the
                // Setup Wizard restores it.
                where.Add("tn.Text LIKE @Query ESCAPE '\\'");
                cmd.Parameters.AddWithValue("@Query", $"%{EscapeLikeWildcards(query)}%");
                break;

            default:
                where.Add("tn.Text LIKE @Query ESCAPE '\\'");
                cmd.Parameters.AddWithValue("@Query", $"%{EscapeLikeWildcards(query)}%");
                break;
        }
    }

    /// <summary>
    /// Adds one parameter per value and returns the placeholder list for an
    /// IN clause. The generated SQL depends only on how many values there
    /// are, never on what they contain.
    /// </summary>
    private static string AddParameters(SqliteCommand cmd, string prefix, IEnumerable<string> values)
    {
        var names = new List<string>();
        var index = 0;

        foreach (var value in values)
        {
            var name = $"@{prefix}{index++}";
            names.Add(name);
            cmd.Parameters.AddWithValue(name, value);
        }

        return string.Join(",", names);
    }

    /// <summary>
    /// Neutralises LIKE's own wildcards in text the user typed. Without this,
    /// searching for a literal "100%" or "de_" silently means something else
    /// entirely - "%" matches any run of characters and "_" any single one -
    /// and the person gets a wall of unrelated results with no clue why.
    /// Paired with ESCAPE '\' on every LIKE that takes user input.
    /// </summary>
    private static string EscapeLikeWildcards(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    /// <summary>
    /// Finds lines containing any of the given word forms. This is the
    /// lemma-aware path: callers expand one inflected form into its whole
    /// paradigm via LemmaRepository, then pass the lot in here, so a search
    /// for λόγος also turns up λόγου, λόγῳ, λόγον and the rest.
    ///
    /// Matching is done on a normalized copy of the text (accents stripped,
    /// lowercased) because Perseus texts aren't consistent about
    /// accentuation or precomposed-vs-combining Unicode. That normalization
    /// happens in memory here rather than in SQL, so this pulls candidate
    /// rows with a LIKE prefilter and then filters them properly in C#.
    /// </summary>
    public async Task<SearchHits> SearchByFormsAsync(
        IReadOnlyList<string> forms, int maxResults = DefaultMaxResults, CancellationToken cancellationToken = default)
    {
        if (forms.Count == 0) return SearchHits.Empty;

        // Fast path: if the inverted word index has been built, resolve
        // everything in one joined query.
        var indexed = await TrySearchViaWordIndexAsync(forms, maxResults, cancellationToken);
        if (indexed != null) return indexed;

        return await SearchByFormsWithLikeAsync(forms, maxResults, cancellationToken);
    }

    /// <summary>
    /// Single-query search against the inverted index. Returns null (not an
    /// empty list) when the index hasn't been built, so the caller can tell
    /// "no index" apart from "index found nothing" and fall back.
    /// </summary>
    private async Task<SearchHits?> TrySearchViaWordIndexAsync(
        IReadOnlyList<string> forms, int maxResults, CancellationToken cancellationToken)
    {
        // Opened once and reused for the has-data check below and the real
        // query that follows it - see WordIndexRepository.HasDataAsync's
        // connection-taking overload for why that used to be two opens.
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        if (!await WordIndexRepository.HasDataAsync(conn, cancellationToken)) return null;

        var allNormalized = forms
            .Select(WordNormalizer.Normalize)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var formsTruncated = allNormalized.Count > MaxFormsPerQuery;
        var normalized = formsTruncated
            ? allNormalized.Take(MaxFormsPerQuery).ToList()
            : allNormalized;

        var results = new List<(int, long, string, string, string, string)>();
        if (normalized.Count == 0) return SearchHits.Empty;

        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;

        var paramNames = new List<string>();
        for (var i = 0; i < normalized.Count; i++)
        {
            paramNames.Add($"@w{i}");
            cmd.Parameters.AddWithValue($"@w{i}", normalized[i]);
        }
        cmd.Parameters.AddWithValue("@Limit", maxResults + 1);

        // The LIMIT belongs on the OUTER query, not the inner one. Inside the
        // subquery it clipped an unordered set of ids and only THEN sorted
        // what survived - so the "first 5000" a reader saw were an arbitrary
        // 5000 that happened to come back first, presented in author order as
        // though they were the first 5000 alphabetically. Out here it means
        // what it looks like it means.
        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM (
                SELECT DISTINCT TextNodeId
                FROM WordIndex
                WHERE NormalizedWord IN ({string.Join(",", paramNames)})
            ) ids
            JOIN TextNodes tn ON ids.TextNodeId = tn.TextNodeId
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @Limit;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }

        var rowsTruncated = results.Count > maxResults;
        if (rowsTruncated) results.RemoveRange(maxResults, results.Count - maxResults);

        return new SearchHits(results, rowsTruncated || formsTruncated);
    }

    private async Task<SearchHits> SearchByFormsWithLikeAsync(
        IReadOnlyList<string> forms, int maxResults, CancellationToken cancellationToken)
    {
        var results = new List<(int, long, string, string, string, string)>();

        // Cap the SQL side - a big paradigm can run to hundreds of forms and
        // there's no point building a huge OR. The rest get caught by the
        // in-memory pass over whatever comes back.
        var formsTruncated = forms.Count > MaxFormsPerLikeQuery;
        var sqlForms = formsTruncated ? forms.Take(MaxFormsPerLikeQuery).ToList() : forms.ToList();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        var clauses = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 180;
        for (var i = 0; i < sqlForms.Count; i++)
        {
            clauses.Add($"tn.Text LIKE @f{i} ESCAPE '\'");
            cmd.Parameters.AddWithValue($"@f{i}", $"%{EscapeLikeWildcards(sqlForms[i])}%");
        }

        cmd.Parameters.AddWithValue("@Limit", maxResults + 1);

        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE {string.Join(" OR ", clauses)}
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @Limit;";

        var normalizedTargets = new HashSet<string>(
            forms.Select(WordNormalizer.Normalize).Where(f => f.Length > 0), StringComparer.Ordinal);

        var rowsRead = 0;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rowsRead++;
            var text = reader.GetString(5);

            // Confirm a real whole-word hit rather than an accidental
            // substring - LIKE '%λογ%' would otherwise match half the corpus.
            var isRealMatch = text
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(WordNormalizer.Normalize)
                .Any(w => w.Length > 0 && normalizedTargets.Contains(w));

            if (!isRealMatch) continue;

            results.Add((
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), text));
        }

        // The whole-word filter above runs after the LIMIT, so this path can
        // return fewer than maxResults and still have been clipped - hence
        // comparing against the row budget rather than the surviving count.
        var rowsTruncated = rowsRead > maxResults;
        if (rowsTruncated && results.Count > maxResults)
        {
            results.RemoveRange(maxResults, results.Count - maxResults);
        }

        return new SearchHits(results, rowsTruncated || formsTruncated);
    }

    /// <summary>
    /// N consecutive lines within one edition, starting at a given
    /// TextNodeId's position. What the export dialog uses to grab a whole
    /// passage rather than just the single line that was right-clicked.
    /// </summary>
    public async Task<List<TextNode>> GetRangeAsync(
        int editionId, long startTextNodeId, int lineCount, CancellationToken cancellationToken = default)
    {
        var results = new List<TextNode>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT TextNodeId, EditionId, CitationRef, SortOrder, Text
            FROM TextNodes
            WHERE EditionId = @EditionId
              AND SortOrder >= (SELECT SortOrder FROM TextNodes WHERE TextNodeId = @StartId)
            ORDER BY SortOrder
            LIMIT @LineCount;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.Parameters.AddWithValue("@StartId", startTextNodeId);
        cmd.Parameters.AddWithValue("@LineCount", lineCount);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TextNode
            {
                TextNodeId = reader.GetInt64(0),
                EditionId = reader.GetInt32(1),
                CitationRef = reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                Text = reader.GetString(4)
            });
        }

        return results;
    }

    /// <summary>
    /// Which edition a line belongs to. Used when jumping to a specific
    /// line whose edition might not be the one currently showing - a work
    /// with more than one translation only shows one at a time, so a jump
    /// target can land in a translation that isn't the active selection.
    /// </summary>
    public async Task<int?> GetEditionIdAsync(long textNodeId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EditionId FROM TextNodes WHERE TextNodeId = @TextNodeId;";
        cmd.Parameters.AddWithValue("@TextNodeId", textNodeId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    /// <summary>
    /// Which edition each of these lines belongs to.
    ///
    /// Exists for cross-work export. The views that gather passages from all
    /// over the library - the Tag Browser, Concordance, Echo results - carry
    /// the work each line came from but not the edition, because until now
    /// nothing downstream needed it. Pairing a line with its translation
    /// does: the counterpart is a sibling edition of the same work, and you
    /// can't ask which sibling to use without knowing which one you're
    /// standing on.
    ///
    /// Resolved in one query rather than per line - a tag can easily cover
    /// several hundred passages, and that many round trips for what is
    /// ultimately a small lookup table would be felt.
    /// </summary>
    public async Task<Dictionary<long, int>> GetEditionIdsAsync(
        IReadOnlyList<long> textNodeIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<long, int>();
        if (textNodeIds.Count == 0) return result;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const int batchSize = 400;
        for (var offset = 0; offset < textNodeIds.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var thisBatch = Math.Min(batchSize, textNodeIds.Count - offset);

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 60;

            var paramNames = new List<string>(thisBatch);
            for (var i = 0; i < thisBatch; i++)
            {
                paramNames.Add($"@n{i}");
                cmd.Parameters.AddWithValue($"@n{i}", textNodeIds[offset + i]);
            }

            cmd.CommandText =
                $"SELECT TextNodeId, EditionId FROM TextNodes WHERE TextNodeId IN ({string.Join(",", paramNames)});";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetInt64(0)] = reader.GetInt32(1);
            }
        }

        return result;
    }

    /// <summary>
    /// How many lines an edition holds, without loading any of them -
    /// GetByEditionAsync would pull a few thousand rows into memory to
    /// answer what is a single COUNT.
    /// </summary>
    public async Task<int> CountByEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM TextNodes WHERE EditionId = @EditionId;";
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.CommandTimeout = 60;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// How much of a work an AI-generated translation edition actually
    /// covers, as (lines translated, lines in the source edition).
    ///
    /// Only meaningful for AI translations, and only because of an invariant
    /// they alone satisfy: CreateTranslationForm writes one line per source
    /// line, under the source's own citation ref. So a source ref with no
    /// counterpart really is a line that hasn't been translated yet - which
    /// is an ordinary state, since a long work takes many batches and the
    /// free tier has a daily limit.
    ///
    /// The same comparison would be meaningless for an ingested translation:
    /// a prose translation of a verse original is legitimately divided far
    /// more coarsely, and counting its lines against the original's would
    /// report every published translation in the library as nine-tenths
    /// missing.
    ///
    /// The source edition isn't recorded anywhere, so it's inferred as
    /// whichever original-language edition of the work shares the most
    /// citation refs with this one. That's the edition it must have been
    /// built from, and inferring it means this works for translations
    /// generated before any of this existed.
    ///
    /// Null when the work has no original-language edition to compare
    /// against, which leaves the caller with nothing to claim either way.
    /// </summary>
    public async Task<(int Translated, int SourceTotal)?> GetTranslationCoverageAsync(
        int translationEditionId, int workId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 60;

        cmd.CommandText = @"
            SELECT COUNT(*) AS SourceTotal,
                   SUM(CASE WHEN EXISTS (
                       SELECT 1 FROM TextNodes t
                       WHERE t.EditionId = @TranslationEditionId
                         AND t.CitationRef = src.CitationRef) THEN 1 ELSE 0 END) AS Covered
            FROM TextNodes src
            JOIN Editions e ON src.EditionId = e.EditionId
            WHERE e.WorkId = @WorkId
              AND e.Kind = 'Original'
              AND e.EditionId <> @TranslationEditionId
            GROUP BY src.EditionId
            ORDER BY Covered DESC
            LIMIT 1;";

        cmd.Parameters.AddWithValue("@TranslationEditionId", translationEditionId);
        cmd.Parameters.AddWithValue("@WorkId", workId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var sourceTotal = reader.GetInt32(0);
        var covered = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);

        return sourceTotal == 0 ? null : (covered, sourceTotal);
    }

    /// <summary>Author/work/citation context for a single text node - used by the reception tracker.</summary>
    public async Task<(string AuthorName, string WorkTitle, string CitationRef, string Text)?> GetTextNodeSourceInfoAsync(
        long textNodeId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT a.Name, w.Title, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE tn.TextNodeId = @TextNodeId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@TextNodeId", textNodeId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    // \p{L} matches a letter in any script, not just ASCII. The original
    // [a-zA-Z] silently made this whole feature English-only: Greek text
    // contains no ASCII letters at all, so no candidate words were ever
    // extracted and echo finding returned nothing for every Greek passage.
    private static readonly System.Text.RegularExpressions.Regex WordPattern =
        new(@"\p{L}{4,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    // A deliberately blunt stopword list - not linguistically rigorous, just
    // enough to filter out words too common to signal anything (rarity is
    // the whole point of this technique).
    //
    // Covers all three languages in the corpus. An English-only list left
    // Greek and Latin function words looking "rare" to the ranking, so a
    // Greek passage would have been matched on things like καί or τῶν - the
    // exact opposite of the signal this is meant to find. Greek entries are
    // listed unaccented because they're compared after normalization.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        "the","and","that","was","for","with","his","her","him","this","have","from","they","were","when","which",
        "what","said","your","then","than","them","their","would","could","should","there","here","been","being",
        "into","upon","about","after","before","through","while","where","those","these","some","such","only",
        "over","under","again","once","also","even","still","much","many","most","more","less","than","just",
        "will","shall","must","might","cannot","never","always","every","each","both","either","neither","against",
        "among","between","around","without","within","toward","upon","because","since","until","unless",
        "himself","herself","itself","themselves","yourself","ourselves",

        // Greek (unaccented - compared post-normalization)
        "και","των","τον","την","τους","τας","τοις","ταις","αυτου","αυτων","αυτον","αυτης","αυτο","αυτος",
        "ουτος","ουτως","τουτο","τουτου","τουτων","ταυτα","εστι","εστιν","ησαν","ειναι","εχων","εχει",
        "μεν","δε","γαρ","ουν","τε","αλλα","οτι","ως","εις","εκ","εν","επι","προς","παρα","περι","υπο",
        "δια","κατα","μετα","απο","ουκ","ουχ","μη","ει","αν","οι","αι","τα","το","του","της","τω","τη",
        "ουδε","μηδε","τις","τι","ποτε","ουτε","μητε","ωστε","επει","επειδη","ινα","οπως","εαν",

        // Latin
        "atque","quod","quae","quam","cum","sed","non","est","sunt","esse","erat","erant","aut","enim",
        "autem","tamen","etiam","quidem","quoque","itaque","igitur","nam","nec","neque","sive","seu",
        "ille","illa","illud","ipse","ipsa","hoc","haec","huius","eius","eorum","earum","quibus","quo",
        "qui","quia","ubi","ibi","inde","unde","ante","post","inter","apud","contra","propter","sine",
        "per","pro","sub","super","ad","ex","in","de","si","ut","ne","vel","tum","tunc","iam","modo"
    };

    /// <summary>
    /// Finds candidate intertextual echoes for a given line - other passages
    /// (anywhere in the corpus, same original/translation kind as the
    /// source) that share unusually rare words with it. This mirrors the
    /// core technique real digital-humanities intertextuality tools use
    /// (e.g. the Tesserae Project): shared rare-word overlap is a much
    /// stronger allusion signal than shared common words, since two authors
    /// independently using "and" or "king" means nothing, but both using an
    /// unusual word does.
    ///
    /// Scope note: this only really works within one language at a time -
    /// comparing an English translation's wording against another English
    /// translation's wording, or Greek/Latin original against original. It
    /// can't detect an echo between the Greek original and an English
    /// translation of a different work, since those aren't the same words.
    /// </summary>
    public async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount)>> FindEchoesAsync(
        long sourceTextNodeId, CancellationToken cancellationToken = default)
    {
        var source = await GetTextNodeContextAsync(sourceTextNodeId, cancellationToken);
        if (source == null) return new();

        // Normalized before the stopword check: accents mean the raw form
        // of a Greek function word won't match an unaccented stoplist entry,
        // which would let καί and τῶν through as if they were rare. The
        // normalized form is also what the word index stores, so this is the
        // right shape for the frequency lookup that follows.
        var candidateWords = WordPattern.Matches(source.Value.Text)
            .Select(m => WordNormalizer.Normalize(m.Value))
            .Where(w => w.Length >= 4)
            .Distinct(StringComparer.Ordinal)
            .Where(w => !StopWords.Contains(w))
            .ToList();

        if (candidateWords.Count == 0) return new();

        // Rank the source's own words by how rare they are across the whole
        // corpus, and keep only the rarest handful - those are the words
        // worth searching on. A word that appears in 40,000 lines tells you
        // nothing; a word that appears in 6 tells you something.
        // One batched query rather than one per candidate word. This loop
        // previously cost two connections and two round-trips per word (the
        // per-call HasDataAsync check was itself a query), so a line with
        // fifteen candidate words meant sixty of them before a single echo
        // was found. The whole set now resolves in one.
        var frequencies = await CountTextNodesContainingWordsAsync(candidateWords, cancellationToken);

        var significantWords = frequencies
            .Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Value)
            .Take(8)
            .Select(kv => kv.Key)
            .ToList();

        if (significantWords.Count == 0) return new();

        var candidates = await FindTextNodesContainingAnyWordAsync(
            significantWords, source.Value.EditionKind, sourceTextNodeId, cancellationToken);

        var significantSet = significantWords.ToHashSet(StringComparer.Ordinal);

        return candidates
            .Select(c => (
                c.WorkId, c.TextNodeId, c.AuthorName, c.WorkTitle, c.CitationRef, c.Text,
                // Compared as normalized whole words rather than raw
                // substrings. The significant words are normalized (accents
                // stripped), so a literal Contains against accented Greek
                // text would never match - and substring matching would
                // also count "war" inside "warden" as a hit.
                SharedWordCount: c.Text
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(WordNormalizer.Normalize)
                    .Where(w => w.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Count(significantSet.Contains)
            ))
            .Where(c => c.SharedWordCount > 0)
            .OrderByDescending(c => c.SharedWordCount)
            .Take(30)
            .ToList();
    }

    private async Task<(string Text, string EditionKind)?> GetTextNodeContextAsync(
        long textNodeId, CancellationToken cancellationToken)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT tn.Text, e.Kind
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            WHERE tn.TextNodeId = @TextNodeId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@TextNodeId", textNodeId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return (reader.GetString(0), reader.GetString(1));
    }

    /// <summary>
    /// Global count of distinct lines containing a word. Tries the word
    /// index first (an indexed exact-match lookup on the normalized word -
    /// fast), falling back to a LIKE scan only if the index hasn't been
    /// built yet.
    /// </summary>
    private async Task<Dictionary<string, int>> CountTextNodesContainingWordsAsync(
        IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        if (words.Count == 0) return frequencies;

        // Checked once for the whole batch, not once per word - and against
        // the same connection the query below uses, rather than a separate
        // connection just for the check.
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        var hasIndex = await WordIndexRepository.HasDataAsync(conn, cancellationToken);

        if (hasIndex)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;

            var paramNames = new List<string>(words.Count);
            for (var i = 0; i < words.Count; i++)
            {
                var normalized = WordNormalizer.Normalize(words[i]);
                paramNames.Add($"@w{i}");
                cmd.Parameters.AddWithValue($"@w{i}", normalized);
            }

            cmd.CommandText = $@"
                SELECT NormalizedWord, COUNT(DISTINCT TextNodeId)
                FROM WordIndex
                WHERE NormalizedWord IN ({string.Join(",", paramNames)})
                GROUP BY NormalizedWord;";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                frequencies[reader.GetString(0)] = reader.GetInt32(1);
            }

            // GROUP BY only returns rows for words that actually occur, so
            // anything absent genuinely has a count of zero. The caller
            // filters those out, but it should see them rather than find
            // the key missing entirely.
            foreach (var word in words)
            {
                var normalized = WordNormalizer.Normalize(word);
                frequencies.TryAdd(normalized, 0);
            }

            return frequencies;
        }

        // No word index built yet - fall back to a LIKE scan per word, the
        // same fallback the single-word version had. Genuinely slow, but
        // this path only runs before "Build Word Index" has ever been run.
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var likeCmd = conn.CreateCommand();
            likeCmd.CommandText = "SELECT COUNT(*) FROM TextNodes WHERE Text LIKE @Word;";
            likeCmd.Parameters.AddWithValue("@Word", $"%{word}%");
            frequencies[word] = Convert.ToInt32(await likeCmd.ExecuteScalarAsync(cancellationToken));
        }

        return frequencies;
    }

    /// <summary>
    /// Candidate lines containing any of the given rare words, restricted to
    /// the same edition kind (original-vs-translation) as the source and
    /// excluding the source line itself. Tries the word index first, same
    /// fallback story as CountTextNodesContainingWordsAsync. Capped at 3000
    /// raw rows so a LIKE fallback on a huge corpus can't run away -
    /// scoring/ranking happens afterward in FindEchoesAsync.
    /// </summary>
    private async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)>> FindTextNodesContainingAnyWordAsync(
        List<string> words, string editionKind, long excludeTextNodeId, CancellationToken cancellationToken)
    {
        var results = new List<(int, long, string, string, string, string)>();

        // One connection for the method, whichever branch below ends up
        // running - the has-data check, the indexed query if it's there,
        // and the LIKE fallback if it isn't. This used to open up to three
        // separate connections for one logical lookup: one for the check,
        // one for the indexed query, one for the LIKE fallback (the last
        // two mutually exclusive, but the check's was always paid).
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        if (await WordIndexRepository.HasDataAsync(conn, cancellationToken))
        {
            var normalized = words.Select(WordNormalizer.Normalize).Where(w => w.Length > 0).Distinct().ToList();
            if (normalized.Count == 0) return results;

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;

            var paramNames = new List<string>();
            for (var i = 0; i < normalized.Count; i++)
            {
                paramNames.Add($"@w{i}");
                cmd.Parameters.AddWithValue($"@w{i}", normalized[i]);
            }
            cmd.Parameters.AddWithValue("@Kind", editionKind);
            cmd.Parameters.AddWithValue("@ExcludeId", excludeTextNodeId);

            cmd.CommandText = $@"
                SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
                FROM (
                    SELECT DISTINCT TextNodeId FROM WordIndex WHERE NormalizedWord IN ({string.Join(",", paramNames)})
                ) ids
                JOIN TextNodes tn ON ids.TextNodeId = tn.TextNodeId
                JOIN Editions e ON tn.EditionId = e.EditionId
                JOIN Works w ON e.WorkId = w.WorkId
                JOIN Authors a ON w.AuthorId = a.AuthorId
                WHERE e.Kind = @Kind AND tn.TextNodeId <> @ExcludeId
                LIMIT 3000;";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add((
                    reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5)));
            }

            return results;
        }

        await using var likeCmd = conn.CreateCommand();
        likeCmd.CommandTimeout = 180;

        var likeClauses = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            likeClauses.Add($"tn.Text LIKE @w{i}");
            likeCmd.Parameters.AddWithValue($"@w{i}", $"%{words[i]}%");
        }
        likeCmd.Parameters.AddWithValue("@Kind", editionKind);
        likeCmd.Parameters.AddWithValue("@ExcludeId", excludeTextNodeId);

        likeCmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE e.Kind = @Kind AND tn.TextNodeId <> @ExcludeId AND ({string.Join(" OR ", likeClauses)})
            LIMIT 3000;";

        await using var likeReader = await likeCmd.ExecuteReaderAsync(cancellationToken);
        while (await likeReader.ReadAsync(cancellationToken))
        {
            results.Add((
                likeReader.GetInt32(0), likeReader.GetInt64(1), likeReader.GetString(2),
                likeReader.GetString(3), likeReader.GetString(4), likeReader.GetString(5)));
        }

        return results;
    }
}
