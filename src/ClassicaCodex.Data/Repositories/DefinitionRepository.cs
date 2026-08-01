using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class DefinitionRepository
{
    /// <summary>
    /// Inserts dictionary entries. Same multi-row-per-statement batching as
    /// LemmaRepository and WordIndexRepository, for the same reason - see
    /// WordIndexRepository's remarks.
    /// </summary>
    public async Task BulkInsertAsync(IReadOnlyList<Definition> definitions, CancellationToken cancellationToken = default)
    {
        if (definitions.Count == 0) return;

        const int rowsPerStatement = 300;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        for (var offset = 0; offset < definitions.Count; offset += rowsPerStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Indexed rather than Skip().Take(): Skip() on an IReadOnlyList
            // restarts from element zero on every batch, making the loop
            // quadratic in the row count.
            var batchSize = Math.Min(rowsPerStatement, definitions.Count - offset);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)transaction;

            var valueRows = new List<string>(batchSize);
            for (var i = 0; i < batchSize; i++)
            {
                var d = definitions[offset + i];
                valueRows.Add($"(@h{i},@nh{i},@l{i},@e{i},@s{i})");
                cmd.Parameters.AddWithValue($"@h{i}", d.Headword);
                cmd.Parameters.AddWithValue($"@nh{i}", d.NormalizedHeadword);
                cmd.Parameters.AddWithValue($"@l{i}", d.Language);
                cmd.Parameters.AddWithValue($"@e{i}", d.Entry);
                cmd.Parameters.AddWithValue($"@s{i}", (object?)d.Source ?? DBNull.Value);
            }

            cmd.CommandText =
                $"INSERT INTO Definitions (Headword, NormalizedHeadword, Language, Entry, Source) VALUES {string.Join(",", valueRows)};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Definitions;";
        cmd.CommandTimeout = 120;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// Entry counts split by language, largest first. A single combined
    /// total hides the failure this exists to surface - one dictionary
    /// loading while the other silently doesn't. "241,000 grc / 0 lat" tells
    /// that story at a glance where "241,000 entries" conceals it.
    /// </summary>
    public async Task<IReadOnlyList<(string Language, int Count)>> CountByLanguageAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<(string, int)>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Language, COUNT(*) FROM Definitions GROUP BY Language ORDER BY COUNT(*) DESC;";
        cmd.CommandTimeout = 120;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add((reader.GetString(0), reader.GetInt32(1)));

        return results;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Definitions;";
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Dictionary entries for a headword. Can legitimately return several -
    /// homograph numbering doesn't map cleanly between lemma data and
    /// lexicons, so all candidates are returned rather than guessing.
    /// </summary>
    public async Task<List<(string Headword, string Entry, string? Source)>> GetByHeadwordAsync(
        string headword, string language, CancellationToken cancellationToken = default)
    {
        var results = new List<(string, string, string?)>();

        var normalized = WordNormalizer.NormalizeHeadword(headword, language);
        if (normalized.Length == 0) return results;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // LIMIT goes at the end in SQLite, not TOP (N) at the start.
        const string sql = @"
            SELECT Headword, Entry, Source
            FROM Definitions
            WHERE NormalizedHeadword = @Normalized
            ORDER BY Headword
            LIMIT 10;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 60;
        cmd.Parameters.AddWithValue("@Normalized", normalized);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return results;
    }
}
