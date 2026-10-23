using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Dionysius;

/// <summary>One of the five works, as Parker's translation divides it.</summary>
public sealed record DionysiusWork(
    string Title, IReadOnlyList<(string Citation, string Text, bool IsHead)> Passages);

/// <summary>
/// The works of Dionysius the Areopagite in John Parker's English, out of the
/// Christian Classics Ethereal Library's ThML (Theological Markup Language)
/// file of his two volumes.
///
/// Each work is a div2, each chapter or letter a div3. A chapter opens with
/// "CAPUT IV." and the line that says what it is about, then numbers its
/// sections "Section I.", "Section II."; a letter opens with "LETTER IX." and
/// its addressee. Passages are cited chapter and section, as Dionysius is
/// cited - Heavenly Hierarchy 7.2 - in Parker's numbering, and a section of
/// several paragraphs continues as 7.2.2. A chapter or letter Parker did not
/// divide is its own section 1: the chapter that names the nine orders,
/// Heavenly Hierarchy 6, is a single paragraph, 6.1. What comes before a
/// chapter's first section - the dedication to Timothy - is section 0.
///
/// Parker's footnotes and the page breaks of the print are left out. So is
/// what he wrote inside the chapters rather than in front of them: the date
/// at the foot of a volume ("St. Michael and All Angels, 1898."), the
/// colophon after the last letter, and his note on Titus between Letters
/// VIII and IX. Each begins at a centred line that is not a section number,
/// and nothing of Dionysius's follows one in its chapter.
/// </summary>
public static class DionysiusLoader
{
    /// <summary>The five works, in the order of Parker's volumes, and what to call them.</summary>
    public static readonly IReadOnlyList<(string Div, string Slug, string Title, string Scheme, string Year)> Works = new[]
    {
        ("On Divine Names", "divine-names", "On Divine Names", "Chapter.Section", "1897"),
        ("Mystic Theology", "mystic-theology", "Mystic Theology", "Chapter.Section", "1897"),
        ("Letters", "letters", "Letters", "Letter.Section", "1897"),
        ("On the Heavenly Hierarchy", "heavenly-hierarchy", "On the Heavenly Hierarchy", "Chapter.Section", "1899"),
        ("Ecclesiastical Hierarchy", "ecclesiastical-hierarchy", "On the Ecclesiastical Hierarchy", "Chapter.Section", "1899")
    };

    private static readonly Regex Caput = new(@"^Caput ([IVXLC]+)\.?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Letter = new(@"^Letter ([IVXLC]+)\.?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Section = new(@"^Section ([IVXLC]+)\.?\s*(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static XDocument Read(Stream stream)
    {
        // ThML declares a DTD it does not need: the file uses no entity but
        // the five XML defines itself.
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        return XDocument.Load(reader);
    }

    public static IReadOnlyList<DionysiusWork> Load(XDocument document)
    {
        var divs = document.Descendants("div2").ToList();
        var works = new List<DionysiusWork>();

        foreach (var (div, _, title, _, _) in Works)
        {
            var work = divs.FirstOrDefault(d => (string?)d.Attribute("title") == div)
                ?? throw new InvalidDataException($"\"{div}\" was not found");

            var passages = new List<(string, string, bool)>();
            foreach (var chapter in work.Elements("div3"))
            {
                passages.AddRange(Chapter(chapter));
            }

            if (passages.Count == 0) throw new InvalidDataException($"\"{div}\" has no text");
            works.Add(new DionysiusWork(title, passages));
        }

        return works;
    }

    /// <summary>
    /// A chapter or letter's passages, or none for a div3 that is neither -
    /// the note Parker added after the Divine Names.
    /// </summary>
    private static IEnumerable<(string Citation, string Text, bool IsHead)> Chapter(XElement div)
    {
        var name = (string?)div.Attribute("title") ?? "";
        var caput = Caput.Match(name);
        var letter = Letter.Match(name);
        if (!caput.Success && !letter.Success) yield break;

        var number = Roman((caput.Success ? caput : letter).Groups[1].Value);
        var blocks = div.Elements().Where(e => e.Name == "p" || e.Name == "h3").ToList();
        var sectioned = blocks.Any(b => b.Name == "p" && Section.IsMatch(Text(b)));

        var head = caput.Success ? $"Caput {caput.Groups[1].Value.ToUpperInvariant()}." : null;
        var section = sectioned ? 0 : 1;
        var paragraph = 0;
        var passages = new List<(string, string, bool)>();

        foreach (var block in blocks)
        {
            var text = Text(block);
            if (text.Length == 0 || block.Name == "h3") continue;

            var kind = (string?)block.Attribute("class") ?? "";

            if (kind == "subject")
            {
                head = head == null ? text : $"{head} {text}";
                continue;
            }

            if (kind == "letter")
            {
                head = Letter.Replace(text, m => $"Letter {m.Groups[1].Value.ToUpperInvariant()}.", 1);
                continue;
            }

            if (kind is "center" or "Centered")
            {
                var label = Section.Match(text);
                if (label.Success)
                {
                    section = Roman(label.Groups[1].Value);
                    paragraph = 0;
                    if (label.Groups[2].Value.Length == 0) continue;
                    text = label.Groups[2].Value;
                }
                else if (kind == "center")
                {
                    // Parker's own matter from here to the end of the chapter.
                    break;
                }
            }

            paragraph++;
            passages.Add((paragraph == 1 ? $"{number}.{section}" : $"{number}.{section}.{paragraph}", text, false));
        }

        if (head != null) yield return ($"{number}.head", head, true);
        foreach (var passage in passages) yield return passage;
    }

    /// <summary>
    /// A block's text without footnotes or page breaks. A scripture
    /// reference left in the running text is kept, set apart where the
    /// transcription runs it into the word before.
    /// </summary>
    internal static string Text(XElement block)
    {
        var sb = new StringBuilder();
        Append(block, sb);
        return Whitespace.Replace(sb.ToString(), " ").Trim();
    }

    private static void Append(XElement element, StringBuilder sb)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;
                case XElement child when child.Name == "note" || child.Name == "pb":
                    break;
                case XElement child when child.Name == "br":
                    sb.Append(' ');
                    break;
                case XElement child when child.Name == "scripRef" && sb.Length > 0 && char.IsLetter(sb[^1]):
                    sb.Append(" [");
                    Append(child, sb);
                    sb.Append(']');
                    break;
                case XElement child:
                    Append(child, sb);
                    break;
            }
        }
    }

    internal static int Roman(string numeral)
    {
        var values = new Dictionary<char, int> { ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50, ['C'] = 100 };
        var digits = numeral.ToUpperInvariant().Select(c => values[c]).ToList();
        var total = 0;
        for (var i = 0; i < digits.Count; i++)
        {
            total += i + 1 < digits.Count && digits[i] < digits[i + 1] ? -digits[i] : digits[i];
        }

        return total;
    }
}
