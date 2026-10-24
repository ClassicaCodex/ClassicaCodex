using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassicaCodex.Core;

namespace ClassicaCodex.Ingestion.Nights;

/// <summary>
/// Richard Burton's Arabian Nights out of Project Gutenberg's HTML: the ten
/// volumes of The Book of the Thousand Nights and a Night (1885), and the
/// third of the Supplemental Nights (1887), which holds Aladdin, Ali Baba and
/// the other tales Galland added.
///
/// <b>Cited by night.</b> The main work is cited night and paragraph, so
/// 3.12 is the twelfth paragraph of the Third Night, wherever the volumes
/// break. Each "Now when it was the Third Night," is the night's heading,
/// 3.head, and the tales' titles are headings within it (3.t1, 3.t2). The
/// numbers are read from Burton's own words, "Hundred and Twenty-fourth"
/// included, and must run in order. The markers are written with a comma, a
/// full stop or nothing after them, in capitals once and in title case twice,
/// and once as "Now when it as the Hundred-and-first Night". Burton prints no
/// heading for the First Night: it begins with the first tale Shahrazad tells,
/// the Trader and the Jinni, and everything before it - the frame story of
/// Shahryar - is night 0.
/// The Supplemental tales mostly have no nights, and are cited tale and
/// paragraph instead (2.40 is the fortieth paragraph of Aladdin).
///
/// Left out: Burton's prefaces, contents, indexes and appendices, the
/// Terminal Essay, Clouston's variants and analogues, the illustrations, and
/// Burton's 5,000 footnotes, which are an anthropology of his own and often a
/// prurient and racist one. Verse is kept, a line to each " / ".
///
/// <b>Why the HTML.</b> Gutenberg's plain text of these volumes indents its
/// paragraphs, verse and footnotes differently from volume to volume, and
/// gathers the footnotes into blocks that end wherever the text resumes. The
/// HTML marks every footnote, page number, heading and verse line by class,
/// and its body is well-formed XML once the void elements are closed.
/// </summary>
public static class NightsLoader
{
    private static readonly Regex Night = new(@"^(?:Now when|When|And when) it w?as the (.+?) Night[,.;:]?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Void = new(@"<(br|hr|img|meta|link|wbr|col|input)\b([^>]*?)/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly string[] Stops = { "INDEX", "TERMINAL ESSAY", "APPENDIX", "VARIANTS AND ANALOGUES", "TRANSCRIBER" };

    private enum Kind { Heading, Night, Paragraph, Verse }

    private sealed record Block(Kind Kind, string Text, int Level = 0);

    /// <summary>
    /// The main work, from the ten volumes in order. The nights must end at
    /// the Thousand and First; a test passes a smaller number.
    /// </summary>
    public static List<(string Citation, string Text, bool IsHead)> Main(IEnumerable<string> volumes, int lastNight = 1001)
    {
        var passages = new List<(string, string, bool)>();
        var night = 0;
        var paragraph = 0;
        var title = 0;
        var topHeadings = 0;
        var first = true;

        foreach (var html in volumes)
        {
            foreach (var block in Blocks(html))
            {
                if (block.Kind == Kind.Night)
                {
                    var number = Ordinal(Night.Match(block.Text).Groups[1].Value);
                    if (number != night + 1)
                    {
                        throw new InvalidDataException($"\"{block.Text}\" follows night {night}");
                    }

                    night = number;
                    paragraph = 0;
                    title = 0;
                    passages.Add(($"{night}.head", block.Text, true));
                    continue;
                }

                if (block.Kind == Kind.Heading)
                {
                    // The First Night has no heading of its own; it opens with
                    // the first tale, the second top-level heading of volume 1.
                    if (first && block.Level <= 2 && ++topHeadings == 2 && night == 0)
                    {
                        night = 1;
                        paragraph = 0;
                        title = 0;
                    }

                    passages.Add(($"{night}.t{++title}", block.Text, true));
                    continue;
                }

                passages.Add(($"{night}.{++paragraph}", block.Text, false));
            }

            first = false;
        }

        if (night != lastNight) throw new InvalidDataException($"The nights end at {night}, not {lastNight}");
        return passages;
    }

    /// <summary>
    /// The third volume of the Supplemental Nights, from its two Gutenberg
    /// parts in order, cited tale and paragraph.
    /// </summary>
    public static List<(string Citation, string Text, bool IsHead)> Supplemental(IEnumerable<string> parts)
    {
        var passages = new List<(string, string, bool)>();
        var tale = 0;
        var paragraph = 0;
        var title = 0;

        foreach (var html in parts)
        {
            foreach (var block in Blocks(html))
            {
                if (block.Kind == Kind.Heading && block.Level <= 2)
                {
                    tale++;
                    paragraph = 0;
                    title = 0;
                    passages.Add(($"{tale}.head", block.Text, true));
                    continue;
                }

                if (tale == 0) continue;

                if (block.Kind is Kind.Heading or Kind.Night)
                {
                    passages.Add(($"{tale}.t{++title}", block.Text, true));
                    continue;
                }

                passages.Add(($"{tale}.{++paragraph}", block.Text, false));
            }
        }

        if (tale == 0) throw new InvalidDataException("No tales were found");
        return passages;
    }

    /// <summary>
    /// The text of one volume in reading order: from the first heading after
    /// its contents to the index, the Terminal Essay or the appendix.
    /// </summary>
    private static IEnumerable<Block> Blocks(string html)
    {
        var all = new List<Block>();
        Walk(Body(html), all);

        var contents = all.FindIndex(b => b.Kind == Kind.Heading && b.Text.StartsWith("CONTENTS OF THE", StringComparison.OrdinalIgnoreCase));
        var start = contents < 0 ? 0 : contents + 1;

        for (var i = start; i < all.Count; i++)
        {
            var block = all[i];
            if (block.Kind == Kind.Heading && block.Level <= 2
                && Stops.Any(s => block.Text.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                yield break;
            }

            yield return block;
        }
    }

    internal static XElement Body(string html)
    {
        var open = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var close = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (open < 0 || close < 0) throw new InvalidDataException("No body in the HTML");

        // The mirror's copies write &nbsp; where Gutenberg's own write the
        // character, and XML knows only its five built-in entities.
        var body = Void.Replace(html[open..(close + "</body>".Length)], "<$1$2/>");
        return XElement.Parse(XmlEntitySanitizer.Sanitize(body), LoadOptions.None);
    }

    private static void Walk(XElement element, List<Block> blocks)
    {
        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            var css = ((string?)child.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (css.Contains("pg-boilerplate") || css.Contains("footnote") || css.Contains("pageno")) continue;
            // An illustration is a block of its own with no text in it.
            if (child.Descendants().Any(d => d.Name.LocalName == "img")
                && !child.Descendants().Any(d => d.Name.LocalName == "p")) continue;

            if (name is "h1" or "h2" or "h3" or "h4" or "h5")
            {
                // h1 is the volume's title page.
                if (name == "h1") continue;
                var heading = Text(child);
                if (heading.Length > 0) blocks.Add(new Block(Kind.Heading, heading, name[1] - '0'));
                continue;
            }

            if (css.Contains("linegroup"))
            {
                var lines = child.Descendants().Where(d => ((string?)d.Attribute("class") ?? "").Split(' ').Contains("line"))
                    .Select(Text).Where(t => t.Length > 0).ToList();
                if (lines.Count > 0) blocks.Add(new Block(Kind.Verse, string.Join(" / ", lines)));
                continue;
            }

            if (name is "p" or "div")
            {
                var text = Text(child);
                if (Night.IsMatch(text))
                {
                    blocks.Add(new Block(Kind.Night, text));
                    continue;
                }

                if (name == "p")
                {
                    if (text.Length > 0 && text != "[Illustration]") blocks.Add(new Block(Kind.Paragraph, text));
                    continue;
                }
            }

            if (name is "table" or "style" or "script") continue;
            Walk(child, blocks);
        }
    }

    /// <summary>An element's text without page numbers or footnote references.</summary>
    private static string Text(XElement element)
    {
        var sb = new StringBuilder();
        Append(element, sb);
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
                case XElement child:
                    var css = (string?)child.Attribute("class") ?? "";
                    if (css.Contains("pageno") || css.Contains("footnote")) break;
                    if (child.Name.LocalName == "a" && ((string?)child.Attribute("href") ?? "").StartsWith("#f", StringComparison.Ordinal)) break;
                    if (child.Name.LocalName == "br") { sb.Append(' '); break; }
                    Append(child, sb);
                    break;
            }
        }
    }

    private static readonly Dictionary<string, int> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        ["first"] = 1, ["second"] = 2, ["third"] = 3, ["fourth"] = 4, ["fifth"] = 5, ["sixth"] = 6, ["seventh"] = 7, ["eighth"] = 8, ["ninth"] = 9,
        ["tenth"] = 10, ["eleventh"] = 11, ["twelfth"] = 12, ["thirteenth"] = 13, ["fourteenth"] = 14, ["fifteenth"] = 15,
        ["sixteenth"] = 16, ["seventeenth"] = 17, ["eighteenth"] = 18, ["nineteenth"] = 19,
        ["twentieth"] = 20, ["thirtieth"] = 30, ["fortieth"] = 40, ["fiftieth"] = 50, ["sixtieth"] = 60,
        ["seventieth"] = 70, ["eightieth"] = 80, ["ninetieth"] = 90,
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
        ["hundredth"] = 100, ["thousandth"] = 1000
    };

    /// <summary>
    /// Burton's ordinal as a number: "Second" 2, "Hundred and Twenty-fourth"
    /// 124, "Three Hundred and Fifty-eighth" 358, "Thousand and First" 1001,
    /// and his inversions, "Five-and-Fiftieth" 55. Five times he writes "the
    /// full Sixtieth" and the like, and "full" counts for nothing.
    /// </summary>
    public static int Ordinal(string words)
    {
        var total = 0;
        var current = 0;
        foreach (var word in Regex.Split(words.Trim(), @"[\s-]+").Where(w => w.Length > 0 && !w.Equals("and", StringComparison.OrdinalIgnoreCase) && !w.Equals("full", StringComparison.OrdinalIgnoreCase)))
        {
            if (word.Equals("hundred", StringComparison.OrdinalIgnoreCase)) { current = (current == 0 ? 1 : current) * 100; continue; }
            if (word.Equals("thousand", StringComparison.OrdinalIgnoreCase)) { total += (current == 0 ? 1 : current) * 1000; current = 0; continue; }
            if (word.Equals("hundredth", StringComparison.OrdinalIgnoreCase)) { current = (current == 0 ? 1 : current) * 100; continue; }
            if (!Units.TryGetValue(word, out var value)) throw new InvalidDataException($"\"{words}\" is not a number Burton writes");
            current += value;
        }

        return total + current;
    }
}
