using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class ApparatusRepository
{
    /// <summary>
    /// Replaces an edition's apparatus wholesale.
    ///
    /// Delete-then-insert rather than merge, matching how text nodes are
    /// handled: a re-ingest after a corpus update must not leave last
    /// version's entries sitting alongside this one's, and there is no stable
    /// identity to merge on - an apparatus entry is not a thing the user owns
    /// or has annotated.
    ///
    /// Batched into multi-row inserts inside one transaction. A large edition
    /// carries thousands of entries and a round trip each would dominate
    /// ingest time.
    /// </summary>
    public async Task ReplaceForEditionAsync(
        int editionId,
        IReadOnlyList<ApparatusEntry> entries,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        await using (var clear = conn.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = "DELETE FROM ApparatusEntries WHERE EditionId = @EditionId;";
            clear.Parameters.AddWithValue("@EditionId", editionId);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        const int batchSize = 200;
        for (var offset = 0; offset < entries.Count; offset += batchSize)
        {
            var batch = entries.Skip(offset).Take(batchSize).ToList();

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)transaction;

            var valueRows = new List<string>(batch.Count);
            for (var i = 0; i < batch.Count; i++)
            {
                var e = batch[i];
                valueRows.Add($"(@e{i},@c{i},@s{i},@k{i},@l{i},@w{i},@x{i})");
                cmd.Parameters.AddWithValue($"@e{i}", editionId);
                cmd.Parameters.AddWithValue($"@c{i}", e.CitationRef);
                cmd.Parameters.AddWithValue($"@s{i}", e.SortOrder);
                cmd.Parameters.AddWithValue($"@k{i}", e.Kind);
                cmd.Parameters.AddWithValue($"@l{i}", (object?)e.Lemma ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@w{i}", (object?)e.Witness ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@x{i}", e.Content);
            }

            cmd.CommandText =
                "INSERT INTO ApparatusEntries (EditionId, CitationRef, SortOrder, Kind, Lemma, Witness, Content) " +
                $"VALUES {string.Join(",", valueRows)};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Every apparatus entry on one line, in source order.</summary>
    public async Task<List<ApparatusEntry>> GetForLineAsync(
        int editionId, string citationRef, CancellationToken cancellationToken = default)
    {
        var results = new List<ApparatusEntry>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ApparatusId, EditionId, CitationRef, SortOrder, Kind, Lemma, Witness, Content
            FROM ApparatusEntries
            WHERE EditionId = @EditionId AND CitationRef = @CitationRef
            ORDER BY SortOrder;";
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.Parameters.AddWithValue("@CitationRef", citationRef);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    /// <summary>
    /// Every apparatus entry for an edition, for the "show the whole
    /// apparatus" view.
    /// </summary>
    public async Task<List<ApparatusEntry>> GetForEditionAsync(
        int editionId, CancellationToken cancellationToken = default)
    {
        var results = new List<ApparatusEntry>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT a.ApparatusId, a.EditionId, a.CitationRef, a.SortOrder, a.Kind, a.Lemma, a.Witness, a.Content
            FROM ApparatusEntries a
            -- Ordered by the line's position in the work, not by citation
            -- reference: refs are strings, so ""1.9"" would otherwise sort
            -- after ""1.10"".
            LEFT JOIN TextNodes t
                   ON t.EditionId = a.EditionId AND t.CitationRef = a.CitationRef
            WHERE a.EditionId = @EditionId
            ORDER BY COALESCE(t.SortOrder, 0), a.SortOrder;";
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.CommandTimeout = 60;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    /// <summary>How many entries an edition has - used to decide whether to offer the view at all.</summary>
    public async Task<int> CountForEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM ApparatusEntries WHERE EditionId = @EditionId;";
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private static ApparatusEntry Map(SqliteDataReader reader) => new()
    {
        ApparatusId = reader.GetInt64(0),
        EditionId = reader.GetInt32(1),
        CitationRef = reader.GetString(2),
        SortOrder = reader.GetInt32(3),
        Kind = reader.GetString(4),
        Lemma = reader.IsDBNull(5) ? null : reader.GetString(5),
        Witness = reader.IsDBNull(6) ? null : reader.GetString(6),
        Content = reader.GetString(7)
    };
}
