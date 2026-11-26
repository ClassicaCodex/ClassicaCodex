using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClassicaCodex.Core.Quotations;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Tools.QuotationLinks;

/// <summary>
/// Rebuilds QuotationLinks.tsv.gz - who quotes what - from two machine
/// searches run over the library outside this repository:
///
///   Testimonia (testimonia.db): an author named beside a citing word, and
///   the named author's passage sharing at least two four-word runs with the
///   words around the name. Every row of its quotes table is taken.
///
///   Afterlives (afterlives-latin.tsv): Latin quotations of the classics in
///   the Patrologia and CSEL, found by shared rare words and by runs of
///   consecutive words. Only the rows it rated high are taken; it puts
///   roughly a third of its low rows right, which makes them leads, not
///   links.
///
/// Both name passages as they were when the search ran. This re-keys every
/// link by the edition's CTS URN and the citation reference, against a
/// library opened read-only, and records a fingerprint of each passage's
/// text there, so the application can tell when a reader's copy differs.
///
///   dotnet run --project tools/QuotationLinks -- \
///       --library   path\to\classicacodex.db \
///       --testimonia path\to\testimonia.db \
///       --afterlives path\to\afterlives-latin.tsv
///
/// It writes src/ClassicaCodex.Core/Quotations/QuotationLinks.tsv.gz, an
/// embedded resource, unless --out says otherwise. Nothing is ever written to
/// the library or to either input.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];

        if (!options.TryGetValue("--library", out var libraryPath)
            || !options.TryGetValue("--testimonia", out var testimoniaPath)
            || !options.TryGetValue("--afterlives", out var afterlivesPath))
        {
            Console.Error.WriteLine("usage: QuotationLinks --library <classicacodex.db> --testimonia <testimonia.db> --afterlives <afterlives-latin.tsv> [--out <file>]");
            return 2;
        }

        var output = options.TryGetValue("--out", out var given)
            ? given
            : Path.Combine(FindRepositoryRoot(), "src", "ClassicaCodex.Core", "Quotations", "QuotationLinks.tsv.gz");

        using var library = OpenReadOnly(libraryPath);
        var lookup = new Library(library);

        var links = new List<QuotationLink>();
        using (var testimonia = OpenReadOnly(testimoniaPath))
            links.AddRange(FromTestimonia(testimonia, lookup));
        links.AddRange(FromAfterlives(afterlivesPath, lookup));

        var unique = new List<QuotationLink>();
        var seen = new HashSet<(PassageKey, string, PassageKey, string, QuotationFinder)>();
        foreach (var link in links)
            if (seen.Add((link.Quoting, link.QuotingFingerprint, link.Source, link.SourceFingerprint, link.Finder)))
                unique.Add(link);

        using (var file = File.Create(output))
            QuotationLinkFile.WriteCompressed(file, unique, lookup.ReferencePrefixes);

        var marked = unique.Select(l => (QuotationIndex.EditionKey(l.Source.EditionUrn), l.Source.CitationRef)).Distinct().Count();
        var quoting = unique.Select(l => (QuotationIndex.EditionKey(l.Quoting.EditionUrn), l.Quoting.CitationRef)).Distinct().Count();
        var both = unique.GroupBy(l => (l.Quoting, l.Source)).Count(g => g.Select(l => l.Finder).Distinct().Count() == 2);
        Console.WriteLine($"{unique.Count:N0} links ({links.Count - unique.Count:N0} duplicates dropped): " +
                          $"{unique.Count(l => l.Finder == QuotationFinder.Testimonia):N0} Testimonia, " +
                          $"{unique.Count(l => l.Finder == QuotationFinder.Afterlives):N0} Afterlives, {both:N0} found by both");
        Console.WriteLine($"{marked:N0} quoted lines in {unique.Select(l => QuotationIndex.EditionKey(l.Source.EditionUrn)).Distinct().Count():N0} editions; " +
                          $"{quoting:N0} quoting passages");
        Console.WriteLine($"wrote {output} ({new FileInfo(output).Length:N0} bytes)");
        return 0;
    }

    // ------------------------------------------------------------------ Testimonia

    private static IEnumerable<QuotationLink> FromTestimonia(SqliteConnection testimonia, Library library)
    {
        var dropped = new Dictionary<string, int>(StringComparer.Ordinal);
        var taken = 0;

        using var cmd = testimonia.CreateCommand();
        cmd.CommandText = @"
            SELECT x.kind, x.shared, x.q_start, x.q_end, x.s_start, x.s_end,
                   lq.edition_urn, p.ref, p.text, ls.edition_urn, s.ref, s.text
              FROM quotes x
              JOIN passages p ON p.node_id = x.node_id
              JOIN sources s ON s.node_id = x.src_node
              JOIN licences lq ON lq.edition_id = p.edition_id
              JOIN licences ls ON ls.edition_id = s.edition_id
             ORDER BY x.node_id, x.entity;";

        var results = new List<QuotationLink>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var quoting = library.Place(reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetInt32(2), reader.GetInt32(3), out var whyQuoting);
            var source = library.Place(reader.GetString(9), reader.GetString(10), reader.GetString(11), reader.GetInt32(4), reader.GetInt32(5), out var whySource);
            if (quoting == null || source == null)
            {
                var why = (quoting == null ? "quoting " + whyQuoting : "quoted " + whySource)!;
                dropped[why] = dropped.GetValueOrDefault(why) + 1;
                continue;
            }

            taken++;
            results.Add(new QuotationLink(
                quoting.Key, quoting.Fingerprint, quoting.Start, quoting.End,
                source.Key, source.Fingerprint, source.Start, source.End,
                QuotationFinder.Testimonia, reader.GetString(0), reader.GetInt32(1), string.Empty));
        }

        Console.WriteLine($"Testimonia: {taken:N0} links placed" + Describe(dropped));
        return results;
    }

    // ------------------------------------------------------------------ Afterlives

    private static IEnumerable<QuotationLink> FromAfterlives(string path, Library library)
    {
        var dropped = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = ReadEscapedTsv(path);
        var header = rows[0];
        int Column(string name) => Array.IndexOf(header, name) is var i and >= 0 ? i : throw new InvalidDataException($"afterlives table has no {name} column");

        int conf = Column("conf"), pathColumn = Column("path"), flag = Column("flag"), score = Column("score"),
            author = Column("author"), work = Column("work"), reference = Column("ref"),
            sourceAuthor = Column("source_author"), sourceWork = Column("source_work"), sourceRef = Column("source_ref"),
            evidence = Column("evidence");

        var results = new List<QuotationLink>();
        var high = 0;
        foreach (var row in rows.Skip(1))
        {
            if (row.Length != header.Length) throw new InvalidDataException("an afterlives row has the wrong number of columns");
            if (row[conf] != "high") continue;
            high++;

            var words = row[evidence];
            var quoting = library.BestPlaceByWords(row[author], row[work], row[reference], words, QuotingCollections);
            if (quoting == null)
            {
                dropped["quoting passage not found or quotation not located"] = dropped.GetValueOrDefault("quoting passage not found or quotation not located") + 1;
                continue;
            }

            foreach (var line in row[sourceRef].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var source = library.FirstPlaceByWords(row[sourceAuthor], row[sourceWork], line, words, SourceCollections);
                if (source == null)
                {
                    dropped["quoted line not found"] = dropped.GetValueOrDefault("quoted line not found") + 1;
                    continue;
                }

                results.Add(new QuotationLink(
                    quoting.Key, quoting.Fingerprint, quoting.Start, quoting.End,
                    source.Key, source.Fingerprint, source.Start, source.End,
                    QuotationFinder.Afterlives, row[pathColumn],
                    double.Parse(row[score], NumberStyles.Float, CultureInfo.InvariantCulture), row[flag]));
            }
        }

        Console.WriteLine($"Afterlives: {high:N0} high rows, {results.Count:N0} links placed" + Describe(dropped));
        return results;
    }

    private static readonly string[] QuotingCollections = { "patrologia-latina", "csel" };
    private static readonly string[] SourceCollections = { "perseus-latin" };

    /// <summary>
    /// A tab-separated table written by Python's csv module with no quoting
    /// and a backslash escape: a backslash makes the next character literal,
    /// tab and line break included.
    /// </summary>
    private static List<string[]> ReadEscapedTsv(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\\' && i + 1 < text.Length)
            {
                field.Append(text[++i]);
            }
            else if (ch == '\t')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
            }
            else
            {
                field.Append(ch);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }

    // ------------------------------------------------------------------ helpers

    private static string Describe(Dictionary<string, int> dropped) =>
        dropped.Count == 0 ? string.Empty : "; dropped: " + string.Join(", ", dropped.Select(d => $"{d.Value:N0} {d.Key}"));

    private static SqliteConnection OpenReadOnly(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("no such database", path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ClassicaCodex.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Run from inside the repository, or pass --out.");
    }
}

/// <summary>A passage as found in the library, with the matched words' positions in its text there.</summary>
internal sealed record Placed(PassageKey Key, string Fingerprint, int Start, int End);

/// <summary>The library, read-only, as the two inputs need to ask it things.</summary>
internal sealed class Library
{
    private readonly SqliteConnection _connection;
    private readonly Dictionary<string, (int Id, string Urn, string Collection, string Kind, string Language, string Title, string Author)> _editions = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Author, string Title), List<int>> _byWork = new();
    private readonly Dictionary<int, string> _urnById = new();

    public Library(SqliteConnection connection)
    {
        _connection = connection;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT e.EditionId, e.CtsUrn, COALESCE(e.Collection, ''), COALESCE(e.Kind, ''), COALESCE(e.Language, ''), w.Title, a.Name
              FROM Editions e JOIN Works w ON w.WorkId = e.WorkId JOIN Authors a ON a.AuthorId = w.AuthorId
             ORDER BY e.EditionId;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var edition = (reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6));
            _editions[QuotationIndex.EditionKey(edition.Item2)] = edition;
            _urnById[edition.Item1] = edition.Item2;
            if (!_byWork.TryGetValue((edition.Item7, edition.Item6), out var list)) _byWork[(edition.Item7, edition.Item6)] = list = new List<int>();
            list.Add(edition.Item1);
        }
    }

    /// <summary>The "urn:cts:namespace:edition." each edition's references begin with, where they do.</summary>
    public Dictionary<string, string> ReferencePrefixes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A Testimonia passage, placed by its edition and reference. Where the
    /// library's text is no longer the harvested one, the harvested span is
    /// looked for word for word in the new text; failing that it is dropped.
    /// </summary>
    public Placed? Place(string editionUrn, string reference, string harvested, int start, int end, out string? why)
    {
        why = null;
        if (!_editions.TryGetValue(QuotationIndex.EditionKey(editionUrn), out var edition))
        {
            why = "edition not in the library";
            return null;
        }

        var nodes = Nodes(edition.Id, new[] { reference });
        if (nodes.Count == 0)
        {
            why = "reference not in the library";
            return null;
        }

        foreach (var (_, text) in nodes)
            if (text == harvested)
                return Make(edition.Id, reference, text, start, end);

        var span = harvested[Math.Clamp(start, 0, harvested.Length)..Math.Clamp(end, 0, harvested.Length)];
        foreach (var (_, text) in nodes)
        {
            var at = text.IndexOf(span, StringComparison.Ordinal);
            if (at >= 0 && text.IndexOf(span, at + 1, StringComparison.Ordinal) < 0)
                return Make(edition.Id, reference, text, at, at + span.Length);
        }

        why = "text changed and the quoted words are gone";
        return null;
    }

    /// <summary>
    /// An Afterlives quoting passage: of the passages under this author,
    /// title and reference in the given collections - a work can be printed
    /// twice - the one where most of the evidence words are found together.
    /// </summary>
    public Placed? BestPlaceByWords(string author, string title, string reference, string evidence, string[] collections)
    {
        Placed? best = null;
        var bestCount = 0;
        foreach (var editionId in Editions(author, title, collections))
        foreach (var (citation, text) in Nodes(editionId, Candidates(editionId, reference)))
        {
            var tokens = AfterlivesWords.Tokens(text);
            var marks = AfterlivesWords.Locate(tokens, evidence);
            if (marks.Count <= bestCount) continue;
            bestCount = marks.Count;
            best = Make(editionId, citation, text, tokens[marks[0]].Start, tokens[marks[^1]].End);
        }

        return best;
    }

    /// <summary>
    /// An Afterlives quoted line: the first edition, by id, that holds the
    /// reference - as its own reader chose them. The matched words are marked
    /// where they are found in the line, and the whole line otherwise.
    /// </summary>
    public Placed? FirstPlaceByWords(string author, string title, string reference, string evidence, string[] collections)
    {
        foreach (var editionId in Editions(author, title, collections))
        {
            var nodes = Nodes(editionId, Candidates(editionId, reference));
            if (nodes.Count == 0) continue;

            var (citation, text) = nodes[0];
            var tokens = AfterlivesWords.Tokens(text);
            var marks = AfterlivesWords.Locate(tokens, evidence);
            return marks.Count == 0
                ? Make(editionId, citation, text, 0, text.Length)
                : Make(editionId, citation, text, tokens[marks[0]].Start, tokens[marks[^1]].End);
        }

        return null;
    }

    private IEnumerable<int> Editions(string author, string title, string[] collections) =>
        _byWork.TryGetValue((author, title), out var ids)
            ? ids.Where(id =>
            {
                var e = _editions[QuotationIndex.EditionKey(_urnById[id])];
                return e.Kind == "Original" && e.Language == "lat" && collections.Contains(e.Collection);
            })
            : Enumerable.Empty<int>();

    /// <summary>
    /// The library writes most references with their edition's whole URN in
    /// front; Afterlives kept only what follows it.
    /// </summary>
    private string[] Candidates(int editionId, string reference)
    {
        var key = QuotationIndex.EditionKey(_urnById[editionId]);
        return new[] { reference, $"urn:cts:latinLit:{key}.{reference}", $"urn:cts:greekLit:{key}.{reference}" };
    }

    private List<(string CitationRef, string Text)> Nodes(int editionId, string[] references)
    {
        var results = new List<(string, string)>();
        using var cmd = _connection.CreateCommand();
        var names = references.Select((_, i) => "@r" + i).ToArray();
        cmd.CommandText = $"SELECT CitationRef, Text FROM TextNodes WHERE EditionId = @e AND CitationRef IN ({string.Join(",", names)}) ORDER BY SortOrder, TextNodeId;";
        cmd.Parameters.AddWithValue("@e", editionId);
        for (var i = 0; i < references.Length; i++) cmd.Parameters.AddWithValue(names[i], references[i]);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) results.Add((reader.GetString(0), reader.GetString(1)));
        return results;
    }

    private Placed Make(int editionId, string citation, string text, int start, int end)
    {
        var urn = _urnById[editionId];
        if (!ReferencePrefixes.ContainsKey(urn))
        {
            var prefix = Regex.Match(citation, @"^urn:cts:[^:]+:" + Regex.Escape(QuotationIndex.EditionKey(urn)) + @"\.");
            if (prefix.Success) ReferencePrefixes[urn] = prefix.Value;
        }

        return new Placed(new PassageKey(urn, citation), TextFingerprint.Of(text), start, end);
    }
}

/// <summary>
/// Afterlives' own word folding and quotation finding, so its evidence words
/// - written in that folded form - are found where its reader found them.
/// Ported from its tess.words and build_reader.tokens/locate.
/// </summary>
internal static class AfterlivesWords
{
    private static readonly Regex Token = new(@"[^\W\d_]+", RegexOptions.Compiled);
    private static readonly Regex Letters = new("[a-z]+", RegexOptions.Compiled);

    public readonly record struct Word(int Start, int End, string Form);

    public static List<Word> Tokens(string text)
    {
        var words = new List<Word>();
        foreach (Match match in Token.Matches(text))
        {
            var form = Fold(match.Value);
            if (form.Length > 0) words.Add(new Word(match.Index, match.Index + match.Length, form));
        }

        return words;
    }

    public static string Fold(string word)
    {
        var t = word.ToLowerInvariant().Replace("æ", "ae").Replace("œ", "oe").Normalize(NormalizationForm.FormD);
        var bare = new StringBuilder(t.Length);
        foreach (var ch in t)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                bare.Append(ch);

        t = bare.ToString().Replace('j', 'i').Replace('v', 'u').Replace('y', 'i').Replace("ae", "e").Replace("oe", "e");
        return string.Concat(Letters.Matches(t).Select(m => m.Value));
    }

    /// <summary>The token positions of the quotation: the exact run if the text has it, or else the densest cluster.</summary>
    public static List<int> Locate(List<Word> tokens, string evidence)
    {
        var wanted = evidence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var n = wanted.Length;
        for (var i = 0; i + n <= tokens.Count && n > 0; i++)
        {
            var all = true;
            for (var k = 0; k < n && all; k++) all = tokens[i + k].Form == wanted[k];
            if (all) return Enumerable.Range(i, n).ToList();
        }

        var set = wanted.ToHashSet(StringComparer.Ordinal);
        var hits = Enumerable.Range(0, tokens.Count).Where(i => set.Contains(tokens[i].Form)).ToList();
        var best = new List<int>();
        var bestCount = 0;
        for (var a = 0; a < hits.Count; a++)
        {
            var group = hits.Skip(a).Where(h => h - hits[a] <= 40).ToList();
            var got = group.Select(h => tokens[h].Form).Distinct().Count();
            if (got > bestCount)
            {
                best = group;
                bestCount = got;
            }
        }

        return best;
    }
}
