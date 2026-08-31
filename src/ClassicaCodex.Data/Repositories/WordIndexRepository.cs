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
        return await HasDataAsync(conn, cancellationToken);
    }

    /// <summary>
    /// Same check, against a connection the caller already has open.
    ///
    /// Every search path that uses the word index calls HasDataAsync first
    /// to decide fast-path-vs-fallback, then immediately opens a connection
    /// of its own to run the real query. That used to mean two full
    /// connection opens per search - and DbConnectionFactory reasserts five
    /// PRAGMAs on every open, so the "just checking whether the index has
    /// any rows" call cost the same setup as the query that follows it. A
    /// caller that's about to query anyway should open once and hand the
    /// connection to both calls.
    /// </summary>
    public static async Task<bool> HasDataAsync(SqliteConnection conn, CancellationToken cancellationToken = default)
    {
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
            // The one definition, from SchemaInitializer - not a second copy
            // that quietly drifts from it the first time a column is added.
            createCmd.CommandText = SchemaInitializer.WordIndexTableDdl;
            await createCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // There is no second index to defer any more. The table IS the index -
        // see SchemaInitializer.WordIndexTableDdl - so the deferred build that
        // used to run after the bulk load has nothing left to do.
    }

    /// <summary>
    /// Kept as a no-op rather than removed, because it is the seam the build
    /// reports "Building lookup index..." at, and because a caller asking for
    /// the index to exist is asking for something that is now true by the time
    /// the table does.
    ///
    /// The work it used to do - a single large sort over everything just
    /// written, to build a covering index over both columns - is gone with the
    /// index. The rows now land in their final B-tree as they are inserted,
    /// which is why BulkInsertAsync sorts each batch first.
    /// </summary>
    public Task CreateIndexAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes just one edition's entries, ahead of re-indexing it - not the
    /// whole-table ClearAsync a full rebuild uses. Needed because
    /// CreateTranslationForm's save clears and reinserts an in-progress
    /// edition's TextNodes on every batch, which means the TextNodeIds
    /// themselves change each time (fresh auto-increment values on every
    /// insert) - the old index rows would otherwise point at ids that no
    /// longer exist, rather than just being absent.
    /// </summary>
    public async Task DeleteByEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "DELETE FROM WordIndex WHERE TextNodeId IN (SELECT TextNodeId FROM TextNodes WHERE EditionId = @EditionId);";
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.CommandTimeout = 60;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
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

        // Sorted into key order before insertion, which is new and is what pays
        // for the WITHOUT ROWID table. Rows arriving in random key order make
        // every insert a seek into the middle of a B-tree; sorted, each batch
        // appends to a handful of pages. Measured over a full corpus, sorting
        // took the build from 273.9 s to 223.0 s - faster than the old shape
        // managed at more than twice the size.
        //
        // Per batch rather than globally: the caller hands over 200,000 entries
        // at a time, and sorting those costs a few milliseconds, where holding
        // all 26 million to sort at once would cost about 1.5 GB of memory for
        // a few seconds more. Locality within a batch is most of the win.
        //
        // Ordinal, to match the collation the primary key is built on. Sorting
        // by anything else would hand SQLite an order it does not recognise as
        // one, and the locality would be imaginary.
        var entriesList = entries
            .OrderBy(e => e.Word, StringComparer.Ordinal)
            .ThenBy(e => e.TextNodeId)
            .ToList();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        for (var offset = 0; offset < entriesList.Count; offset += rowsPerStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Index into the list directly. Skip() on an IReadOnlyList still
            // walks from element zero every single time, so batching this way
            // was quadratic in the row count - and this is the one method in
            // the app that genuinely sees tens of millions of rows, where
            // that goes from academic to being most of the build time.
            var batchSize = Math.Min(rowsPerStatement, entriesList.Count - offset);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)transaction;

            var valueRows = new List<string>(batchSize);
            for (var i = 0; i < batchSize; i++)
            {
                var entry = entriesList[offset + i];
                valueRows.Add($"(@w{i},@n{i})");
                cmd.Parameters.AddWithValue($"@w{i}", entry.Word);
                cmd.Parameters.AddWithValue($"@n{i}", entry.TextNodeId);
            }

            // OR IGNORE, because (word, line) is now a primary key rather than
            // two ordinary columns. TokenizeLine already takes Distinct() words
            // per line so a collision should not arise - but ReindexEditionAsync
            // can be called twice over an edition whose delete did not remove
            // everything, and a duplicate there should cost nothing rather than
            // abort the write and lose the batch.
            cmd.CommandText =
                $"INSERT OR IGNORE INTO WordIndex (NormalizedWord, TextNodeId) VALUES {string.Join(",", valueRows)};";
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
    /// automatic refresh hook: nothing re-runs it when a new Setup source
    /// finishes ingesting, so a Renaissance or First1KGreek pass added
    /// after the last build leaves every line it contributed silently
    /// unindexed. SetupWizardForm compares this against GetTextNodeCountAsync
    /// to say so, rather than leaving that gap to be discovered obliquely
    /// through a search that should have found something and didn't.
    ///
    /// A small, fixed gap even immediately after a full rebuild is expected
    /// and does not mean staleness: WordIndexService.BuildAsync records a
    /// placeholder for any line with no indexable words at all (see
    /// NoIndexableWordsMarker there), specifically so this count still
    /// includes it. If that placeholder is ever removed, this count would
    /// permanently undercount by however many such lines the corpus has,
    /// and every fresh build would look stale forever.
    /// </summary>
    public async Task<long> GetIndexedTextNodeCountAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(DISTINCT TextNodeId) FROM WordIndex;";
        cmd.CommandTimeout = 300;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>Every current TextNode for one edition, id + text - what ReindexEditionAsync tokenizes.</summary>
    public async Task<List<(long TextNodeId, string Text)>> GetTextNodesByEditionAsync(
        int editionId, CancellationToken cancellationToken = default)
    {
        var results = new List<(long, string)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = "SELECT TextNodeId, Text FROM TextNodes WHERE EditionId = @EditionId;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 60;
        cmd.Parameters.AddWithValue("@EditionId", editionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        return results;
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
}
