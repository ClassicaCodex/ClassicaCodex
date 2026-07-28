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
        cmd.CommandText = @"SELECT WorkId, AuthorId, CtsUrn, Title, CitationScheme
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
                CitationScheme = reader.IsDBNull(4) ? null : reader.GetString(4)
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
}
