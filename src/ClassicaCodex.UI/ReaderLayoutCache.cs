using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// Remembers where a work's passages were cut into rows, and how tall each row
/// came out, so that opening it again does not have to work it out afresh.
///
/// Worth doing because of what the work costs: dividing a passage is arithmetic
/// and nearly free, but every row's height has to come from Windows text
/// measurement, which costs about a millisecond whatever the row says. Pliny's
/// Natural History is about 17,900 rows across its two panes, so reopening it
/// is twenty seconds of measuring text that was measured last time.
///
/// ONLY LARGE WORKS ARE KEPT. Not by guessing which those are - length is a
/// poor predictor, since fifteen thousand lines of verse cost less than three
/// thousand paragraphs of prose - but by timing the work and keeping it only if
/// it actually took long enough to be worth keeping. A work that opens quickly
/// never touches any of this, which is most of them, and which is the point:
/// the only texts exposed to a cache are the ones a cache is for.
///
/// A STALE ENTRY CANNOT SHOW THE WRONG TEXT. The file holds lengths, not text.
/// Rows are always sliced out of the passages as they are in the database now,
/// and an entry is only used if every passage's pieces add up to exactly the
/// length that passage currently has. Anything else - a re-ingest, an edited
/// translation, a truncated file - fails that check and is thrown away, and the
/// work is divided again from scratch. The worst a bad entry can do is waste
/// the time it was meant to save.
/// </summary>
internal static class ReaderLayoutCache
{
    /// <summary>
    /// How long a work has to take before its layout is worth keeping.
    ///
    /// Below this, the cache would be all risk and no benefit: the work reopens
    /// in about the time it takes to read the file back, and every entry
    /// written is another chance to be wrong about something. Pliny is twenty
    /// seconds; Herodotus is three; most works are well under one.
    /// </summary>
    internal const int WorthKeepingMilliseconds = 1500;

    /// <summary>
    /// Where entries are kept. Settable so that tests write somewhere of their
    /// own rather than into the reader's real settings folder.
    /// </summary>
    internal static string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "layout-cache");

    /// <summary>
    /// How many entries to keep. One per edition per pane width per font size,
    /// so a reader who drags the splitter about accumulates them; the oldest go
    /// first. Each is a few tens of kilobytes at most.
    /// </summary>
    internal const int MaxEntries = 40;

    // 2 added a hash of each passage's text. Entries written by an earlier
    // version are refused rather than upgraded, which costs one slow open of
    // each cached work and then they are rewritten.
    private const int Version = 2;

    /// <summary>
    /// Everything an entry has to match to be usable. A change to any of it
    /// means the cuts would fall somewhere else.
    /// </summary>
    /// <param name="LineHeight">
    /// What one line of the font actually measures, in pixels.
    ///
    /// Present because <paramref name="FontSize"/> is in points and every
    /// number in the file is in pixels, so the two are related by the display
    /// scaling and the key could not see it. The same 12pt Palatino is 22px a
    /// line at 100% and 27px at 125%, and a reader who changes their scaling -
    /// or drags the window to a second monitor with a different one - would
    /// have last session's entry accepted for this session's pixels wherever
    /// the pane's usable width happened to coincide. Every row would then be
    /// given a height measured for smaller text.
    /// </param>
    internal readonly record struct Key(
        int EditionId, int Width, string FontFamily, float FontSize, int LineHeight)
    {
        internal string FileName =>
            $"e{EditionId}-w{Width}-{Sanitise(FontFamily)}-{FontSize:0.##}-h{LineHeight}.layout";

        private static string Sanitise(string family)
        {
            var clean = family.Where(char.IsLetterOrDigit).ToArray();
            return clean.Length > 0 ? new string(clean) : "font";
        }
    }

    /// <summary>
    /// A stable fingerprint of a passage's text.
    ///
    /// Stable is the whole point: <see cref="string.GetHashCode()"/> is seeded
    /// per process in .NET, so it cannot say anything about a file written by
    /// a previous run. FNV-1a over the UTF-16 units is a few lines, needs no
    /// package, and costs a few milliseconds across a whole work - against the
    /// ten to twenty seconds the entry exists to avoid.
    ///
    /// It is not defending against anyone; it is defending against a passage
    /// that was re-ingested or re-saved between two runs and happens to have
    /// the same length as before.
    /// </summary>
    private static long Fingerprint(string text)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;

            foreach (var c in text)
            {
                hash = (hash ^ c) * 1099511628211UL;
            }

            return (long)hash;
        }
    }

    /// <summary>
    /// The rows a previous run worked out, or null when there is nothing usable
    /// - no entry, an entry for different text, or a file that will not read.
    ///
    /// The passages are the ones the caller is about to show, and every row
    /// comes out of them, so the text shown is always the text the database has
    /// right now regardless of what the file says.
    /// </summary>
    internal static (List<ReaderRow> Rows, Dictionary<string, int> Heights)? TryLoad(
        Key key, IReadOnlyList<TextNode> nodes)
    {
        try
        {
            var path = Path.Combine(Directory, key.FileName);
            if (!File.Exists(path)) return null;

            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (reader.ReadInt32() != Version) return null;
            if (reader.ReadInt32() != nodes.Count) return null;

            var rows = new List<ReaderRow>(nodes.Count);
            var heights = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var node in nodes)
            {
                // In order, and matched on identity: an entry whose passages
                // are not these passages, in this order, is not this work.
                if (reader.ReadInt64() != node.TextNodeId) return null;

                // And matched on content, not merely on length. The lengths
                // adding up is what stops a stale entry from slicing text that
                // is not there; it is not enough to stop one from giving a row
                // a height measured for text that has since been replaced by
                // the same number of different characters. That happens: a
                // re-ingest refresh and a saved translation both update Text in
                // place and deliberately keep the TextNodeId. The height would
                // be handed to the control unchallenged and the surplus line
                // clipped away with no marker, since a row under 255px does not
                // count as truncated - which is the exact defect that showing
                // passages whole was meant to end.
                if (reader.ReadInt64() != Fingerprint(node.Text)) return null;

                var segmentCount = reader.ReadInt32();
                if (segmentCount <= 0) return null;

                var start = 0;
                for (var i = 0; i < segmentCount; i++)
                {
                    var length = reader.ReadInt32();
                    var height = reader.ReadInt32();

                    // The integrity check that makes a stale entry harmless
                    // rather than dangerous: a piece that would run past the
                    // end of the passage means the text has changed underneath.
                    if (length <= 0 || start + length > node.Text.Length) return null;

                    var text = node.Text.Substring(start, length);
                    rows.Add(new ReaderRow(node, text, i, segmentCount, height));
                    heights[text] = height;
                    start += length;
                }

                // And every passage has to be accounted for exactly - no
                // characters left over at the end, none missing.
                if (start != node.Text.Length) return null;
            }

            return (rows, heights);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or EndOfStreamException or ArgumentException
                                      or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Keeps this layout, if the work took long enough to be worth keeping.
    ///
    /// Failing to write is not worth telling anyone about: the next open simply
    /// works it out again, which is what it did before any of this existed.
    /// </summary>
    internal static void Save(
        Key key, IReadOnlyList<ReaderRow> rows, Func<string, int> heightOf, int tookMilliseconds)
    {
        if (tookMilliseconds < WorthKeepingMilliseconds) return;

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            // Written beside and moved into place, so that a run interrupted
            // half way through leaves the previous entry rather than a
            // half-written one. A truncated file would be rejected on load
            // anyway; this just avoids losing a good entry to a bad moment.
            var path = Path.Combine(Directory, key.FileName);
            var temporary = path + ".partial";

            using (var stream = File.Create(temporary))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Version);

                var passages = rows.Where(r => r.IsFirst).ToList();
                writer.Write(passages.Count);

                var index = 0;
                foreach (var first in passages)
                {
                    writer.Write(first.Node.TextNodeId);
                    writer.Write(Fingerprint(first.Node.Text));
                    writer.Write(first.SegmentCount);

                    for (var i = 0; i < first.SegmentCount; i++)
                    {
                        var row = rows[index + i];
                        writer.Write(row.Text.Length);
                        writer.Write(heightOf(row.Text));
                    }

                    index += first.SegmentCount;
                }
            }

            File.Move(temporary, path, overwrite: true);
            Evict();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            // See above.
        }
    }

    /// <summary>Keeps the folder from growing without limit, oldest first.</summary>
    private static void Evict()
    {
        try
        {
            var files = new DirectoryInfo(Directory).GetFiles("*.layout");
            if (files.Length <= MaxEntries) return;

            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - MaxEntries))
            {
                file.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Nothing here is worth interrupting a reader over.
        }
    }
}
