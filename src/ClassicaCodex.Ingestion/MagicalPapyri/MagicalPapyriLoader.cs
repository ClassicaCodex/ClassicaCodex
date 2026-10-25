using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ClassicaCodex.Ingestion.Epigraphy;

namespace ClassicaCodex.Ingestion.MagicalPapyri;

/// <summary>A Greek magical papyrus as papyri.info edits it: its lines, column by column.</summary>
public sealed record GreekMagicalText(MagicalPapyrus Papyrus, string Language, IReadOnlyList<EpiDocLine> Lines);

/// <summary>
/// One line of a Kyprianos text or of its translation.
/// </summary>
/// <param name="Section">The page, side or column it is on, 1-based in the order the text gives them.</param>
/// <param name="SectionLabel">How the text heads that section ("p. 2 (= Kropp 1)", "Recto").</param>
/// <param name="Line">The line number, or null for a line the edition does not number (a description of a drawing).</param>
public sealed record KyprianosLine(int Section, string? SectionLabel, string? Line, string Text);

/// <summary>A Coptic magical text from the Kyprianos database, with its English translation.</summary>
/// <param name="Id">Its KYP T number.</param>
/// <param name="Sigla">The manuscript it is on ("P. Mich. Inv. 599 + 594").</param>
public sealed record KyprianosText(
    int Id,
    string Title,
    string Type,
    string Language,
    string? Dialect,
    string Sigla,
    int SectionCount,
    IReadOnlyList<KyprianosLine> Lines,
    IReadOnlyList<KyprianosLine> Translation);

/// <summary>
/// Reads the magical papyri: the Greek ones from papyri.info's EpiDoc (see
/// <see cref="MagicalPapyriCatalogue"/>), and the Coptic ones from the
/// Kyprianos database's export of its texts.
/// </summary>
public static class MagicalPapyriLoader
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";

    public static XDocument LoadXml(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(path, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    /// <summary>
    /// A papyrus's edited text, or null if its edition is empty - which the
    /// catalogue already rules out, but a later revision of a file could empty it.
    /// </summary>
    public static GreekMagicalText? ReadGreek(MagicalPapyrus papyrus, XDocument doc)
    {
        var edition = doc.Descendants(Tei + "div").FirstOrDefault(d => (string?)d.Attribute("type") == "edition");
        if (edition == null) return null;

        var lines = EpiDocText.Lines(edition);
        if (lines.Count == 0) return null;

        var language = ((string?)edition.Attribute(XNamespace.Xml + "lang"))?.Trim().ToLowerInvariant() switch
        {
            "cop" => "cop",
            "la" or "lat" => "lat",
            _ => "grc"
        };

        return new GreekMagicalText(papyrus, language, lines);
    }

    // ---- Kyprianos

    /// <summary>
    /// The texts in a Kyprianos export, skipping the few with no words in them
    /// (a tableau of magical signs, a drawing).
    /// </summary>
    public static List<KyprianosText> ReadKyprianos(string csvPath)
    {
        var rows = ReadCsv(File.ReadAllText(csvPath, Encoding.UTF8));
        if (rows.Count == 0) return new List<KyprianosText>();

        var header = rows[0];
        int Column(string name) => header.FindIndex(h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        var id = Column("text_id");
        var title = Column("conventional title");
        var type = Column("type_of_text");
        var languages = Column("languages");
        var dialect = Column("dialect");
        var sigla = Column("sigla");
        var text = Column("text");
        var translation = Column("translation");

        if (new[] { id, title, type, languages, sigla, text, translation }.Any(i => i < 0))
            throw new InvalidDataException("The Kyprianos export does not have the columns this reader expects.");

        string Field(List<string> row, int i) => i >= 0 && i < row.Count ? row[i].Trim() : string.Empty;

        var result = new List<KyprianosText>();
        foreach (var row in rows.Skip(1))
        {
            if (!int.TryParse(Field(row, id), out var number)) continue;
            var language = LanguageOf(Field(row, languages));
            if (language == null) continue;

            var (lines, sections) = ReadKyprianosLines(Field(row, text));
            if (lines.Count == 0) continue;

            result.Add(new KyprianosText(
                number,
                Field(row, title),
                Field(row, type),
                language,
                Field(row, dialect) is { Length: > 0 } d ? d : null,
                Field(row, sigla),
                sections.Count,
                lines,
                ReadKyprianosTranslation(Field(row, translation), sections)));
        }

        return result.OrderBy(t => t.Id).ToList();
    }

    /// <summary>
    /// "Egyptian (Coptic)" is Coptic, "Greek" Greek; a text in both is filed
    /// under the one named first. Null for "[non-linguistic]", a drawing or a
    /// grid of signs.
    /// </summary>
    public static string? LanguageOf(string languages)
    {
        var first = languages.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        if (first.StartsWith("[", StringComparison.Ordinal) || first.Length == 0) return null;
        return first.StartsWith("Greek", StringComparison.OrdinalIgnoreCase) ? "grc" : "cop";
    }

    private static readonly Regex NumberedLine = new(@"^\s*(\d+[a-z]?)\.\s*(.*)$", RegexOptions.Compiled);

    private static readonly Regex SectionHeader = new(
        @"^(p\.\s*\d+|\d+(st|nd|rd|th)\s+page|recto|verso|convex|concave|col\.\s*\d+|fol\.\s*\d+|ostracon\s+\d+|side\s+[a-z0-9]+|face\s+[a-z0-9]+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A text's lines. Kyprianos writes one line of the manuscript per line,
    /// numbered "11. ⲭⲏⲣⲉ …", under headers for the page or side ("p. 10 (P.
    /// Mich. Inv. 598 vo)", "Recto"); arrows for the direction of writing and
    /// rules drawn across the page have no words and are left out. A line the
    /// edition does not number - "[tableau 1]", "Inside tableau:" - is kept,
    /// without a number.
    /// </summary>
    public static (List<KyprianosLine> Lines, List<string> Sections) ReadKyprianosLines(string text)
    {
        var lines = new List<KyprianosLine>();
        var sections = new List<string>();
        string? label = null;

        foreach (var raw in text.Replace("\r\n", "\n").Split('\r', '\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.Length <= 80 && SectionHeader.IsMatch(line) && !NumberedLine.IsMatch(line))
            {
                label = line;
                sections.Add(line);
                continue;
            }

            if (!line.Any(char.IsLetter)) continue;

            var section = Math.Max(sections.Count, 1);
            var m = NumberedLine.Match(line);
            lines.Add(m.Success
                ? new KyprianosLine(section, label, m.Groups[1].Value, m.Groups[2].Value.Trim())
                : new KyprianosLine(section, label, null, line));
        }

        return (lines, sections);
    }

    // [11], [11-12], [p. 3, 1], [p. 3 l. 1], [p. 3, l. 1], [2nd page, l. 4],
    // [verso, 5], [p. 2 col. 1 l. 3]. Square brackets round anything else -
    // [you], [...] - are the translator's, and stay in the text.
    private static readonly Regex TranslationMarker = new(
        @"\[(?<section>(?:p\.\s*\d+|\d+(?:st|nd|rd|th)\s+page|recto|verso|col\.\s*\d+|ostracon\s+\d+|side\s+[a-z0-9]+)(?:\s*,?\s*col\.\s*\d+)?\s*,?\s*)?(?:l\.\s*)?(?<line>\d+[a-z]?)(?:\s*[-–]\s*\d+[a-z]?)?\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The translation, cut where it marks the manuscript's lines so that each
    /// piece can stand beside the line it begins on. A translation follows the
    /// sense rather than the line, so a piece may hold the end of one line and
    /// the start of the next; it is filed under the line its marker names.
    /// </summary>
    public static List<KyprianosLine> ReadKyprianosTranslation(string translation, IReadOnlyList<string> sections)
    {
        var result = new List<KyprianosLine>();
        if (string.IsNullOrWhiteSpace(translation)) return result;

        var keys = sections.Select(SectionKey).ToList();
        var section = 1;
        string? label = sections.Count > 0 ? sections[0] : null;

        var matches = TranslationMarker.Matches(translation);
        var lead = Collapse(matches.Count > 0 ? translation[..matches[0].Index] : translation);

        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            if (m.Groups["section"].Success)
            {
                var found = keys.IndexOf(SectionKey(m.Groups["section"].Value));
                if (found >= 0)
                {
                    section = found + 1;
                    label = sections[found];
                }
            }

            var end = i + 1 < matches.Count ? matches[i + 1].Index : translation.Length;
            var piece = Collapse(translation[(m.Index + m.Length)..end]);

            // Whatever comes before the first marker - a heading, the first
            // words - belongs with the first line rather than on its own.
            if (i == 0 && lead.Length > 0) piece = Collapse(lead + " " + piece);
            if (piece.Length > 0) result.Add(new KyprianosLine(section, label, m.Groups["line"].Value, piece));
        }

        if (matches.Count == 0 && lead.Length > 0) result.Add(new KyprianosLine(1, label, null, lead));
        return result;
    }

    /// <summary>
    /// The page or side a header or marker names, reduced to compare: "p. 2 (=
    /// Kropp 1)" and "p. 2, " are both "p2"; "Recto (hair)" is "recto".
    /// </summary>
    public static string SectionKey(string s)
    {
        s = s.Trim().ToLowerInvariant();
        Match m;
        if ((m = Regex.Match(s, @"^p\.\s*(\d+)")).Success) return "p" + m.Groups[1].Value;
        if ((m = Regex.Match(s, @"^(\d+)(?:st|nd|rd|th)\s+page")).Success) return "page" + m.Groups[1].Value;
        if ((m = Regex.Match(s, @"^(col|fol)\.\s*(\d+)")).Success) return m.Groups[1].Value + m.Groups[2].Value;
        if ((m = Regex.Match(s, @"^(ostracon|side|face)\s+([a-z0-9]+)")).Success) return m.Groups[1].Value + m.Groups[2].Value;
        if ((m = Regex.Match(s, @"^(recto|verso|convex|concave)")).Success) return m.Groups[1].Value;
        return s;
    }

    private static string Collapse(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>
    /// Rows of a CSV file, quoted fields and all. The Kyprianos export ends
    /// its lines with a bare carriage return - inside fields and between rows
    /// alike - so a reader that splits on newlines first sees one enormous line.
    /// </summary>
    public static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r' or '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
