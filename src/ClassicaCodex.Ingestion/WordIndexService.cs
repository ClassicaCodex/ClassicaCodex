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
/// and holds nothing the user created, so a rebuild always starts by
/// clearing rather than trying to merge.
/// </summary>
public class WordIndexService
{
    private readonly WordIndexRepository _wordIndexRepo = new();

    private const int ReadBatchSize = 20000;
    private const int WriteBatchSize = 200000;

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

                // Distinct per line: a word repeated in one line only needs
                // one index entry, since the index answers "which lines
                // contain this word", not "how many times".
                var words = text
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(WordNormalizer.Normalize)
                    .Where(w => w.Length > 0 && w.Length <= 200)
                    .Distinct(StringComparer.Ordinal);

                foreach (var word in words)
                {
                    pending.Add((word, textNodeId));
                }
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

        progress?.Report(new WordIndexProgress(nodesProcessed, totalNodes, entriesWritten));
    }
}
