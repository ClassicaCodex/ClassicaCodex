using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// Bookmarks are stored against (EditionId, CitationRef) rather than the
/// TextNodeId a caller hands in, so they survive a re-ingest that renumbers
/// every text node. See the PassageTags comment in SchemaInitializer for the
/// full reasoning.
///
/// Callers still pass a TextNodeId - that's what the reader panes have in
/// hand, and resolving it to a citation is this class's job, not theirs.
/// </summary>
public class BookmarkRepository
{
    public async Task<int> AddAsync(long textNodeId, string? note, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // INSERT ... SELECT resolves the passage in the same statement, so
        // there's no window where the node could vanish between a lookup and
        // the write.
        const string sql = @"
            INSERT INTO Bookmarks (EditionId, CitationRef, Note)
            SELECT tn.EditionId, tn.CitationRef, @Note
            FROM TextNodes tn
            WHERE tn.TextNodeId = @TextNodeId
            RETURNING BookmarkId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@TextNodeId", textNodeId);
        cmd.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);

        // Null means the TextNodeId didn't resolve - the line was removed
        // between the reader painting it and the bookmark being saved.
        if (result == null || result == DBNull.Value)
        {
            throw new InvalidOperationException(
                "That line is no longer in the library, so it couldn't be bookmarked. " +
                "Reopen the work and try again.");
        }

        return Convert.ToInt32(result);
    }

    /// <summary>
    /// All bookmarks with enough context (author/work/citation/line text) to
    /// browse and jump from, newest first.
    ///
    /// The join to TextNodes is an inner one, so a bookmark whose passage
    /// isn't currently ingested simply doesn't appear. It is not deleted -
    /// it's dormant, and comes back on its own if a later ingest restores
    /// that citation. CountDormantAsync reports how many are in that state
    /// so the Bookmarks window can say so rather than appearing to have lost
    /// them.
    /// </summary>
    public async Task<List<(int BookmarkId, int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Note, DateTime CreatedAt, string? Milestone)>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<(int, int, long, string, string, string, string, string?, DateTime, string?)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // MIN(tn.TextNodeId) because one citation ref can legitimately match
        // several text nodes (Perseus editions aren't guaranteed unique on
        // @n). The bookmark is on the passage; jumping to the first node of
        // it is the right landing spot, and grouping keeps one row per
        // bookmark instead of one per matching line.
        const string sql = @"
            SELECT b.BookmarkId, w.WorkId, MIN(tn.TextNodeId), a.Name, w.Title,
                   b.CitationRef, tn.Text, b.Note, b.CreatedAt, tn.Milestone
            FROM Bookmarks b
            JOIN TextNodes tn ON b.EditionId = tn.EditionId AND b.CitationRef = tn.CitationRef
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            GROUP BY b.BookmarkId
            ORDER BY b.CreatedAt DESC;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDateTime(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return results;
    }

    /// <summary>
    /// How many bookmarks point at a passage that isn't currently in the
    /// library. Normally zero. A non-zero count means a corpus was ingested
    /// with different citation refs than the notes were made against, which
    /// is worth telling the reader plainly - the alternative is bookmarks
    /// that appear to have silently vanished.
    /// </summary>
    public async Task<int> CountDormantAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT COUNT(*)
            FROM Bookmarks b
            WHERE NOT EXISTS (
                SELECT 1 FROM TextNodes tn
                WHERE tn.EditionId = b.EditionId AND tn.CitationRef = b.CitationRef);";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 60;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task DeleteAsync(int bookmarkId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = "DELETE FROM Bookmarks WHERE BookmarkId = @BookmarkId;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@BookmarkId", bookmarkId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
