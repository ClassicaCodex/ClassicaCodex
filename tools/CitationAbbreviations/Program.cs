using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.Tools.CitationAbbreviations;

/// <summary>
/// Rebuilds src/ClassicaCodex.Core/Citations/CitationAbbreviations.tsv: how
/// LSJ and Lewis and Short abbreviate the works they cite, and which work each
/// abbreviation names.
///
///   dotnet run --project tools/CitationAbbreviations -- \
///       --lexica path\to\PerseusDL\lexica \
///       --out src\ClassicaCodex.Core\Citations\CitationAbbreviations.tsv
///
/// Every citation in the two dictionaries that Perseus gave a CTS URN is a
/// pair: the abbreviation as printed ("Pl. R.", "Verg. A.", "Th.") and the
/// work its URN names. Counted over the whole of both, an abbreviation names
/// one work nearly always, and that is the table. The one exception kept is
/// the orators, cited by speech: "D. 18.169" is the eighteenth speech of
/// Demosthenes, and the number picks the work. Where an abbreviation's first
/// number picks its work consistently, the table says which work each number
/// is (Isocrates' speech 4 is tlg0010.tlg011, not tlg004).
///
/// Lewis and Short is read from lat.ls.perseus-eng2.xml alone, the 2023
/// revision, as tools/LexiconCitations does.
/// </summary>
public static class Program
{
    // Where the reference starts: a digit, a section sign, or one of the
    // words the dictionaries put in front of a number.
    private static readonly Regex ReferenceStart = new(
        @"(?:\d|§|(?<![\p{L}])(?:fr|Fr|frg|p|pp|pr|praef|prol|init|fin|ext|extr)\.?\s*(?=\d))",
        RegexOptions.CultureInvariant);

    private static readonly Regex FirstNumber = new(@"^\s*(\d+)", RegexOptions.CultureInvariant);

    private static readonly Regex LeadingDigits = new(@"^\d+", RegexOptions.CultureInvariant);

    private static readonly string[] Skipped = { "id.", "Id.", "ib.", "Ib.", "ibid.", "Ibid.", "v.", "cf.", "Vulg." };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];
        if (!options.TryGetValue("--lexica", out var lexica) || !options.TryGetValue("--out", out var output))
        {
            Console.Error.WriteLine("usage: CitationAbbreviations --lexica <PerseusDL/lexica> --out <CitationAbbreviations.tsv>");
            return 2;
        }

        var root = Path.Combine(lexica, "CTS_XML_TEI", "perseus", "pdllex");
        var sources = new List<(string Language, string File)>();
        sources.AddRange(Directory.GetFiles(Path.Combine(root, "grc", "lsj"), "grc.lsj.perseus-eng*.xml").Order(StringComparer.Ordinal).Select(f => ("grc", f)));
        sources.Add(("lat", Path.Combine(root, "lat", "ls", "lat.ls.perseus-eng2.xml")));

        var uses = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var numbered = new Dictionary<string, List<(int Number, string Work, bool Repeats)>>(StringComparer.Ordinal);
        var seen = 0;

        foreach (var (language, file) in sources)
        {
            var reader = new LexiconCitationReader(language);
            foreach (var bibl in Bibls(file))
            {
                var urn = bibl.Attribute("n")?.Value;
                if (urn == null || !urn.StartsWith("urn:cts:", StringComparison.Ordinal)) continue;
                var parts = urn.Split(':');
                if (parts.Length < 5) continue;
                var segments = parts[3].Split('.');
                if (segments.Length < 2) continue;

                var label = LexiconCitationReader.Label(bibl);
                var start = ReferenceStart.Match(label);
                if (!start.Success) continue;
                var abbreviation = label[..start.Index].Trim().TrimEnd(',', ';').Trim();
                if (!abbreviation.Any(char.IsLetter) || abbreviation.Any(char.IsDigit)) continue;
                if (Skipped.Any(s => abbreviation.StartsWith(s, StringComparison.Ordinal))) continue;

                // The reader's own corrections where it makes them (Horace's
                // Odes); the URN's work where it declines the reference.
                var work = reader.Read(urn, label)?.WorkUrn ?? $"{segments[0]}.{segments[1]}";
                seen++;

                if (!uses.TryGetValue(abbreviation, out var works)) uses[abbreviation] = works = new(StringComparer.Ordinal);
                works[work] = works.GetValueOrDefault(work) + 1;

                if (FirstNumber.Match(label[start.Index..]) is { Success: true } first)
                {
                    var number = int.Parse(first.Groups[1].Value, CultureInfo.InvariantCulture);
                    // Whether the URN's reference begins with the same number:
                    // a page ("980a" for Metaph. 980a21) or a book does, and is
                    // no speech number.
                    var passage = LeadingDigits.Match(string.Join(':', parts.Skip(4))).Value;
                    if (!numbered.TryGetValue(abbreviation, out var list)) numbered[abbreviation] = list = new();
                    list.Add((number, work, passage == first.Groups[1].Value));
                }
            }
        }

        var rows = new List<string>();
        var speechAbbreviations = 0;
        foreach (var (abbreviation, works) in uses.OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            var total = works.Values.Sum();
            if (Speeches(works, numbered.GetValueOrDefault(abbreviation)) is { } speeches)
            {
                speechAbbreviations++;
                foreach (var (number, work) in speeches)
                    rows.Add($"s\t{abbreviation}\t{number.ToString(CultureInfo.InvariantCulture)}\t{work}");
                continue;
            }

            foreach (var (work, count) in works.OrderByDescending(w => w.Value).ThenBy(w => w.Key, StringComparer.Ordinal))
                if (count >= 2 && count * 10 >= total)
                    rows.Add($"a\t{abbreviation}\t{work}\t{count.ToString(CultureInfo.InvariantCulture)}");
        }

        var header = new[]
        {
            "# How LSJ and Lewis and Short abbreviate the works they cite. Built by tools/CitationAbbreviations",
            "# from the Perseus lexica (PerseusDL/lexica, CC BY-SA 4.0): each citation Perseus gave a CTS URN pairs",
            "# the abbreviation as printed with the work the URN names.",
            "#   a <abbreviation> <work> <citations>  - the abbreviation names that work, in that many citations",
            "#   s <abbreviation> <number> <work>     - an orator cited by speech: the first number picks the work",
        };

        File.WriteAllLines(output, header.Concat(rows), new UTF8Encoding(false));
        Console.WriteLine($"{seen:N0} citations, {uses.Count:N0} abbreviations, {speechAbbreviations} cited by speech, {rows.Count:N0} rows -> {output}");
        return 0;
    }

    /// <summary>
    /// The work each speech number names, where the abbreviation is an
    /// orator's: spread over several works, its first number picking the work
    /// nine times in ten or more, and that number not repeated in the URN's
    /// own reference (Lycurgus' one speech is cited by section, and is not).
    /// </summary>
    private static IReadOnlyList<(int Number, string Work)>? Speeches(
        Dictionary<string, int> works, List<(int Number, string Work, bool Repeats)>? numbers)
    {
        if (works.Count < 2 || numbers == null || numbers.Count < 20) return null;
        if (numbers.Count(n => n.Repeats) * 10 > numbers.Count * 3) return null;

        var byNumber = numbers.GroupBy(n => n.Number).ToList();
        var consistent = byNumber.Sum(g => g.GroupBy(n => n.Work).Max(w => w.Count()));
        if (consistent * 10 < numbers.Count * 9) return null;

        var speeches = new List<(int, string)>();
        foreach (var group in byNumber.OrderBy(g => g.Key))
        {
            var top = group.GroupBy(n => n.Work).OrderByDescending(w => w.Count()).ThenBy(w => w.Key, StringComparer.Ordinal).First();
            if (group.Count() >= 2 && top.Count() * 5 >= group.Count() * 4) speeches.Add((group.Key, top.Key));
        }

        return speeches.Count >= 2 ? speeches : null;
    }

    private static IEnumerable<XElement> Bibls(string file)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true };
        using var reader = XmlReader.Create(file, settings);
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "bibl")
                yield return (XElement)XNode.ReadFrom(reader);
            else
                reader.Read();
        }
    }
}
