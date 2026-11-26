using System.Reflection;
using System.Text.RegularExpressions;

namespace ClassicaCodex.Core.Quotations;

/// <summary>
/// Who quotes what, looked up by passage, from both ends: the later passages
/// that quote a line (its "Quoted by"), and the earlier lines a passage
/// quotes (the link back).
///
/// <b>Matched against whatever the reader's library holds.</b> The shipped
/// table names some seven hundred editions; a library without one of them
/// simply never asks about it, so a link whose other end is missing costs
/// nothing and shows nothing. Nothing is copied into the library, which is
/// why this needs no setup step and no table of its own.
///
/// <b>Edition URNs are compared without their "urn:cts:namespace:" head.</b>
/// The library writes "tlg0003.tlg001.perseus-grc2" where the CTS standard
/// and some older libraries write the whole URN; both name the same edition.
/// References are compared exactly, as the library stores them.
/// </summary>
public sealed class QuotationIndex
{
    private const string ResourceName = "ClassicaCodex.Core.Quotations.QuotationLinks.tsv.gz";

    private static readonly Regex UrnHead = new(@"^urn:cts:[^:]+:", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Lazy<QuotationIndex> LazyShipped = new(LoadShipped, LazyThreadSafetyMode.ExecutionAndPublication);

    private static QuotationIndex? _instead;

    private readonly Dictionary<string, Dictionary<string, List<QuotationLink>>> _bySource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, List<QuotationLink>>> _byQuoting = new(StringComparer.Ordinal);

    public QuotationIndex(IEnumerable<QuotationLink> links)
    {
        var all = new List<QuotationLink>();
        foreach (var link in links)
        {
            all.Add(link);
            Add(_bySource, link.Source, link);
            Add(_byQuoting, link.Quoting, link);
        }

        Links = all;
    }

    /// <summary>An index with nothing in it, which is what a damaged or missing table loads as.</summary>
    public static QuotationIndex Empty { get; } = new(Array.Empty<QuotationLink>());

    /// <summary>The table that ships with the application.</summary>
    public static QuotationIndex Shipped => LazyShipped.Value;

    /// <summary>The index the reader consults: the shipped one, unless a test has put another in its place.</summary>
    public static QuotationIndex Current => _instead ?? Shipped;

    /// <summary>
    /// Puts another index in place of the shipped one until the returned
    /// object is disposed - for tests, which cannot seed a library with the
    /// real editions the shipped table names.
    /// </summary>
    public static IDisposable UseInstead(QuotationIndex index)
    {
        var previous = _instead;
        _instead = index;
        return new Restore(() => _instead = previous);
    }

    public IReadOnlyList<QuotationLink> Links { get; }

    public int Count => Links.Count;

    /// <summary>The later passages that quote this one.</summary>
    public IReadOnlyList<QuotationLink> QuotersOf(string editionUrn, string citationRef) =>
        Find(_bySource, editionUrn, citationRef);

    /// <summary>The earlier passages this one quotes.</summary>
    public IReadOnlyList<QuotationLink> QuotedBy(string editionUrn, string citationRef) =>
        Find(_byQuoting, editionUrn, citationRef);

    /// <summary>Every reference in an edition that some later passage quotes: the lines that carry the mark.</summary>
    public IReadOnlyCollection<string> QuotedReferencesIn(string editionUrn) =>
        _bySource.TryGetValue(EditionKey(editionUrn), out var refs) ? refs.Keys : Array.Empty<string>();

    /// <summary>Every link whose quoting passage is in this edition, for underlining the quoted words.</summary>
    public IEnumerable<QuotationLink> QuotationsIn(string editionUrn) =>
        _byQuoting.TryGetValue(EditionKey(editionUrn), out var refs) ? refs.Values.SelectMany(l => l) : Enumerable.Empty<QuotationLink>();

    /// <summary>The edition URN without its "urn:cts:namespace:" head, as this index compares them.</summary>
    public static string EditionKey(string editionUrn) => UrnHead.Replace(editionUrn.Trim(), string.Empty);

    private static void Add(Dictionary<string, Dictionary<string, List<QuotationLink>>> index, PassageKey key, QuotationLink link)
    {
        var edition = EditionKey(key.EditionUrn);
        if (!index.TryGetValue(edition, out var refs))
        {
            refs = new Dictionary<string, List<QuotationLink>>(StringComparer.Ordinal);
            index[edition] = refs;
        }

        if (!refs.TryGetValue(key.CitationRef, out var list))
        {
            list = new List<QuotationLink>();
            refs[key.CitationRef] = list;
        }

        list.Add(link);
    }

    private static IReadOnlyList<QuotationLink> Find(
        Dictionary<string, Dictionary<string, List<QuotationLink>>> index, string editionUrn, string citationRef) =>
        index.TryGetValue(EditionKey(editionUrn), out var refs) && refs.TryGetValue(citationRef, out var list)
            ? list
            : Array.Empty<QuotationLink>();

    /// <summary>
    /// Read from the embedded table. A table that will not read loads as no
    /// links at all rather than taking the reader down with it: the marks are
    /// an addition to reading, and reading has to work without them.
    /// </summary>
    private static QuotationIndex LoadShipped()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            return stream == null ? Empty : new QuotationIndex(QuotationLinkFile.ReadCompressed(stream));
        }
        catch (InvalidDataException)
        {
            return Empty;
        }
    }

    private sealed class Restore : IDisposable
    {
        private Action? _undo;

        public Restore(Action undo) => _undo = undo;

        public void Dispose()
        {
            _undo?.Invoke();
            _undo = null;
        }
    }
}
