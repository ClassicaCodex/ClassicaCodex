using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class WorkRepository
{
    public async Task<int> UpsertAsync(Work work, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            INSERT INTO Works (AuthorId, CtsUrn, Title, CitationScheme)
            VALUES (@AuthorId, @CtsUrn, @Title, @CitationScheme)
            ON CONFLICT(CtsUrn) DO UPDATE SET
                Title = excluded.Title,
                CitationScheme = excluded.CitationScheme,
                AuthorId = excluded.AuthorId
            RETURNING WorkId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@AuthorId", work.AuthorId);
        cmd.Parameters.AddWithValue("@CtsUrn", work.CtsUrn);
        cmd.Parameters.AddWithValue("@Title", work.Title);
        cmd.Parameters.AddWithValue("@CitationScheme", (object?)work.CitationScheme ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<List<Work>> GetByAuthorAsync(int authorId, CancellationToken cancellationToken = default)
    {
        var results = new List<Work>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"SELECT WorkId, AuthorId, CtsUrn, Title, CitationScheme,
                   AttributionStatus, AttributionNote, AttributionSetByUser
                             FROM Works WHERE AuthorId = @AuthorId ORDER BY Title;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@AuthorId", authorId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new Work
            {
                WorkId = reader.GetInt32(0),
                AuthorId = reader.GetInt32(1),
                CtsUrn = reader.GetString(2),
                Title = reader.GetString(3),
                CitationScheme = reader.IsDBNull(4) ? null : reader.GetString(4),
                AttributionStatus = ParseStatus(reader.GetString(5)),
                AttributionNote = reader.IsDBNull(6) ? null : reader.GetString(6),
                AttributionSetByUser = reader.GetInt32(7) != 0
            });
        }

        return results;
    }

    /// <summary>
    /// Every work in the library, grouped by author, in one query.
    ///
    /// The library tree previously called GetByAuthorAsync once per author
    /// while building itself - with a full Perseus corpus that's hundreds
    /// of authors, so hundreds of queries and hundreds of connections
    /// opened and torn down before the tree could show anything. Authors
    /// with no works simply don't appear as keys; the caller treats a
    /// missing key as an empty list.
    /// </summary>
    public async Task<Dictionary<int, List<Work>>> GetAllGroupedByAuthorAsync(CancellationToken cancellationToken = default)
    {
        var grouped = new Dictionary<int, List<Work>>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = @"SELECT WorkId, AuthorId, CtsUrn, Title, CitationScheme,
                   AttributionStatus, AttributionNote, AttributionSetByUser
                            FROM Works ORDER BY AuthorId, Title;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var work = new Work
            {
                WorkId = reader.GetInt32(0),
                AuthorId = reader.GetInt32(1),
                CtsUrn = reader.GetString(2),
                Title = reader.GetString(3),
                CitationScheme = reader.IsDBNull(4) ? null : reader.GetString(4),
                AttributionStatus = ParseStatus(reader.GetString(5)),
                AttributionNote = reader.IsDBNull(6) ? null : reader.GetString(6),
                AttributionSetByUser = reader.GetInt32(7) != 0
            };

            if (!grouped.TryGetValue(work.AuthorId, out var list))
            {
                list = new List<Work>();
                grouped[work.AuthorId] = list;
            }
            list.Add(work);
        }

        return grouped;
    }

    /// <summary>
    /// One work's attribution, for the reader header and the editor.
    /// </summary>
    public async Task<(AttributionStatus Status, string? Note, bool SetByUser)> GetAttributionAsync(
        int workId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT AttributionStatus, AttributionNote, AttributionSetByUser
            FROM Works WHERE WorkId = @WorkId;";
        cmd.Parameters.AddWithValue("@WorkId", workId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return (AttributionStatus.Accepted, null, false);

        return (ParseStatus(reader.GetString(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetInt32(2) != 0);
    }

    /// <summary>
    /// Attribution for many works at once, for the pool builders.
    ///
    /// One query rather than one per work: a stylometry pool is every work in a
    /// language, which on a full Perseus install is thousands.
    /// </summary>
    public async Task<Dictionary<int, AttributionStatus>> GetAttributionsAsync(
        IReadOnlyList<int> workIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<int, AttributionStatus>();
        if (workIds.Count == 0) return result;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT WorkId, AttributionStatus FROM Works;";

        var wanted = workIds.ToHashSet();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(0);
            if (wanted.Contains(id)) result[id] = ParseStatus(reader.GetString(1));
        }

        return result;
    }

    /// <summary>
    /// Records a judgement about a work's attribution, made by a person.
    ///
    /// Sets AttributionSetByUser, which stops the catalog from ever revising
    /// it - including when the catalog grows or the corpus is re-ingested. That
    /// is the whole point: a decision that a later default could silently undo
    /// is not a decision.
    /// </summary>
    public async Task SetAttributionAsync(
        int workId,
        AttributionStatus status,
        string? note,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            UPDATE Works
            SET AttributionStatus = @Status,
                AttributionNote = @Note,
                AttributionSetByUser = 1
            WHERE WorkId = @WorkId;";

        cmd.Parameters.AddWithValue("@Status", status.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WorkId", workId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Hands a work back to the catalog, forgetting any judgement made about
    /// it. Returns it to whatever the built-in table says, or to Accepted when
    /// the table says nothing.
    /// </summary>
    public async Task ClearAttributionOverrideAsync(
        int workId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE Works
                SET AttributionStatus = 'accepted',
                    AttributionNote = NULL,
                    AttributionSetByUser = 0
                WHERE WorkId = @WorkId;";
            cmd.Parameters.AddWithValue("@WorkId", workId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await ApplyCatalogDefaultsAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds attribution from the built-in catalog, leaving anything a person
    /// has set alone.
    ///
    /// Safe to run as often as you like - after an ingest, after an upgrade,
    /// on every startup if that turns out to be wanted. It only ever writes
    /// where AttributionSetByUser is 0, so running it cannot undo a judgement,
    /// and it writes 'accepted' back over a stale default when an entry is
    /// removed from the catalog rather than leaving a status nothing supports.
    ///
    /// Returns how many works it changed, which is the number worth showing
    /// after an ingest: "12 works marked as doubted" is information, and a
    /// silent reclassification of somebody's library is not.
    /// </summary>
    public async Task<int> ApplyCatalogDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var changed = 0;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        var works = new List<(int WorkId, string Author, string Title, string Status, string? Note)>();

        await using (var read = conn.CreateCommand())
        {
            read.CommandText = @"
                SELECT w.WorkId, a.Name, w.Title, w.AttributionStatus, w.AttributionNote
                FROM Works w
                JOIN Authors a ON a.AuthorId = w.AuthorId
                WHERE w.AttributionSetByUser = 0;";

            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                works.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                           reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        foreach (var work in works)
        {
            var entry = DisputedWorkData.Lookup(work.Author, work.Title);

            var status = (entry?.Status ?? AttributionStatus.Accepted).ToString().ToLowerInvariant();
            var note = entry?.Note;

            if (status == work.Status && note == work.Note) continue;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Works
                SET AttributionStatus = @Status, AttributionNote = @Note
                WHERE WorkId = @WorkId AND AttributionSetByUser = 0;";
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@WorkId", work.WorkId);

            changed += await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        return changed;
    }

    /// <summary>
    /// Reads a status back, tolerating anything unexpected as Accepted.
    ///
    /// A row written by a newer version with a status this one does not know
    /// should read as an ordinary work rather than throw and take the library
    /// with it.
    /// </summary>
    private static AttributionStatus ParseStatus(string stored) =>
        Enum.TryParse<AttributionStatus>(stored, ignoreCase: true, out var parsed)
            ? parsed
            : AttributionStatus.Accepted;
}
