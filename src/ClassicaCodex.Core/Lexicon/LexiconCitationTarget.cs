using System.Text.RegularExpressions;

namespace ClassicaCodex.Core.Lexicon;

/// <summary>
/// The passage a dictionary citation names: a work, the edition the
/// dictionary had in mind if it says, and a reference into it.
///
/// Perseus gave most of LSJ's and Lewis and Short's citations a CTS URN, in
/// its own shape: "urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:1.441" for
/// Il. 1.441, "urn:cts:latinLit:phi1002.phi001:1:5:61" for Quint. 1.5.61 -
/// colons between levels in one dictionary and dots in the other, an edition
/// named in some and not in others. Both read into this, and it writes itself
/// back out in one shape, the dotted one, which is what is kept in an entry.
///
/// The work is compared without its "urn:cts:namespace:" head, as the
/// library's own identifiers are: the library writes "tlg0012.tlg001".
/// </summary>
/// <param name="Namespace">"greekLit" or "latinLit".</param>
/// <param name="WorkUrn">"tlg0012.tlg001": the text group and the work.</param>
/// <param name="EditionUrn">"tlg0012.tlg001.perseus-grc1", or null where the dictionary names only the work.</param>
/// <param name="Passage">The reference, one level to an item: ["1", "441"].</param>
public sealed record LexiconCitationTarget(string Namespace, string WorkUrn, string? EditionUrn, IReadOnlyList<string> Passage)
{
    private static readonly Regex AndFollowing = new(@"^(\d+)(?:sqq?|s|ff?)$", RegexOptions.CultureInvariant);

    /// <summary>The reference as the library writes one: "1.441".</summary>
    public string PassageRef => string.Join('.', Passage);

    /// <summary>The same target with the reference replaced.</summary>
    public LexiconCitationTarget WithPassage(IReadOnlyList<string> passage) => this with { Passage = passage };

    public override string ToString() => $"urn:cts:{Namespace}:{EditionUrn ?? WorkUrn}:{PassageRef}";

    /// <summary>Equal when they name the same passage of the same edition: the reference compared by value, not by list.</summary>
    public bool Equals(LexiconCitationTarget? other) =>
        other is not null && Namespace == other.Namespace && WorkUrn == other.WorkUrn
        && EditionUrn == other.EditionUrn && PassageRef == other.PassageRef;

    public override int GetHashCode() => HashCode.Combine(Namespace, WorkUrn, EditionUrn, PassageRef);

    /// <summary>
    /// Reads a citation's URN, or returns null for one that names no passage:
    /// not a CTS URN at all (Perseus gave papyri Trismegistos addresses), a
    /// work with no reference after it, or LSJ's "000", which stands for a
    /// passage it did not number.
    ///
    /// Two habits of the printed dictionaries are undone on the way in. A
    /// reference followed by "s", "sq." or "f." - "and the lines following" -
    /// keeps its number and loses the letters. And a label in front of the
    /// numbers - Lewis and Short's "Orat::1:1:2" for the De Oratore - is
    /// dropped, since no edition in the library numbers by it.
    /// </summary>
    public static LexiconCitationTarget? Parse(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn)) return null;

        var parts = urn.Trim().Split(':');
        if (parts.Length < 5 || !parts[0].Equals("urn", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("cts", StringComparison.OrdinalIgnoreCase)) return null;

        var work = parts[3].Trim();
        var segments = work.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;

        var workUrn = $"{segments[0]}.{segments[1]}";
        var editionUrn = segments.Length > 2 ? string.Join('.', segments) : null;

        var passage = new List<string>();
        foreach (var raw in string.Join(':', parts.Skip(4)).Split(new[] { ':', '.' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var item = raw.Trim();
            if (item.Length == 0) continue;
            var following = AndFollowing.Match(item);
            passage.Add(following.Success ? following.Groups[1].Value : item);
        }

        while (passage.Count > 0 && !char.IsDigit(passage[0][0])) passage.RemoveAt(0);
        if (passage.Count == 0 || passage is ["000"]) return null;

        return new LexiconCitationTarget(parts[2].Trim(), workUrn, editionUrn, passage);
    }
}

/// <summary>
/// A citation an entry keeps, as the citations table stores it: its place
/// among the entry's citations, the passage it names, and the sense of the
/// entry it was printed under ("A.II.2"), where the dictionary numbers one.
/// </summary>
public sealed record LexiconCitation(int Ordinal, LexiconCitationTarget Target, string? Sense);
