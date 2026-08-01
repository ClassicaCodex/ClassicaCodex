using ClassicaCodex.Core;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion;

public record WordIndexProgress(long NodesProcessed, long TotalNodes, long EntriesWritten, string Phase = "Indexing");

/// <summary>
/// Builds the inverted word index over every ingested line: tokenize, strip
/// accents, and record one (word, line) pair per distinct word in the line.
///
/// This is pure derived data - it can be rebuilt from the corpus at any time
/// and holds nothing the user created, so a full rebuild always starts by
/// clearing rather than trying to merge. ReindexEditionAsync is the
/// exception: a single edition re-indexed in place, for callers (right now,
/// just CreateTranslationForm) that create or update one edition's content
/// live and need the index to stay current for it without paying for a
/// whole-corpus rebuild every time.
/// </summary>
public class WordIndexService
{
    private readonly WordIndexRepository _wordIndexRepo = new();

    private const int ReadBatchSize = 20000;
    private const int WriteBatchSize = 200000;

    /// <summary>
    /// Recorded for a TextNode when TokenizeLine finds nothing indexable in
    /// it - a lacuna marker, a bare citation number, a line of pure
    /// punctuation. Without some row for that TextNodeId, it never appears
    /// in WordIndex at all, so GetIndexedTextNodeCountAsync's count of
    /// distinct indexed lines falls permanently short of the true line
    /// count by however many lines are like this - not because the index is
    /// stale, but because those lines were never going to contribute a word
    /// no matter how many times the build runs.
    ///
    /// That standing gap used to be indistinguishable from real staleness:
    /// SetupWizardForm compares indexed count against total count and calls
    /// any shortfall "out of date," so a handful of wordless lines meant the
    /// warning could never be resolved - it told the reader to rebuild
    /// something that rebuilding could never fix.
    ///
    /// The empty string is safe as a marker because it can never collide
    /// with a real search: WordNormalizer.Normalize returns "" for exactly
    /// this kind of content, and every caller that queries WordIndex already
    /// filters normalized forms to Length > 0 before building its query -
    /// this marker is what those filters were already guarding against
    /// receiving.
    /// </summary>
    private const string NoIndexableWordsMarker = "";

    public async Task BuildAsync(
        IProgress<WordIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Checkpointing first: a long session with many large sequential
        // writes can leave the WAL file substantially larger than the main
        // database file, and starting a big operation against an already-
        // bloated WAL is worth ruling out as a cause of slowness up front.
        progress?.Report(new WordIndexProgress(0, 0, 0, "Checkpointing database..."));
        await DbConnectionFactory.CheckpointAsync(cancellationToken);

        // Reported before either of these runs, specifically so the UI has
        // something to show during them - previously nothing was reported
        // until the main loop started, which made a slow count or clear
        // look like the whole operation had frozen.
        progress?.Report(new WordIndexProgress(0, 0, 0, "Counting existing lines..."));
        var totalNodes = await _wordIndexRepo.GetTextNodeCountAsync(cancellationToken);

        progress?.Report(new WordIndexProgress(0, totalNodes, 0, "Clearing previous index..."));
        await _wordIndexRepo.ClearAsync(cancellationToken);

        long afterId = 0;
        long nodesProcessed = 0;
        long entriesWritten = 0;
        var pending = new List<(string Word, long TextNodeId)>(WriteBatchSize);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await _wordIndexRepo.GetTextNodeBatchAsync(afterId, ReadBatchSize, cancellationToken);
            if (batch.Count == 0) break;

            foreach (var (textNodeId, text) in batch)
            {
                afterId = textNodeId;
                nodesProcessed++;

                var wordsInLine = 0;
                foreach (var word in TokenizeLine(text))
                {
                    pending.Add((word, textNodeId));
                    wordsInLine++;
                }

                // See NoIndexableWordsMarker: without this, a line with no
                // real words is invisible to WordIndex forever, and the
                // staleness check reads that as unfinished work.
                if (wordsInLine == 0) pending.Add((NoIndexableWordsMarker, textNodeId));
            }

            if (pending.Count >= WriteBatchSize)
            {
                await _wordIndexRepo.BulkInsertAsync(pending, cancellationToken);
                entriesWritten += pending.Count;
                pending.Clear();
            }

            progress?.Report(new WordIndexProgress(nodesProcessed, totalNodes, entriesWritten));
        }

        if (pending.Count > 0)
        {
            await _wordIndexRepo.BulkInsertAsync(pending, cancellationToken);
            entriesWritten += pending.Count;
        }

        // Built here rather than before the load - see ClearAsync. This is a
        // single large sort over everything just written, so it gets its own
        // progress message: it can run for minutes with no row-level
        // progress to report, and silence there reads as a freeze.
        progress?.Report(new WordIndexProgress(nodesProcessed, totalNodes, entriesWritten, "Building lookup index..."));
        await _wordIndexRepo.CreateIndexAsync(cancellationToken);

        progress?.Report(new WordIndexProgress(nodesProcessed, totalNodes, entriesWritten));
    }

    /// <summary>
    /// Re-indexes exactly one edition's current TextNodes - clears just this
    /// edition's existing entries first, then re-tokenizes and re-inserts
    /// from scratch. Meant to be called right after that edition's TextNodes
    /// themselves are rewritten, so the two never drift out of sync: if a
    /// caller clears and reinserts an edition's lines (getting fresh
    /// TextNodeIds every time, as CreateTranslationForm's incremental save
    /// does), the old index rows would otherwise point at ids that no
    /// longer exist rather than simply being absent.
    ///
    /// Deliberately not the same code path as BuildAsync - that one defers
    /// creating the lookup index until after a corpus-wide bulk load
    /// finishes, which only pays for itself at that scale. An edition is at
    /// most a few thousand lines; the index (already built, if BuildAsync
    /// has ever run) just gets maintained incrementally by SQLite as these
    /// rows go in, the same as any other ordinary insert.
    /// </summary>
    public async Task ReindexEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await _wordIndexRepo.DeleteByEditionAsync(editionId, cancellationToken);

        var nodes = await _wordIndexRepo.GetTextNodesByEditionAsync(editionId, cancellationToken);
        var pending = new List<(string Word, long TextNodeId)>();

        foreach (var (textNodeId, text) in nodes)
        {
            var wordsInLine = 0;
            foreach (var word in TokenizeLine(text))
            {
                pending.Add((word, textNodeId));
                wordsInLine++;
            }

            // Same reasoning as BuildAsync: a wordless line still needs a
            // row, or a translation that happens to be e.g. a single em dash
            // would silently uncount itself from every future staleness
            // check for this edition.
            if (wordsInLine == 0) pending.Add((NoIndexableWordsMarker, textNodeId));
        }

        await _wordIndexRepo.BulkInsertAsync(pending, cancellationToken);
    }

    /// <summary>
    /// One line's distinct, normalized, indexable words - shared by both
    /// BuildAsync and ReindexEditionAsync so a full rebuild and an
    /// incremental one can never quietly disagree on what counts as a word.
    /// Distinct per line: a word repeated in one line only needs one index
    /// entry, since the index answers "which lines contain this word", not
    /// "how many times".
    /// </summary>
    private static IEnumerable<string> TokenizeLine(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(WordNormalizer.Normalize)
            .Where(w => w.Length > 0 && w.Length <= 200)
            .Distinct(StringComparer.Ordinal);
}
