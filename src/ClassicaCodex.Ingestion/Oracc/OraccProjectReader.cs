using System.Text;
using System.Text.Json;

namespace ClassicaCodex.Ingestion.Oracc;

/// <summary>
/// One catalogue row, which is the metadata Oracc holds about a text apart
/// from the text itself. Internal because it is a parsing detail: what the
/// importer works from is <see cref="OraccText"/>, which has these folded in.
/// </summary>
internal sealed record OraccCatalogueEntry(
    string? Designation,
    string? Ruler,
    string? Genre,
    string? Period,
    string? Provenience,
    string? LanguageName,
    string? Credits,
    string? TranslationLanguages);

/// <summary>
/// Reads an extracted Oracc project download into <see cref="OraccProject"/>.
///
/// WHY THIS IS NOT TeiParser. Oracc does publish TEI per text, but only
/// through the website and only the transliteration - the same content this
/// reads, in a heavier format, one HTTP request at a time. The JSON download
/// is one file, offline, and carries the lemmatisation and the cuneiform
/// signs that the TEI view drops. So this is a new reader rather than a new
/// front end on the old one.
///
/// THE SHAPE. Every text is a "cdl" tree - Oracc's Corpus Data Language -
/// of nodes that each declare what they are:
///
///   "c"  a chunk: the text, a discourse, a sentence. Recurse into its cdl.
///   "d"  a discontinuity: an object, a surface, a line start, a break.
///        The ones that matter here are line starts, which is where a new
///        row begins.
///   "l"  a lemma, which is to say a word.
///
/// Lines are not nested inside anything. A line-start "d" node opens a line
/// and the words that follow belong to it until the next one, so this walks
/// the tree depth-first and accumulates rather than recursing per line.
///
/// WHAT IS NOT HERE, and it is the thing a reader will look for first:
/// translations. Oracc's open data ships transliteration, lemmatisation and
/// an English word-to-line search index, but not the translated prose -
/// verified against rinap/rinap1 and saao/saa01, neither of which carries a
/// translation node in any text. The prose exists on the website. For RINAP
/// it is Tadmor and Yamada's, published by Eisenbrauns, which is very
/// probably why it is not in a CC0 download. So an imported Oracc text has
/// an original and a word-by-word gloss, and whatever English stands beside
/// it has to come from somewhere this importer does not reach.
/// </summary>
public static class OraccProjectReader
{
    /// <summary>
    /// Reads the project rooted at <paramref name="projectDirectory"/> - the
    /// folder holding metadata.json and corpusjson/, which inside a download
    /// is two levels down (rinap/rinap1), not at the top of the zip.
    /// </summary>
    public static OraccProject Read(string projectDirectory)
    {
        var metadataPath = Path.Combine(projectDirectory, "metadata.json");
        if (!File.Exists(metadataPath))
            throw new FileNotFoundException(
                $"No metadata.json in '{projectDirectory}'. An Oracc download nests the project " +
                "one or two folders deep - rinap-rinap1.zip extracts to rinap/rinap1 - so this " +
                "wants the inner folder, not the top of the archive.", metadataPath);

        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var root = metadata.RootElement;
        var config = root.TryGetProperty("config", out var c) ? c : default;

        var project = new OraccProject
        {
            ProjectPath = String(root, "project"),
            Source = String(root, "source"),
            License = String(root, "license"),
            LicenseUrl = String(root, "license-url"),
            Name = config.ValueKind == JsonValueKind.Object ? String(config, "name") : string.Empty,
            Abbreviation = config.ValueKind == JsonValueKind.Object ? String(config, "abbrev") : string.Empty
        };

        var designations = ReadCatalogue(Path.Combine(projectDirectory, "catalogue.json"));

        var corpusDirectory = Path.Combine(projectDirectory, "corpusjson");
        if (!Directory.Exists(corpusDirectory)) return project;

        // Ordered by name so an import is reproducible: two runs over the same
        // download must produce the same library, and directory order is not
        // promised to be stable across machines or filesystems.
        foreach (var file in Directory.GetFiles(corpusDirectory, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var textId = Path.GetFileNameWithoutExtension(file);

            // Empty before malformed, because they are different facts about
            // the download and a reader chasing a missing text wants to know
            // which one they have.
            var content = File.ReadAllText(file);
            if (content.AsSpan().Trim().IsEmpty)
            {
                project.Skipped.Add(new OraccSkippedText(textId, "the file is empty"));
                continue;
            }

            try
            {
                designations.TryGetValue(textId, out var catalogue);
                project.Texts.Add(ReadText(content, textId, catalogue));
            }
            catch (JsonException e)
            {
                project.Skipped.Add(new OraccSkippedText(textId, $"the file is not valid JSON: {e.Message}"));
            }
        }

        return project;
    }

    /// <summary>
    /// What the catalogue knows about each text, which is a great deal more
    /// than its title: the ruler it belongs to, its genre and period, where it
    /// was excavated, and the credits line naming its editors and its licence.
    ///
    /// A project may ship no catalogue, or one missing entries for texts that
    /// exist, so every caller of this treats a miss as normal.
    /// </summary>
    private static Dictionary<string, OraccCatalogueEntry> ReadCatalogue(string path)
    {
        var entries = new Dictionary<string, OraccCatalogueEntry>(StringComparer.Ordinal);
        if (!File.Exists(path)) return entries;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("members", out var members)
            || members.ValueKind != JsonValueKind.Object) return entries;

        foreach (var entry in members.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object) continue;

            entries[entry.Name] = new OraccCatalogueEntry(
                Optional(entry.Value, "designation"),
                Optional(entry.Value, "ruler"),
                Optional(entry.Value, "genre"),
                Optional(entry.Value, "period"),
                Optional(entry.Value, "provenience"),
                Optional(entry.Value, "language"),
                Optional(entry.Value, "credits"),
                Languages(entry.Value, "trans"));
        }

        return entries;
    }

    /// <summary>
    /// One text. Takes the content rather than the path so the caller can tell
    /// an empty file from a malformed one before getting here, and so this is
    /// testable without a download on disk.
    /// </summary>
    internal static OraccText ReadText(string content, string fallbackId, OraccCatalogueEntry? catalogue)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        var textId = String(root, "textid");
        if (textId.Length == 0) textId = fallbackId;

        var text = new OraccText
        {
            TextId = textId,
            Designation = catalogue?.Designation ?? textId,
            Ruler = catalogue?.Ruler,
            Genre = catalogue?.Genre,
            Period = catalogue?.Period,
            Provenience = catalogue?.Provenience,
            LanguageName = catalogue?.LanguageName,
            Credits = catalogue?.Credits,
            TranslationLanguages = catalogue?.TranslationLanguages
        };

        if (!root.TryGetProperty("cdl", out var cdl) || cdl.ValueKind != JsonValueKind.Array) return text;

        OraccLine? current = null;
        foreach (var node in cdl.EnumerateArray()) Walk(node, text, ref current);

        // Texts that are entirely lost exist in the catalogue and carry no
        // words at all. Importing them would put empty rows in the reader, so
        // the caller gets the text with no lines and decides.
        return text;
    }

    /// <summary>
    /// Depth-first over the cdl tree, opening a line whenever a line-start
    /// appears and hanging every word that follows on whichever line is open.
    /// </summary>
    private static void Walk(JsonElement node, OraccText text, ref OraccLine? current)
    {
        if (node.ValueKind != JsonValueKind.Object) return;

        switch (String(node, "node"))
        {
            case "d":
                if (String(node, "type") == "line-start")
                {
                    // "label" is how the edition prints it and can be "2'" or
                    // "i 14"; "n" is a plain counter. Prefer the label, since
                    // that is what a citation refers to.
                    var label = String(node, "label");
                    if (label.Length == 0) label = String(node, "n");

                    current = new OraccLine { Label = label, Reference = String(node, "ref") };
                    text.Lines.Add(current);
                }
                return;

            case "l":
                // A word before any line-start is possible in a malformed or
                // unusual text. Dropping it would lose text silently, so it
                // opens an unlabelled line instead.
                if (current == null)
                {
                    current = new OraccLine { Label = string.Empty, Reference = String(node, "ref") };
                    text.Lines.Add(current);
                }

                current.Words.Add(ReadWord(node));
                return;

            default:
                if (node.TryGetProperty("cdl", out var children) && children.ValueKind == JsonValueKind.Array)
                    foreach (var child in children.EnumerateArray())
                        Walk(child, text, ref current);
                return;
        }
    }

    private static OraccWord ReadWord(JsonElement node)
    {
        var f = node.TryGetProperty("f", out var form) && form.ValueKind == JsonValueKind.Object ? form : default;

        // "frag" is the form as it stands in this line, including any editorial
        // marking; f.form is the cleaner form. Prefer frag and fall back, so
        // what the reader sees matches what the edition printed.
        var written = String(node, "frag");
        if (written.Length == 0 && f.ValueKind == JsonValueKind.Object) written = String(f, "form");

        return new OraccWord
        {
            Form = written,
            IsNormalised = f.ValueKind == JsonValueKind.Object && String(f, "form") == "*",
            Cuneiform = f.ValueKind == JsonValueKind.Object ? ReadSigns(f) : string.Empty,
            Language = f.ValueKind == JsonValueKind.Object ? String(f, "lang") : string.Empty,
            CitationForm = Optional(f, "cf"),
            GuideWord = Optional(f, "gw"),
            Sense = Optional(f, "sense"),
            Normalized = Optional(f, "norm"),
            PartOfSpeech = Optional(f, "pos")
        };
    }

    /// <summary>
    /// The cuneiform for one word, gathered from its grapheme description.
    ///
    /// Oracc nests the description: f.gdl is a list of groups, a group holds
    /// signs or further groups, and a sign carries its glyph in "utf8". A
    /// compound sign is a group inside a group, so this recurses rather than
    /// reading two levels and hoping.
    ///
    /// Signs are concatenated with nothing between them, which is how
    /// cuneiform is written - the word-level separator is the space this
    /// reader's caller puts between words. Every glyph here is outside the
    /// Basic Multilingual Plane; see LetterRuns for what that costs anything
    /// that walks a string by char.
    /// </summary>
    private static string ReadSigns(JsonElement f)
    {
        if (!f.TryGetProperty("gdl", out var gdl) || gdl.ValueKind != JsonValueKind.Array) return string.Empty;

        var signs = new StringBuilder();
        foreach (var group in gdl.EnumerateArray()) Gather(group, signs);
        return signs.ToString();
    }

    private static void Gather(JsonElement node, StringBuilder signs)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var child in node.EnumerateArray()) Gather(child, signs);
                return;

            case JsonValueKind.Object:
                if (node.TryGetProperty("utf8", out var glyph) && glyph.ValueKind == JsonValueKind.String)
                    signs.Append(glyph.GetString());

                // A sign can carry a glyph AND contain a group - a determinative
                // written inside a compound - so both are taken, not one or the
                // other.
                foreach (var key in new[] { "group", "gdl", "seq" })
                    if (node.TryGetProperty(key, out var nested)) Gather(nested, signs);
                return;
        }
    }

    private static string String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// The translation languages a catalogue entry lists. Oracc writes them as
    /// an array - "trans": ["en"] - which a string read takes for absent, so
    /// every text looked untranslated; a plain string is accepted too.
    /// </summary>
    private static string? Languages(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;

        var languages = value.ValueKind switch
        {
            JsonValueKind.Array => string.Join(",", value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString())
                .Where(v => !string.IsNullOrWhiteSpace(v))),
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => string.Empty
        };

        return languages.Length == 0 ? null : languages;
    }

    private static string? Optional(JsonElement element, string name)
    {
        var value = String(element, name);
        return value.Length == 0 ? null : value;
    }
}
