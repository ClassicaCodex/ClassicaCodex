using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class DefinitionRepository
{
    /// <summary>
    /// Inserts dictionary entries, and the citations they carry (see
    /// <see cref="Definition.Citations"/>). Same multi-row-per-statement
    /// batching as LemmaRepository and WordIndexRepository, for the same
    /// reason - see WordIndexRepository's remarks.
    ///
    /// The ids are given here rather than left to SQLite, read as one past the
    /// highest inside the transaction: a citation has to name the entry it is
    /// in, and a multi-row INSERT does not say which id each of its rows was
    /// given. Each definition's <see cref="Definition.DefinitionId"/> is set.
    /// </summary>
    public async Task BulkInsertAsync(IReadOnlyList<Definition> definitions, CancellationToken cancellationToken = default)
    {
        if (definitions.Count == 0) return;

        const int rowsPerStatement = 300;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        long nextId;
        await using (var max = conn.CreateCommand())
        {
            max.Transaction = (SqliteTransaction)transaction;
            max.CommandText = "SELECT COALESCE(MAX(DefinitionId), 0) FROM Definitions;";
            nextId = Convert.ToInt64(await max.ExecuteScalarAsync(cancellationToken)) + 1;
        }

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
                d.DefinitionId = nextId++;
                valueRows.Add($"(@id{i},@h{i},@nh{i},@l{i},@e{i},@s{i})");
                cmd.Parameters.AddWithValue($"@id{i}", d.DefinitionId);
                cmd.Parameters.AddWithValue($"@h{i}", d.Headword);
                cmd.Parameters.AddWithValue($"@nh{i}", d.NormalizedHeadword);
                cmd.Parameters.AddWithValue($"@l{i}", d.Language);
                cmd.Parameters.AddWithValue($"@e{i}", d.Entry);
                cmd.Parameters.AddWithValue($"@s{i}", (object?)d.Source ?? DBNull.Value);
            }

            cmd.CommandText =
                $"INSERT INTO Definitions (DefinitionId, Headword, NormalizedHeadword, Language, Entry, Source) VALUES {string.Join(",", valueRows)};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertCitationsAsync(conn, (SqliteTransaction)transaction, definitions, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertCitationsAsync(
        SqliteConnection conn, SqliteTransaction transaction, IReadOnlyList<Definition> definitions, CancellationToken cancellationToken)
    {
        const int rowsPerStatement = 300;

        var rows = definitions.SelectMany(d => d.Citations.Select(c => (d.DefinitionId, Citation: c))).ToList();
        for (var offset = 0; offset < rows.Count; offset += rowsPerStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchSize = Math.Min(rowsPerStatement, rows.Count - offset);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;

            var valueRows = new List<string>(batchSize);
            for (var i = 0; i < batchSize; i++)
            {
                var (definitionId, c) = rows[offset + i];
                valueRows.Add($"(@d{i},@o{i},@w{i},@e{i},@p{i},@s{i})");
                cmd.Parameters.AddWithValue($"@d{i}", definitionId);
                cmd.Parameters.AddWithValue($"@o{i}", c.Ordinal);
                cmd.Parameters.AddWithValue($"@w{i}", c.Target.WorkUrn);
                cmd.Parameters.AddWithValue($"@e{i}", (object?)c.Target.EditionUrn ?? DBNull.Value);
                cmd.Parameters.AddWithValue($"@p{i}", c.Target.PassageRef);
                cmd.Parameters.AddWithValue($"@s{i}", (object?)c.Sense ?? DBNull.Value);
            }

            cmd.CommandText =
                $"INSERT INTO LexiconCitations (DefinitionId, Ordinal, WorkUrn, EditionUrn, Passage, Sense) VALUES {string.Join(",", valueRows)};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
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

    /// <summary>
    /// Removes one dictionary's entries and no other's, so a collection that
    /// brings its own glossary can replace it on a re-run without touching
    /// LSJ or Lewis and Short.
    /// </summary>
    public async Task<int> ClearBySourceAsync(string source, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "DELETE FROM LexiconCitations WHERE DefinitionId IN (SELECT DefinitionId FROM Definitions WHERE Source = @Source);";
        cmd.Parameters.AddWithValue("@Source", source);
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        cmd.CommandText = "DELETE FROM Definitions WHERE Source = @Source;";
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM LexiconCitations; DELETE FROM Definitions;";
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Clears one language's entries, leaving the others alone - the same
    /// reason as LemmaRepository.ClearByLanguageAsync. A collection that brings
    /// its own dictionary has to be able to replace it on a re-run without
    /// taking LSJ and Lewis &amp; Short with it.
    /// </summary>
    public async Task<int> ClearByLanguageAsync(
        string language, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "DELETE FROM LexiconCitations WHERE DefinitionId IN (SELECT DefinitionId FROM Definitions WHERE Language = @Language);";
        cmd.Parameters.AddWithValue("@Language", language);
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        cmd.CommandText = "DELETE FROM Definitions WHERE Language = @Language;";
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
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

        // Language is part of the question, not just of the normalizing. It
        // was missing from this WHERE clause, and 2,999 normalized headwords
        // in this library belong to more than one language - so asking for the
        // Latin "Batrachomyomachia" returned its two Lewis & Short entries and
        // then WordNet's English one underneath them, in a dictionary pane
        // that had been told which language it was reading.
        //
        // It is also the leading column of IX_Definitions_Normalized, which
        // makes this the difference between seeking the index and reading all
        // 423,551 definitions.
        //
        // The headword as asked for comes first. Normalising strips the vowel
        // points, so in Hebrew שָׂטָן "adversary" and שָׂטַן "to accuse" share
        // one key and arrive together - rightly, since a reader may want both -
        // but sorted by spelling alone the verb came first under a word whose
        // lemma data had already said it was the noun.
        //
        // LIMIT goes at the end in SQLite, not TOP (N) at the start.
        const string sql = @"
            SELECT Headword, Entry, Source
            FROM Definitions
            WHERE Language = @Language AND NormalizedHeadword = @Normalized
            ORDER BY Headword = @Headword DESC, Headword
            LIMIT 10;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 60;
        cmd.Parameters.AddWithValue("@Language", language);
        cmd.Parameters.AddWithValue("@Normalized", normalized);
        cmd.Parameters.AddWithValue("@Headword", headword);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return results;
    }
}
