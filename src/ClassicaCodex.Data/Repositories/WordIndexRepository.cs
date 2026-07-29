using ClassicaCodex.Core;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// Access to the inverted word index. Every row is one (normalized word,
/// line) pair, so "which lines contain any of these forms?" becomes an
/// index seek instead of a full scan per form.
/// </summary>
public class WordIndexRepository
{
    /// <summary>
    /// Cheap "has the index been built?" check. Deliberately not COUNT - on
    /// a multi-million-row table that's real work, and the hot path only
    /// needs to know whether any rows exist at all.
    /// </summary>
    public async Task<bool> HasDataAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM WordIndex LIMIT 1;";
        cmd.CommandTimeout = 30;
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null && result != DBNull.Value;
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM WordIndex;";
        cmd.CommandTimeout = 300;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // Drop and recreate rather than DELETE FROM. SQLite has an
        // optimization where a DELETE with no WHERE clause can skip
        // row-by-row deletion, but whether that actually kicks in depends
        // on the exact SQLite build and table configuration - not
        // something worth trusting blindly on a table that can hold tens
        // of millions of rows. Drop+recreate is unconditionally fast
        // regardless of that optimization's behavior here.
        await using (var dropCmd = conn.CreateCommand())
        {
            dropCmd.CommandText = "DROP TABLE IF EXISTS WordIndex;";
            dropCmd.CommandTimeout = 60;
            await dropCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = "CREATE TABLE WordIndex (NormalizedWord TEXT NOT NULL, TextNodeId INTEGER NOT NULL);";
            await createCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Deliberately NOT creating the index here. Every INSERT into an
        // indexed table has to update that index's B-tree as it goes, and
        // this table takes tens of millions of rows - paying that cost per
        // row is far more expensive than building the index once, in a
        // single sort pass, after the data is all in. CreateIndexAsync
        // below is called at the end of the build instead.
    }

    /// <summary>
    /// Builds the lookup index, after the bulk load rather than before it -
    /// see the note in ClearAsync for why the order matters. IF NOT EXISTS
    /// so that an interrupted build followed by a re-run can't fail here;
    /// a build that's cancelled partway simply leaves the table unindexed,
    /// which makes searches slow but never wrong, and the next full run
    /// recreates everything from scratch anyway.
    /// </summary>
    public async Task CreateIndexAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var indexCmd = conn.CreateCommand();
        indexCmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_WordIndex_Word ON WordIndex (NormalizedWord, TextNodeId);";

        // The index build over a full corpus is a single large sort - it can
        // legitimately take minutes, and the default timeout would abort it.
        indexCmd.CommandTimeout = 600;
        await indexCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts word-index entries. This is the single largest bulk insert in
    /// the app (potentially tens of millions of rows for a full corpus), so
    /// unlike the other repositories' one-row-per-statement transaction
    /// loop, this batches many rows into each INSERT statement (a single
    /// multi-row VALUES list) - at this scale, per-statement overhead
    /// (prepare/step/reset) dominates, and cutting the statement count by
    /// ~400x is what actually matters, more than the transaction wrapping
    /// alone. 400 rows/statement keeps parameter count (800) comfortably
    /// under SQLite's limit even on an older bundled version.
    /// </summary>
    public async Task BulkInsertAsync(
        IReadOnlyCollection<(string Word, long TextNodeId)> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0) return;

        const int rowsPerStatement = 400;
        var entriesList = entries as IReadOnlyList<(string Word, long TextNodeId)> ?? entries.ToList();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = conn.BeginTransaction();

        for (var offset = 0; offset < entriesList.Count; offset += rowsPerStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = entriesList.Skip(offset).Take(rowsPerStatement).ToList();

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;

            var valueRows = new List<string>(batch.Count);
            for (var i = 0; i < batch.Count; i++)
            {
                valueRows.Add($"(@w{i},@n{i})");
                cmd.Parameters.AddWithValue($"@w{i}", batch[i].Word);
                cmd.Parameters.AddWithValue($"@n{i}", batch[i].TextNodeId);
            }

            cmd.CommandText = $"INSERT INTO WordIndex (NormalizedWord, TextNodeId) VALUES {string.Join(",", valueRows)};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Total number of text nodes, so a build can report meaningful progress.
    /// </summary>
    public async Task<long> GetTextNodeCountAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM TextNodes;";
        cmd.CommandTimeout = 300;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// How many distinct lines the index actually covers right now - not
    /// the same as GetTextNodeCountAsync once any source has been ingested
    /// since the last build. The index is pure derived data with no
    /// automatic refresh hook (see WordIndexService's own remarks): nothing
    /// re-runs it when a new Setup source finishes ingesting, so a Renaissance
    /// or First1KGreek pass added after the last build leaves every line it
    /// contributed silently unindexed. SetupWizardForm compares this against
    /// GetTextNodeCountAsync to say so, rather than leaving that gap to be
    /// discovered obliquely through a search that should have found
    /// something and didn't.
    /// </summary>
    public async Task<long> GetIndexedTextNodeCountAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(DISTINCT TextNodeId) FROM WordIndex;";
        cmd.CommandTimeout = 300;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// Reads a batch of text nodes by ascending id, for indexing. Paging by
    /// id rather than OFFSET keeps each batch an index seek instead of
    /// re-scanning everything before it.
    /// </summary>
    public async Task<List<(long TextNodeId, string Text)>> GetTextNodeBatchAsync(
        long afterTextNodeId, int batchSize, CancellationToken cancellationToken = default)
    {
        var results = new List<(long, string)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT TextNodeId, Text
            FROM TextNodes
            WHERE TextNodeId > @AfterId
            ORDER BY TextNodeId
            LIMIT @BatchSize;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 300;
        cmd.Parameters.AddWithValue("@BatchSize", batchSize);
        cmd.Parameters.AddWithValue("@AfterId", afterTextNodeId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        return results;
    }

    /// <summary>
    /// The lines containing any of the given forms. Forms are normalized
    /// here so callers can pass raw inflected forms straight from the lemma
    /// data.
    /// </summary>
    public async Task<List<long>> FindTextNodeIdsAsync(
        IReadOnlyList<string> forms, int maxResults = 5000, CancellationToken cancellationToken = default)
    {
        var results = new List<long>();

        var normalized = forms
            .Select(WordNormalizer.Normalize)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(200)
            .ToList();

        if (normalized.Count == 0) return results;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;

        var paramNames = new List<string>();
        for (var i = 0; i < normalized.Count; i++)
        {
            paramNames.Add($"@w{i}");
            cmd.Parameters.AddWithValue($"@w{i}", normalized[i]);
        }

        cmd.CommandText = $@"
            SELECT DISTINCT TextNodeId
            FROM WordIndex
            WHERE NormalizedWord IN ({string.Join(",", paramNames)})
            LIMIT @MaxResults;";
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetInt64(0));
        }

        return results;
    }
}
