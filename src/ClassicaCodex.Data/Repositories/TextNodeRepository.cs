using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class TextNodeRepository
{
    /// <summary>
    /// Resolves a saved passage after a corpus re-ingest. TextNodeId is only
    /// a fast hint; edition CTS URN plus citation is the durable address.
    /// Exact saved text breaks ties when an edition contains duplicate refs.
    /// </summary>
    public async Task<(int WorkId, long TextNodeId)?> ResolvePassageNavigationAsync(
        long textNodeIdHint, string editionCtsUrn, string citationRef, string savedText,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT w.WorkId,tn.TextNodeId FROM TextNodes tn
            JOIN Editions e ON e.EditionId=tn.EditionId JOIN Works w ON w.WorkId=e.WorkId
            WHERE e.CtsUrn=@Edition AND (tn.TextNodeId=@Hint OR tn.CitationRef=@Citation)
            ORDER BY CASE WHEN tn.TextNodeId=@Hint THEN 0 WHEN tn.Text=@Text THEN 1 ELSE 2 END,
                     tn.SortOrder,tn.TextNodeId LIMIT 1;";
        cmd.Parameters.AddWithValue("@Edition", editionCtsUrn);
        cmd.Parameters.AddWithValue("@Hint", textNodeIdHint);
        cmd.Parameters.AddWithValue("@Citation", citationRef);
        cmd.Parameters.AddWithValue("@Text", savedText);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetInt32(0), reader.GetInt64(1)) : null;
    }

    /// <summary>
    /// How many matching lines a search returns before it stops and says so.
    /// Not a correctness limit - it's there so a two-letter query against a
    /// multi-million-line corpus doesn't try to materialise the whole thing
    /// into a List and take the app down with it.
    /// </summary>
    public const int DefaultMaxResults = 5000;

    /// <summary>
    /// How many inflected forms get expanded into a single query. A large
    /// Greek paradigm can run past this; when it does the search is still
    /// correct as far as it goes, but incomplete, and SearchHits.Truncated
    /// says so rather than letting it pass for a full answer.
    /// </summary>
    private const int MaxFormsPerQuery = 200;

    /// <summary>
    /// The same cap for the no-index fallback path, which builds one LIKE
    /// clause per form rather than an IN list - far more expensive per form,
    /// hence the much lower ceiling.
    /// </summary>
    private const int MaxFormsPerLikeQuery = 60;

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
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        for (var offset = 0; offset < nodes.Count; offset += rowsPerStatement)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Indexed rather than Skip().Take() - see WordIndexRepository's
            // note; Skip() restarts from element zero on every batch.
            var batchSize = Math.Min(rowsPerStatement, nodes.Count - offset);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)transaction;

            var valueRows = new List<string>(batchSize);
            for (var i = 0; i < batchSize; i++)
            {
                var node = nodes[offset + i];
                valueRows.Add($"(@e{i},@c{i},@s{i},@t{i},@a{i},@k{i},@v{i},@m{i})");
                cmd.Parameters.AddWithValue($"@e{i}", node.EditionId);
                cmd.Parameters.AddWithValue($"@c{i}", node.CitationRef);
                cmd.Parameters.AddWithValue($"@s{i}", node.SortOrder);
                cmd.Parameters.AddWithValue($"@t{i}", node.Text);
                cmd.Parameters.AddWithValue($"@a{i}", node.IsAthetized ? 1 : 0);
                cmd.Parameters.AddWithValue($"@k{i}",
                    string.IsNullOrWhiteSpace(node.NodeKind) ? TextNodeKinds.Line : node.NodeKind);
                cmd.Parameters.AddWithValue($"@v{i}", node.IsVerse ? 1 : 0);
                cmd.Parameters.AddWithValue($"@m{i}", (object?)node.Milestone ?? DBNull.Value);
            }

            cmd.CommandText =
                $"INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text, IsAthetized, NodeKind, IsVerse, Milestone) VALUES {string.Join(",", valueRows)};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <param name="readingLinesOnly">
    /// Restricts the result to nodes the author wrote - see
    /// <see cref="TextNode.NodeKind"/>. False for anything that displays or
    /// exports an edition, since a play needs its speakers and stage
    /// directions; true for anything that counts words, since those are not
    /// the author's vocabulary.
    ///
    /// COALESCE rather than a bare comparison because a library ingested
    /// before migration 14 has the column but not the labelling: every row
    /// reads 'line', which is what those rows were being treated as anyway.
    /// </param>
    public async Task<List<TextNode>> GetByEditionAsync(
        int editionId, bool readingLinesOnly = false, CancellationToken cancellationToken = default)
    {
        var results = new List<TextNode>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        var kindFilter = readingLinesOnly
            ? "AND COALESCE(NodeKind, 'line') = 'line' "
            : string.Empty;

        var sql = @"SELECT TextNodeId, EditionId, CitationRef, SortOrder, Text,
                           COALESCE(IsAthetized, 0), COALESCE(NodeKind, 'line'),
                           COALESCE(IsVerse, 0), Milestone
                    FROM TextNodes
                    WHERE EditionId = @EditionId " + kindFilter + "ORDER BY SortOrder;";

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
                Text = reader.GetString(4),
                IsAthetized = reader.GetInt32(5) != 0,
                NodeKind = reader.GetString(6),
                IsVerse = reader.GetInt32(7) != 0,
                Milestone = reader.IsDBNull(8) ? null : reader.GetString(8)
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
    public async Task<SearchHits> SearchAsync(
        string query, int maxResults = DefaultMaxResults, CancellationToken cancellationToken = default)
    {
        var results = new List<(int, long, string, string, string, string, string?)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // LIMIT one more than we intend to keep: if that extra row comes
        // back, there was at least one more match than we're showing, which
        // is exactly what Truncated needs to know. Cheaper and more honest
        // than a second COUNT(*) over the same predicate.
        const string sql = @"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text, tn.Milestone
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE tn.Text LIKE @Query ESCAPE '\'
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @Limit;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 180;
        cmd.Parameters.AddWithValue("@Query", $"%{EscapeLikeWildcards(query)}%");
        cmd.Parameters.AddWithValue("@Limit", maxResults + 1);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        var truncated = results.Count > maxResults;
        if (truncated) results.RemoveRange(maxResults, results.Count - maxResults);

        return new SearchHits(results, truncated);
    }

    /// <summary>
    /// Search with filters, for the search window.
    ///
    /// Built as one SQL statement rather than by filtering in memory: the
    /// narrowing is exactly what keeps a broad query from hitting the result
    /// cap, so it has to happen before the LIMIT, not after. Filtering
    /// afterwards would mean "the first 5000 matches anywhere, of which
    /// three happen to be Aeschylus" instead of "the first 5000 in
    /// Aeschylus".
    ///
    /// Every clause is optional and every value is parameterised. The only
    /// SQL assembled from a variable is the count of placeholders in the IN
    /// lists, which is derived from collection sizes rather than content.
    /// </summary>
    public async Task<SearchHits> SearchFilteredAsync(
        SearchFilters filters, CancellationToken cancellationToken = default)
    {
        var query = filters.Query.Trim();
        if (query.Length == 0) return SearchHits.Empty;

        // An era that matched no authors is a real result - no passages can
        // qualify - and must not be confused with "no era filter".
        if (filters.EraAuthorIds is { Count: 0 }) return SearchHits.Empty;

        var results = new List<(int, long, string, string, string, string, string?)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 180;

        // Whole-word matching wants the word index, and asks the connection
        // that's already open rather than opening a second one.
        var indexAvailable = filters.MatchMode == SearchMatchMode.WholeWord
                             && await WordIndexRepository.HasDataAsync(conn, cancellationToken);

        var where = BuildFilterPredicate(cmd, filters, query, indexAvailable);

        cmd.Parameters.AddWithValue("@Limit", filters.MaxResults + 1);

        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text, tn.Milestone
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE {string.Join(" AND ", where)}
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @Limit;";

        return await ReadFilteredRowsAsync(cmd, filters, query, indexAvailable, results, cancellationToken);
    }

    /// <summary>
    /// How many lines match, and how they are distributed across the works
    /// that contain them. Counts everything: this deliberately has no LIMIT,
    /// because a capped count is not a count.
    ///
    /// WHY THIS EXISTS AS A SEPARATE QUERY. A filtered search stops at
    /// MaxResults, and the results it stops with are ordered by author name.
    /// So the cap does not take a sample of the matches - it truncates the
    /// alphabet. Grouping those rows to show "which works use this word" then
    /// produces a table that looks exactly like a distribution and is not one.
    ///
    /// Measured against a full library, searching for Latin "vel": 44,457
    /// lines across 1,623 works, of which the capped rows could see 5,000
    /// lines across 168 works. 1,455 works - 90% of the ones that contain it -
    /// showed nothing at all, and the last author the cap reached was
    /// Augustine. Augustine is also the work with the most matches in the
    /// whole corpus, 1,056 of them, and the grouped view credited him with
    /// 184, because it ran out of room in the middle of him. For Greek
    /// "λόγος" the top of the real distribution is the Homeric scholia, and
    /// the grouped view led with Aesop, who is there because of the A.
    ///
    /// Nobody could tell from the screen. That is the failure worth paying a
    /// second query for.
    ///
    /// The cost is small because this reads the index rather than the text:
    /// measured on a full library, the complete per-work distribution for
    /// "virtus" (5,728 lines across 897 works) took 26 ms, and for "vel"
    /// (44,457 across 1,623) 116 ms - against 88 ms and 245 ms for the row
    /// query that was already running.
    /// </summary>
    public async Task<SearchDistribution> CountMatchesByWorkAsync(
        SearchFilters filters, CancellationToken cancellationToken = default)
    {
        var query = filters.Query.Trim();
        if (query.Length == 0) return SearchDistribution.Empty;
        if (filters.EraAuthorIds is { Count: 0 }) return SearchDistribution.Empty;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 180;

        var indexAvailable = filters.MatchMode == SearchMatchMode.WholeWord
                             && await WordIndexRepository.HasDataAsync(conn, cancellationToken);

        var where = BuildFilterPredicate(cmd, filters, query, indexAvailable);

        // Grouped by work rather than by edition, so a work this library
        // holds twice - CSEL and Migne both ship Augustine - is one row
        // rather than two rows with the same title, which would read as a
        // bug even though both would be true.
        //
        // What is counted is therefore matching LINES IN THE LIBRARY, and a
        // work held in two editions contributes both. That is the same
        // quantity the row list has always shown, and it is the honest one
        // for a question about this library; someone asking about the
        // literature rather than about their copy of it wants the collection
        // filter, which reaches this count like every other filter.
        cmd.CommandText = $@"
            SELECT w.WorkId, a.Name, w.Title, COUNT(*) AS Matches
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE {string.Join(" AND ", where)}
            GROUP BY w.WorkId
            ORDER BY Matches DESC, a.Name, w.Title;";

        var works = new List<(int WorkId, string AuthorName, string WorkTitle, long Matches)>();
        long total = 0;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var matches = reader.GetInt64(3);
            total += matches;
            works.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), matches));
        }

        // The LIKE fallback's whole-word confirmation runs per row and cannot
        // run inside an aggregate, so a count taken without the index would
        // be of the prefilter rather than of the search. Saying so is better
        // than quietly returning a larger number than the rows support.
        return new SearchDistribution(works, total, ExactlyMatchesTheSearch:
            filters.MatchMode != SearchMatchMode.WholeWord || indexAvailable);
    }

    /// <summary>
    /// Every WHERE clause a filtered search applies, with its parameters
    /// registered on the command.
    ///
    /// Pulled out of SearchFilteredAsync so that counting the matches and
    /// fetching them run the same predicate rather than two that resemble
    /// each other. A count that disagrees with the rows it is counting is
    /// worse than no count.
    /// </summary>
    private static List<string> BuildFilterPredicate(
        SqliteCommand cmd, SearchFilters filters, string query, bool indexAvailable)
    {
        var where = new List<string>();

        AppendTextPredicate(cmd, where, query, filters.MatchMode, indexAvailable);

        if (filters.Languages.Count > 0)
        {
            where.Add($"e.Language IN ({AddParameters(cmd, "lang", filters.Languages)})");
        }

        if (filters.Corpora.Count > 0)
        {
            where.Add($"a.Namespace IN ({AddParameters(cmd, "ns", filters.Corpora)})");
        }

        if (filters.Collections.Count > 0)
        {
            // An edition belonging to no collection is never excluded by a
            // collection filter. The collections are the corpora that were
            // ingested; a translation the reader wrote themselves belongs to
            // none of them, and "e.Collection IN (...)" is false for NULL, so
            // ticking every box in the Collections menu used to hide exactly
            // the editions the reader had made. Two ways of saying
            // "everything" - tick them all, or the menu's own "Show all
            // collections", which unticks them - gave different answers, and
            // the one that looked more thorough was the one that quietly
            // dropped 622 of their own passages.
            where.Add($"(e.Collection IN ({AddParameters(cmd, "coll", filters.Collections)}) "
                      + "OR e.Collection IS NULL)");
        }

        if (filters.OriginalsOnly != null)
        {
            // Asymmetric on purpose. "Originals only" means exactly the
            // original-language text. "Translations only" means everything that
            // is not it - which is what a reader picking that option is asking
            // for, and which matters because an edition ingest could not
            // classify is not an original either.
            //
            // Asking for Kind = 'Translation' exactly made those editions
            // findable with the filter off and invisible with it set, while the
            // reader - which sorts the same editions into its two panes on the
            // same question - had already started showing them. One rule, so a
            // filter cannot hide a text the reader will happily open.
            where.Add(filters.OriginalsOnly.Value ? "e.Kind = 'Original'" : "e.Kind <> 'Original'");
        }

        if (filters.AuthorId != null)
        {
            where.Add("a.AuthorId = @AuthorId");
            cmd.Parameters.AddWithValue("@AuthorId", filters.AuthorId.Value);
        }

        if (filters.WorkId != null)
        {
            where.Add("w.WorkId = @WorkId");
            cmd.Parameters.AddWithValue("@WorkId", filters.WorkId.Value);
        }

        if (filters.EraAuthorIds is { Count: > 0 })
        {
            where.Add($"a.AuthorId IN ({AddParameters(cmd, "era", filters.EraAuthorIds.Select(id => id.ToString()))})");
        }

        // Tags and bookmarks hang off (EditionId, CitationRef), not off the
        // text node - see SchemaInitializer's PassageTags comment - so these
        // join on the passage rather than the row.
        if (!string.IsNullOrWhiteSpace(filters.TagName))
        {
            where.Add(@"EXISTS (
                SELECT 1 FROM PassageTags pt
                JOIN Tags t ON pt.TagId = t.TagId
                WHERE pt.EditionId = tn.EditionId AND pt.CitationRef = tn.CitationRef
                  AND t.Name = @TagName)");
            cmd.Parameters.AddWithValue("@TagName", filters.TagName);
        }

        if (filters.BookmarkedOnly)
        {
            where.Add(@"EXISTS (
                SELECT 1 FROM Bookmarks b
                WHERE b.EditionId = tn.EditionId AND b.CitationRef = tn.CitationRef)");
        }

        return where;
    }

    /// <summary>
    /// Runs the row query and applies the whole-word confirmation the LIKE
    /// fallback needs. Split from SearchFilteredAsync only so that building
    /// the predicate and reading the rows are separable - the count shares
    /// the first and has no use for the second.
    /// </summary>
    private static async Task<SearchHits> ReadFilteredRowsAsync(
        SqliteCommand cmd,
        SearchFilters filters,
        string query,
        bool indexAvailable,
        List<(int, long, string, string, string, string, string?)> results,
        CancellationToken cancellationToken)
    {
        // Only for the LIKE fallback. The index path has already matched on
        // whole normalized words, and re-checking the raw text against an
        // unaccented query here would throw away exactly the rows the index
        // was able to find.
        var wholeWordTargets = filters.MatchMode == SearchMatchMode.WholeWord && !indexAvailable
            ? query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(WordNormalizer.Normalize)
                .Where(w => w.Length > 0)
                .ToHashSet(StringComparer.Ordinal)
            : null;

        var rowsRead = 0;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rowsRead++;
            var text = reader.GetString(5);

            if (wholeWordTargets != null)
            {
                var isWholeWordHit = text
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(WordNormalizer.Normalize)
                    .Any(w => w.Length > 0 && wholeWordTargets.Contains(w));

                if (!isWholeWordHit) continue;
            }

            results.Add((
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), text,
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        // Measured against rows read rather than rows kept: the whole-word
        // confirmation runs after the LIMIT, so this can return fewer than
        // the cap and still have been clipped.
        var truncated = rowsRead > filters.MaxResults;
        if (truncated && results.Count > filters.MaxResults)
        {
            results.RemoveRange(filters.MaxResults, results.Count - filters.MaxResults);
        }

        return new SearchHits(results, truncated);
    }

    /// <summary>
    /// The text half of the WHERE clause.
    ///
    /// WholeWord is done with GLOB rather than a regular expression, since
    /// SQLite ships no regex by default: "[^a-z]" style character classes
    /// are the one pattern facility available, and bounding the term with
    /// non-letters on each side is what "whole word" means here. It's
    /// case-sensitive where LIKE isn't, so both cases of the first letter
    /// are tried.
    /// </summary>
    private static void AppendTextPredicate(
        SqliteCommand cmd, List<string> where, string query, SearchMatchMode mode, bool indexAvailable)
    {
        switch (mode)
        {
            case SearchMatchMode.AllWords:
                var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < words.Length; i++)
                {
                    where.Add($"tn.Text LIKE @w{i} ESCAPE '\\'");
                    cmd.Parameters.AddWithValue($"@w{i}", $"%{EscapeLikeWildcards(words[i])}%");
                }

                // A query of only punctuation splits to nothing, which would
                // leave the WHERE clause empty and return the whole corpus.
                if (words.Length == 0) where.Add("1 = 0");
                break;

            case SearchMatchMode.WholeWord when indexAvailable:
                // Against the word index, which stores one normalized word
                // per line - accents stripped, final sigma folded. That
                // makes this both genuinely whole-word (the index holds
                // words, not substrings) and accent-insensitive, so a Greek
                // word matches however the edition accents it.
                //
                // It has to come from the index rather than from LIKE: a
                // LIKE pattern is compared against the raw text, so a query
                // typed without accents contains characters the text simply
                // doesn't have, and no amount of confirming afterwards can
                // recover a row the prefilter already excluded.
                //
                // Each word is then expanded into its u/v and i/j spellings,
                // for the reason SpellingVariants gives at length: those were
                // one letter each in antiquity, editors disagree about which
                // glyph to print, and searching one spelling was returning a
                // fraction of the evidence - 31.8% of it hidden across
                // twenty-two ordinary Latin query words, and 65.8% of
                // "iustitia" specifically, which is the spelling a reader is
                // actually taught. Greek has one of its own - the lunate
                // sigma 87 editions in this corpus are set in.
                //
                // It is close to free in time too. Every extra spelling is
                // one more seek on the index's leading key column, and most
                // of them miss: measured on a full library, "iustitia" went
                // from 5 ms and 1,445 hits to 15 ms and 4,196.
                var queryWords = query
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(WordNormalizer.Normalize)
                    .Where(w => w.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (queryWords.Count == 0)
                {
                    where.Add("1 = 0");
                    break;
                }

                // One clause per word, ANDed - a line has to contain all of
                // them, not any of them.
                //
                // It read as one IN over every word's spellings at once,
                // which is an OR, and for a single-word query the two are the
                // same thing. They stopped being the same thing when this
                // became the default mode, because typing more than one word
                // into a search box is normal and OR is not what anybody
                // means by it. Searching this library for "gallia est omnis
                // divisa" returned 5,000+ lines led by an argumentum to a
                // letter of Cyprian - every line in the corpus containing
                // "est" - where the reader wanted the one line that opens the
                // Gallic War. That is the worst kind of wrong answer: a
                // confident, enormous one.
                //
                // IN over a subquery driven FROM the index, not EXISTS
                // correlated back to it. The two return the same rows and are
                // not the same query.
                //
                // EXISTS made the index the inner loop: SQLite scanned all
                // 2,338,662 text nodes and probed WordIndex once per node, so
                // the cost was the size of the corpus rather than the size of
                // the answer. Driven the other way it is one seek on
                // NormalizedWord - the leading column of the primary key -
                // followed by a row lookup per hit.
                //
                // Measured on a full library, searching Latin "uirtus" for
                // 1,284 hits: 962 ms against 6 ms. The plan shows why - SCAN
                // tn USING COVERING INDEX with a CORRELATED SCALAR SUBQUERY
                // becomes SEARCH wi USING PRIMARY KEY and SEARCH tn USING
                // INTEGER PRIMARY KEY.
                //
                // SearchByFormsAsync and FindTextNodesContainingAnyWordAsync
                // were already written this way. This one was not, and it is
                // the search window's default mode.
                //
                // Each word's own spellings stay ORed together inside its own
                // clause, since "iustitia" and "justitia" are one word and
                // requiring both would match nothing.
                // A distinct prefix per word: AddParameters numbers from zero
                // on every call, so sharing one would name two parameters
                // @ww0 and throw.
                for (var i = 0; i < queryWords.Count; i++)
                {
                    var spellings = SpellingVariants.Of(queryWords[i]);
                    where.Add($@"tn.TextNodeId IN (
                        SELECT wi.TextNodeId FROM WordIndex wi
                        WHERE wi.NormalizedWord IN ({AddParameters(cmd, $"ww{i}_", spellings)}))");
                }
                break;

            case SearchMatchMode.WholeWord:
                // No word index built yet. Falls back to a LIKE prefilter
                // plus the normalized confirmation below, which still
                // rejects substrings correctly but can only find a word
                // spelled as typed - accent-insensitivity is exactly the
                // part the index was providing. Building the index from the
                // Setup Wizard restores it.
                where.Add("tn.Text LIKE @Query ESCAPE '\\'");
                cmd.Parameters.AddWithValue("@Query", $"%{EscapeLikeWildcards(query)}%");
                break;

            default:
                where.Add("tn.Text LIKE @Query ESCAPE '\\'");
                cmd.Parameters.AddWithValue("@Query", $"%{EscapeLikeWildcards(query)}%");
                break;
        }
    }

    /// <summary>
    /// Adds one parameter per value and returns the placeholder list for an
    /// IN clause. The generated SQL depends only on how many values there
    /// are, never on what they contain.
    /// </summary>
    private static string AddParameters(SqliteCommand cmd, string prefix, IEnumerable<string> values)
    {
        var names = new List<string>();
        var index = 0;

        foreach (var value in values)
        {
            var name = $"@{prefix}{index++}";
            names.Add(name);
            cmd.Parameters.AddWithValue(name, value);
        }

        return string.Join(",", names);
    }

    /// <summary>
    /// Neutralises LIKE's own wildcards in text the user typed. Without this,
    /// searching for a literal "100%" or "de_" silently means something else
    /// entirely - "%" matches any run of characters and "_" any single one -
    /// and the person gets a wall of unrelated results with no clue why.
    /// Paired with ESCAPE '\' on every LIKE that takes user input.
    /// </summary>
    private static string EscapeLikeWildcards(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

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
    ///
    /// Optionally restricted to a chosen set of works. The corpus-wide
    /// answer is often too big to be an answer: a common word returns
    /// thousands of lines, stops at the result limit, and tells you only
    /// that the word is common. Narrowed it becomes the useful question -
    /// how does this author use it, in this text, or across this trilogy.
    ///
    /// An empty or null set means no restriction, so "everything" costs
    /// nothing rather than being expressed as a list of every id.
    /// </summary>
    public async Task<SearchHits> SearchByFormsAsync(
        IReadOnlyList<string> forms, int maxResults = DefaultMaxResults,
        IReadOnlyCollection<int>? workIds = null, CancellationToken cancellationToken = default)
    {
        if (forms.Count == 0) return SearchHits.Empty;

        var scope = workIds == null || workIds.Count == 0 ? null : workIds.Distinct().ToList();

        // Fast path: if the inverted word index has been built, resolve
        // everything in one joined query.
        var indexed = await TrySearchViaWordIndexAsync(forms, maxResults, scope, cancellationToken);
        if (indexed != null) return indexed;

        return await SearchByFormsWithLikeAsync(forms, maxResults, scope, cancellationToken);
    }

    /// <summary>
    /// Finds a name that may run to more than one word - what a click on the
    /// places map asks for.
    ///
    /// <see cref="SearchByFormsAsync"/> reads what it is given as alternative
    /// spellings of a single word and ORs them, and normalization drops
    /// everything that is not a letter. A name with a space in it therefore
    /// reaches the word index as one impossible token: "Euxine sea" becomes
    /// "euxinesea", which no line contains. Eight of the map's 240 pins
    /// returned nothing at all because of that, having returned real mentions
    /// before it - 86 for the Euxine sea, 57 for the Arabian Gulf, 12 for lake
    /// Moeris.
    ///
    /// One word behaves exactly as SearchByFormsAsync does, which is the whole
    /// point of the change that introduced this path: clicking Ur must not
    /// return "during", "figure" and "purple".
    ///
    /// More than one word requires every word through the index - each lookup
    /// is a prefix seek on the index's own primary key, so this stays cheap -
    /// and then requires the phrase itself to be present, so "Egyptian Thebes"
    /// does not match a line carrying both words a paragraph apart.
    ///
    /// This is NOT the same set a substring search returns, and it is not
    /// meant to be. It is deliberately smaller: "Le Mans" as a substring
    /// matches "noble mansions", "ille mansuetudine" and "noble mans house",
    /// thirty passages of which none is the city, and requiring both words as
    /// words is what removes them.
    ///
    /// It is also, in two ways, slightly smaller than it should be, both
    /// measured rather than supposed:
    ///
    ///  - A spelling variant in the middle of a word is missed. Strabo's
    ///    "Aegyptian Thebes" matches the phrase but tokenizes to "aegyptian",
    ///    which no prefix of "egyptian" reaches. One mention across the map's
    ///    nine multi-word pins.
    ///
    ///  - Accents and non-ASCII case are matched inconsistently between the
    ///    two branches. The index holds normalized words, so a one-word search
    ///    finds Tanais and Tanaïs alike; the phrase LIKE uses SQLite's default
    ///    collation, which folds ASCII only, so a two-word phrase typed
    ///    without its accents finds nothing. Fixing that needs a normalized
    ///    copy of the text to match against, which is a schema change, not a
    ///    query change.
    /// </summary>
    public async Task<SearchHits> SearchPhraseAsync(
        string phrase, int maxResults = DefaultMaxResults,
        IReadOnlyCollection<int>? workIds = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return SearchHits.Empty;

        var words = IndexableWordsOf(phrase);
        if (words.Count == 0) return SearchHits.Empty;

        if (words.Count == 1)
            return await SearchByFormsAsync(new[] { phrase }, maxResults, workIds, cancellationToken);

        var scope = workIds == null || workIds.Count == 0 ? null : workIds.Distinct().ToList();

        var indexed = await TrySearchPhraseViaWordIndexAsync(
            phrase, words, maxResults, scope, cancellationToken);
        if (indexed != null) return indexed;

        // No word index built yet. The substring search is the right answer
        // for a phrase already - it is only slower, and it is what this did
        // before the index path existed.
        return await SearchAsync(phrase, maxResults, cancellationToken);
    }

    /// <summary>
    /// Words at least this long are matched by prefix in a phrase search, so
    /// that a plural or a word run together with the next by missing
    /// punctuation still counts. See TrySearchPhraseViaWordIndexAsync for why
    /// it is four and not three or five.
    /// </summary>
    private const int PrefixMatchFromLength = 4;

    /// <summary>
    /// The exclusive upper bound of a prefix range: everything that starts
    /// with <paramref name="word"/> sorts at or after it and before this.
    ///
    /// The index compares with SQLite's default BINARY collation, which orders
    /// UTF-8 bytes - and UTF-8 preserves code point order, so incrementing the
    /// last code point gives a correct bound for Greek and Latin alike.
    /// </summary>
    private static string PrefixUpperBound(string word)
    {
        var chars = word.ToCharArray();
        chars[^1]++;
        return new string(chars);
    }

    /// <summary>
    /// The distinct words of a phrase in the shape the word index stores them.
    /// Deliberately the same three steps as WordIndexService.TokenizeLine: a
    /// lookup built any other way would not find what the index holds.
    /// </summary>
    private static List<string> IndexableWordsOf(string phrase) => phrase
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(WordNormalizer.Normalize)
        .Where(w => w.Length > 0 && w.Length <= 200)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Phrase search against the inverted index. Returns null (not an empty
    /// list) when the index has not been built, so the caller can fall back
    /// rather than report that nothing matched - the same contract
    /// <see cref="TrySearchViaWordIndexAsync"/> keeps.
    /// </summary>
    private async Task<SearchHits?> TrySearchPhraseViaWordIndexAsync(
        string phrase, IReadOnlyList<string> words, int maxResults,
        List<int>? workIds, CancellationToken cancellationToken)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        if (!await WordIndexRepository.HasDataAsync(conn, cancellationToken)) return null;

        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;

        var required = new List<string>();
        for (var i = 0; i < words.Count && i < MaxFormsPerQuery; i++)
        {
            var word = words[i];

            // A word of four letters or more is matched by prefix, not
            // exactly. The index holds whole words, and a phrase does not
            // always sit on whole-word boundaries in the text: "the Persian
            // and Arabian Gulfs" tokenizes "Gulfs", which is not "gulf", and
            // Strabo's "to Egyptian Thebes."And of its power" has no space
            // after the quotation mark, so the token is "thebesand". The
            // phrase LIKE below matches both; the exact conjunct rejected
            // them, losing five real mentions across the map's nine
            // multi-word pins.
            //
            // Four is the threshold because shorter words are too ambiguous
            // to prefix. "Le Mans" is the case that sets it: "le" as a prefix
            // matches "less", "left", "legions" - some word in almost any
            // passage - so the conjunct stops excluding anything and the pin
            // returns "noble mansions" and "ille mansuetudine" again. Those
            // twenty-two are exactly the noise this whole path exists to
            // remove. Measured over all nine pins: at four, Le Mans stays at
            // nothing and the five real mentions come back; at five, "gulf"
            // falls below the threshold and three of them are lost again.
            //
            // Widening this can only ever add candidates - the phrase LIKE
            // stays the precision filter - which is why it costs nothing in
            // accuracy and 1-26ms in time.
            if (word.Length >= PrefixMatchFromLength)
            {
                required.Add(
                    $"tn.TextNodeId IN (SELECT TextNodeId FROM WordIndex " +
                    $"WHERE NormalizedWord >= @w{i} AND NormalizedWord < @wend{i})");
                cmd.Parameters.AddWithValue($"@w{i}", word);
                cmd.Parameters.AddWithValue($"@wend{i}", PrefixUpperBound(word));
            }
            else
            {
                required.Add(
                    $"tn.TextNodeId IN (SELECT TextNodeId FROM WordIndex WHERE NormalizedWord = @w{i})");
                cmd.Parameters.AddWithValue($"@w{i}", word);
            }
        }

        cmd.Parameters.AddWithValue("@Phrase", $"%{EscapeLikeWildcards(phrase.Trim())}%");
        cmd.Parameters.AddWithValue("@Limit", maxResults + 1);

        // Verbatim string: the backslash in ESCAPE '\' is a literal backslash
        // here. Written as "\\" inside an ordinary interpolated string it
        // would be an escaped apostrophe, which is how a sibling query came to
        // send SQLite ESCAPE '' and fail outright.
        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text, tn.Milestone
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE {string.Join(" AND ", required)}
              AND tn.Text LIKE @Phrase ESCAPE '\'
              {WorkScopeClause(cmd, workIds, "AND")}
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @Limit;";

        var results = new List<(int, long, string, string, string, string, string?)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        var truncated = results.Count > maxResults;
        if (truncated) results.RemoveRange(maxResults, results.Count - maxResults);

        return new SearchHits(results, truncated);
    }

    /// <summary>
    /// The WHERE fragment restricting a search to chosen works, with its
    /// parameters registered on the command. Empty when unrestricted.
    ///
    /// Delegates to WorkScope so morphology search, which needs the same
    /// fragment, cannot drift from what the keyword searches do.
    /// </summary>
    private static string WorkScopeClause(
        SqliteCommand cmd, List<int>? workIds, string keyword) =>
        WorkScope.Clause(cmd, workIds, keyword);

    /// <summary>
    /// Single-query search against the inverted index. Returns null (not an
    /// empty list) when the index hasn't been built, so the caller can tell
    /// "no index" apart from "index found nothing" and fall back.
    /// </summary>
    private async Task<SearchHits?> TrySearchViaWordIndexAsync(
        IReadOnlyList<string> forms, int maxResults, List<int>? workIds, CancellationToken cancellationToken)
    {
        // Opened once and reused for the has-data check below and the real
        // query that follows it - see WordIndexRepository.HasDataAsync's
        // connection-taking overload for why that used to be two opens.
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        if (!await WordIndexRepository.HasDataAsync(conn, cancellationToken)) return null;

        var allNormalized = forms
            .Select(WordNormalizer.Normalize)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var formsTruncated = allNormalized.Count > MaxFormsPerQuery;
        var normalized = formsTruncated
            ? allNormalized.Take(MaxFormsPerQuery).ToList()
            : allNormalized;

        var results = new List<(int, long, string, string, string, string, string?)>();
        if (normalized.Count == 0) return SearchHits.Empty;

        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;

        var paramNames = new List<string>();
        for (var i = 0; i < normalized.Count; i++)
        {
            paramNames.Add($"@w{i}");
            cmd.Parameters.AddWithValue($"@w{i}", normalized[i]);
        }
        cmd.Parameters.AddWithValue("@Limit", maxResults + 1);

        // The LIMIT belongs on the OUTER query, not the inner one. Inside the
        // subquery it clipped an unordered set of ids and only THEN sorted
        // what survived - so the "first 5000" a reader saw were an arbitrary
        // 5000 that happened to come back first, presented in author order as
        // though they were the first 5000 alphabetically. Out here it means
        // what it looks like it means.
        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text, tn.Milestone
            FROM (
                SELECT DISTINCT TextNodeId
                FROM WordIndex
                WHERE NormalizedWord IN ({string.Join(",", paramNames)})
            ) ids
            JOIN TextNodes tn ON ids.TextNodeId = tn.TextNodeId
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            {WorkScopeClause(cmd, workIds, "WHERE")}
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @Limit;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        var rowsTruncated = results.Count > maxResults;
        if (rowsTruncated) results.RemoveRange(maxResults, results.Count - maxResults);

        return new SearchHits(results, rowsTruncated || formsTruncated);
    }

    private async Task<SearchHits> SearchByFormsWithLikeAsync(
        IReadOnlyList<string> forms, int maxResults, List<int>? workIds, CancellationToken cancellationToken)
    {
        var results = new List<(int, long, string, string, string, string, string?)>();

        // Cap the SQL side - a big paradigm can run to hundreds of forms and
        // there's no point building a huge OR. The rest get caught by the
        // in-memory pass over whatever comes back.
        var formsTruncated = forms.Count > MaxFormsPerLikeQuery;
        var sqlForms = formsTruncated ? forms.Take(MaxFormsPerLikeQuery).ToList() : forms.ToList();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        var clauses = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 180;
        for (var i = 0; i < sqlForms.Count; i++)
        {
            // Two backslashes, not one. This is an interpolated string, not a
            // verbatim one, so "\'" is an escaped apostrophe and the SQL that
            // reached SQLite was ESCAPE '' - an empty escape expression, which
            // it rejects when preparing the statement. Every other LIKE in
            // this file gets it right; this one path was only reached when the
            // word index is empty, which is the state of every library between
            // a first ingest and a first index build.
            clauses.Add($"tn.Text LIKE @f{i} ESCAPE '\\'");
            cmd.Parameters.AddWithValue($"@f{i}", $"%{EscapeLikeWildcards(sqlForms[i])}%");
        }

        cmd.Parameters.AddWithValue("@Limit", maxResults + 1);

        cmd.CommandText = $@"
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text, tn.Milestone
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE ({string.Join(" OR ", clauses)})
                  {WorkScopeClause(cmd, workIds, "AND")}
            ORDER BY a.Name, w.Title, tn.SortOrder
            LIMIT @Limit;";

        var normalizedTargets = new HashSet<string>(
            forms.Select(WordNormalizer.Normalize).Where(f => f.Length > 0), StringComparer.Ordinal);

        var rowsRead = 0;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rowsRead++;
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
                reader.GetString(3), reader.GetString(4), text,
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        // The whole-word filter above runs after the LIMIT, so this path can
        // return fewer than maxResults and still have been clipped - hence
        // comparing against the row budget rather than the surviving count.
        var rowsTruncated = rowsRead > maxResults;
        if (rowsTruncated && results.Count > maxResults)
        {
            results.RemoveRange(maxResults, results.Count - maxResults);
        }

        return new SearchHits(results, rowsTruncated || formsTruncated);
    }

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
            SELECT TextNodeId, EditionId, CitationRef, SortOrder, Text,
                   COALESCE(NodeKind, 'line'), COALESCE(IsVerse, 0), Milestone
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
                Text = reader.GetString(4),
                NodeKind = reader.GetString(5),
                IsVerse = reader.GetInt32(6) != 0,
                Milestone = reader.IsDBNull(7) ? null : reader.GetString(7)
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

    /// <summary>
    /// Which edition each of these lines belongs to.
    ///
    /// Exists for cross-work export. The views that gather passages from all
    /// over the library - the Tag Browser, Concordance, Echo results - carry
    /// the work each line came from but not the edition, because until now
    /// nothing downstream needed it. Pairing a line with its translation
    /// does: the counterpart is a sibling edition of the same work, and you
    /// can't ask which sibling to use without knowing which one you're
    /// standing on.
    ///
    /// Resolved in one query rather than per line - a tag can easily cover
    /// several hundred passages, and that many round trips for what is
    /// ultimately a small lookup table would be felt.
    /// </summary>
    public async Task<Dictionary<long, int>> GetEditionIdsAsync(
        IReadOnlyList<long> textNodeIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<long, int>();
        if (textNodeIds.Count == 0) return result;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const int batchSize = 400;
        for (var offset = 0; offset < textNodeIds.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var thisBatch = Math.Min(batchSize, textNodeIds.Count - offset);

            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 60;

            var paramNames = new List<string>(thisBatch);
            for (var i = 0; i < thisBatch; i++)
            {
                paramNames.Add($"@n{i}");
                cmd.Parameters.AddWithValue($"@n{i}", textNodeIds[offset + i]);
            }

            cmd.CommandText =
                $"SELECT TextNodeId, EditionId FROM TextNodes WHERE TextNodeId IN ({string.Join(",", paramNames)});";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetInt64(0)] = reader.GetInt32(1);
            }
        }

        return result;
    }

    /// <summary>
    /// How many lines an edition holds, without loading any of them -
    /// GetByEditionAsync would pull a few thousand rows into memory to
    /// answer what is a single COUNT.
    /// </summary>
    /// <summary>
    /// Total character length of an edition's text.
    ///
    /// Used by the stylometry pool filter as a cheap proxy for token count.
    /// Tokenising the whole corpus just to decide which works are long enough
    /// to tokenise would defeat the purpose; SUM(LENGTH(Text)) reads straight
    /// off the table.
    ///
    /// Returns 0 for an edition with no nodes rather than null, so callers can
    /// treat "no text" and "very little text" the same way - which for a
    /// length threshold is the right behaviour.
    /// </summary>
    public async Task<int> GetCharacterCountAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(LENGTH(Text)), 0) FROM TextNodes WHERE EditionId = @EditionId;";
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.CommandTimeout = 60;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> CountByEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM TextNodes WHERE EditionId = @EditionId;";
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.CommandTimeout = 60;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// Which other edition of a work shares the most citation references
    /// with this one - the closest thing available to "which original was
    /// this translated from".
    ///
    /// Nothing records that link. An edition knows its work, its language
    /// and its translator, but not which text somebody sat down with. When a
    /// work has two originals and three translations, pairing them is
    /// therefore inference rather than lookup, and citation references are
    /// the only evidence: a translation made against one edition carries
    /// that edition's reference scheme, so overlap is high with its own
    /// source and low with a differently-lineated sibling.
    ///
    /// Good evidence, not proof - two editions lineated identically are
    /// indistinguishable this way, which is why the caller offers the answer
    /// as a default to change rather than a decision already made.
    /// </summary>
    public async Task<int?> FindClosestEditionAsync(
        int editionId, int workId, EditionKind kind, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 60;

        cmd.CommandText = @"
            SELECT other.EditionId, COUNT(*) AS Shared
            FROM TextNodes mine
            JOIN TextNodes theirs ON theirs.CitationRef = mine.CitationRef
            JOIN Editions other ON theirs.EditionId = other.EditionId
            WHERE mine.EditionId = @EditionId
              AND other.EditionId <> @EditionId
              AND other.WorkId = @WorkId
              AND other.Kind = @Kind
            GROUP BY other.EditionId
            ORDER BY Shared DESC
            LIMIT 1;";

        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.Parameters.AddWithValue("@WorkId", workId);
        cmd.Parameters.AddWithValue("@Kind", kind.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? reader.GetInt32(0) : null;
    }

    /// <summary>
    /// How much of a work an AI-generated translation edition actually
    /// covers, as (lines translated, lines in the source edition).
    ///
    /// Only meaningful for AI translations, and only because of an invariant
    /// they alone satisfy: CreateTranslationForm writes one line per source
    /// line, under the source's own citation ref. So a source ref with no
    /// counterpart really is a line that hasn't been translated yet - which
    /// is an ordinary state, since a long work takes many batches and the
    /// free tier has a daily limit.
    ///
    /// The same comparison would be meaningless for an ingested translation:
    /// a prose translation of a verse original is legitimately divided far
    /// more coarsely, and counting its lines against the original's would
    /// report every published translation in the library as nine-tenths
    /// missing.
    ///
    /// The source edition isn't recorded anywhere, so it's inferred as
    /// whichever original-language edition of the work shares the most
    /// citation refs with this one. That's the edition it must have been
    /// built from, and inferring it means this works for translations
    /// generated before any of this existed.
    ///
    /// Null when the work has no original-language edition to compare
    /// against, which leaves the caller with nothing to claim either way.
    /// </summary>
    public async Task<(int Translated, int SourceTotal)?> GetTranslationCoverageAsync(
        int translationEditionId, int workId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 60;

        cmd.CommandText = @"
            SELECT COUNT(*) AS SourceTotal,
                   SUM(CASE WHEN EXISTS (
                       SELECT 1 FROM TextNodes t
                       WHERE t.EditionId = @TranslationEditionId
                         AND t.CitationRef = src.CitationRef) THEN 1 ELSE 0 END) AS Covered
            FROM TextNodes src
            JOIN Editions e ON src.EditionId = e.EditionId
            WHERE e.WorkId = @WorkId
              AND e.Kind = 'Original'
              AND e.EditionId <> @TranslationEditionId
            GROUP BY src.EditionId
            ORDER BY Covered DESC
            LIMIT 1;";

        cmd.Parameters.AddWithValue("@TranslationEditionId", translationEditionId);
        cmd.Parameters.AddWithValue("@WorkId", workId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var sourceTotal = reader.GetInt32(0);
        var covered = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);

        return sourceTotal == 0 ? null : (covered, sourceTotal);
    }

    /// <summary>
    /// The line at a citation reference within a work, whichever edition of
    /// it holds one - for restoring a remembered reading position.
    ///
    /// Takes a work's CTS URN rather than an id because the caller is a
    /// preference file that outlives any particular database. Prefers the
    /// original-language edition when more than one edition has that
    /// reference, since that is the side someone reading a classical text is
    /// usually anchored to.
    ///
    /// Null when the work is gone or that reference no longer exists - both
    /// ordinary after a corpus changes, and both meaning "just open normally".
    /// </summary>
    public async Task<(int WorkId, long TextNodeId)?> FindByWorkUrnAndCitationAsync(
        string workCtsUrn, string citationRef, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;

        cmd.CommandText = @"
            SELECT w.WorkId, tn.TextNodeId
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            WHERE w.CtsUrn = @WorkCtsUrn AND tn.CitationRef = @CitationRef
            ORDER BY CASE WHEN e.Kind = 'Original' THEN 0 ELSE 1 END, tn.SortOrder
            LIMIT 1;";

        cmd.Parameters.AddWithValue("@WorkCtsUrn", workCtsUrn);
        cmd.Parameters.AddWithValue("@CitationRef", citationRef);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    /// <summary>
    /// What <see cref="SyncEditionAsync"/> changed, in the form the word
    /// index needs to keep itself current: which lines were rewritten and
    /// what they used to say, which are new, and which are gone.
    ///
    /// The old text is carried because the index is keyed on the words a
    /// line contained, and after the line has been rewritten there is no
    /// other way to know what those were.
    /// </summary>
    public sealed class EditionTextChanges
    {
        public List<(long TextNodeId, string OldText, string NewText)> Rewritten { get; } = new();
        public List<(long TextNodeId, string Text)> Added { get; } = new();
        public List<(long TextNodeId, string Text)> Removed { get; } = new();

        public bool Any => Rewritten.Count > 0 || Added.Count > 0 || Removed.Count > 0;
    }

    /// <summary>
    /// Brings an edition's lines into line with what the caller wants them to
    /// be, matching on citation reference, and says what it changed.
    ///
    /// The point is that a line keeps its TextNodeId. The obvious way to do
    /// this - delete the edition's lines and insert the new set - gives every
    /// line a new id on every save, and that has two costs that are easy to
    /// miss. The word index is keyed on line ids, so every row it holds for
    /// this edition is orphaned each time; and the only way to clear those
    /// orphans afterwards is a scan of the whole index, because nothing can
    /// find rows by line id. Create Translation saved after every batch, so a
    /// long run paid both costs once per batch, and the cost of the second
    /// grew with the number of lines already translated.
    ///
    /// Matching is on citation reference because that is what the two sides
    /// of a translation genuinely share. Sort order and node kind are
    /// updated in place when they differ; a line whose text is unchanged is
    /// not touched at all, and does not appear in the result.
    /// </summary>
    public async Task<EditionTextChanges> SyncEditionAsync(
        int editionId, IReadOnlyList<TextNode> desired, CancellationToken cancellationToken = default)
    {
        var changes = new EditionTextChanges();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        // Existing lines by citation reference. A duplicate reference should
        // not happen and is not an error worth throwing over - the first is
        // kept and the rest are treated as lines that should no longer exist,
        // which is how they get cleaned up rather than accumulating.
        var existing = new Dictionary<string, (long Id, string Text, int SortOrder, string NodeKind)>(StringComparer.Ordinal);
        var surplus = new List<(long Id, string Text)>();

        await using (var read = conn.CreateCommand())
        {
            read.Transaction = (SqliteTransaction)transaction;
            read.CommandText =
                "SELECT TextNodeId, CitationRef, Text, SortOrder, NodeKind FROM TextNodes WHERE EditionId = @EditionId;";
            read.Parameters.AddWithValue("@EditionId", editionId);

            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = (reader.GetInt64(0), reader.GetString(2), reader.GetInt32(3), reader.GetString(4));
                var citation = reader.GetString(1);
                if (!existing.TryAdd(citation, row)) surplus.Add((row.Item1, row.Item2));
            }
        }

        var wanted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in desired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            wanted.Add(node.CitationRef);

            if (existing.TryGetValue(node.CitationRef, out var current))
            {
                var textChanged = !string.Equals(current.Text, node.Text, StringComparison.Ordinal);
                var shapeChanged = current.SortOrder != node.SortOrder
                                   || !string.Equals(current.NodeKind, node.NodeKind, StringComparison.Ordinal);
                if (!textChanged && !shapeChanged) continue;

                await using var update = conn.CreateCommand();
                update.Transaction = (SqliteTransaction)transaction;
                update.CommandText =
                    "UPDATE TextNodes SET Text = @Text, SortOrder = @SortOrder, NodeKind = @NodeKind " +
                    "WHERE TextNodeId = @TextNodeId;";
                update.Parameters.AddWithValue("@Text", node.Text);
                update.Parameters.AddWithValue("@SortOrder", node.SortOrder);
                update.Parameters.AddWithValue("@NodeKind", node.NodeKind);
                update.Parameters.AddWithValue("@TextNodeId", current.Id);
                await update.ExecuteNonQueryAsync(cancellationToken);

                if (textChanged) changes.Rewritten.Add((current.Id, current.Text, node.Text));
                continue;
            }

            await using var insert = conn.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText =
                "INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text, IsAthetized, NodeKind, IsVerse, Milestone) " +
                "VALUES (@EditionId, @CitationRef, @SortOrder, @Text, @IsAthetized, @NodeKind, @IsVerse, @Milestone); " +
                "SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("@EditionId", editionId);
            insert.Parameters.AddWithValue("@CitationRef", node.CitationRef);
            insert.Parameters.AddWithValue("@SortOrder", node.SortOrder);
            insert.Parameters.AddWithValue("@Text", node.Text);
            insert.Parameters.AddWithValue("@IsAthetized", node.IsAthetized ? 1 : 0);
            insert.Parameters.AddWithValue("@NodeKind", node.NodeKind);
            insert.Parameters.AddWithValue("@IsVerse", node.IsVerse ? 1 : 0);
            insert.Parameters.AddWithValue("@Milestone", (object?)node.Milestone ?? DBNull.Value);

            var newId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            changes.Added.Add((newId, node.Text));
        }

        foreach (var (citation, row) in existing)
        {
            if (wanted.Contains(citation)) continue;
            surplus.Add((row.Id, row.Text));
        }

        foreach (var (id, text) in surplus)
        {
            await using var delete = conn.CreateCommand();
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM TextNodes WHERE TextNodeId = @TextNodeId;";
            delete.Parameters.AddWithValue("@TextNodeId", id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            changes.Removed.Add((id, text));
        }

        await transaction.CommitAsync(cancellationToken);
        return changes;
    }

    /// <summary>
    /// Saves one translated line, replacing whatever was there for that
    /// citation reference.
    ///
    /// One line at a time rather than rewriting the edition, because this
    /// backs someone working through a text over weeks: closing the window
    /// mid-passage should cost the passage being typed and nothing before
    /// it. Keyed on the citation reference so returning to an earlier
    /// passage revises it rather than appending a second copy.
    ///
    /// An empty translation deletes the line instead of storing a blank -
    /// that keeps "how much is done" an honest count of real work, which is
    /// what the progress figure and the resume point both read.
    /// </summary>
    /// <returns>
    /// What changed, in the shape the word index needs. Nothing else in this
    /// method's behaviour changed when that return value was added; the
    /// caller could not previously keep the index current because it was
    /// never told what had happened, and so it did not try.
    /// </returns>
    public async Task<EditionTextChanges> SaveTranslatedLineAsync(
        int editionId, string citationRef, int sortOrder, string? text,
        CancellationToken cancellationToken = default)
    {
        var changes = new EditionTextChanges();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // What is there now, before anything is written. Both branches need
        // it: to delete a line's index rows you have to know the words it
        // contributed, and after the write there is no way to find out.
        long existingId = 0;
        string existingText = string.Empty;
        await using (var look = conn.CreateCommand())
        {
            look.CommandText =
                "SELECT TextNodeId, Text FROM TextNodes WHERE EditionId = @EditionId AND CitationRef = @CitationRef " +
                "ORDER BY TextNodeId LIMIT 1;";
            look.Parameters.AddWithValue("@EditionId", editionId);
            look.Parameters.AddWithValue("@CitationRef", citationRef);
            await using var reader = await look.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existingId = reader.GetInt64(0);
                existingText = reader.GetString(1);
            }
        }

        await using var cmd = conn.CreateCommand();

        if (string.IsNullOrWhiteSpace(text))
        {
            cmd.CommandText =
                "DELETE FROM TextNodes WHERE EditionId = @EditionId AND CitationRef = @CitationRef;";
            cmd.Parameters.AddWithValue("@EditionId", editionId);
            cmd.Parameters.AddWithValue("@CitationRef", citationRef);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (existingId != 0) changes.Removed.Add((existingId, existingText));
            return changes;
        }

        var trimmed = text.Trim();

        // Updated in place when the line already exists, rather than deleted
        // and re-inserted. The old pair of statements gave the line a new
        // TextNodeId every time it was saved, and the word index is keyed on
        // that id - so each edit orphaned the line's index rows and the only
        // way to find them again was a scan of the whole index.
        if (existingId != 0)
        {
            if (string.Equals(existingText, trimmed, StringComparison.Ordinal))
            {
                // Still worth writing the sort order, which can move without
                // the text changing, but there is nothing for the index here.
                cmd.CommandText = "UPDATE TextNodes SET SortOrder = @SortOrder WHERE TextNodeId = @TextNodeId;";
                cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
                cmd.Parameters.AddWithValue("@TextNodeId", existingId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                return changes;
            }

            cmd.CommandText =
                "UPDATE TextNodes SET Text = @Text, SortOrder = @SortOrder WHERE TextNodeId = @TextNodeId;";
            cmd.Parameters.AddWithValue("@Text", trimmed);
            cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
            cmd.Parameters.AddWithValue("@TextNodeId", existingId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            changes.Rewritten.Add((existingId, existingText, trimmed));
            return changes;
        }

        cmd.CommandText = @"
            INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text)
            VALUES (@EditionId, @CitationRef, @SortOrder, @Text);
            SELECT last_insert_rowid();";

        cmd.Parameters.AddWithValue("@EditionId", editionId);
        cmd.Parameters.AddWithValue("@CitationRef", citationRef);
        cmd.Parameters.AddWithValue("@SortOrder", sortOrder);
        cmd.Parameters.AddWithValue("@Text", trimmed);

        var newId = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        changes.Added.Add((newId, trimmed));
        return changes;
    }

    /// <summary>Author/work/citation context for a single text node - used by the reception tracker.</summary>
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

    /// <summary>Stable corpus identity for persisting a passage in a research investigation.</summary>
    public async Task<PassageResearchIdentity?> GetPassageResearchIdentityAsync(
        long textNodeId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT w.WorkId,tn.TextNodeId,e.EditionId,a.Name,w.Title,w.CtsUrn,e.CtsUrn,
            tn.CitationRef,tn.Text,e.Language FROM TextNodes tn
            JOIN Editions e ON e.EditionId=tn.EditionId
            JOIN Works w ON w.WorkId=e.WorkId
            JOIN Authors a ON a.AuthorId=w.AuthorId
            WHERE tn.TextNodeId=@Id;";
        cmd.Parameters.AddWithValue("@Id", textNodeId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new PassageResearchIdentity(
            reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
            reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9));
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
    public async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount, string? Milestone)>> FindEchoesAsync(
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
        // One batched query rather than one per candidate word. This loop
        // previously cost two connections and two round-trips per word (the
        // per-call HasDataAsync check was itself a query), so a line with
        // fifteen candidate words meant sixty of them before a single echo
        // was found. The whole set now resolves in one.
        var frequencies = await CountTextNodesContainingWordsAsync(candidateWords, cancellationToken);

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
                , c.Milestone
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
    private async Task<Dictionary<string, int>> CountTextNodesContainingWordsAsync(
        IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        if (words.Count == 0) return frequencies;

        // Checked once for the whole batch, not once per word - and against
        // the same connection the query below uses, rather than a separate
        // connection just for the check.
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        var hasIndex = await WordIndexRepository.HasDataAsync(conn, cancellationToken);

        if (hasIndex)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;

            var paramNames = new List<string>(words.Count);
            for (var i = 0; i < words.Count; i++)
            {
                var normalized = WordNormalizer.Normalize(words[i]);
                paramNames.Add($"@w{i}");
                cmd.Parameters.AddWithValue($"@w{i}", normalized);
            }

            cmd.CommandText = $@"
                SELECT NormalizedWord, COUNT(DISTINCT TextNodeId)
                FROM WordIndex
                WHERE NormalizedWord IN ({string.Join(",", paramNames)})
                GROUP BY NormalizedWord;";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                frequencies[reader.GetString(0)] = reader.GetInt32(1);
            }

            // GROUP BY only returns rows for words that actually occur, so
            // anything absent genuinely has a count of zero. The caller
            // filters those out, but it should see them rather than find
            // the key missing entirely.
            foreach (var word in words)
            {
                var normalized = WordNormalizer.Normalize(word);
                frequencies.TryAdd(normalized, 0);
            }

            return frequencies;
        }

        // No word index built yet - fall back to a LIKE scan per word, the
        // same fallback the single-word version had. Genuinely slow, but
        // this path only runs before "Build Word Index" has ever been run.
        foreach (var word in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var likeCmd = conn.CreateCommand();
            likeCmd.CommandText = "SELECT COUNT(*) FROM TextNodes WHERE Text LIKE @Word;";
            likeCmd.Parameters.AddWithValue("@Word", $"%{word}%");
            frequencies[word] = Convert.ToInt32(await likeCmd.ExecuteScalarAsync(cancellationToken));
        }

        return frequencies;
    }

    /// <summary>
    /// Candidate lines containing any of the given rare words, restricted to
    /// the same edition kind (original-vs-translation) as the source and
    /// excluding the source line itself. Tries the word index first, same
    /// fallback story as CountTextNodesContainingWordsAsync. Capped at 3000
    /// raw rows so a LIKE fallback on a huge corpus can't run away -
    /// scoring/ranking happens afterward in FindEchoesAsync.
    /// </summary>
    private async Task<List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Milestone)>> FindTextNodesContainingAnyWordAsync(
        List<string> words, string editionKind, long excludeTextNodeId, CancellationToken cancellationToken)
    {
        var results = new List<(int, long, string, string, string, string, string?)>();

        // One connection for the method, whichever branch below ends up
        // running - the has-data check, the indexed query if it's there,
        // and the LIKE fallback if it isn't. This used to open up to three
        // separate connections for one logical lookup: one for the check,
        // one for the indexed query, one for the LIKE fallback (the last
        // two mutually exclusive, but the check's was always paid).
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        if (await WordIndexRepository.HasDataAsync(conn, cancellationToken))
        {
            var normalized = words.Select(WordNormalizer.Normalize).Where(w => w.Length > 0).Distinct().ToList();
            if (normalized.Count == 0) return results;

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
                SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text, tn.Milestone
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
                    reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }

            return results;
        }

        await using var likeCmd = conn.CreateCommand();
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
            SELECT w.WorkId, tn.TextNodeId, a.Name, w.Title, tn.CitationRef, tn.Text, tn.Milestone
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
                likeReader.GetString(3), likeReader.GetString(4), likeReader.GetString(5),
                likeReader.IsDBNull(6) ? null : likeReader.GetString(6)));
        }

        return results;
    }
}
