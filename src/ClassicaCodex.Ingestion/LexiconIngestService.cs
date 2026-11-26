using System.Text;
using System.Xml;
using System.Xml.Linq;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Lexicon;
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

    // Markup that shouldn't end up in the readable entry text: an author or
    // title standing outside a citation. Inside one they are part of what the
    // citation prints, and a citation is kept whole - see AppendText.
    private static readonly string[] SkipElementNames = { "biblscope", "title", "author" };

    // Perseus lexicon filenames pack language and lexicon into dotted /
    // hyphenated tokens: grc.lsj.perseus-eng3.xml, lat.ls.perseus-eng1.xml.
    private static readonly char[] TokenSeparators = { '.', '-', '_', ' ' };

    // Counted in what a reader sees - the text and each citation as printed,
    // not the passages the citations name. Twice the old cap, which counted a
    // text with no citations in it: with them, 30 of LSJ's and Lewis and
    // Short's 268,084 rows are cut, where 91 were before - the article, logos
    // and the rest that were always too long.
    private const int MaxEntryLength = 40000;

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
        foreach (var entry in ReadEntryElements(path, cancellationToken))
        foreach (var definition in BuildDefinitions(entry, language, sourceName))
            yield return definition;
    }

    /// <summary>
    /// Every entry element of one lexicon file, with the language and lexicon
    /// the file belongs to - the raw TEI, for the tool that measures the
    /// citations against a library and has to see what each one quotes.
    /// </summary>
    public static IEnumerable<(XElement Entry, string Language, string Source)> ReadEntryElements(
        string file, string fallbackLanguage, string fallbackSource, CancellationToken cancellationToken = default)
    {
        var (language, source) = DetectLanguageAndSource(file, fallbackLanguage, fallbackSource);
        foreach (var entry in ReadEntryElements(file, cancellationToken)) yield return (entry, language, source);
    }

    private static IEnumerable<XElement> ReadEntryElements(string path, CancellationToken cancellationToken)
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

                yield return entry;
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
        var (text, citations) = FlattenEntry(entry, language);
        if (string.IsNullOrWhiteSpace(LexiconCitationMarkup.Plain(text))) yield break;

        var (cut, kept) = LexiconCitationMarkup.Truncate(text, MaxEntryLength);
        if (cut.Length != text.Length)
        {
            text = cut;
            citations = citations.Where(c => c.Ordinal < kept).ToList();
        }

        // The citations go with the first row only: the others are the same
        // entry under another spelling, and the passages it cites should list
        // it once.
        var first = true;

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
                Source = sourceName,
                Citations = first ? citations : Array.Empty<LexiconCitation>()
            };
            first = false;
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
    /// Flattens an entry's markup into readable text, with its citations kept
    /// in place and marked off (see <see cref="LexiconCitationMarkup"/>), and
    /// the citations that name a passage listed for the citations table.
    ///
    /// They used to be dropped, on the reasoning that a lexicon entry is dense
    /// with references and they would bury the definition. That left Word
    /// Study quoting Greek and Latin without saying where from, which to a
    /// classicist makes the entry look broken; the citations are what LSJ and
    /// Lewis and Short are for. Word Study sets them back in a smaller, fainter
    /// face instead, so the definition still reads first.
    /// </summary>
    public static (string Text, List<LexiconCitation> Citations) FlattenEntry(XElement entry, string language)
    {
        var state = new FlattenState(new LexiconCitationReader(language));
        AppendText(entry, state, senseDepth: 0);
        return (CollapseWhitespace(state.Text.ToString()), state.Citations);
    }

    private sealed class FlattenState(LexiconCitationReader reader)
    {
        public StringBuilder Text { get; } = new();
        public LexiconCitationReader Reader { get; } = reader;
        public List<LexiconCitation> Citations { get; } = new();
        public SortedDictionary<int, string> Senses { get; } = new();
        public int Ordinal { get; set; }

        /// <summary>The sense being read, as the dictionary numbers it: "A.II.2". Null before the first.</summary>
        public string? Sense
        {
            get
            {
                var path = string.Join('.', Senses.Values.Where(v => v.Length > 0));
                return path.Length > 0 ? path : null;
            }
        }
    }

    private static void AppendText(XElement element, FlattenState state, int senseDepth)
    {
        var sb = state.Text;
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value).Append(' ');
                    break;

                case XElement child:
                    var name = child.Name.LocalName;

                    // A citation, whole: what it prints, and the passage it
                    // names where it names one that can be trusted.
                    if (string.Equals(name, "bibl", StringComparison.OrdinalIgnoreCase))
                    {
                        var label = LexiconCitationReader.Label(child);
                        if (label.Length == 0) continue;

                        var target = state.Reader.Read(child);
                        if (target != null) state.Citations.Add(new LexiconCitation(state.Ordinal, target, state.Sense));
                        state.Ordinal++;
                        sb.Append(LexiconCitationMarkup.Write(target, label)).Append(' ');
                        continue;
                    }

                    if (SkipElementNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                    // Start a new line at each numbered sense so long
                    // entries stay readable instead of running together.
                    if (string.Equals(name, "sense", StringComparison.OrdinalIgnoreCase))
                    {
                        var n = child.Attribute("n")?.Value?.Trim() ?? string.Empty;
                        sb.AppendLine();
                        if (n.Length > 0) sb.Append(n).Append(". ");

                        // LSJ and Lewis and Short set their senses side by
                        // side and say how deep each is in @level; nesting is
                        // the fallback for a dictionary that nests instead.
                        var level = int.TryParse(child.Attribute("level")?.Value, out var stated) ? stated : senseDepth + 1;
                        foreach (var deeper in state.Senses.Keys.Where(k => k >= level).ToList()) state.Senses.Remove(deeper);
                        state.Senses[level] = n;

                        AppendText(child, state, level);
                        continue;
                    }

                    AppendText(child, state, senseDepth);
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
