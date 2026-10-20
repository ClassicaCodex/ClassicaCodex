using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ClassicaCodex.Ingestion.Epigraphy;

namespace ClassicaCodex.Ingestion.Defixiones;

/// <summary>
/// One curse tablet: its text line by line, and its translations, line by line
/// where the source divides them so.
/// </summary>
/// <param name="Source">"attic", "sicily" or "heidelberg".</param>
/// <param name="Number">How the tablet is numbered in its corpus ("125", "175 I", "30073", "12").</param>
/// <param name="Label">How it is cited ("IG II/III³ 8, 1, 125", "ISic030073", "HD000012").</param>
/// <param name="Group">The work it belongs in: for the Heidelberg tablets, the Roman province.</param>
public sealed record CurseTablet(
    string Source,
    string Number,
    string Label,
    string Group,
    string? Place,
    string Language,
    IReadOnlyList<EpiDocLine> Lines,
    IReadOnlyDictionary<string, (string? Translator, IReadOnlyList<EpiDocLine> Lines)> Translations);

/// <summary>
/// Reads curse tablets from the three open sources that publish them in
/// EpiDoc: the Attic volume of the Inscriptiones Graecae, I.Sicily and the
/// Epigraphic Database Heidelberg. See <see cref="DefixionesCatalogue"/>.
/// </summary>
public static class DefixionesLoader
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";
    private static readonly XNamespace Xml = XNamespace.Xml;

    /// <summary>The group for a Heidelberg tablet whose province the database does not know.</summary>
    public const string UnknownProvince = "Unknown province";

    public static XDocument LoadXml(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(path, settings);
        return XDocument.Load(reader);
    }

    /// <summary>
    /// A tablet from IG II/III³ 8, 1. The volume gives every tablet an English
    /// translation (Jaime Curbera) and a German one (Klaus Hallof), numbered
    /// line for line with the Greek, so all three can be read side by side.
    /// </summary>
    public static CurseTablet? ReadAttic(string number, XDocument doc)
    {
        var edition = Division(doc, "edition");
        if (edition == null) return null;

        var lines = EpiDocText.Lines(edition);
        if (lines.Count == 0) return null;

        return new CurseTablet("attic", number, $"IG II/III³ 8, 1, {number}", "Attica", "Attica", "grc",
            lines, Translations(doc));
    }

    /// <summary>
    /// A tablet from I.Sicily. Many of its curse-tablet files are placeholders
    /// with an empty edition, and those return null; the ones with a text carry
    /// it in rich EpiDoc, and a few have a translation.
    /// </summary>
    public static CurseTablet? ReadSicily(string id, XDocument doc)
    {
        var edition = Division(doc, "edition");
        if (edition == null) return null;

        var lines = EpiDocText.Lines(edition);
        if (lines.Count == 0 || lines.All(l => IsOnlyLoss(l.Text))) return null;

        var place = doc.Descendants(Tei + "origPlace").Select(p => Collapse(p.Value)).FirstOrDefault(v => v.Length > 0);
        var language = ((string?)edition.Attribute(Xml + "lang")) ?? "grc";
        var number = int.TryParse(Regex.Replace(id, @"\D", string.Empty), out var n) ? n.ToString() : id;

        return new CurseTablet("sicily", number, id, "Sicily", place, language.StartsWith("la") ? "lat" : "grc",
            lines, Translations(doc));
    }

    /// <summary>
    /// A tablet from the Heidelberg database. Grouped by its Roman province,
    /// which the database records, so the Bath and Uley tablets sit together
    /// under Britannia and the Mainz tablets under Germania superior. The
    /// database has no translations.
    /// </summary>
    public static CurseTablet? ReadHeidelberg(string id, XDocument doc)
    {
        var edition = Division(doc, "edition");
        if (edition == null) return null;

        var lines = EpiDocText.Lines(edition);
        if (lines.Count == 0 || lines.All(l => IsOnlyLoss(l.Text))) return null;

        // The repository's files name the province as "provinceItalicRegion"
        // and leave the ancient findspot untyped inside origPlace, with the
        // modern one untyped inside provenance; the database's own export
        // types them "province", "ancient" and "modern". Both are read. The
        // ancient name is sometimes an empty reference (Uley has none), and
        // then the modern one stands in.
        string? Named(IEnumerable<XElement> places) => places
            .Select(p => Collapse(p.Value))
            .FirstOrDefault(v => v.Length > 0);

        var placeNames = doc.Descendants(Tei + "placeName").ToList();
        string? Place(params string[] types) =>
            Named(placeNames.Where(p => types.Contains((string?)p.Attribute("type"))));
        string? Untyped(string within) =>
            Named(placeNames.Where(p => p.Attribute("type") == null && p.Parent?.Name.LocalName == within));

        // The dump writes an unknown province in German.
        var province = Place("province", "provinceItalicRegion") is { } named && named != "unbekannt" ? named : UnknownProvince;
        var place = Place("ancient") ?? Untyped("origPlace") ?? Place("modern") ?? Untyped("provenance");
        var text = string.Concat(lines.Select(l => l.Text));
        var greek = text.Count(c => c is >= 'Ͱ' and <= 'Ͽ' or >= 'ἀ' and <= '῿');
        var latin = text.Count(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        var number = int.Parse(id[2..]).ToString();

        return new CurseTablet("heidelberg", number, id, province, place, greek > latin ? "grc" : "lat",
            lines, new Dictionary<string, (string?, IReadOnlyList<EpiDocLine>)>());
    }

    private static XElement? Division(XDocument doc, string type) =>
        doc.Descendants(Tei + "div").FirstOrDefault(d => (string?)d.Attribute("type") == type);

    /// <summary>
    /// The translations a file carries, by language, skipping the empty
    /// placeholders I.Sicily puts in every file.
    /// </summary>
    private static Dictionary<string, (string?, IReadOnlyList<EpiDocLine>)> Translations(XDocument doc)
    {
        var result = new Dictionary<string, (string?, IReadOnlyList<EpiDocLine>)>(StringComparer.Ordinal);
        foreach (var div in doc.Descendants(Tei + "div").Where(d => (string?)d.Attribute("type") == "translation"))
        {
            var language = LanguageCode((string?)div.Attribute(Xml + "lang"));
            if (language == null || result.ContainsKey(language)) continue;

            var lines = EpiDocText.Lines(div);
            if (lines.Count == 0) continue;

            result[language] = ((string?)div.Attribute("resp"), lines);
        }

        return result;
    }

    /// <summary>The ISO 639-2 code for a translation's xml:lang ("EN", "en", "de").</summary>
    public static string? LanguageCode(string? lang) => lang?.Trim().ToLowerInvariant() switch
    {
        "en" or "eng" => "eng",
        "de" or "deu" or "ger" => "deu",
        "fr" or "fra" or "fre" => "fra",
        "it" or "ita" => "ita",
        "es" or "spa" => "spa",
        _ => null
    };

    private static readonly Regex LossOnly = new(@"^[\s\[\]\-–‒.·⟦⟧{}()]*$", RegexOptions.Compiled);

    /// <summary>True for a line that is nothing but loss marks - [- - -] - with no letter on it.</summary>
    public static bool IsOnlyLoss(string text) => LossOnly.IsMatch(text);

    private static string Collapse(string s) => Regex.Replace(s, @"\s+", " ").Trim();
}
