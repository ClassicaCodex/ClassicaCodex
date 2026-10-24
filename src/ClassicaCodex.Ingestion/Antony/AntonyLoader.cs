using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassicaCodex.Ingestion.Dionysius;

namespace ClassicaCodex.Ingestion.Antony;

/// <summary>
/// Athanasius's Life of Antony in the English of the Rev. H. Ellershaw, out
/// of the Christian Classics Ethereal Library's ThML file of the Nicene and
/// Post-Nicene Fathers, second series, volume 4 (1892).
///
/// The Life is one div2 of a 900-page volume of Athanasius. Ellershaw numbers
/// it in the 94 sections every edition uses, each opening its paragraph with
/// its number ("8. Thus tightening his hold upon himself..."); a section that
/// runs to a second paragraph continues as 8.2. The letter to the monks
/// abroad that comes before section 1 is the preface, "pr".
///
/// The editor's table of contents at the head of the Life is not text, but
/// its lines ("§§8-10. His life in the tombs, and combats with demons
/// there.") become headings before the sections they describe, which is how
/// the printed volume uses them. Footnotes and page breaks are left out.
/// </summary>
public static class AntonyLoader
{
    public const string Life = "Life of Antony. (Vita Antoni.)";

    private static readonly Regex Numbered = new(@"^(\d{1,2})\.\s+", RegexOptions.Compiled);
    private static readonly Regex Contents = new(@"^§§?\s*(\d{1,2})[\d,\s–-]*\.\s+(.+)$", RegexOptions.Compiled);

    public static List<(string Citation, string Text, bool IsHead)> Load(XDocument document)
    {
        var life = document.Descendants("div2").FirstOrDefault(d => (string?)d.Attribute("title") == Life)
            ?? throw new InvalidDataException("The Life of Antony was not found");

        var headings = new Dictionary<int, string>();
        var passages = new List<(string, string, bool)>();
        var section = 0;
        var paragraph = 0;

        foreach (var div in life.Elements("div3"))
        {
            var title = (string?)div.Attribute("title") ?? "";
            var paragraphs = div.Elements("p").Select(DionysiusLoader.Text).Where(t => t.Length > 0).ToList();

            if (title.StartsWith("Prologue", StringComparison.Ordinal))
            {
                foreach (var line in paragraphs)
                {
                    var entry = Contents.Match(line);
                    if (entry.Success) headings[int.Parse(entry.Groups[1].Value)] = entry.Groups[2].Value.Trim();
                }

                continue;
            }

            if (title.StartsWith("Preface", StringComparison.Ordinal))
            {
                // The heading and the title of the Life are the volume's, not
                // Athanasius's; the letter begins at its salutation.
                var start = paragraphs.FindIndex(p => p.StartsWith("Athanasius the bishop", StringComparison.Ordinal));
                var letter = paragraphs.Skip(Math.Max(0, start)).ToList();
                for (var i = 0; i < letter.Count; i++) passages.Add((i == 0 ? "pr" : $"pr.{i + 1}", letter[i], false));
                continue;
            }

            foreach (var text in paragraphs)
            {
                var number = Numbered.Match(text);
                if (number.Success)
                {
                    var n = int.Parse(number.Groups[1].Value);
                    if (n != section + 1)
                    {
                        throw new InvalidDataException($"Section {n} follows section {section}");
                    }

                    section = n;
                    paragraph = 1;
                    if (headings.TryGetValue(n, out var heading)) passages.Add(($"{n}.head", heading, true));
                    // The number is the citation; it is not repeated in the text.
                    passages.Add(($"{n}", text[number.Length..], false));
                }
                else if (section > 0)
                {
                    paragraph++;
                    passages.Add(($"{section}.{paragraph}", text, false));
                }
            }
        }

        if (section != 94) throw new InvalidDataException($"{section} sections were found, not 94");
        return passages;
    }
}
