using System.Text;
using System.Text.RegularExpressions;

namespace ClassicaCodex.Core;

/// <summary>Structured citation metadata attached to one scholarship evidence item.</summary>
public sealed class EvidenceBibliographyMetadata
{
    public long EvidenceItemId { get; set; }
    public string ImportFormat { get; set; } = "Manual";
    public string EntryType { get; set; } = "MISC";
    public string? CiteKey { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<string> Authors { get; set; } = [];
    public string? Year { get; set; }
    public string? ContainerTitle { get; set; }
    public string? Volume { get; set; }
    public string? Issue { get; set; }
    public string? Pages { get; set; }
    public string? Publisher { get; set; }
    public string? Doi { get; set; }
    public string? Url { get; set; }
    public string? Isbn { get; set; }
    public string? Abstract { get; set; }
    public List<string> Keywords { get; set; } = [];
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public bool IsStored { get; set; }

    public BibliographyRecord ToRecord() => new(ImportFormat, EntryType, CiteKey, Title, Authors,
        Year, ContainerTitle, Volume, Issue, Pages, Publisher, Doi, Url, Isbn, Abstract, Keywords);

    public static EvidenceBibliographyMetadata FromRecord(long evidenceItemId, BibliographyRecord record) => new()
    {
        EvidenceItemId = evidenceItemId, ImportFormat = record.ImportFormat, EntryType = record.EntryType,
        CiteKey = record.CiteKey, Title = record.Title, Authors = record.Authors.ToList(), Year = record.Year,
        ContainerTitle = record.ContainerTitle, Volume = record.Volume, Issue = record.Issue,
        Pages = record.Pages, Publisher = record.Publisher, Doi = record.Doi, Url = record.Url,
        Isbn = record.Isbn, Abstract = record.Abstract, Keywords = record.Keywords.ToList()
    };
}

/// <summary>Offline, deterministic bibliography export suitable for Zotero import.</summary>
public static partial class BibliographyExport
{
    public static string ToBibTeX(IEnumerable<BibliographyRecord> records)
    {
        var items = Prepare(records);
        var output = new StringBuilder();
        foreach (var (record, key) in items)
        {
            output.Append('@').Append(BibType(record.EntryType)).Append('{').Append(key).AppendLine(",");
            Field(output, "author", record.Authors.Count == 0 ? null : string.Join(" and ", record.Authors));
            Field(output, "title", record.Title);
            Field(output, "year", record.Year);
            var type = BibType(record.EntryType);
            var containerField = type switch
            {
                "article" => "journal",
                "inproceedings" or "incollection" => "booktitle",
                _ => null
            };
            if (containerField != null) Field(output, containerField, record.ContainerTitle);
            Field(output, "volume", record.Volume);
            Field(output, "number", record.Issue);
            Field(output, "pages", record.Pages?.Replace("-", "--"));
            Field(output, "publisher", record.Publisher);
            Field(output, "doi", BibliographyImport.NormalizeDoi(record.Doi));
            Field(output, "url", record.Url);
            Field(output, "isbn", record.Isbn);
            Field(output, "abstract", record.Abstract);
            Field(output, "keywords", record.Keywords.Count == 0 ? null : string.Join(", ", record.Keywords));
            var comma = output.Length - Environment.NewLine.Length - 1;
            if (comma >= 0 && output[comma] == ',') output.Remove(comma, 1);
            output.AppendLine("}").AppendLine();
        }
        return output.ToString();
    }

    public static string ToRis(IEnumerable<BibliographyRecord> records)
    {
        var output = new StringBuilder();
        foreach (var (record, key) in Prepare(records))
        {
            Ris(output, "TY", RisType(record.EntryType));
            Ris(output, "ID", key);
            foreach (var author in record.Authors) Ris(output, "AU", author);
            Ris(output, "TI", record.Title); Ris(output, "PY", record.Year);
            Ris(output, "JO", record.ContainerTitle); Ris(output, "VL", record.Volume);
            Ris(output, "IS", record.Issue);
            var pages = (record.Pages ?? string.Empty).Split('-', 2, StringSplitOptions.TrimEntries);
            if (pages.Length > 0) Ris(output, "SP", pages[0]);
            if (pages.Length > 1) Ris(output, "EP", pages[1]);
            Ris(output, "PB", record.Publisher); Ris(output, "DO", BibliographyImport.NormalizeDoi(record.Doi));
            Ris(output, "UR", record.Url); Ris(output, "SN", record.Isbn); Ris(output, "AB", record.Abstract);
            foreach (var keyword in record.Keywords) Ris(output, "KW", keyword);
            output.AppendLine("ER  -").AppendLine();
        }
        return output.ToString();
    }

    public static string SuggestCiteKey(BibliographyRecord record)
    {
        var author = record.Authors.FirstOrDefault() ?? "source";
        if (author.Contains(',')) author = author[..author.IndexOf(',')];
        else author = author.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "source";
        var titleWord = Words().Matches(record.Title).Select(m => m.Value)
            .FirstOrDefault(w => w.Length > 3) ?? "work";
        var raw = author + (record.Year ?? "nd") + titleWord;
        var key = KeyNoise().Replace(raw, string.Empty);
        return string.IsNullOrWhiteSpace(key) ? "source" : key;
    }

    private static List<(BibliographyRecord Record, string Key)> Prepare(IEnumerable<BibliographyRecord> records)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(BibliographyRecord, string)>();
        foreach (var record in records)
        {
            var baseKey = string.IsNullOrWhiteSpace(record.CiteKey) ? SuggestCiteKey(record) : record.CiteKey.Trim();
            baseKey = KeyNoise().Replace(baseKey, string.Empty);
            if (baseKey.Length == 0) baseKey = "source";
            var key = baseKey;
            for (var suffix = 2; !used.Add(key); suffix++) key = baseKey + suffix;
            result.Add((record, key));
        }
        return result;
    }

    private static void Field(StringBuilder output, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        output.Append("  ").Append(name).Append(" = {").Append(Escape(value.Trim())).AppendLine("},");
    }
    private static void Ris(StringBuilder output, string tag, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) output.Append(tag).Append("  - ").AppendLine(value.Trim());
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
    private static string BibType(string type) => type.ToUpperInvariant() switch
    {
        "JOUR" or "JOURNAL" or "ARTICLE" => "article", "BOOK" => "book",
        "CHAP" or "CHAPTER" or "INBOOK" => "incollection", "CONF" or "CPAPER" or "INPROCEEDINGS" => "inproceedings",
        "THES" or "THESIS" or "PHDTHESIS" => "phdthesis", _ => "misc"
    };
    private static string RisType(string type) => BibType(type) switch
    {
        "article" => "JOUR", "book" => "BOOK", "incollection" => "CHAP",
        "inproceedings" => "CPAPER", "phdthesis" => "THES", _ => "GEN"
    };

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
    [GeneratedRegex(@"[^\p{L}\p{N}_:.+\-]")]
    private static partial Regex KeyNoise();
}
