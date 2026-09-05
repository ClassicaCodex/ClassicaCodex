using System.Xml.Linq;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Tracks the canonical page reference a passage falls under while the parser
/// walks a text.
///
/// Plato is cited by Stephanus page and Aristotle by Bekker number, in every
/// article, syllabus and commentary in the field. Perseus encodes both, but
/// not as structure: the &lt;div&gt; carries the page and what makes the
/// citation precise arrives inline, as an empty marker in the middle of the
/// prose.
///
/// <code>
/// &lt;milestone n="327"  unit="page"    resp="Stephanus"/&gt;
/// &lt;milestone n="327a" unit="section" resp="Stephanus"/&gt;
///
/// &lt;milestone n="1094a" unit="page" resp="Bekker"/&gt;
/// &lt;milestone n="5"     unit="line" resp="Bekker"/&gt;
/// </code>
///
/// &lt;milestone&gt; carries no text, so the parser skipped it and the rest
/// went with it. Euthyphro 2a was displayed as "2.1" - a page, and a paragraph
/// index this application invented, which no reader could look up anywhere.
///
/// The two schemes divide their pages differently, and the difference is in
/// the markers rather than in anything this class decides:
///
/// <b>A Stephanus section is already whole.</b> "327a" is page and column
/// together, so it is taken as it stands and the page marker beside it adds
/// nothing.
///
/// <b>A Bekker line is not.</b> Bekker's own "a" and "b" are columns and
/// belong to the page - "1094a" - while the line markers restart at every
/// column and mean nothing apart from it. They are composed, which is how the
/// reference is written in print: <i>NE</i> 1094a1.
///
/// And one rule common to both: <b>a passage is cited where it begins.</b> A
/// marker inside a paragraph starts a new section partway through it -
/// Euthyphro's 2c opens three words before the end of a speech. That speech
/// belongs to 2b, where the reader started reading it, and 2c governs whatever
/// comes next. This is what a printed edition means by putting the letter in
/// the margin, and it is why the reference cannot be read off a paragraph on
/// its own.
/// </summary>
internal sealed class CanonicalMilestones
{
    /// <summary>
    /// Whose pagination counts as a citation.
    ///
    /// Named rather than "any milestone with an @n" because most milestones in
    /// this corpus are not citation schemes at all - they mark card divisions
    /// and manuscript pages, which nobody cites and which would overwrite a
    /// real Stephanus letter with a number meaning something else entirely.
    /// </summary>
    private static readonly HashSet<string> Authorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stephanus",
        "Bekker"
    };

    private const string Section = "section";
    private const string Line = "line";
    private const string Page = "page";

    /// <summary>
    /// The finer unit this file uses, if any: "section" where the marker is a
    /// whole reference, "line" where it is a suffix to the page.
    /// </summary>
    private string? _fine;

    private string? _page;
    private string? _fineValue;

    /// <summary>True when this file carries pagination worth recording at all.</summary>
    public bool InUse { get; private set; }

    /// <summary>
    /// Reads the whole text once to see which units it uses, and forgets any
    /// previous file.
    ///
    /// A pre-pass because "is this a sectioned text" cannot be answered from
    /// the marker in front of you: the first thing the Republic declares is a
    /// page, and whether that page is the citation or only half of one depends
    /// on a marker that has not arrived yet.
    /// </summary>
    public void Begin(XElement body)
    {
        _fine = null;
        _page = null;
        _fineValue = null;
        InUse = false;

        foreach (var element in body.DescendantsAndSelf())
        {
            if (!IsMilestone(element)) continue;
            var unit = element.Attribute("unit")?.Value?.Trim();

            if (string.Equals(unit, Section, StringComparison.OrdinalIgnoreCase))
            {
                // Nothing finer exists; no need to read the rest.
                _fine = Section;
                InUse = true;
                return;
            }

            if (string.Equals(unit, Line, StringComparison.OrdinalIgnoreCase)) _fine = Line;
            if (unit != null) InUse = true;
        }
    }

    /// <summary>
    /// The reference in force right now, composed as the scheme requires, or
    /// null before the first marker.
    /// </summary>
    private string? Reference => _fine switch
    {
        Section => _fineValue,
        // Before the first line marker of a column, the column is the whole
        // reference there is - and it is a true one.
        Line => _page == null ? null : _page + _fineValue,
        _ => _page
    };

    /// <summary>
    /// The reference to cite the passage inside <paramref name="element"/> by,
    /// advancing past every marker within it so the next passage inherits the
    /// last one.
    ///
    /// Call once per emitted node, in document order. Calling it out of order
    /// would attribute a passage to a section it does not sit in.
    /// </summary>
    public string? Enter(XElement element)
    {
        if (!InUse) return null;

        var atStart = Reference;
        var seenText = false;
        Scan(element, ref seenText, ref atStart);
        return Span(atStart);
    }

    /// <summary>
    /// The same, for a node built from a run of inline children rather than
    /// from one element - a mixed block's own text, where the marker is a
    /// sibling of the words it governs rather than their parent.
    /// </summary>
    public string? Enter(IEnumerable<XElement> elements)
    {
        if (!InUse) return null;

        var atStart = Reference;
        var seenText = false;
        foreach (var element in elements) Scan(element, ref seenText, ref atStart);
        return Span(atStart);
    }

    /// <summary>
    /// What the passage covers, from where it began to wherever the walk has
    /// now reached.
    ///
    /// Perseus divides the Republic one Stephanus page to a paragraph, so a
    /// single node runs from 329e to the end of 330e. Naming it "329e" is true
    /// of its first line and of nothing else in it; a reader who looked up
    /// that reference for a sentence near the bottom would not find it there.
    /// A speech that sits inside one section still reports the one, because
    /// "2a-2a" is not how anybody writes it.
    /// </summary>
    private string? Span(string? atStart)
    {
        var atEnd = Reference;
        if (atStart == null) return atEnd;
        return atEnd == null || atEnd == atStart ? atStart : $"{atStart}–{Shorten(atStart, atEnd)}";
    }

    /// <summary>
    /// Drops from the end of a range whatever the start already said, which is
    /// how the range is written in print: 328a-e, 1094a1-15, 1094a15-b10, and
    /// 329e-330e in full because those two share nothing.
    ///
    /// Splitting on the change between digits and letters is what makes this
    /// work for both schemes without knowing which one it is holding. A
    /// Stephanus reference is a page and a section - 328, a - and a Bekker one
    /// a page, a column and a line - 1094, a, 15. Matching those pieces from
    /// the left removes exactly the leading part a reader would not repeat.
    /// </summary>
    internal static string Shorten(string start, string end)
    {
        var from = Pieces(start);
        var to = Pieces(end);

        var shared = 0;
        while (shared < from.Count && shared < to.Count
               && string.Equals(from[shared], to[shared], StringComparison.Ordinal))
            shared++;

        // Every piece matched, so there is nothing left to name the end by -
        // keep it whole rather than returning an empty half-range.
        return shared == 0 || shared >= to.Count
            ? end
            : string.Concat(to.Skip(shared));
    }

    private static List<string> Pieces(string reference)
    {
        var pieces = new List<string>();
        var start = 0;

        for (var i = 1; i <= reference.Length; i++)
        {
            if (i < reference.Length && char.IsDigit(reference[i]) == char.IsDigit(reference[i - 1])) continue;
            pieces.Add(reference[start..i]);
            start = i;
        }

        return pieces;
    }

    private void Scan(XNode node, ref bool seenText, ref string? atStart)
    {
        if (node is XText text)
        {
            if (!string.IsNullOrWhiteSpace(text.Value)) seenText = true;
            return;
        }

        if (node is not XElement element) return;

        if (IsMilestone(element))
        {
            if (Take(element) && !seenText) atStart = Reference;
            return;
        }

        // Apparatus and notes are not the text, so a marker quoted inside one
        // is not the reader's position in the work.
        if (TeiParser.IsEditorialElement(element)) return;

        foreach (var child in element.Nodes()) Scan(child, ref seenText, ref atStart);
    }

    /// <summary>Applies one marker, returning whether it moved the reference.</summary>
    private bool Take(XElement element)
    {
        var value = element.Attribute("n")?.Value?.Trim();
        if (string.IsNullOrEmpty(value)) return false;

        var unit = element.Attribute("unit")?.Value?.Trim();

        if (string.Equals(unit, Page, StringComparison.OrdinalIgnoreCase))
        {
            _page = value;
            // Bekker's lines restart in every column, so the old line number
            // is not merely stale on a new page - composed with it, it would
            // name a line that exists and is somewhere else entirely.
            if (_fine == Line) _fineValue = null;
            // A file with sections cites by those alone, so its page markers
            // move nothing a reader would see.
            return _fine != Section;
        }

        if (!string.Equals(unit, _fine, StringComparison.OrdinalIgnoreCase)) return false;

        _fineValue = value;
        return true;
    }

    private static bool IsMilestone(XElement element) =>
        string.Equals(element.Name.LocalName, "milestone", StringComparison.OrdinalIgnoreCase)
        && element.Attribute("resp")?.Value is { } resp
        && Authorities.Contains(resp.Trim());
}
