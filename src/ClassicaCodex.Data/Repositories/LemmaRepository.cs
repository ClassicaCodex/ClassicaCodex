using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class LemmaRepository
{
    /// <summary>
    /// Inserts lemma mappings. A full Greek lemma set is on the order of a
    /// million rows - batched into multi-row INSERT statements rather than
    /// one row per statement, since at this scale per-statement overhead
    /// (not just the lack of a transaction) is what actually costs time. See
    /// WordIndexRepository's remarks for the full reasoning; 300 rows per
    /// statement here (vs. 400 for WordIndex's 2 columns) keeps parameter
    /// count comfortably under SQLite's limit with 5 columns per row.
    /// </summary>
    public async Task BulkInsertAsync(IReadOnlyList<Lemma> lemmas, CancellationToken cancellationToken = default)
    {
        if (lemmas.Count == 0) return;

        const int rowsPerStatement = 300;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = conn.BeginTransaction();

        for (var offset = 0; offset < lemmas.Count; offset += rowsPerStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = lemmas.Skip(offset).Take(rowsPerStatement).ToList();

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;

            var valueRows = new List<string>(batch.Count);
            for (var i = 0; i < batch.Count; i++)
            {
                var l = batch[i];
                valueRows.Add($"(@f{i},@nf{i},@h{i},@l{i},@p{i})");
                cmd.Parameters.AddWithValue($"@f{i}", l.Form);
                cmd.Parameters.AddWithValue($"@nf{i}", l.NormalizedForm);
                cmd.Parameters.AddWithValue($"@h{i}", l.Headword);
                cmd.Parameters.AddWithValue($"@l{i}", l.Language);
                cmd.Parameters.AddWithValue($"@p{i}", (object?)l.PartOfSpeech ?? DBNull.Value);
            }

            cmd.CommandText =
                $"INSERT INTO Lemmas (Form, NormalizedForm, Headword, Language, PartOfSpeech) VALUES {string.Join(",", valueRows)};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Lemmas;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// Same as CountAsync, filtered to one language - lets the Setup Wizard
    /// tell "Greek Lemma Data" and "Latin Lemma Data" apart, since a single
    /// combined count can't distinguish which of the two has actually run.
    /// </summary>
    public async Task<int> CountByLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Lemmas WHERE Language = @Language;";
        cmd.Parameters.AddWithValue("@Language", language);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>Clears all lemma data, ahead of a re-ingest.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Lemmas;";
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// All headwords a given inflected form could derive from. Usually one;
    /// sometimes several, which is genuine ambiguity worth showing the user
    /// rather than silently picking a winner.
    /// </summary>
    public async Task<List<(string Headword, string? PartOfSpeech)>> GetHeadwordsForFormAsync(
        string form, CancellationToken cancellationToken = default)
    {
        var results = new List<(string, string?)>();
        var normalized = WordNormalizer.Normalize(form);
        if (normalized.Length == 0) return results;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT DISTINCT Headword, PartOfSpeech
            FROM Lemmas
            WHERE NormalizedForm = @NormalizedForm
            ORDER BY Headword;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@NormalizedForm", normalized);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return results;
    }

    /// <summary>
    /// Every attested inflected form of a headword - this is what turns a
    /// search for one word into a search for the whole paradigm.
    /// </summary>
    public async Task<List<string>> GetFormsForHeadwordAsync(
        string headword, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT DISTINCT Form
            FROM Lemmas
            WHERE Headword = @Headword
            ORDER BY Form;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Headword", headword);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    /// <summary>
    /// Given any inflected form, returns every form sharing any of its
    /// headwords - i.e. the full set of strings worth searching for if you
    /// want all occurrences of that word regardless of inflection. Includes
    /// the original form even if the lemma data doesn't recognize it, so a
    /// lookup miss degrades to a plain single-form search rather than to
    /// nothing at all.
    /// </summary>
    public async Task<List<string>> ExpandFormAsync(string form, CancellationToken cancellationToken = default)
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { form };

        var headwords = await GetHeadwordsForFormAsync(form, cancellationToken);
        foreach (var (headword, _) in headwords)
        {
            foreach (var related in await GetFormsForHeadwordAsync(headword, cancellationToken))
            {
                forms.Add(related);
            }
        }

        return forms.ToList();
    }
}
