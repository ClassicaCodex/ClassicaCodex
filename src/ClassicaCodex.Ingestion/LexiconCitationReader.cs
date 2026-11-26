using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClassicaCodex.Core.Lexicon;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Reads the citations of one dictionary entry, in the order printed: what
/// each says, and which passage it names.
///
/// Perseus's URNs are mostly right, and taken as they are. Where they are
/// not, the faults were found by checking citations against the passages
/// they resolve to - whether the words the dictionary quotes are there - over
/// both dictionaries and the whole library, and each correction below is one
/// whose faults were systematic rather than scattered:
///
/// <list type="bullet">
/// <item>A reference printed as a bare number after an earlier one -
/// "Il. 4.106, 125" - means the same book. Perseus gave the second only its
/// line, "125", so it takes the missing levels from the citation of the same
/// work before it in the entry.</item>
/// <item>Lewis and Short's "Vulg." carries the Septuagint's and the Greek New
/// Testament's URNs, so it would open Greek where the dictionary quotes
/// Jerome's Latin. Every citation of those two in the Latin dictionary - an
/// "id." after a "Vulg." included - is left unlinked.</item>
/// <item>Lewis and Short's Pliny, the Natural History, is given URNs that name
/// Hardouin's chapters, which no edition in the library numbers; nine in ten
/// of those checked opened the wrong passage. Left unlinked.</item>
/// <item>A citation printed with a section sign whose URN does not end in that
/// section - "Cic. Q. Fr. 3, 1, 2, § 4" given 3:1:2 - is naming something
/// else. Left unlinked.</item>
/// <item>Lewis and Short's Odes of Horace, "Hor. C.", carry the URN of the
/// Carmen Saeculare, a single poem of 76 lines. Moved to the Odes.</item>
/// <item>Lewis and Short cites Plautus and Terence by act, scene and line,
/// counting the lines within each scene; the library numbers them straight
/// through the play, so "Plaut. Trin. 2, 4, 129" names a line the edition
/// calls something else, and none of the hundreds checked opened the line
/// quoted. Those three-part references are left unlinked; a plain line
/// number is kept.</item>
/// <item>LSJ cites Sophocles' Ichneutae by a numbering the library's edition
/// does not follow: none of 134 checked opened the line quoted. Left
/// unlinked.</item>
/// </list>
///
/// How Cicero is cited is a matter of which edition step to trust rather than
/// of the URN, and is in <see cref="CitationConventions"/>.
/// </summary>
public sealed class LexiconCitationReader
{
    private const string NaturalHistory = "phi0978.phi001";
    private const string Septuagint = "tlg0527.";
    private const string GreekNewTestament = "tlg0031.";
    private const string HoraceCarmenSaeculare = "phi0893.phi002";
    private const string HoraceOdes = "phi0893.phi001";
    private const string Plautus = "phi0119.";
    private const string Terence = "phi0134.";
    private const string Ichneutae = "tlg0011.tlg008";

    private static readonly Regex BareReference = new(@"^[\d\s.,;:§a-e]*(?:sqq?\.?|al\.|ff?\.?)?\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex SectionSign = new(@"§\s*(\d+)", RegexOptions.CultureInvariant);
    private static readonly Regex HoraceOdesLabel = new(@"^(?:Hor|id)\.\s+(?:C|Od)\.\s+\d", RegexOptions.CultureInvariant);

    private readonly bool _latinDictionary;
    private readonly Dictionary<string, IReadOnlyList<string>> _lastPassageByWork = new(StringComparer.Ordinal);

    /// <param name="language">The dictionary's language: "grc" for LSJ, "lat" for Lewis and Short.</param>
    public LexiconCitationReader(string language)
    {
        _latinDictionary = string.Equals(language, "lat", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The citation as printed, with its whitespace closed up: author, work and
    /// reference. The parts are kept apart where the markup runs them together -
    /// "&lt;author&gt;Pl.&lt;/author&gt;&lt;title&gt;R.&lt;/title&gt;" is "Pl. R.", not "Pl.R." - since
    /// the space between them is only there when the file is laid out with it.
    /// </summary>
    public static string Label(XElement bibl)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var text in bibl.DescendantNodes().OfType<XText>())
        {
            var value = text.Value;
            if (value.Length == 0) continue;
            if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1]) && char.IsLetterOrDigit(value[0])) sb.Append(' ');
            sb.Append(value);
        }

        return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// The passage a citation names, or null where it names none that can be
    /// trusted. Called for each citation in the order the entry prints them,
    /// since a bare reference reads from the one before it.
    /// </summary>
    public LexiconCitationTarget? Read(XElement bibl) => Read(bibl.Attribute("n")?.Value, Label(bibl));

    /// <inheritdoc cref="Read(XElement)"/>
    public LexiconCitationTarget? Read(string? urn, string label)
    {
        if (LexiconCitationTarget.Parse(urn) is not { } target) return null;

        if (_latinDictionary)
        {
            if (target.WorkUrn.StartsWith(Septuagint, StringComparison.Ordinal)
                || target.WorkUrn.StartsWith(GreekNewTestament, StringComparison.Ordinal)) return null;
            if (target.WorkUrn == NaturalHistory) return null;
            if (target.WorkUrn == HoraceCarmenSaeculare && HoraceOdesLabel.IsMatch(label))
                target = target with { WorkUrn = HoraceOdes, EditionUrn = null };
        }

        if (target.WorkUrn == Ichneutae) return null;
        if (SectionSign.Match(label) is { Success: true } section && target.Passage[^1] != section.Groups[1].Value) return null;

        if (BareReference.IsMatch(label)
            && _lastPassageByWork.TryGetValue(target.WorkUrn, out var previous)
            && target.Passage.Count < previous.Count)
        {
            target = target.WithPassage(previous.Take(previous.Count - target.Passage.Count).Concat(target.Passage).ToList());
        }

        _lastPassageByWork[target.WorkUrn] = target.Passage;

        // After the bare reference has been read in, which can make one.
        if (_latinDictionary && target.Passage.Count == 3
            && (target.WorkUrn.StartsWith(Plautus, StringComparison.Ordinal) || target.WorkUrn.StartsWith(Terence, StringComparison.Ordinal)))
            return null;

        return target;
    }
}
