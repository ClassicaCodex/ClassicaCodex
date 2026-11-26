using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ClassicaCodex.Core.Quotations;

/// <summary>
/// The shipped table of quotation links: written by tools/QuotationLinks,
/// embedded in the application as QuotationLinks.tsv.gz, read once.
///
/// <b>References and positions only, never text.</b> Every word the reader
/// sees comes out of their own library. Shipping the quoted lines would be
/// redistributing other people's editions inside the executable, which the
/// CATMuS and BFM catalogues avoid for the same reason; it would also make the
/// table ten times the size.
///
/// Tab-separated, in three sections, each row numbered by its place in its
/// section:
///
///   quotation-links  1
///   E  edition URN  reference prefix
///   P  edition row  reference  fingerprint
///   L  quoting P row  start  end  quoted P row  start  end  T|A  kind  score  flag
///
/// Most references repeat their edition's URN in full
/// (urn:cts:greekLit:tlg0003.tlg001.perseus-grc2.1.3.4.1), so an edition
/// records that prefix once and its references are written as "~" and the
/// rest. A reference that happens to start with "~" or "=" is written behind
/// "=", so the two cannot be confused.
/// </summary>
public static class QuotationLinkFile
{
    public const string Header = "quotation-links";
    public const int Version = 1;

    public static void Write(TextWriter writer, IEnumerable<QuotationLink> links, IReadOnlyDictionary<string, string>? referencePrefixes = null)
    {
        var editions = new List<string>();
        var editionRow = new Dictionary<string, int>(StringComparer.Ordinal);
        var passages = new List<(int Edition, string Ref, string Fingerprint)>();
        var passageRow = new Dictionary<(string, string, string), int>();
        var rows = new List<string>();

        int Passage(PassageKey key, string fingerprint)
        {
            Check(key.EditionUrn, "an edition URN");
            Check(key.CitationRef, "a reference");
            if (!editionRow.TryGetValue(key.EditionUrn, out var edition))
            {
                edition = editions.Count;
                editions.Add(key.EditionUrn);
                editionRow[key.EditionUrn] = edition;
            }

            if (!passageRow.TryGetValue((key.EditionUrn, key.CitationRef, fingerprint), out var row))
            {
                row = passages.Count;
                passages.Add((edition, key.CitationRef, fingerprint));
                passageRow[(key.EditionUrn, key.CitationRef, fingerprint)] = row;
            }

            return row;
        }

        foreach (var link in links)
        {
            Check(link.Kind, "a kind");
            Check(link.Flag, "a flag");
            var quoting = Passage(link.Quoting, link.QuotingFingerprint);
            var source = Passage(link.Source, link.SourceFingerprint);
            rows.Add(string.Join('\t',
                "L",
                quoting.ToString(CultureInfo.InvariantCulture),
                link.QuotingStart.ToString(CultureInfo.InvariantCulture),
                link.QuotingEnd.ToString(CultureInfo.InvariantCulture),
                source.ToString(CultureInfo.InvariantCulture),
                link.SourceStart.ToString(CultureInfo.InvariantCulture),
                link.SourceEnd.ToString(CultureInfo.InvariantCulture),
                link.Finder == QuotationFinder.Testimonia ? "T" : "A",
                link.Kind,
                link.Score.ToString("0.###", CultureInfo.InvariantCulture),
                link.Flag));
        }

        writer.Write($"{Header}\t{Version}\n");

        var prefixes = new string[editions.Count];
        for (var i = 0; i < editions.Count; i++)
        {
            prefixes[i] = referencePrefixes != null && referencePrefixes.TryGetValue(editions[i], out var prefix) ? prefix : string.Empty;
            Check(prefixes[i], "a reference prefix");
            writer.Write($"E\t{editions[i]}\t{prefixes[i]}\n");
        }

        foreach (var (edition, reference, fingerprint) in passages)
            writer.Write($"P\t{edition.ToString(CultureInfo.InvariantCulture)}\t{EncodeReference(reference, prefixes[edition])}\t{fingerprint}\n");

        foreach (var row in rows) writer.Write(row + "\n");
    }

    public static List<QuotationLink> Read(TextReader reader)
    {
        var first = reader.ReadLine();
        var head = first?.Split('\t');
        if (head is not { Length: 2 } || head[0] != Header)
            throw new InvalidDataException("not a quotation links file");
        if (!int.TryParse(head[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version != Version)
            throw new InvalidDataException($"quotation links file version {head[1]}, expected {Version}");

        var editions = new List<(string Urn, string Prefix)>();
        var passages = new List<(PassageKey Key, string Fingerprint)>();
        var links = new List<QuotationLink>();
        var number = 1;

        while (reader.ReadLine() is { } line)
        {
            number++;
            if (line.Length == 0) continue;

            var parts = line.Split('\t');
            switch (parts[0])
            {
                case "E" when parts.Length == 3:
                    editions.Add((parts[1], parts[2]));
                    break;

                case "P" when parts.Length == 4:
                {
                    var edition = editions[Index(parts[1], editions.Count, number)];
                    passages.Add((new PassageKey(edition.Urn, DecodeReference(parts[2], edition.Prefix)), parts[3]));
                    break;
                }

                case "L" when parts.Length == 11:
                {
                    var quoting = passages[Index(parts[1], passages.Count, number)];
                    var source = passages[Index(parts[4], passages.Count, number)];
                    links.Add(new QuotationLink(
                        quoting.Key, quoting.Fingerprint, Number(parts[2], number), Number(parts[3], number),
                        source.Key, source.Fingerprint, Number(parts[5], number), Number(parts[6], number),
                        parts[7] == "T" ? QuotationFinder.Testimonia : QuotationFinder.Afterlives,
                        parts[8],
                        double.TryParse(parts[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var score) ? score : 0,
                        parts[10]));
                    break;
                }

                default:
                    throw new InvalidDataException($"line {number} of the quotation links is not a row this reader knows");
            }
        }

        return links;
    }

    /// <summary>The table compressed, as it is embedded.</summary>
    public static void WriteCompressed(Stream output, IEnumerable<QuotationLink> links, IReadOnlyDictionary<string, string>? referencePrefixes = null)
    {
        using var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
        Write(writer, links, referencePrefixes);
    }

    public static List<QuotationLink> ReadCompressed(Stream input)
    {
        using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return Read(reader);
    }

    internal static string EncodeReference(string reference, string prefix)
    {
        if (prefix.Length > 0 && reference.Length > prefix.Length && reference.StartsWith(prefix, StringComparison.Ordinal))
            return "~" + reference[prefix.Length..];

        return reference.StartsWith('~') || reference.StartsWith('=') ? "=" + reference : reference;
    }

    internal static string DecodeReference(string written, string prefix) =>
        written.StartsWith('~') ? prefix + written[1..]
        : written.StartsWith('=') ? written[1..]
        : written;

    private static void Check(string value, string what)
    {
        if (value.Contains('\t') || value.Contains('\n') || value.Contains('\r'))
            throw new ArgumentException($"{what} holds a tab or a line break, which the table cannot carry: {value}");
    }

    private static int Index(string text, int count, int line) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < count
            ? index
            : throw new InvalidDataException($"line {line} of the quotation links refers to a row that is not there");

    private static int Number(string text, int line) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"line {line} of the quotation links has a position that is not a number");
}
