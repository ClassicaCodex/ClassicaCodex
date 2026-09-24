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
    /// Clears one language's mappings, leaving the others alone.
    ///
    /// A lemma step re-run has to be able to replace what it loaded last time,
    /// and the whole-table clear cannot be what does it: the Greek, Latin and
    /// English mappings are separate multi-minute downloads, and re-running one
    /// of them must not throw away the other two.
    /// </summary>
    public async Task<int> ClearByLanguageAsync(
        string language, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Lemmas WHERE Language = @Language;";
        cmd.Parameters.AddWithValue("@Language", language);
        cmd.CommandTimeout = 300;
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
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

        await PutAnswerableHeadwordsFirstAsync(conn, results, effectiveLanguage, cancellationToken);
        PutTheWordAsWrittenFirst(results, form);
        return results;
    }

    /// <summary>
    /// Puts the headword that is spelled like the word the reader clicked
    /// ahead of one that only matches once the accents are taken off.
    ///
    /// The lookup is deliberately accent-blind, because that is what makes it
    /// find anything at all; the ordering was accent-blind too, and that is a
    /// different matter. Rows came back ordered by headword, which is binary
    /// order, so a capital sorts before a lower-case letter and a rare
    /// homograph can sit above the word actually on the page. Whatever led
    /// the list was then selected for the reader and its dictionary entry
    /// shown, so the wrong lead was not a nuisance - it was the answer.
    ///
    /// What that looked like: right-click the second word of the Iliad and
    /// the app said θεά was θέα, "seeing, looking at", because θ-έ sorts
    /// above θ-ε. esse led with "Es", a magistrate who superintended
    /// religious exhibitions; bello with "Bellius"; regem with "Rex"; πόλιν
    /// with the place called Πόλις. Nine of thirty sampled forms led with
    /// something the reader had not clicked on.
    ///
    /// Two rules, applied weakest first so that the strongest ends up on top:
    /// a headword that is not capitalised when the word is not capitalised
    /// beats one that is, which settles the proper nouns and the
    /// sentence-initial artefacts; and an exact match beats everything,
    /// which settles θεά. Both are stable, so everything already decided -
    /// the dictionary-answerable promotion above, and alphabetical order
    /// under that - survives wherever these two have nothing to say.
    /// </summary>
    private static void PutTheWordAsWrittenFirst(
        List<(string Headword, string? PartOfSpeech)> results, string form)
    {
        if (results.Count < 2) return;

        var asWritten = form.Trim();
        if (asWritten.Length == 0) return;

        var wordIsLowercase = !char.IsUpper(asWritten[0]);

        var ordered = results
            .OrderByDescending(r => SharedOpening(r.Headword, asWritten))
            .ToList();

        ordered = ordered
            .OrderByDescending(r => wordIsLowercase
                                    && r.Headword.Length > 0
                                    && !char.IsUpper(r.Headword[0]))
            .ToList();

        ordered = ordered
            .OrderByDescending(r => string.Equals(r.Headword, asWritten, StringComparison.Ordinal))
            .ToList();

        results.Clear();
        results.AddRange(ordered);
    }

    /// <summary>
    /// How many characters a headword and the clicked word begin with in
    /// common - the weakest of the three orderings, and the one that settles
    /// a pair where neither is capitalised and neither is an exact match.
    ///
    /// ἔχει is the case it exists for. Its two candidates are ἔχω and χέω,
    /// both lower case, neither spelled like the form; ordered by headword
    /// the pouring one came first, so the commonest verb in Greek reported
    /// itself as "χέω, diffuse completely". They share ἔχ and nothing
    /// respectively, which decides it.
    ///
    /// Deliberately crude. It compares characters as written, so an accent
    /// difference ends the run - which is wanted, since a headword keeping
    /// the accents of the form is the better match. It never overrides a
    /// capitalisation or an exact-spelling decision, and where two
    /// candidates share an opening of the same length it changes nothing.
    /// </summary>
    private static int SharedOpening(string headword, string form)
    {
        var shared = 0;
        while (shared < headword.Length && shared < form.Length && headword[shared] == form[shared]) shared++;
        return shared;
    }

    /// <summary>
    /// Sorts headwords the dictionary can actually answer for to the top,
    /// keeping the alphabetical order within each group.
    ///
    /// Word Study selects the first headword automatically, so whichever one
    /// leads is the one a reader is shown. Alphabetical order put the wrong
    /// one there almost every time: the Latin lemma data carries capitalised
    /// headwords that nothing in Lewis and Short answers to - only 43,507 of
    /// its 139,190 headwords have a dictionary entry behind them at all - and
    /// a capital sorts before a lowercase letter. So "regere" led with Reger
    /// over rego, "amare" with Amar over amo, "ferre" with Ferres over fero.
    ///
    /// The reader clicked a word and was shown "no dictionary entry found",
    /// with the real entry sitting unnoticed one line below. On the feature
    /// this application is most for.
    ///
    /// Normalising through WordNormalizer rather than in SQL is deliberate:
    /// it is the same call the definition lookup itself makes - trailing
    /// digits trimmed, and v/j folded to u/i for Latin - so the two cannot
    /// drift apart and quietly start disagreeing about what has an entry.
    /// </summary>
    private static async Task PutAnswerableHeadwordsFirstAsync(
        SqliteConnection conn,
        List<(string Headword, string? PartOfSpeech)> results,
        string language,
        CancellationToken cancellationToken)
    {
        // One headword cannot be in the wrong order, and no headwords cannot
        // be either. Both are the common case.
        if (results.Count < 2) return;

        var normalizedByHeadword = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (headword, _) in results)
        {
            if (!normalizedByHeadword.ContainsKey(headword))
                normalizedByHeadword[headword] = WordNormalizer.NormalizeHeadword(headword, language);
        }

        var answerable = new HashSet<string>(StringComparer.Ordinal);
        var distinct = normalizedByHeadword.Values.Where(v => v.Length > 0).Distinct().ToList();
        if (distinct.Count == 0) return;

        await using (var cmd = conn.CreateCommand())
        {
            var names = new List<string>(distinct.Count);
            for (var i = 0; i < distinct.Count; i++)
            {
                names.Add($"@h{i}");
                cmd.Parameters.AddWithValue($"@h{i}", distinct[i]);
            }

            cmd.CommandText =
                "SELECT DISTINCT NormalizedHeadword FROM Definitions " +
                $"WHERE Language = @Language AND NormalizedHeadword IN ({string.Join(",", names)});";
            cmd.Parameters.AddWithValue("@Language", language);
            cmd.CommandTimeout = 30;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) answerable.Add(reader.GetString(0));
        }

        // Nothing to promote, and nothing to demote - leave the order alone
        // rather than reshuffling for no reason.
        if (answerable.Count == 0 || answerable.Count == distinct.Count) return;

        var ordered = results
            .OrderByDescending(r => answerable.Contains(normalizedByHeadword[r.Headword]))
            .ToList();

        results.Clear();
        results.AddRange(ordered);
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
    public async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string MatchedForm, string Headword, string Tag, string? Milestone)>>
        SearchByMorphologyAsync(string globPattern9, string globPattern10, string language, int maxResults = 2000, IReadOnlyCollection<int>? workIds = null, CancellationToken cancellationToken = default)
    {
        var results = new List<(int, long, string, string, string, string, string, string, string, string?)>();

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
        // A corpus-wide search truncates at maxResults, so filtering
        // afterwards would leave a search scoped to one work returning
        // nothing at all while reporting a full result set.
        //
        // Ordered after the LIMIT rather than in SQL. Sorting in the database
        // means sorting everything the join produces before the LIMIT can
        // discard it, and for a broad selection that is not a page of rows:
        // "every Greek verb" matches 114,458 distinct forms, which fan out
        // through WordIndex to six million joined rows, all of which SQLite
        // put through a temp b-tree to hand back two thousand. Measured on a
        // full library: 67,061 ms with the ORDER BY, 29 ms without it.
        //
        // For every search that does not reach maxResults - which is every
        // scoped search in practice - the whole result set comes back and
        // sorting it here gives byte-identical output to sorting it there.
        // Only a search that truncates differs, and a truncated search was
        // already returning an arbitrary slice: with the ORDER BY it was the
        // alphabet's first two authors and nothing else in the corpus.
        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text,
                   m.Form, m.Headword, m.PartOfSpeech, tn.Milestone, tn.SortOrder
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
            LIMIT @MaxResults;";

        cmd.Parameters.AddWithValue("@Pattern9", globPattern9);
        cmd.Parameters.AddWithValue("@Pattern10", globPattern10);
        cmd.Parameters.AddWithValue("@Language", language);
        cmd.Parameters.AddWithValue("@MaxResults", maxResults);

        // SortOrder is read for the ordering below and then dropped: it is how
        // a work sequences its own lines, which is what the third sort key
        // needs, and it is of no use to the caller.
        var ordered = new List<(int SortOrder, (int, long, string, string, string, string, string, string, string, string?) Row)>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ordered.Add((
                reader.GetInt32(10),
                (reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                 reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                 reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                 reader.IsDBNull(9) ? null : reader.GetString(9))));
        }

        results.AddRange(ordered
            .OrderBy(x => x.Row.Item3, StringComparer.Ordinal)
            .ThenBy(x => x.Row.Item4, StringComparer.Ordinal)
            .ThenBy(x => x.SortOrder)
            .Select(x => x.Row));

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
