using System.Text;
using System.Text.RegularExpressions;

namespace ClassicaCodex.Core;

public sealed record BibliographyRecord(
    string ImportFormat,
    string EntryType,
    string? CiteKey,
    string Title,
    IReadOnlyList<string> Authors,
    string? Year,
    string? ContainerTitle,
    string? Volume,
    string? Issue,
    string? Pages,
    string? Publisher,
    string? Doi,
    string? Url,
    string? Isbn,
    string? Abstract,
    IReadOnlyList<string> Keywords)
{
    /// <summary>
    /// The identifier the importer de-duplicates on. An ISBN only counts for a record
    /// that is itself a book: on a chapter it is the volume's, and RIS writes a
    /// journal's ISSN into the same SN tag, so two chapters of one Companion, or two
    /// articles from one journal, used to share an identifier and the second was
    /// refused as a duplicate of the first.
    /// </summary>
    public string StableIdentifier => BibliographyImport.NormalizeDoi(Doi) is { } doi
        ? $"https://doi.org/{doi}"
        : !string.IsNullOrWhiteSpace(Isbn) && BibliographyImport.IsWholeBook(EntryType) ? $"isbn:{Isbn.Trim()}"
        : Url?.Trim() ?? string.Empty;

    public string DisplayTitle
    {
        get
        {
            var author = Authors.FirstOrDefault();
            if (author?.Contains(',') == true) author = author[..author.IndexOf(',')];
            var prefix = string.Join(" ", new[] { author, Year }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.IsNullOrWhiteSpace(prefix) ? Title : $"{prefix} — {Title}";
        }
    }

    /// <summary>
    /// A one-line citation, stored as the evidence's provenance. Built from parts
    /// joined once, so a part that already ends in a full stop - an initial, an
    /// abbreviated journal - does not get a second, and a part that is missing
    /// leaves no empty ". ." behind.
    /// </summary>
    public string FormatCitation()
    {
        var parts = new List<string?>();
        if (Authors.Count > 0) parts.Add(string.Join("; ", Authors));
        if (!string.IsNullOrWhiteSpace(Year)) parts.Add($"({Year.Trim()})");
        parts.Add(Title);
        parts.Add(ContainerTitle);
        var locator = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Volume)) locator.Append(Volume.Trim());
        if (!string.IsNullOrWhiteSpace(Issue)) locator.Append('(').Append(Issue.Trim()).Append(')');
        if (!string.IsNullOrWhiteSpace(Pages))
            locator.Append(string.IsNullOrWhiteSpace(Volume) ? "pp. " : ": ").Append(Pages.Trim());
        parts.Add(locator.ToString());
        parts.Add(Publisher);
        parts.Add(BibliographyImport.NormalizeDoi(Doi) is { } doi ? $"https://doi.org/{doi}" : Url);
        var builder = new StringBuilder();
        foreach (var part in parts.Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)))
        {
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(part);
            if (part![^1] is not ('.' or '?' or '!')) builder.Append('.');
        }
        return builder.ToString();
    }
}

/// <summary>Offline parser for bibliography exports; it performs no DOI or web lookup.</summary>
public static partial class BibliographyImport
{
    public static IReadOnlyList<BibliographyRecord> Parse(string text, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<BibliographyRecord>();
        var extension = Path.GetExtension(fileName ?? string.Empty);
        return extension.Equals(".ris", StringComparison.OrdinalIgnoreCase) ||
               (!extension.Equals(".bib", StringComparison.OrdinalIgnoreCase) && RisLine().IsMatch(text))
            ? ParseRis(text)
            : ParseBibTeX(text);
    }

    public static string? NormalizeDoi(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var doi = value.Trim();
        foreach (var prefix in new[]
                 {
                     "https://doi.org/", "http://doi.org/", "https://dx.doi.org/", "http://dx.doi.org/",
                     "doi.org/", "dx.doi.org/", "doi:"
                 })
            if (doi.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                doi = doi[prefix.Length..];
        doi = doi.Trim().TrimEnd('.', ',', ';');
        return doi.Length == 0 ? null : doi.ToLowerInvariant();
    }

    /// <summary>
    /// Whether an entry type names a whole book, so that an ISBN on it identifies it
    /// rather than the volume it is part of. RIS and BibTeX spellings both.
    /// </summary>
    public static bool IsWholeBook(string? entryType) => entryType?.Trim().ToUpperInvariant() is
        "BOOK" or "EBOOK" or "EDBOOK" or "MVBOOK" or "COLLECTION" or "MVCOLLECTION" or "PROCEEDINGS" or "BOOKLET";

    private static IReadOnlyList<BibliographyRecord> ParseRis(string text)
    {
        var records = new List<BibliographyRecord>();
        var fields = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? lastTag = null;
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var match = RisLine().Match(rawLine);
            if (!match.Success)
            {
                if (lastTag != null && !string.IsNullOrWhiteSpace(rawLine))
                {
                    var continuationValues = fields[lastTag];
                    continuationValues[^1] = continuationValues[^1] + " " + rawLine.Trim();
                }
                continue;
            }
            var tag = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim();
            lastTag = tag;
            if (tag.Equals("ER", StringComparison.OrdinalIgnoreCase))
            {
                AddRisRecord(fields, records);
                fields.Clear();
                lastTag = null;
                continue;
            }
            if (!fields.TryGetValue(tag, out var values)) fields[tag] = values = new List<string>();
            values.Add(value);
        }
        if (fields.Count > 0) AddRisRecord(fields, records);
        return records;
    }

    private static void AddRisRecord(
        IReadOnlyDictionary<string, List<string>> fields, ICollection<BibliographyRecord> records)
    {
        string? First(params string[] tags) => tags.SelectMany(t => fields.TryGetValue(t, out var v) ? v : [])
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        List<string> All(params string[] tags) => tags.SelectMany(t => fields.TryGetValue(t, out var v) ? v : [])
            .Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        var title = First("TI", "T1", "CT");
        if (string.IsNullOrWhiteSpace(title)) return;
        var start = First("SP");
        var end = First("EP");
        var pages = start == null ? null : end == null || end == start ? start : $"{start}-{end}";
        records.Add(new BibliographyRecord(
            "RIS", First("TY") ?? "GEN", First("ID"), title, All("AU", "A1"),
            YearPart(First("PY", "Y1", "DA")), First("JO", "JF", "T2", "BT"),
            First("VL"), First("IS"), pages, First("PB"), First("DO"), First("UR"),
            First("SN"), First("AB", "N2"), All("KW")));
    }

    private static IReadOnlyList<BibliographyRecord> ParseBibTeX(string text)
    {
        var records = new List<BibliographyRecord>();
        // @string abbreviations, plus the month names every BibTeX style predefines.
        var macros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["jan"] = "January", ["feb"] = "February", ["mar"] = "March", ["apr"] = "April",
            ["may"] = "May", ["jun"] = "June", ["jul"] = "July", ["aug"] = "August",
            ["sep"] = "September", ["oct"] = "October", ["nov"] = "November", ["dec"] = "December"
        };
        var position = 0;
        while ((position = text.IndexOf('@', position)) >= 0)
        {
            var typeStart = ++position;
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] is '-' or '_')) position++;
            var type = text[typeStart..position].Trim();
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position >= text.Length || text[position] is not ('{' or '(')) continue;
            var open = text[position++];
            var contentStart = position;
            if (!FindEntryEnd(text, ref position, open)) break;
            var content = text[contentStart..(position - 1)];

            if (type.Equals("comment", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("preamble", StringComparison.OrdinalIgnoreCase)) continue;
            if (type.Equals("string", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (name, value) in ParseBibFields(content, macros)) macros[name] = value;
                continue;
            }

            var comma = FindTopLevelComma(content);
            if (comma < 0) continue;
            var key = content[..comma].Trim();
            var fields = ParseBibFields(content[(comma + 1)..], macros);
            if (!fields.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(CleanBibText(title))) continue;
            string? Get(string name) => fields.TryGetValue(name, out var value) ? Empty(CleanBibText(value)) : null;
            string? Verbatim(string name) => fields.TryGetValue(name, out var value) ? Empty(CleanBibVerbatim(value)) : null;
            records.Add(new BibliographyRecord(
                "BibTeX", type.ToUpperInvariant(), key, CleanBibText(title),
                SplitBibNames(fields.TryGetValue("author", out var author) ? author : null),
                YearPart(Get("year") ?? Get("date")), Get("journal") ?? Get("journaltitle") ?? Get("booktitle"),
                Get("volume"), Get("number") ?? Get("issue"),
                fields.TryGetValue("pages", out var pages) ? Empty(CleanBibPages(pages)) : null, Get("publisher"),
                Verbatim("doi"), Verbatim("url"), Verbatim("isbn"), Get("abstract"),
                (Get("keywords") ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        }
        return records;
    }

    /// <summary>
    /// Advances past the end of an entry whose opening delimiter has just been read.
    /// Braces nest anywhere, inside a quoted value or not, so they are counted
    /// throughout. A quotation mark delimits a value only at the entry's own level:
    /// inside braces it is a character, as in <c>{M"uller}</c> or <c>{a 12" record}</c>,
    /// and treating it as an opening quote there used to swallow the rest of the file
    /// in search of its partner.
    /// </summary>
    private static bool FindEntryEnd(string text, ref int position, char open)
    {
        var braces = 0;
        var quoted = false;
        while (position < text.Length)
        {
            var c = text[position++];
            if (c == '\\') { position++; continue; }
            if (c == '{') { braces++; continue; }
            if (c == '}')
            {
                if (braces > 0) braces--;
                else if (open == '{') return true;
                continue;
            }
            if (braces > 0) continue;
            if (c == '"') { quoted = !quoted; continue; }
            if (!quoted && open == '(' && c == ')') return true;
        }
        return false;
    }

    private static Dictionary<string, string> ParseBibFields(string content, IReadOnlyDictionary<string, string> macros)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < content.Length)
        {
            while (i < content.Length && (char.IsWhiteSpace(content[i]) || content[i] == ',')) i++;
            var nameStart = i;
            while (i < content.Length && (char.IsLetterOrDigit(content[i]) || content[i] is '-' or '_')) i++;
            if (i == nameStart) break;
            var name = content[nameStart..i];
            while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
            if (i >= content.Length || content[i++] != '=') break;
            var parts = new List<string>();
            do
            {
                while (i < content.Length && (char.IsWhiteSpace(content[i]) || content[i] == '#')) i++;
                var (value, bare) = ReadBibValue(content, ref i);
                parts.Add(bare && macros.TryGetValue(value, out var expanded) ? expanded : value);
                while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
            } while (i < content.Length && content[i] == '#');
            fields[name] = string.Concat(parts);
            while (i < content.Length && content[i] != ',') i++;
        }
        return fields;
    }

    /// <summary>A braced, quoted or bare value; bare ones may name an @string.</summary>
    private static (string Value, bool Bare) ReadBibValue(string text, ref int i)
    {
        if (i >= text.Length) return (string.Empty, false);
        if (text[i] == '{')
        {
            var start = ++i;
            var depth = 1;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) break;
                i++;
            }
            var end = Math.Min(i, text.Length);
            if (i < text.Length) i++;
            return (text[start..end], false);
        }
        if (text[i] == '"')
        {
            var start = ++i;
            var braces = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '{') braces++;
                else if (c == '}') { if (braces > 0) braces--; }
                else if (c == '"' && braces == 0) break;
                i++;
            }
            var end = Math.Min(i, text.Length);
            if (i < text.Length) i++;
            return (text[start..end], false);
        }
        var valueStart = i;
        while (i < text.Length && text[i] is not (',' or '#')) i++;
        return (text[valueStart..i].Trim(), true);
    }

    private static int FindTopLevelComma(string text)
    {
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"' && depth == 0 && (i == 0 || text[i - 1] != '\\')) quoted = !quoted;
            if (quoted) continue;
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
            else if (text[i] == ',' && depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// Splits a BibTeX name list on "and" - but only at brace level zero, where
    /// BibTeX itself splits, and on any whitespace either side, since a long list is
    /// usually wrapped onto the next line straight after an "and". Splitting the
    /// cleaned text on " and " did neither: a wrapped list came out as one author
    /// with a line break in the middle, and <c>{Barnes and Noble}</c> as two people.
    /// </summary>
    private static IReadOnlyList<string> SplitBibNames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        var mask = raw.ToCharArray();
        var depth = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '{') depth++;
            if (depth > 0 || c == '\\') mask[i] = '_';
            if (c == '\\' && i + 1 < raw.Length) mask[++i] = '_';
            else if (c == '}' && depth > 0) depth--;
        }
        var names = new List<string>();
        var start = 0;
        foreach (Match separator in NameSeparator().Matches(new string(mask)))
        {
            names.Add(raw[start..separator.Index]);
            start = separator.Index + separator.Length;
        }
        names.Add(raw[start..]);
        return names.Select(CleanBibText).Where(n => n.Length > 0).ToList();
    }

    // Stand-ins for characters the value really contains, held while the braces that
    // are only markup are stripped.
    private const char LiteralOpenBrace = '\uE000';
    private const char LiteralCloseBrace = '\uE001';
    private const char LiteralBackslash = '\uE002';

    /// <summary>
    /// A text field as a reader should see it: LaTeX accents and escapes decoded,
    /// protective braces dropped, en and em dashes restored from <c>--</c> and
    /// <c>---</c>, and the line breaks and indentation of a wrapped value collapsed.
    /// <c>M{\"u}ller</c> used to arrive as <c>M\"uller</c>, and leave again on export
    /// as <c>M\\"uller</c> - a line break, to LaTeX.
    /// </summary>
    private static string CleanBibText(string value)
    {
        var text = DecodeLatex(value).Replace("{", string.Empty).Replace("}", string.Empty);
        text = text.Replace(LiteralOpenBrace, '{').Replace(LiteralCloseBrace, '}').Replace(LiteralBackslash, '\\');
        text = text.Replace("---", "\u2014").Replace("--", "\u2013");
        return Whitespace().Replace(text, " ").Trim();
    }

    /// <summary>A page range, with any dash between the numbers stored as a hyphen.</summary>
    private static string CleanBibPages(string value) =>
        PageDash().Replace(CleanBibText(value), "-");

    /// <summary>
    /// A DOI, URL or ISBN: no LaTeX to decode and no typography to restore - a
    /// <c>--</c> in a URL is two hyphens - so only the braces, the backslash escapes
    /// some exporters put before an underscore or percent sign, and any whitespace go.
    /// </summary>
    private static string CleanBibVerbatim(string value) =>
        Whitespace().Replace(VerbatimEscape().Replace(value, "$1").Replace("{", string.Empty).Replace("}", string.Empty), string.Empty);

    private static string DecodeLatex(string value)
    {
        if (value.IndexOf('\\') < 0 && value.IndexOf('~') < 0) return value;
        var text = TextBackslash().Replace(value, LiteralBackslash.ToString());
        text = SymbolAccent().Replace(text, ApplyAccent);
        text = LetterAccent().Replace(text, ApplyAccent);
        text = SpecialLetter().Replace(text, m => SpecialLetters[m.Groups[1].Value]);
        text = EscapedSymbol().Replace(text, m => m.Groups[1].Value switch
        {
            "{" => LiteralOpenBrace.ToString(),
            "}" => LiteralCloseBrace.ToString(),
            var symbol => symbol
        });
        text = SpacingCommand().Replace(text, m => m.Groups[1].Value is "-" or "/" ? string.Empty : " ");
        // Formatting commands - \emph, \textit, \textsc - keep their argument and lose
        // their name; the braces round the argument go with the rest.
        text = OtherCommand().Replace(text, string.Empty);
        return Tie().Replace(text, " ");
    }

    private static string ApplyAccent(Match match)
    {
        var letter = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
        letter = letter switch { @"\i" => "i", @"\j" => "j", _ => letter };
        return Accents.TryGetValue(match.Groups[1].Value, out var mark)
            ? (letter + mark).Normalize(NormalizationForm.FormC)
            : match.Value;
    }

    private static readonly Dictionary<string, char> Accents = new()
    {
        ["\""] = '\u0308', ["'"] = '\u0301', ["`"] = '\u0300', ["^"] = '\u0302', ["~"] = '\u0303',
        ["="] = '\u0304', ["."] = '\u0307', ["c"] = '\u0327', ["v"] = '\u030C', ["u"] = '\u0306',
        ["H"] = '\u030B', ["k"] = '\u0328', ["r"] = '\u030A', ["d"] = '\u0323', ["b"] = '\u0331'
    };

    private static readonly Dictionary<string, string> SpecialLetters = new()
    {
        ["ss"] = "ß", ["ae"] = "æ", ["AE"] = "Æ", ["oe"] = "œ", ["OE"] = "Œ", ["aa"] = "å", ["AA"] = "Å",
        ["o"] = "ø", ["O"] = "Ø", ["l"] = "ł", ["L"] = "Ł", ["i"] = "ı", ["j"] = "ȷ"
    };

    private static string? Empty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? YearPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Year().Match(value);
        return match.Success ? match.Value : value.Trim();
    }

    [GeneratedRegex(@"(?m)^([A-Z0-9]{2})  - ?(.*)$")]
    private static partial Regex RisLine();
    [GeneratedRegex(@"\b(?:1[5-9]|20|21)\d{2}\b")]
    private static partial Regex Year();
    [GeneratedRegex(@"\s+and\s+", RegexOptions.IgnoreCase)]
    private static partial Regex NameSeparator();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
    [GeneratedRegex(@"\s*[-\u2010-\u2015]+\s*")]
    private static partial Regex PageDash();
    [GeneratedRegex(@"\\([_%#&$~])")]
    private static partial Regex VerbatimEscape();
    [GeneratedRegex(@"\\textbackslash(?![A-Za-z])(?:\{\})?")]
    private static partial Regex TextBackslash();
    [GeneratedRegex(@"\\([""'`^~=.])\s*(?:\{\s*(\\[ij]|[A-Za-z])\s*\}|(\\[ij](?![A-Za-z])|[A-Za-z]))")]
    private static partial Regex SymbolAccent();
    [GeneratedRegex(@"\\([cvuHkrdb])(?:\s*\{\s*(\\[ij]|[A-Za-z])\s*\}|\s+(\\[ij](?![A-Za-z])|[A-Za-z]))")]
    private static partial Regex LetterAccent();
    [GeneratedRegex(@"\\(ss|ae|AE|oe|OE|aa|AA|o|O|l|L|i|j)(?![A-Za-z])(?:\{\}|\s)?")]
    private static partial Regex SpecialLetter();
    [GeneratedRegex(@"\\([&%$#_{}])")]
    private static partial Regex EscapedSymbol();
    [GeneratedRegex(@"\\([,;: \-/])")]
    private static partial Regex SpacingCommand();
    [GeneratedRegex(@"\\[A-Za-z]+\*?\s*")]
    private static partial Regex OtherCommand();
    [GeneratedRegex(@"(?<!\\)~")]
    private static partial Regex Tie();
}
