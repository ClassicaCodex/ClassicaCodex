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
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        for (var offset = 0; offset < lemmas.Count; offset += rowsPerStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Indexed rather than Skip().Take(): Skip() on an IReadOnlyList
            // restarts from element zero on every batch, making the loop
            // quadratic in the row count.
            var batchSize = Math.Min(rowsPerStatement, lemmas.Count - offset);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)transaction;

            var valueRows = new List<string>(batchSize);
            for (var i = 0; i < batchSize; i++)
            {
                var l = lemmas[offset + i];
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
    /// Falls back to guessing the corpus from the word's script when the
    /// caller doesn't know it. Reliable only for Greek, which has its own
    /// alphabet - English and Latin share one, so callers that can tell
    /// them apart should say so rather than rely on this.
    /// </summary>
    private static string DetectLanguage(string word)
    {
        foreach (var c in word)
        {
            // Greek and Coptic, plus Greek Extended (the polytonic
            // accented forms that most of this corpus actually uses).
            if ((c >= '\u0370' && c <= '\u03FF') || (c >= '\u1F00' && c <= '\u1FFF'))
            {
                return "grc";
            }
        }

        return "lat";
    }

    /// <summary>
    /// All headwords a given inflected form could derive from. Usually one;
    /// sometimes several, which is genuine ambiguity worth showing the user
    /// rather than silently picking a winner.
    /// </summary>
    public async Task<List<(string Headword, string? PartOfSpeech)>> GetHeadwordsForFormAsync(
        string form, string? language = null, CancellationToken cancellationToken = default)
    {
        var results = new List<(string, string?)>();
        var normalized = WordNormalizer.Normalize(form);
        if (normalized.Length == 0) return results;

        var effectiveLanguage = language ?? DetectLanguage(normalized);

        // The two kinds of lemma data need different lookups.
        //
        // Greek and Latin ship a row per attested form, so the word as
        // written is looked up directly. English ships base forms plus an
        // exception list for irregulars, leaving regular endings to be
        // stripped by rule - so "speaks" needs "speak" tried as well.
        // EnglishLemmatizer supplies those candidates, ordered so the word
        // itself is tried first and an exact match wins.
        var formsToTry = effectiveLanguage == "eng"
            ? EnglishLemmatizer.CandidateLemmas(normalized)
            : new[] { normalized };

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // Filtering by language matters twice over.
        //
        // Correctness: the lemma corpora are independent, and the Latin one
        // tags Greek quotations as foreign words. Without this, looking up
        // a Greek word returned those alongside the real Greek entries -
        // which is where junk like a "Greek" headword tagged FOR, or a
        // transliterated "KAI", was coming from. English and Latin also
        // share an alphabet, so nothing but the language column separates
        // them at all.
        //
        // Speed: IX_Lemmas_NormalizedForm is on (Language, NormalizedForm).
        // A query that filters only on NormalizedForm can't use an index
        // whose leading column is missing, so this lookup was scanning the
        // whole Lemmas table - millions of rows - on every word clicked.
        const string sql = @"
            SELECT DISTINCT Headword, PartOfSpeech
            FROM Lemmas
            WHERE Language = @Language AND NormalizedForm = @NormalizedForm
            ORDER BY Headword;";

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in formsToTry)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@Language", effectiveLanguage);
            cmd.Parameters.AddWithValue("@NormalizedForm", candidate);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var headword = reader.GetString(0);
                var pos = reader.IsDBNull(1) ? null : reader.GetString(1);

                // Several candidates can reduce to the same entry - "cities"
                // yields both "city" and "citie", and a word that is already
                // a base form matches itself as well as any stripped guess.
                if (seen.Add($"{headword}\u0001{pos}")) results.Add((headword, pos));
            }

            // An exact hit on the word as written is the answer; only keep
            // stripping when nothing has matched yet, so "saw" the noun
            // isn't buried under speculative verb stems.
            if (results.Count > 0) break;
        }

        return results;
    }

    /// <summary>
    /// Every attested inflected form of a headword - this is what turns a
    /// search for one word into a search for the whole paradigm.
    /// </summary>
    public async Task<List<string>> GetFormsForHeadwordAsync(
        string headword, string? language = null, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT DISTINCT Form
            FROM Lemmas
            WHERE Language = @Language AND Headword = @Headword
            ORDER BY Form;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Language", language ?? DetectLanguage(headword));
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

        // One self-join rather than "look up the headwords, then run another
        // query per headword". This runs on every lemma-aware search, and a
        // form with several candidate headwords - which is common, since
        // genuine ambiguity is exactly why the lemma tables are many-to-many
        // - previously cost one connection and one round-trip each.
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT related.Form
            FROM Lemmas source
            JOIN Lemmas related
              ON related.Headword = source.Headword
             AND related.Language = source.Language
            WHERE source.NormalizedForm = @NormalizedForm;";
        cmd.Parameters.AddWithValue("@NormalizedForm", WordNormalizer.Normalize(form));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            forms.Add(reader.GetString(0));
        }

        return forms.ToList();
    }

    /// <summary>
    /// Headwords for many forms at once, as form -> candidate headwords.
    ///
    /// The per-form method above is a query each, which is fine for a word
    /// someone clicked and hopeless for the several thousand distinct forms
    /// in a single work. Chunked because SQLite has a parameter ceiling and
    /// a work can carry more distinct forms than one IN clause will hold.
    ///
    /// Forms with no lemma data are absent from the result rather than
    /// present with an empty list - the caller has to distinguish "no
    /// headword known" from "headword known to be nothing", and an absent
    /// key says the first without inventing the second.
    /// </summary>
    public async Task<Dictionary<string, List<string>>> GetHeadwordsForFormsAsync(
        IReadOnlyCollection<string> normalizedForms, string language,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (normalizedForms.Count == 0) return result;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const int chunkSize = 400;
        var forms = normalizedForms.Distinct(StringComparer.Ordinal).ToList();

        for (var offset = 0; offset < forms.Count; offset += chunkSize)
        {
            var chunk = forms.Skip(offset).Take(chunkSize).ToList();

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;

            var names = new List<string>(chunk.Count);
            for (var i = 0; i < chunk.Count; i++)
            {
                names.Add($"@f{i}");
                cmd.Parameters.AddWithValue($"@f{i}", chunk[i]);
            }

            cmd.Parameters.AddWithValue("@Language", language);
            cmd.CommandText =
                $@"SELECT DISTINCT NormalizedForm, Headword
                   FROM Lemmas
                   WHERE Language = @Language
                     AND NormalizedForm IN ({string.Join(",", names)});";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var form = reader.GetString(0);
                var headword = reader.GetString(1);

                if (!result.TryGetValue(form, out var headwords))
                {
                    headwords = new List<string>();
                    result[form] = headwords;
                }

                headwords.Add(headword);
            }
        }

        return result;
    }

    /// <summary>
    /// Finds passages containing word forms whose morphological tag matches
    /// a positional pattern - "every aorist optative", "every genitive
    /// plural". Goes Lemmas -> WordIndex -> TextNodes, the same route the
    /// main lemma-aware search takes, so it inherits the same speed.
    ///
    /// GLOB rather than LIKE: '?' matches exactly one character so tag
    /// positions stay aligned, and GLOB is case-sensitive, so a lowercase
    /// Greek pattern can't match an uppercase Latin tag that happens to be
    /// the same length. See MorphologyDecoder.BuildGlobPattern.
    /// </summary>
    public async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string MatchedForm, string Headword, string Tag)>>
        SearchByMorphologyAsync(string globPattern9, string globPattern10, string language, int maxResults = 2000, IReadOnlyCollection<int>? workIds = null, CancellationToken cancellationToken = default)
    {
        var results = new List<(int, long, string, string, string, string, string, string, string)>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;

        var scope = workIds == null || workIds.Count == 0 ? null : workIds.Distinct().ToList();

        // Matching both patterns because the corpus carries both tag
        // layouts in bulk - see MorphologyDecoder.BuildGlobPatterns.
        // DISTINCT on the inner select: a line containing several forms that
        // all match would otherwise repeat, and the join to Lemmas can also
        // multiply rows when one form has several lemma candidates.
        //
        // The work scope goes in the WHERE rather than being filtered after
        // the fact, because LIMIT applies before the caller ever sees a row.
        // A corpus-wide search truncates at maxResults in author order, so
        // filtering afterwards would leave a search scoped to one late-
        // alphabet author returning nothing at all while reporting a full
        // result set.
        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text,
                   m.Form, m.Headword, m.PartOfSpeech
            FROM (
                SELECT DISTINCT l.NormalizedForm, l.Form, l.Headword, l.PartOfSpeech
                FROM Lemmas l
                WHERE l.Language = @Language
                  AND l.PartOfSpeech IS NOT NULL
                  AND (l.PartOfSpeech GLOB @Pattern9 OR l.PartOfSpeech GLOB @Pattern10)
            ) m
            JOIN WordIndex wi ON wi.NormalizedWord = m.NormalizedForm
            JOIN TextNodes tn ON wi.TextNodeId = tn.TextNodeId
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            {WorkScope.Clause(cmd, scope, "WHERE")}
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @MaxResults;";

        cmd.Parameters.AddWithValue("@Pattern9", globPattern9);
        cmd.Parameters.AddWithValue("@Pattern10", globPattern10);
        cmd.Parameters.AddWithValue("@Language", language);
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.IsDBNull(8) ? string.Empty : reader.GetString(8)));
        }

        return results;
    }

    /// <summary>
    /// How many distinct forms carry a decodable morphological tag, per
    /// language. The morphology features are only meaningful if the loaded
    /// lemma data actually carries tags - this is what lets the UI say so
    /// plainly instead of just returning nothing and looking broken.
    /// </summary>
    public async Task<int> CountTaggedFormsAsync(string language, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(DISTINCT NormalizedForm)
            FROM Lemmas
            WHERE Language = @Language AND PartOfSpeech IS NOT NULL;";
        cmd.Parameters.AddWithValue("@Language", language);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }
}
