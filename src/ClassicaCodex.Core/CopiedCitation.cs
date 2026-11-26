using System.Text.RegularExpressions;

namespace ClassicaCodex.Core;

/// <summary>
/// A citation as it is copied for use in this app: an edition's identifier in
/// the library, a space, and the passage's reference.
///
/// The Atlas of Ancient Myth and the reports built from the library copy
/// exactly that when a reference is clicked - the edition as the library
/// stores it and the reference with any URN head taken off:
/// "tlg0020.tlg001.perseus-grc2 457",
/// "urn:menota:anonymous:gylfaginning:am-242-fol 10.22",
/// "oracc:cams/gkab:P338345:transliteration o i 46'". An identifier never
/// holds a space and a reference may (Oracc's "o i 46'"), so the first space
/// is the divide.
///
/// Also read: a CTS passage URN written whole,
/// "urn:cts:greekLit:tlg0020.tlg001.perseus-grc2:457", and a work's
/// identifier where an edition's would be, "tlg0020.tlg001 457".
/// </summary>
public sealed record CopiedCitation(string Identifier, string Reference)
{
    // Letters, digits and the punctuation identifiers in the library use:
    // dots (CTS), colons (URNs, Oracc), slashes (Oracc projects), hyphens and
    // underscores. Letters of any script: Menota's are named in Old Norse,
    // "urn:menota:anonymous:vǫluspá:am-544-4to".
    private static readonly Regex IdentifierShape = new(@"^[\p{L}\p{N}][\p{L}\p{N}\p{M}_.:/\-]*$", RegexOptions.CultureInvariant);

    private const string ArtifactHead = "artifact:";

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    // The two ends of a range, "10.235–10.240", "o 26'–o 29'", and "1.1-1.2"
    // with a hyphen - tried after the dashes, since a hyphen can also be part
    // of a reference: "64-2.53–64-2.57" in the Egyptian texts.
    private static readonly Regex RangeDash = new(@"\s*[–—]\s*", RegexOptions.CultureInvariant);
    private static readonly Regex RangeHyphen = new(@"\s*-\s*", RegexOptions.CultureInvariant);

    /// <summary>
    /// The museum object a citation names, for the objects the Atlas cites
    /// beside the texts - "artifact:London E 163" - or null for a passage.
    /// </summary>
    public string? ArtifactName => Identifier.StartsWith(ArtifactHead, StringComparison.OrdinalIgnoreCase) ? Identifier[ArtifactHead.Length..] : null;

    /// <summary>The citation in a piece of text, or null if its first line is not one.</summary>
    public static CopiedCitation? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var line = text.Split('\n', '\r').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line == null) return null;

        line = Unquote(Spaces.Replace(line, " "));

        // An object's name has spaces in it, and the pages copy it twice over
        // ("artifact:London E 163 London E 163"), as identifier and reference.
        if (line.StartsWith(ArtifactHead, StringComparison.OrdinalIgnoreCase))
        {
            var name = line[ArtifactHead.Length..].Trim();
            var half = (name.Length - 1) / 2;
            if (name.Length % 2 == 1 && name[half] == ' ' && name[..half] == name[(half + 1)..]) name = name[..half];
            return name.Length == 0 ? null : new CopiedCitation(ArtifactHead + name, string.Empty);
        }

        string identifier, reference;
        var space = line.IndexOf(' ');
        if (space >= 0)
        {
            identifier = line[..space];
            reference = line[(space + 1)..];
        }
        else if (line.StartsWith("urn:cts:", StringComparison.OrdinalIgnoreCase) && line.Split(':') is { Length: >= 5 } parts)
        {
            // urn:cts:namespace:work.edition:passage
            identifier = string.Join(':', parts.Take(4));
            reference = string.Join(':', parts.Skip(4));
        }
        else
        {
            identifier = line;
            reference = string.Empty;
        }

        // An identifier is dotted or coloned: a bare word is a name or a
        // sentence, not a place in the library.
        if (!IdentifierShape.IsMatch(identifier) || identifier.IndexOfAny(new[] { '.', ':' }) < 0) return null;

        return new CopiedCitation(identifier, reference.Trim().TrimEnd(',', ';').Trim());
    }

    /// <summary>
    /// The references to look for, most literal first: as written; without a
    /// URN head stored in front of it; the first of several spans; the start
    /// of a range. Duplicates are dropped, and an empty reference gives none.
    /// </summary>
    public IReadOnlyList<string> References()
    {
        var found = new List<string>();

        void Add(string reference)
        {
            reference = reference.Trim();
            if (reference.Length > 0 && !found.Contains(reference, StringComparer.Ordinal)) found.Add(reference);
        }

        Add(Reference);
        Add(PassageAligner.ExtractPassageRef(Reference));

        var firstSpan = PassageAligner.ExtractPassageRef(Reference.Split(';')[0].Trim());
        Add(firstSpan);
        Add(RangeDash.Split(firstSpan)[0]);
        Add(RangeHyphen.Split(RangeDash.Split(firstSpan)[0])[0]);

        return found;
    }

    public override string ToString() => Reference.Length == 0 ? Identifier : $"{Identifier} {Reference}";

    /// <summary>Takes off quotation marks or brackets that wrap the whole thing.</summary>
    private static string Unquote(string line)
    {
        foreach (var (open, close) in new[] { ('"', '"'), ('“', '”'), ('\'', '\''), ('‘', '’'), ('(', ')'), ('[', ']'), ('<', '>'), ('«', '»') })
        {
            if (line.Length >= 2 && line[0] == open && line[^1] == close) return line[1..^1].Trim();
        }

        return line;
    }
}
