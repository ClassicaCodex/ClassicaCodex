using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Lexicon;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.UI;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Tools.LexiconCitations;

/// <summary>
/// Measures how many of LSJ's and Lewis and Short's citations a library can
/// open, with the code the application ships: the entries read as the
/// dictionary step reads them, each citation resolved as Word Study resolves
/// it, in the edition the reader would open.
///
///   dotnet run --project tools/LexiconCitations -- \
///       --lexica path\to\PerseusDL\lexica \
///       --library %LOCALAPPDATA%\ClassicaCodex\classicacodex.db \
///       [--sample 80] [--seed 2026] [--out sample.tsv]
///
/// The library is opened through a read-only SQLite URI, and a write is tried
/// first to prove it. Nothing is written anywhere but the sample file.
///
/// Lewis and Short is read from lat.ls.perseus-eng2.xml alone, the 2023
/// revision; the dictionary step loads the older eng1 beside it, which would
/// count every Latin citation twice.
///
/// Besides the counts, every resolved citation that quotes at least two words
/// is checked against the passage it opens: whether at least half the quoted
/// words are in it or the line either side (the next thirty passages, for a
/// citation of a chapter the edition divides). The dictionaries abridge and
/// adapt what they quote, so this undercounts right answers; it is a floor,
/// and the hand reading of the sample is the measure.
/// </summary>
public static class Program
{
    private static readonly Regex Word = new(@"\w+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];

        if (!options.TryGetValue("--lexica", out var lexica) || !options.TryGetValue("--library", out var library))
        {
            Console.Error.WriteLine("usage: LexiconCitations --lexica <PerseusDL/lexica> --library <classicacodex.db> [--sample 80] [--seed 2026] [--out sample.tsv] [--only grc|lat]");
            return 2;
        }

        var sampleSize = options.TryGetValue("--sample", out var s) ? int.Parse(s, CultureInfo.InvariantCulture) : 80;
        var random = new Random(options.TryGetValue("--seed", out var seed) ? int.Parse(seed, CultureInfo.InvariantCulture) : 2026);
        var output = options.TryGetValue("--out", out var o) ? o : "lexicon-citation-sample.tsv";

        UseReadOnly(library);

        var pdllex = Path.Combine(lexica, "CTS_XML_TEI", "perseus", "pdllex");
        var dictionaries = new[]
        {
            (Name: "LSJ", Language: "grc", Files: Directory.GetFiles(Path.Combine(pdllex, "grc", "lsj"), "*.xml").OrderBy(f => f, StringComparer.Ordinal).ToArray()),
            (Name: "Lewis & Short", Language: "lat", Files: new[] { Path.Combine(pdllex, "lat", "ls", "lat.ls.perseus-eng2.xml") })
        };

        var resolver = new LexiconCitationResolver(e => ReaderEditionOrder.DefaultFirst(e, PreferredCollectionSettings.Preferred));
        await using var conn = await DbConnectionFactory.OpenConnectionAsync();

        var sample = new List<SampleRow>();
        var dumped = 0;
        var seen = 0;

        foreach (var dictionary in dictionaries)
        {
            if (options.TryGetValue("--only", out var only) && only != dictionary.Language) continue;
            var tally = new Tally();

            foreach (var file in dictionary.Files)
            foreach (var (entry, language, _) in LexiconIngestService.ReadEntryElements(file, dictionary.Language, dictionary.Name))
            {
                var (text, _) = LexiconIngestService.FlattenEntry(entry, language);
                var citations = LexiconCitationMarkup.Citations(text);
                if (citations.Count == 0) continue;

                var quotes = QuotesByOrdinal(entry);
                var resolved = await resolver.ResolveAsync(citations.Select(c => c.Target).ToList());

                for (var i = 0; i < citations.Count; i++)
                {
                    var citation = citations[i];
                    tally.Citations++;
                    if (citation.Target is not { } target) continue;

                    tally.Named++;
                    if (resolved[i] is not { } passage)
                    {
                        tally.Unresolved[target.WorkUrn] = tally.Unresolved.GetValueOrDefault(target.WorkUrn) + 1;
                        continue;
                    }

                    tally.Resolved++;
                    tally.ByMatch[passage.How] = tally.ByMatch.GetValueOrDefault(passage.How) + 1;
                    tally.Lines.Add((passage.Edition.EditionId, passage.TextNodeId));

                    var quote = i < quotes.Count ? quotes[i] : null;
                    var quoted = quote == null ? new List<string>() : Words(language == "grc" ? BetaCodeConverter.Convert(quote) : quote);
                    if (quoted.Count >= 2)
                    {
                        var around = Words(await AroundAsync(conn, passage.TextNodeId, passage.How == CitationMatch.Section ? 30 : 1)).ToHashSet(StringComparer.Ordinal);
                        var found = quoted.Count(around.Contains) * 2 >= quoted.Count;
                        if (!found && options.TryGetValue("--dump", out var dump) && dump == $"{target.WorkUrn}:{passage.How}" && dumped++ < 10)
                            Console.WriteLine($"   FAIL {citation.Label} | {target} | quoted: {quote} | opens {PassageCitation.Display(passage.CitationRef)} | {Clean(passage.Text)[..Math.Min(140, Clean(passage.Text).Length)]}");
                        tally.Quoted++;
                        if (found) tally.QuoteFound++;
                        var k = tally.QuoteByMatch.GetValueOrDefault(passage.How);
                        tally.QuoteByMatch[passage.How] = (k.Found + (found ? 1 : 0), k.Checked + 1);
                        var w = tally.QuoteByWork.GetValueOrDefault((target.WorkUrn, passage.How));
                        tally.QuoteByWork[(target.WorkUrn, passage.How)] = (w.Found + (found ? 1 : 0), w.Checked + 1);
                    }

                    // A reservoir sample of resolved citations, for reading by hand.
                    seen++;
                    var row = new SampleRow(dictionary.Name, Headword(entry, language), target, citation.Label,
                        quote == null ? string.Empty : language == "grc" ? BetaCodeConverter.Convert(quote) : quote, passage);
                    if (sample.Count < sampleSize) sample.Add(row);
                    else if (random.Next(seen) is var slot && slot < sampleSize) sample[slot] = row;
                }
            }

            await ReportAsync(conn, dictionary.Name, tally);
        }

        await WriteSampleAsync(conn, output, sample.OrderBy(_ => random.Next()).ToList());
        Console.WriteLine($"Sample of {sample.Count} resolved citations written to {Path.GetFullPath(output)}.");
        return 0;
    }

    private sealed class Tally
    {
        public int Citations, Named, Resolved, Quoted, QuoteFound;
        public readonly Dictionary<CitationMatch, int> ByMatch = new();
        public readonly Dictionary<CitationMatch, (int Found, int Checked)> QuoteByMatch = new();
        public readonly Dictionary<(string Work, CitationMatch How), (int Found, int Checked)> QuoteByWork = new();
        public readonly Dictionary<string, int> Unresolved = new(StringComparer.Ordinal);
        public readonly HashSet<(int Edition, long Node)> Lines = new();
    }

    private sealed record SampleRow(string Dictionary, string Headword, LexiconCitationTarget Target, string Label, string Quote, CitedPassage Passage);

    private static async Task ReportAsync(SqliteConnection conn, string name, Tally t)
    {
        static string Pct(int a, int b) => b == 0 ? "-" : (100.0 * a / b).ToString("0.0", CultureInfo.InvariantCulture) + "%";

        Console.WriteLine();
        Console.WriteLine($"== {name}");
        Console.WriteLine($"citations {t.Citations:N0}; naming a passage {t.Named:N0} ({Pct(t.Named, t.Citations)}); " +
                          $"resolved {t.Resolved:N0} ({Pct(t.Resolved, t.Citations)} of all, {Pct(t.Resolved, t.Named)} of those naming one); " +
                          $"distinct lines {t.Lines.Count:N0} in {t.Lines.Select(l => l.Edition).Distinct().Count():N0} editions");
        foreach (var (how, n) in t.ByMatch.OrderByDescending(kv => kv.Value))
        {
            var q = t.QuoteByMatch.GetValueOrDefault(how);
            Console.WriteLine($"   {how,-18} {n,9:N0}   quote found {q.Found:N0} of {q.Checked:N0} ({Pct(q.Found, q.Checked)})");
        }

        Console.WriteLine($"quote check, all: {t.QuoteFound:N0} of {t.Quoted:N0} ({Pct(t.QuoteFound, t.Quoted)})");
        Console.WriteLine("   works and steps where the quoted words are found less than half the time, most misses first: " + string.Join(", ", t.QuoteByWork
            .Where(kv => kv.Value.Checked >= 30 && kv.Value.Found * 2 < kv.Value.Checked)
            .OrderByDescending(kv => kv.Value.Checked - kv.Value.Found)
            .Take(int.TryParse(Environment.GetEnvironmentVariable("CC_WEAKEST"), out var take) ? take : 12)
            .Select(kv => $"{kv.Key.Work} {kv.Key.How} {kv.Value.Found}/{kv.Value.Checked}")));

        var held = 0;
        var notHeld = 0;
        var heldWorks = new List<(string Work, int Count)>();
        foreach (var (work, count) in t.Unresolved)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT EXISTS(SELECT 1 FROM Editions e JOIN Works w ON w.WorkId = e.WorkId
                                 WHERE (TRIM(w.CtsUrn) = @Work OR TRIM(w.CtsUrn) LIKE 'urn:cts:%:' || @Work) AND e.Kind = 'Original');";
            cmd.Parameters.AddWithValue("@Work", work);
            if (Convert.ToInt64(await cmd.ExecuteScalarAsync()) != 0)
            {
                held += count;
                heldWorks.Add((work, count));
            }
            else notHeld += count;
        }

        Console.WriteLine($"unresolved: {notHeld:N0} in works the library does not hold, {held:N0} in works it holds but numbers differently");
        Console.WriteLine("   most unresolved held works: " + string.Join(", ", heldWorks.OrderByDescending(w => w.Count).Take(12).Select(w => $"{w.Work} {w.Count:N0}")));
    }

    /// <summary>The words a dictionary quotes before each citation, by the citation's place in the entry; null where it quotes none.</summary>
    private static List<string?> QuotesByOrdinal(XElement entry)
    {
        var quotes = new List<string?>();
        foreach (var bibl in entry.Descendants().Where(e => e.Name.LocalName == "bibl"))
        {
            if (bibl.Ancestors().Any(a => a.Name.LocalName is "bibl" or "author" or "title" or "biblScope")) continue;
            if (LexiconCitationReader.Label(bibl).Length == 0) continue;

            var cit = bibl.Parent is { } parent && parent.Name.LocalName == "cit" ? parent : null;
            quotes.Add(cit?.Elements().FirstOrDefault(e => e.Name.LocalName == "quote")?.Value);
        }

        return quotes;
    }

    private static string Headword(XElement entry, string language)
    {
        var key = entry.Attribute("key")?.Value ?? string.Empty;
        return language == "grc" ? BetaCodeConverter.Convert(key) : key;
    }

    private static List<string> Words(string text) =>
        Word.Matches(Fold(text)).Select(m => m.Value).Where(w => w.Length >= 3).ToList();

    private static string Fold(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { 'ς' => 'σ', 'j' => 'i', 'v' => 'u', _ => c });
        }

        return sb.ToString();
    }

    /// <summary>The passage's text with the one before it and <paramref name="after"/> after it, in reading order.</summary>
    private static async Task<string> AroundAsync(SqliteConnection conn, long textNodeId, int after)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT group_concat(t.Text, ' ')
              FROM TextNodes n
              JOIN TextNodes t ON t.EditionId = n.EditionId AND t.SortOrder BETWEEN n.SortOrder - 1 AND n.SortOrder + @After
             WHERE n.TextNodeId = @Id;";
        cmd.Parameters.AddWithValue("@Id", textNodeId);
        cmd.Parameters.AddWithValue("@After", after);
        return await cmd.ExecuteScalarAsync() as string ?? string.Empty;
    }

    private static async Task WriteSampleAsync(SqliteConnection conn, string path, IReadOnlyList<SampleRow> rows)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(string.Join('\t', "n", "dictionary", "headword", "citation", "urn", "quoted", "opens", "how", "passage", "before", "after"));

        var n = 0;
        foreach (var r in rows)
        {
            var (before, after) = await NeighboursAsync(conn, r.Passage.TextNodeId);
            var opens = $"{r.Passage.AuthorName}, {r.Passage.WorkTitle} {PassageCitation.Display(r.Passage.CitationRef, r.Passage.Milestone)} [{r.Passage.Edition.CtsUrn}]";
            await writer.WriteLineAsync(string.Join('\t', new[]
            {
                (++n).ToString(CultureInfo.InvariantCulture), r.Dictionary, r.Headword, r.Label, r.Target.ToString(), r.Quote,
                opens, r.Passage.How.ToString(), r.Passage.Text, before, after
            }.Select(Clean)));
        }
    }

    private static async Task<(string Before, string After)> NeighboursAsync(SqliteConnection conn, long textNodeId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.SortOrder - n.SortOrder, t.Text
              FROM TextNodes n
              JOIN TextNodes t ON t.EditionId = n.EditionId AND t.SortOrder IN (n.SortOrder - 1, n.SortOrder + 1)
             WHERE n.TextNodeId = @Id;";
        cmd.Parameters.AddWithValue("@Id", textNodeId);
        string before = string.Empty, after = string.Empty;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.GetInt64(0) < 0) before = reader.GetString(1);
            else after = reader.GetString(1);
        }

        return (before, after);
    }

    private static string Clean(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > 600 ? flat[..600] + "…" : flat;
    }

    /// <summary>
    /// Points the connection factory at the library through a read-only URI,
    /// and proves it with a write that would change nothing even if allowed.
    /// </summary>
    private static void UseReadOnly(string library)
    {
        typeof(DbConnectionFactory)
            .GetField("_databasePath", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, "file:" + Path.GetFullPath(library).Replace('\\', '/') + "?mode=ro");

        using var conn = DbConnectionFactory.OpenConnectionAsync().GetAwaiter().GetResult();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Tags WHERE 0;";
        try
        {
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 8)
        {
            return;
        }

        throw new InvalidOperationException("The library opened writable; stopping before anything else touches it.");
    }
}
