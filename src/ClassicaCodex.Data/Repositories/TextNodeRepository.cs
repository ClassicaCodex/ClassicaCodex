using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class TextNodeRepository
{
    /// <summary>
    /// Inserts text nodes for one edition. Given "ingest everything" scope,
    /// this runs for every edition in every work - potentially low millions
    /// of rows across a full corpus - so it batches multiple rows into each
    /// INSERT statement rather than one row per statement. See
    /// WordIndexRepository's remarks for why that matters more than just
    /// wrapping everything in a transaction. 200 rows per statement here
    /// (smaller than the other tables) since a line's Text value can
    /// occasionally run long, and this keeps each statement's total size
    /// reasonable regardless.
    /// </summary>
    public async Task BulkInsertAsync(IReadOnlyList<TextNode> nodes, CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0) return;

        const int rowsPerStatement = 200;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = conn.BeginTransaction();

        for (var offset = 0; offset < nodes.Count; offset += rowsPerStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = nodes.Skip(offset).Take(rowsPerStatement).ToList();

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;

            var valueRows = new List<string>(batch.Count);
            for (var i = 0; i < batch.Count; i++)
            {
                var node = batch[i];
                valueRows.Add($"(@e{i},@c{i},@s{i},@t{i})");
                cmd.Parameters.AddWithValue($"@e{i}", node.EditionId);
                cmd.Parameters.AddWithValue($"@c{i}", node.CitationRef);
                cmd.Parameters.AddWithValue($"@s{i}", node.SortOrder);
                cmd.Parameters.AddWithValue($"@t{i}", node.Text);
            }

            cmd.CommandText =
                $"INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text) VALUES {string.Join(",", valueRows)};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<List<TextNode>> GetByEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        var results = new List<TextNode>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"SELECT TextNodeId, EditionId, CitationRef, SortOrder, Text
                             FROM TextNodes WHERE EditionId = @EditionId ORDER BY SortOrder;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@EditionId", editionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TextNode
            {
                TextNodeId = reader.GetInt64(0),
                EditionId = reader.GetInt32(1),
                CitationRef = reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                Text = reader.GetString(4)
            });
        }

        return results;
    }

    /// <summary>
    /// Plain substring search across every line. SQL Server's version of
    /// this tried Full-Text Search first for word-stem matching - that's a
    /// SQL-Server-only feature with no SQLite equivalent worth building
    /// (FTS5 would mostly replicate light English stemming that nothing
    /// else in the app depends on; the lemma system + WordIndex already
    /// carry the real Greek/Latin search workload). Plain LIKE is the whole
    /// path now.
    /// </summary>
    public async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)>> SearchAsync(
        string query, CancellationToken cancellationToken = default)
    {
        var results = new List<(int, long, string, string, string, string)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE tn.Text LIKE @Query
            ORDER BY a.Name, w.Title, tn.SortOrder;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@Query", $"%{query}%");

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return results;
    }

    /// <summary>
    /// Finds lines containing any of the given word forms. This is the
    /// lemma-aware path: callers expand one inflected form into its whole
    /// paradigm via LemmaRepository, then pass the lot in here, so a search
    /// for λόγος also turns up λόγου, λόγῳ, λόγον and the rest.
    ///
    /// Matching is done on a normalized copy of the text (accents stripped,
    /// lowercased) because Perseus texts aren't consistent about
    /// accentuation or precomposed-vs-combining Unicode. That normalization
    /// happens in memory here rather than in SQL, so this pulls candidate
    /// rows with a LIKE prefilter and then filters them properly in C#.
    /// </summary>
    public async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)>> SearchByFormsAsync(
        IReadOnlyList<string> forms, CancellationToken cancellationToken = default)
    {
        var results = new List<(int, long, string, string, string, string)>();
        if (forms.Count == 0) return results;

        // Fast path: if the inverted word index has been built, resolve
        // everything in one joined query.
        var indexed = await TrySearchViaWordIndexAsync(forms, cancellationToken);
        if (indexed != null) return indexed;

        return await SearchByFormsWithLikeAsync(forms, cancellationToken);
    }

    /// <summary>
    /// Single-query search against the inverted index. Returns null (not an
    /// empty list) when the index hasn't been built, so the caller can tell
    /// "no index" apart from "index found nothing" and fall back.
    /// </summary>
    private async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)>?> TrySearchViaWordIndexAsync(
        IReadOnlyList<string> forms, CancellationToken cancellationToken)
    {
        var wordIndexRepo = new WordIndexRepository();
        if (!await wordIndexRepo.HasDataAsync(cancellationToken)) return null;

        var normalized = forms
            .Select(WordNormalizer.Normalize)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(200)
            .ToList();

        var results = new List<(int, long, string, string, string, string)>();
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
        cmd.Parameters.AddWithValue("@MaxResults", 5000);

        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM (
                SELECT DISTINCT TextNodeId
                FROM WordIndex
                WHERE NormalizedWord IN ({string.Join(",", paramNames)})
                LIMIT @MaxResults
            ) ids
            JOIN TextNodes tn ON ids.TextNodeId = tn.TextNodeId
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            ORDER BY a.Name, w.Title, tn.SortOrder;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }

        return results;
    }

    private async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)>> SearchByFormsWithLikeAsync(
        IReadOnlyList<string> forms, CancellationToken cancellationToken)
    {
        var results = new List<(int, long, string, string, string, string)>();

        // Cap the SQL side - a big paradigm can run to hundreds of forms and
        // there's no point building a huge OR. The rest get caught by the
        // in-memory pass over whatever comes back.
        var sqlForms = forms.Take(60).ToList();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        var clauses = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 180;
        for (var i = 0; i < sqlForms.Count; i++)
        {
            clauses.Add($"tn.Text LIKE @f{i}");
            cmd.Parameters.AddWithValue($"@f{i}", $"%{sqlForms[i]}%");
        }

        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE {string.Join(" OR ", clauses)}
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT 5000;";

        var normalizedTargets = new HashSet<string>(
            forms.Select(WordNormalizer.Normalize).Where(f => f.Length > 0), StringComparer.Ordinal);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var text = reader.GetString(5);

            // Confirm a real whole-word hit rather than an accidental
            // substring - LIKE '%λογ%' would otherwise match half the corpus.
            var isRealMatch = text
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(WordNormalizer.Normalize)
                .Any(w => w.Length > 0 && normalizedTargets.Contains(w));

            if (!isRealMatch) continue;

            results.Add((
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), text));
        }

        return results;
    }

    /// <summary>Author/work/citation context for a single text node - used by the reception tracker.</summary>
    /// <summary>
    /// N consecutive lines within one edition, starting at a given
    /// TextNodeId's position. What the export dialog uses to grab a whole
    /// passage rather than just the single line that was right-clicked.
    /// </summary>
    public async Task<List<TextNode>> GetRangeAsync(
        int editionId, long startTextNodeId, int lineCount, CancellationToken cancellationToken = default)
    {
        var results = new List<TextNode>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT TextNodeId, EditionId, CitationRef, SortOrder, Text
            FROM TextNodes
            WHERE EditionId = @EditionId
              AND SortOrder >= (SELECT SortOrder FROM TextNodes WHERE TextNodeId = @StartId)
            ORDER BY SortOrder
            LIMIT @LineCount;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.Parameters.AddWithValue("@StartId", startTextNodeId);
        cmd.Parameters.AddWithValue("@LineCount", lineCount);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TextNode
            {
                TextNodeId = reader.GetInt64(0),
                EditionId = reader.GetInt32(1),
                CitationRef = reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                Text = reader.GetString(4)
            });
        }

        return results;
    }

    /// <summary>
    /// Which edition a line belongs to. Used when jumping to a specific
    /// line whose edition might not be the one currently showing - a work
    /// with more than one translation only shows one at a time, so a jump
    /// target can land in a translation that isn't the active selection.
    /// </summary>
    public async Task<int?> GetEditionIdAsync(long textNodeId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EditionId FROM TextNodes WHERE TextNodeId = @TextNodeId;";
        cmd.Parameters.AddWithValue("@TextNodeId", textNodeId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    public async Task<(string AuthorName, string WorkTitle, string CitationRef, string Text)?> GetTextNodeSourceInfoAsync(
        long textNodeId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT a.Name, w.Title, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE tn.TextNodeId = @TextNodeId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@TextNodeId", textNodeId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    // \p{L} matches a letter in any script, not just ASCII. The original
    // [a-zA-Z] silently made this whole feature English-only: Greek text
    // contains no ASCII letters at all, so no candidate words were ever
    // extracted and echo finding returned nothing for every Greek passage.
    private static readonly System.Text.RegularExpressions.Regex WordPattern =
        new(@"\p{L}{4,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    // A deliberately blunt stopword list - not linguistically rigorous, just
    // enough to filter out words too common to signal anything (rarity is
    // the whole point of this technique).
    //
    // Covers all three languages in the corpus. An English-only list left
    // Greek and Latin function words looking "rare" to the ranking, so a
    // Greek passage would have been matched on things like καί or τῶν - the
    // exact opposite of the signal this is meant to find. Greek entries are
    // listed unaccented because they're compared after normalization.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        "the","and","that","was","for","with","his","her","him","this","have","from","they","were","when","which",
        "what","said","your","then","than","them","their","would","could","should","there","here","been","being",
        "into","upon","about","after","before","through","while","where","those","these","some","such","only",
        "over","under","again","once","also","even","still","much","many","most","more","less","than","just",
        "will","shall","must","might","cannot","never","always","every","each","both","either","neither","against",
        "among","between","around","without","within","toward","upon","because","since","until","unless",
        "himself","herself","itself","themselves","yourself","ourselves",

        // Greek (unaccented - compared post-normalization)
        "και","των","τον","την","τους","τας","τοις","ταις","αυτου","αυτων","αυτον","αυτης","αυτο","αυτος",
        "ουτος","ουτως","τουτο","τουτου","τουτων","ταυτα","εστι","εστιν","ησαν","ειναι","εχων","εχει",
        "μεν","δε","γαρ","ουν","τε","αλλα","οτι","ως","εις","εκ","εν","επι","προς","παρα","περι","υπο",
        "δια","κατα","μετα","απο","ουκ","ουχ","μη","ει","αν","οι","αι","τα","το","του","της","τω","τη",
        "ουδε","μηδε","τις","τι","ποτε","ουτε","μητε","ωστε","επει","επειδη","ινα","οπως","εαν",

        // Latin
        "atque","quod","quae","quam","cum","sed","non","est","sunt","esse","erat","erant","aut","enim",
        "autem","tamen","etiam","quidem","quoque","itaque","igitur","nam","nec","neque","sive","seu",
        "ille","illa","illud","ipse","ipsa","hoc","haec","huius","eius","eorum","earum","quibus","quo",
        "qui","quia","ubi","ibi","inde","unde","ante","post","inter","apud","contra","propter","sine",
        "per","pro","sub","super","ad","ex","in","de","si","ut","ne","vel","tum","tunc","iam","modo"
    };

    /// <summary>
    /// Finds candidate intertextual echoes for a given line - other passages
    /// (anywhere in the corpus, same original/translation kind as the
    /// source) that share unusually rare words with it. This mirrors the
    /// core technique real digital-humanities intertextuality tools use
    /// (e.g. the Tesserae Project): shared rare-word overlap is a much
    /// stronger allusion signal than shared common words, since two authors
    /// independently using "and" or "king" means nothing, but both using an
    /// unusual word does.
    ///
    /// Scope note: this only really works within one language at a time -
    /// comparing an English translation's wording against another English
    /// translation's wording, or Greek/Latin original against original. It
    /// can't detect an echo between the Greek original and an English
    /// translation of a different work, since those aren't the same words.
    /// </summary>
    public async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount)>> FindEchoesAsync(
        long sourceTextNodeId, CancellationToken cancellationToken = default)
    {
        var source = await GetTextNodeContextAsync(sourceTextNodeId, cancellationToken);
        if (source == null) return new();

        // Normalized before the stopword check: accents mean the raw form
        // of a Greek function word won't match an unaccented stoplist entry,
        // which would let καί and τῶν through as if they were rare. The
        // normalized form is also what the word index stores, so this is the
        // right shape for the frequency lookup that follows.
        var candidateWords = WordPattern.Matches(source.Value.Text)
            .Select(m => WordNormalizer.Normalize(m.Value))
            .Where(w => w.Length >= 4)
            .Distinct(StringComparer.Ordinal)
            .Where(w => !StopWords.Contains(w))
            .ToList();

        if (candidateWords.Count == 0) return new();

        // Rank the source's own words by how rare they are across the whole
        // corpus, and keep only the rarest handful - those are the words
        // worth searching on. A word that appears in 40,000 lines tells you
        // nothing; a word that appears in 6 tells you something.
        var frequencies = new Dictionary<string, int>();
        foreach (var word in candidateWords)
        {
            frequencies[word] = await CountTextNodesContainingWordAsync(word, cancellationToken);
        }

        var significantWords = frequencies
            .Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Value)
            .Take(8)
            .Select(kv => kv.Key)
            .ToList();

        if (significantWords.Count == 0) return new();

        var candidates = await FindTextNodesContainingAnyWordAsync(
            significantWords, source.Value.EditionKind, sourceTextNodeId, cancellationToken);

        var significantSet = significantWords.ToHashSet(StringComparer.Ordinal);

        return candidates
            .Select(c => (
                c.WorkId, c.TextNodeId, c.AuthorName, c.WorkTitle, c.CitationRef, c.Text,
                // Compared as normalized whole words rather than raw
                // substrings. The significant words are normalized (accents
                // stripped), so a literal Contains against accented Greek
                // text would never match - and substring matching would
                // also count "war" inside "warden" as a hit.
                SharedWordCount: c.Text
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(WordNormalizer.Normalize)
                    .Where(w => w.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Count(significantSet.Contains)
            ))
            .Where(c => c.SharedWordCount > 0)
            .OrderByDescending(c => c.SharedWordCount)
            .Take(30)
            .ToList();
    }

    private async Task<(string Text, string EditionKind)?> GetTextNodeContextAsync(
        long textNodeId, CancellationToken cancellationToken)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT tn.Text, e.Kind
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            WHERE tn.TextNodeId = @TextNodeId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@TextNodeId", textNodeId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return (reader.GetString(0), reader.GetString(1));
    }

    /// <summary>
    /// Global count of distinct lines containing a word. Tries the word
    /// index first (an indexed exact-match lookup on the normalized word -
    /// fast), falling back to a LIKE scan only if the index hasn't been
    /// built yet.
    /// </summary>
    private async Task<int> CountTextNodesContainingWordAsync(string word, CancellationToken cancellationToken)
    {
        var wordIndexRepo = new WordIndexRepository();
        if (await wordIndexRepo.HasDataAsync(cancellationToken))
        {
            var normalized = WordNormalizer.Normalize(word);
            await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(DISTINCT TextNodeId) FROM WordIndex WHERE NormalizedWord = @Word;";
            cmd.Parameters.AddWithValue("@Word", normalized);
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }

        await using var likeConn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var likeCmd = likeConn.CreateCommand();
        likeCmd.CommandText = "SELECT COUNT(*) FROM TextNodes WHERE Text LIKE @Word;";
        likeCmd.Parameters.AddWithValue("@Word", $"%{word}%");
        var likeResult = await likeCmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(likeResult);
    }

    /// <summary>
    /// Candidate lines containing any of the given rare words, restricted to
    /// the same edition kind (original-vs-translation) as the source and
    /// excluding the source line itself. Tries the word index first, same
    /// fallback story as CountTextNodesContainingWordAsync. Capped at 3000
    /// raw rows so a LIKE fallback on a huge corpus can't run away -
    /// scoring/ranking happens afterward in FindEchoesAsync.
    /// </summary>
    private async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)>> FindTextNodesContainingAnyWordAsync(
        List<string> words, string editionKind, long excludeTextNodeId, CancellationToken cancellationToken)
    {
        var results = new List<(int, long, string, string, string, string)>();
        var wordIndexRepo = new WordIndexRepository();

        if (await wordIndexRepo.HasDataAsync(cancellationToken))
        {
            var normalized = words.Select(WordNormalizer.Normalize).Where(w => w.Length > 0).Distinct().ToList();
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
            cmd.Parameters.AddWithValue("@Kind", editionKind);
            cmd.Parameters.AddWithValue("@ExcludeId", excludeTextNodeId);

            cmd.CommandText = $@"
                SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
                FROM (
                    SELECT DISTINCT TextNodeId FROM WordIndex WHERE NormalizedWord IN ({string.Join(",", paramNames)})
                ) ids
                JOIN TextNodes tn ON ids.TextNodeId = tn.TextNodeId
                JOIN Editions e ON tn.EditionId = e.EditionId
                JOIN Works w ON e.WorkId = w.WorkId
                JOIN Authors a ON w.AuthorId = a.AuthorId
                WHERE e.Kind = @Kind AND tn.TextNodeId <> @ExcludeId
                LIMIT 3000;";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add((
                    reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5)));
            }

            return results;
        }

        await using var likeConn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var likeCmd = likeConn.CreateCommand();
        likeCmd.CommandTimeout = 180;

        var likeClauses = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            likeClauses.Add($"tn.Text LIKE @w{i}");
            likeCmd.Parameters.AddWithValue($"@w{i}", $"%{words[i]}%");
        }
        likeCmd.Parameters.AddWithValue("@Kind", editionKind);
        likeCmd.Parameters.AddWithValue("@ExcludeId", excludeTextNodeId);

        likeCmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE e.Kind = @Kind AND tn.TextNodeId <> @ExcludeId AND ({string.Join(" OR ", likeClauses)})
            LIMIT 3000;";

        await using var likeReader = await likeCmd.ExecuteReaderAsync(cancellationToken);
        while (await likeReader.ReadAsync(cancellationToken))
        {
            results.Add((
                likeReader.GetInt32(0), likeReader.GetInt64(1), likeReader.GetString(2),
                likeReader.GetString(3), likeReader.GetString(4), likeReader.GetString(5)));
        }

        return results;
    }
}
