using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Quran;

/// <summary>
/// One sura of the Tanzil text: its number, its Arabic name, the basmala
/// written above it where the mushaf has one, and its verses.
/// </summary>
public sealed record TanzilSura(
    int Number, string Name, string? Basmala, IReadOnlyList<(int Verse, string Text)> Verses);

/// <summary>
/// Reads the Quran out of its two sources: the Tanzil Project's XML, and
/// Project Gutenberg's file of three English translations side by side.
/// </summary>
public static class QuranLoader
{
    /// <summary>
    /// The suras and the copyright notice of one Tanzil XML file.
    ///
    /// <b>The XML rather than Tanzil's plain text, for the basmala.</b> The
    /// plain text writes "In the name of God, the Merciful, the
    /// Compassionate" into the first verse of every sura but the first and
    /// the ninth, where the Madina mushaf prints it above the sura and does
    /// not count it. The XML gives it an attribute of its own, so verse 1 is
    /// the verse and the translation beside it says the same thing.
    ///
    /// The notice is returned because the terms require it to travel with
    /// every copy of the text, unaltered.
    /// </summary>
    public static (IReadOnlyList<TanzilSura> Suras, string Notice) ParseTanzil(string xml)
    {
        var document = XDocument.Parse(xml);

        var notice = document.Nodes().OfType<XComment>()
            .Select(c => c.Value)
            .FirstOrDefault(c => c.Contains("Tanzil", StringComparison.Ordinal))
            ?? throw new InvalidDataException("The Tanzil copyright notice is missing");

        var suras = (document.Root?.Elements("sura") ?? Enumerable.Empty<XElement>())
            .Select(sura =>
            {
                var verses = sura.Elements("aya").ToList();
                return new TanzilSura(
                    (int)sura.Attribute("index")!,
                    (string?)sura.Attribute("name") ?? "",
                    verses.Count == 0 ? null : (string?)verses[0].Attribute("bismillah"),
                    verses.Select(a => ((int)a.Attribute("index")!, (string?)a.Attribute("text") ?? "")).ToList());
            })
            .ToList();

        var lines = notice.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd());
        return (suras, string.Join("\n", lines).Trim('\n'));
    }

    private static readonly Regex Label = new(@"^\d{1,3}\.\d{1,3}$", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Pickthall's translation, verse by verse, out of Gutenberg's
    /// "Three Translations of The Koran" (#16955).
    ///
    /// Each verse is a label - 002.255 - and three paragraphs marked Y:, P:
    /// and S:, for Yusuf Ali, Pickthall and Shakir. Only P: is taken. Pickthall
    /// (1930) died in 1936 and is in the public domain everywhere; the other
    /// two are not safe to sell under - Yusuf Ali's has circulated in revised
    /// editions that are still in copyright, and Shakir's is claimed by its
    /// publisher.
    ///
    /// <b>Four labels in the file are damaged.</b> 17.33 is printed "0.033",
    /// 39.46 "039.04", 45.32 "04.032" and 56.26 "05.026": a digit lost each
    /// time. Each is read as the verse that comes next, provided the damaged
    /// label is what that verse's label would be with characters missing;
    /// anything else is refused rather than guessed at, so a verse can never
    /// be filed under another verse's number.
    /// </summary>
    public static IReadOnlyList<(int Sura, int Verse, string Text)> Pickthall(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("*** START OF", StringComparison.Ordinal));
        var end = Array.FindIndex(lines, l => l.StartsWith("*** END OF", StringComparison.Ordinal));
        if (start < 0) throw new InvalidDataException("Gutenberg's start marker was not found");
        if (end < 0) end = lines.Length;

        var verses = new List<(int Sura, int Verse, string Text)>();
        (int Sura, int Verse) previous = (1, 0);

        for (var i = start + 1; i < end; i++)
        {
            var label = lines[i].Trim();
            if (!Label.IsMatch(label)) continue;

            var current = Resolve(label, previous);
            var translation = Paragraph(lines, i + 1, end, "P: ");
            if (translation == null)
            {
                throw new InvalidDataException($"No Pickthall translation of {current.Sura}.{current.Verse}");
            }

            verses.Add((current.Sura, current.Verse, translation));
            previous = current;
        }

        return verses;
    }

    /// <summary>
    /// The verse a label stands for: itself when it is whole and in sequence,
    /// the next verse or the first of the next sura when it is a damaged copy
    /// of one of them.
    /// </summary>
    private static (int Sura, int Verse) Resolve(string label, (int Sura, int Verse) previous)
    {
        (int, int)[] next = { (previous.Sura, previous.Verse + 1), (previous.Sura + 1, 1) };

        foreach (var (sura, verse) in next)
        {
            var whole = $"{sura:000}.{verse:000}";
            if (label == whole) return (sura, verse);
        }

        foreach (var (sura, verse) in next)
        {
            if (IsDamagedCopy(label, $"{sura:000}.{verse:000}")) return (sura, verse);
        }

        throw new InvalidDataException(
            $"Verse label {label} after {previous.Sura}.{previous.Verse} is neither the next verse nor a damaged copy of it");
    }

    /// <summary>Whether a label is the whole one with characters dropped and nothing else changed.</summary>
    private static bool IsDamagedCopy(string label, string whole)
    {
        if (label.Length >= whole.Length) return false;

        var j = 0;
        foreach (var ch in whole)
        {
            if (j < label.Length && label[j] == ch) j++;
        }

        return j == label.Length;
    }

    /// <summary>
    /// The paragraph that begins with this marker in a verse's block, its
    /// lines rejoined; null if the block has none.
    /// </summary>
    private static string? Paragraph(string[] lines, int from, int end, string marker)
    {
        for (var i = from; i < end; i++)
        {
            var line = lines[i];
            if (Label.IsMatch(line.Trim())) return null;
            if (!line.StartsWith(marker, StringComparison.Ordinal)) continue;

            var parts = new List<string> { line[marker.Length..] };
            for (var j = i + 1; j < end && lines[j].Trim().Length > 0; j++) parts.Add(lines[j]);

            return Whitespace.Replace(string.Join(" ", parts), " ").Trim();
        }

        return null;
    }
}
