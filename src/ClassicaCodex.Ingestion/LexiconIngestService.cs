using System.Text;
using System.Xml;
using System.Xml.Linq;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion;

public record LexiconIngestProgress(string CurrentFile, int FilesProcessed, int TotalFiles, int EntriesLoaded);

/// <summary>
/// Loads dictionary entries from Perseus's published lexica - LSJ for Greek,
/// Lewis &amp; Short for Latin. Both are long out of copyright and Perseus
/// publishes them as open TEI XML.
///
/// Same tolerance policy as the lemma loader: the exact element and
/// attribute naming varies between lexica and between releases of the same
/// lexicon, so this accepts several conventions rather than assuming one.
/// If a run loads zero entries, add the real names to EntryElementNames /
/// HeadwordAttributeNames below after looking at one file.
/// </summary>
public class LexiconIngestService
{
    private readonly DefinitionRepository _definitionRepo = new();

    // Perseus lexica mark an entry with <entryFree>; some older conversions
    // use <entry> or a numbered <div>.
    private static readonly string[] EntryElementNames = { "entryfree", "entry", "div2" };

    // The headword is usually an attribute on the entry, and also appears as
    // an <orth> child element.
    private static readonly string[] HeadwordAttributeNames = { "key", "sortkey", "n" };
    private static readonly string[] HeadwordElementNames = { "orth" };

    // Markup that shouldn't end up in the readable entry text.
    private static readonly string[] SkipElementNames = { "bibl", "biblscope", "title", "author" };

    // Perseus lexicon filenames pack language and lexicon into dotted /
    // hyphenated tokens: grc.lsj.perseus-eng3.xml, lat.ls.perseus-eng1.xml.
    private static readonly char[] TokenSeparators = { '.', '-', '_', ' ' };

    private const int MaxEntryLength = 20000;

    /// <param name="path">Folder or single file to ingest lexicon entries from.</param>
    /// <param name="fallbackLanguage">
    /// Language ("grc"/"lat") to fall back on for a file whose own name and
    /// folder don't reveal one. Detection is per file (see
    /// <see cref="DetectLanguageAndSource"/>), so this is only used when a
    /// file can't identify itself - not stamped across the whole tree.
    /// </param>
    /// <param name="fallbackSource">
    /// Lexicon name to fall back on when the file can't identify its own and
    /// the language doesn't imply one.
    /// </param>
    /// <param name="progress">Reports files processed as the run proceeds.</param>
    /// <param name="cancellationToken">Cancels a run in progress.</param>
    public async Task IngestAsync(
        string path,
        string fallbackLanguage,
        string fallbackSource,
        IProgress<LexiconIngestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Folder not found: {path}");

        var files = Directory.GetFiles(path, "*.xml", SearchOption.AllDirectories);
        if (files.Length == 0)
            throw new InvalidOperationException($"No .xml files found anywhere under {path}.");

        var batch = new List<Definition>();
        var totalLoaded = 0;
        Exception? lastFailure = null;

        for (var i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[i];

            // Each file names its own language and lexicon; the dropdown is
            // only a fallback. This is what lets the loader be pointed at a
            // repository root holding both dictionaries and tag each entry
            // correctly, instead of stamping every entry with one language
            // (which filed Latin under grc and vice versa, corrupted the
            // normalized headwords, and left Word Study finding nothing).
            var (fileLanguage, fileSource) = DetectLanguageAndSource(file, fallbackLanguage, fallbackSource);

            progress?.Report(new LexiconIngestProgress(Path.GetFileName(file), i, files.Length, totalLoaded));

            try
            {
                foreach (var definition in ExtractFromFile(file, fileLanguage, fileSource, cancellationToken))
                {
                    batch.Add(definition);

                    if (batch.Count >= 2000)
                    {
                        await _definitionRepo.BulkInsertAsync(batch, cancellationToken);
                        totalLoaded += batch.Count;
                        batch.Clear();

                        // A lexicon is often one enormous file, so per-file
                        // progress would sit still for minutes. Report on
                        // each flush instead.
                        progress?.Report(new LexiconIngestProgress(
                            Path.GetFileName(file), i, files.Length, totalLoaded));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Keep going through the remaining files, but hold onto the
                // reason so a completely empty run can explain itself
                // instead of just reporting zero.
                lastFailure = ex;
            }
        }

        if (batch.Count > 0)
        {
            await _definitionRepo.BulkInsertAsync(batch, cancellationToken);
            totalLoaded += batch.Count;
        }

        if (totalLoaded == 0 && lastFailure != null)
            throw new InvalidOperationException(
                $"No entries could be read. First failure: {lastFailure.Message}", lastFailure);

        progress?.Report(new LexiconIngestProgress("Done", files.Length, files.Length, totalLoaded));
    }

    /// <summary>
    /// Works out which language and lexicon a file belongs to from its own
    /// name and folder, rather than trusting a single dropdown value across
    /// a whole tree.
    ///
    /// Perseus names these unambiguously. The language is the leading
    /// filename token and also the containing folder -
    /// .../pdllex/grc/grc.lsj.perseus-eng3.xml,
    /// .../pdllex/lat/lat.ls.perseus-eng1.xml - and the lexicon is the
    /// lsj / ls token. Getting the language right per file matters for more
    /// than tagging: it decides whether headwords go through Beta Code
    /// decoding and which u/v-i/j folding the normalizer applies, so a
    /// mis-tagged entry stores a normalized form nothing can ever match.
    ///
    /// The passed-in values are used only when a file reveals neither.
    /// </summary>
    private static (string Language, string Source) DetectLanguageAndSource(
        string filePath, string fallbackLanguage, string fallbackSource)
    {
        var fileTokens = Path.GetFileName(filePath)
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);

        // Folder names too, so a file under .../pdllex/grc/ is still placed
        // correctly even if it were named less predictably than Perseus does.
        var dirSegments = (Path.GetDirectoryName(filePath) ?? string.Empty)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                   StringSplitOptions.RemoveEmptyEntries);

        static string? MatchLanguage(string token) =>
            token.Equals("grc", StringComparison.OrdinalIgnoreCase) ? "grc"
            : token.Equals("lat", StringComparison.OrdinalIgnoreCase) ? "lat"
            : null;

        // Filename first (most specific), then folder. Whole-token equality,
        // so "lat" in some longer word can't be mistaken for the language.
        var language = fileTokens.Select(MatchLanguage).FirstOrDefault(l => l != null)
                       ?? dirSegments.Select(MatchLanguage).FirstOrDefault(l => l != null);

        static string? MatchSource(string token) =>
            token.Equals("lsj", StringComparison.OrdinalIgnoreCase) ? "LSJ"
            : token.Equals("ls", StringComparison.OrdinalIgnoreCase) ? "Lewis & Short"
            : null;

        var source = fileTokens.Select(MatchSource).FirstOrDefault(s => s != null);

        language ??= fallbackLanguage;

        // With the language known, the lexicon follows from it - which is
        // exactly the pairing both callers used to spell out by hand.
        source ??= language switch
        {
            "grc" => "LSJ",
            "lat" => "Lewis & Short",
            _ => fallbackSource
        };

        return (language, source);
    }

    /// <summary>
    /// Streams entries out of one lexicon file.
    ///
    /// These files can't be handed straight to an XML parser. They open with
    /// a TEI P4 DOCTYPE whose entity set lives in a DTD on perseus.tufts.edu,
    /// so the parser either refuses the DTD outright or treats every named
    /// entity in the file as undeclared and fails. They're also 40-80MB, far
    /// too big to hold as a DOM.
    ///
    /// So the file is first rewritten to a temp copy - DOCTYPE removed,
    /// entities resolved to real characters - and then read with a streaming
    /// reader that materializes only one entry at a time.
    /// </summary>
    private static IEnumerable<Definition> ExtractFromFile(
        string path, string language, string sourceName, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"classicacodex-lex-{Guid.NewGuid():N}.xml");

        try
        {
            PreprocessFile(path, tempPath, cancellationToken);

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                CheckCharacters = false,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };

            using var reader = XmlReader.Create(tempPath, settings);

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.NodeType != XmlNodeType.Element) continue;
                if (!EntryElementNames.Contains(reader.LocalName, StringComparer.OrdinalIgnoreCase)) continue;

                XElement entry;
                try
                {
                    entry = XElement.Load(reader.ReadSubtree());
                }
                catch (XmlException)
                {
                    continue; // one bad entry shouldn't end the file
                }

                foreach (var definition in BuildDefinitions(entry, language, sourceName))
                {
                    yield return definition;
                }
            }
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>
    /// Copies the file with the DOCTYPE stripped and named entities
    /// resolved. Line-by-line so memory stays flat regardless of file size;
    /// entity references don't span lines, so this is safe.
    /// </summary>
    private static void PreprocessFile(string sourcePath, string destPath, CancellationToken cancellationToken)
    {
        using var input = new StreamReader(sourcePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var output = new StreamWriter(destPath, false, new UTF8Encoding(false));

        var inDoctype = false;
        var doctypeHandled = false;
        string? line;

        while ((line = input.ReadLine()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!doctypeHandled)
            {
                if (!inDoctype)
                {
                    var start = line.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
                    if (start >= 0)
                    {
                        inDoctype = true;
                        var before = line[..start];

                        // A single-line DOCTYPE with no internal subset ends
                        // at the first '>' after it.
                        if (!line.Contains('[', StringComparison.Ordinal))
                        {
                            var close = line.IndexOf('>', start);
                            if (close >= 0)
                            {
                                inDoctype = false;
                                doctypeHandled = true;
                                output.WriteLine(XmlEntitySanitizer.Sanitize(before + line[(close + 1)..]));
                                continue;
                            }
                        }

                        if (before.Trim().Length > 0) output.WriteLine(XmlEntitySanitizer.Sanitize(before));
                        line = line[start..];
                    }
                }

                if (inDoctype)
                {
                    // The internal subset closes with ']>' - anything after
                    // that on the same line is real document content.
                    var end = line.IndexOf("]>", StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        inDoctype = false;
                        doctypeHandled = true;

                        var rest = line[(end + 2)..];
                        if (rest.Trim().Length > 0) output.WriteLine(XmlEntitySanitizer.Sanitize(rest));
                    }
                    continue;
                }
            }

            output.WriteLine(XmlEntitySanitizer.Sanitize(line));
        }
    }

    private static IEnumerable<Definition> BuildDefinitions(XElement entry, string language, string sourceName)
    {
        var text = FlattenEntry(entry);
        if (string.IsNullOrWhiteSpace(text)) yield break;

        if (text.Length > MaxEntryLength) text = text[..MaxEntryLength] + "...";

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var isGreek = string.Equals(language, "grc", StringComparison.OrdinalIgnoreCase);

        foreach (var candidate in ReadHeadwords(entry))
        {
            // LSJ's @key and <orth> both turn out to be Beta Code (i)a/ for
            // ἰά), not Unicode Greek - confirmed by inspecting the raw XML
            // rather than assumed. Without converting, every Greek headword
            // gets indexed under a string a Unicode search can never match.
            // Latin entries need no conversion; they're already Latin script.
            var headword = isGreek ? BetaCodeConverter.Convert(candidate) : candidate.Trim();
            if (headword.Length == 0 || headword.Length > 200) continue;

            var normalized = WordNormalizer.NormalizeHeadword(headword, language);
            if (normalized.Length == 0) continue;
            if (!seenKeys.Add(normalized)) continue;

            yield return new Definition
            {
                Headword = headword,
                NormalizedHeadword = normalized,
                Language = language,
                Entry = text,
                Source = sourceName
            };
        }
    }

    /// <summary>
    /// Every form an entry can be looked up by, most useful first.
    ///
    /// LSJ writes both its @key attribute and its &lt;orth&gt; element in
    /// Beta Code, not Unicode - confirmed directly from the XML rather than
    /// assumed, after an earlier guess here turned out wrong. Both are still
    /// collected (rather than picking one), since a lexicon that happens to
    /// vary its convention between entries is exactly the kind of thing
    /// worth being defensive about; BuildDefinitions converts whichever
    /// candidates come back through Beta Code decoding for Greek entries.
    /// </summary>
    private static IEnumerable<string> ReadHeadwords(XElement entry)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);

        // <orth> first - it's the human-readable form, and in Greek lexica
        // it's the Unicode one.
        var orthElements = entry.Elements()
            .Where(e => HeadwordElementNames.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (orthElements.Count == 0)
        {
            orthElements = entry.Descendants()
                .Where(e => HeadwordElementNames.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
                .Take(3)
                .ToList();
        }

        foreach (var orth in orthElements)
        {
            var value = orth.Value.Trim();
            if (value.Length > 0 && emitted.Add(value)) yield return value;
        }

        foreach (var name in HeadwordAttributeNames)
        {
            var attr = entry.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            if (attr == null) continue;

            var value = attr.Value.Trim();
            if (value.Length > 0 && emitted.Add(value)) yield return value;
        }
    }

    /// <summary>
    /// Flattens an entry's markup into readable text, dropping the citation
    /// apparatus. A full lexicon entry is dense with references to ancient
    /// sources; keeping them would bury the actual definition, and the app
    /// already has its own way of finding passages.
    /// </summary>
    private static string FlattenEntry(XElement entry)
    {
        var sb = new StringBuilder();
        AppendText(entry, sb);
        return CollapseWhitespace(sb.ToString());
    }

    private static void AppendText(XElement element, StringBuilder sb)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value).Append(' ');
                    break;

                case XElement child:
                    if (SkipElementNames.Contains(child.Name.LocalName, StringComparer.OrdinalIgnoreCase)) continue;

                    // Start a new line at each numbered sense so long
                    // entries stay readable instead of running together.
                    if (string.Equals(child.Name.LocalName, "sense", StringComparison.OrdinalIgnoreCase))
                    {
                        var n = child.Attribute("n")?.Value;
                        sb.AppendLine();
                        if (!string.IsNullOrWhiteSpace(n)) sb.Append(n).Append(". ");
                    }

                    AppendText(child, sb);
                    break;
            }
        }
    }

    private static string CollapseWhitespace(string input)
    {
        var lines = input.Split('\n')
            .Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(line => line.Length > 0);

        return string.Join(Environment.NewLine, lines).Trim();
    }
}
