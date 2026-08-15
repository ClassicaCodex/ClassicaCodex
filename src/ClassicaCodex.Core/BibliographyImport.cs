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
    public string StableIdentifier => BibliographyImport.NormalizeDoi(Doi) is { } doi
        ? $"https://doi.org/{doi}"
        : !string.IsNullOrWhiteSpace(Isbn) ? $"isbn:{Isbn.Trim()}"
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

    public string FormatCitation()
    {
        var builder = new StringBuilder();
        if (Authors.Count > 0) builder.Append(string.Join("; ", Authors)).Append(". ");
        if (!string.IsNullOrWhiteSpace(Year)) builder.Append('(').Append(Year).Append("). ");
        builder.Append(Title.Trim().TrimEnd('.')).Append(". ");
        if (!string.IsNullOrWhiteSpace(ContainerTitle)) builder.Append(ContainerTitle.Trim().TrimEnd('.')).Append(". ");
        if (!string.IsNullOrWhiteSpace(Volume)) builder.Append(Volume);
        if (!string.IsNullOrWhiteSpace(Issue)) builder.Append('(').Append(Issue).Append(')');
        if (!string.IsNullOrWhiteSpace(Pages)) builder.Append(string.IsNullOrWhiteSpace(Volume) ? "pp. " : ": ").Append(Pages);
        if (!string.IsNullOrWhiteSpace(Publisher)) builder.Append(". ").Append(Publisher.Trim().TrimEnd('.'));
        if (BibliographyImport.NormalizeDoi(Doi) is { } doi) builder.Append(". https://doi.org/").Append(doi);
        else if (!string.IsNullOrWhiteSpace(Url)) builder.Append(". ").Append(Url.Trim());
        return builder.ToString().Trim().TrimEnd('.') + ".";
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
        foreach (var prefix in new[] { "https://doi.org/", "http://doi.org/", "http://dx.doi.org/", "doi:" })
            if (doi.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                doi = doi[prefix.Length..];
        doi = doi.Trim().TrimEnd('.', ',', ';');
        return doi.Length == 0 ? null : doi.ToLowerInvariant();
    }

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
        var position = 0;
        while ((position = text.IndexOf('@', position)) >= 0)
        {
            var typeStart = ++position;
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] is '-' or '_')) position++;
            var type = text[typeStart..position].Trim();
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position >= text.Length || text[position] is not ('{' or '(')) continue;
            var open = text[position++];
            var close = open == '{' ? '}' : ')';
            var contentStart = position;
            var depth = 1;
            var quoted = false;
            var escaped = false;
            while (position < text.Length && depth > 0)
            {
                var c = text[position++];
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"') { quoted = !quoted; continue; }
                if (quoted) continue;
                if (c == open) depth++;
                else if (c == close) depth--;
            }
            if (depth != 0) break;
            var content = text[contentStart..(position - 1)];
            var comma = FindTopLevelComma(content);
            if (comma < 0) continue;
            var key = content[..comma].Trim();
            var fields = ParseBibFields(content[(comma + 1)..]);
            if (!fields.TryGetValue("title", out var title) || string.IsNullOrWhiteSpace(title)) continue;
            string? Get(string name) => fields.TryGetValue(name, out var value) ? CleanBibValue(value) : null;
            var authors = (Get("author") ?? string.Empty).Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            records.Add(new BibliographyRecord(
                "BibTeX", type.ToUpperInvariant(), key, CleanBibValue(title), authors,
                YearPart(Get("year") ?? Get("date")), Get("journal") ?? Get("booktitle"),
                Get("volume"), Get("number") ?? Get("issue"), Get("pages"), Get("publisher"),
                Get("doi"), Get("url"), Get("isbn"), Get("abstract"),
                (Get("keywords") ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
        }
        return records;
    }

    private static Dictionary<string, string> ParseBibFields(string content)
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
                parts.Add(ReadBibValue(content, ref i));
                while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
            } while (i < content.Length && content[i] == '#');
            fields[name] = string.Concat(parts);
            while (i < content.Length && content[i] != ',') i++;
        }
        return fields;
    }

    private static string ReadBibValue(string text, ref int i)
    {
        if (i >= text.Length) return string.Empty;
        if (text[i] == '{')
        {
            var start = ++i;
            var depth = 1;
            while (i < text.Length && depth > 0)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}') depth--;
                i++;
            }
            return text[start..Math.Max(start, i - 1)];
        }
        if (text[i] == '"')
        {
            var start = ++i;
            var escaped = false;
            while (i < text.Length)
            {
                if (!escaped && text[i] == '"') break;
                escaped = !escaped && text[i] == '\\';
                if (text[i] != '\\') escaped = false;
                i++;
            }
            var result = text[start..Math.Min(i, text.Length)];
            if (i < text.Length) i++;
            return result;
        }
        var valueStart = i;
        while (i < text.Length && text[i] is not (',' or '#')) i++;
        return text[valueStart..i].Trim();
    }

    private static int FindTopLevelComma(string text)
    {
        var depth = 0;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '"' && (i == 0 || text[i - 1] != '\\')) quoted = !quoted;
            if (quoted) continue;
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
            else if (text[i] == ',' && depth == 0) return i;
        }
        return -1;
    }

    private static string CleanBibValue(string value) => value
        .Replace("{", string.Empty).Replace("}", string.Empty)
        .Replace("--", "-").Trim();
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
}
