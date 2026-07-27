using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class BookmarkRepository
{
    public async Task<int> AddAsync(long textNodeId, string? note, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            INSERT INTO Bookmarks (TextNodeId, Note)
            VALUES (@TextNodeId, @Note)
            RETURNING BookmarkId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@TextNodeId", textNodeId);
        cmd.Parameters.AddWithValue("@Note", (object?)note ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// All bookmarks with enough context (author/work/citation/line text) to
    /// browse and jump from, newest first.
    /// </summary>
    public async Task<List<(int BookmarkId, int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Note, DateTime CreatedAt)>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<(int, int, long, string, string, string, string, string?, DateTime)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT b.BookmarkId, w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text, b.Note, b.CreatedAt
            FROM Bookmarks b
            JOIN TextNodes tn ON b.TextNodeId = tn.TextNodeId
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
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
                reader.GetDateTime(8)));
        }

        return results;
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
