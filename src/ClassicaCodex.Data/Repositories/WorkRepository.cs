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

        const string sql = @"SELECT WorkId, AuthorId, CtsUrn, Title, CitationScheme
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
                CitationScheme = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        return results;
    }
}
