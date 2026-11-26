using System.Text.RegularExpressions;

namespace ClassicaCodex.Core.Lexicon;

/// <summary>How a dictionary's reference was found in an edition - said so the measurements can be broken down by it.</summary>
public enum CitationMatch
{
    /// <summary>The edition numbers the passage exactly as the dictionary does: Il. 1.441 is 1.441.</summary>
    Exact,

    /// <summary>The edition adds a paragraph number to prose: Quint. 1.5.61 is 1.5.61.1.</summary>
    Paragraph,

    /// <summary>The dictionary cites a chapter the edition divides further: Hdt. 4.25 is the start of 4.25.0.1.</summary>
    Section,

    /// <summary>A Stephanus or Bekker reference found in the range the edition records: Pl. R. 390e in 390a–e.</summary>
    Milestone,

    /// <summary>Chapter and section where the edition numbers sections straight through: Cic. Brut. 76, 265 is 265.</summary>
    ContinuousSection,

    /// <summary>The edition carries one level above the dictionary's, and only one passage fits: Juv. 1.170 is 1.1.170.</summary>
    Suffix
}

/// <summary>
/// One edition's passages, ready to be asked for by a dictionary's reference.
///
/// The library numbers its editions as their TEI does, and the dictionaries
/// cite them as nineteenth-century scholars did, so the same passage is
/// written differently on the two sides more often than not. The ways it
/// differs were measured against the whole library, citation by citation, and
/// each one has a step here, tried in order, the most literal first:
///
/// <list type="number">
/// <item>The reference as it stands.</item>
/// <item>With a paragraph number after it. The library numbers the
/// paragraphs of prose and not the lines of verse, so Plutarch 1.2 is
/// stored as 1.2.1 and Iliad 1.2 as 1.2.</item>
/// <item>The first passage beneath it, where the dictionary cites a chapter
/// and the edition divides it into sections.</item>
/// <item>A Stephanus or Bekker reference, inside the range the edition
/// records for a passage (Plato's Republic is divided a page at a time,
/// "329e–330e").</item>
/// <item>For a reference ending chapter, section where the section is the
/// larger number, the section alone in place of both - only for a work
/// whose sections run straight through, which is Cicero's (see
/// <see cref="CitationConventions"/>). Tried on everything, it found the
/// quoted words in fewer than a quarter of the Greek passages it opened.</item>
/// <item>A passage whose reference ends with the whole of the dictionary's,
/// one level down, if exactly one does: Juvenal is cited by satire and line,
/// and the library keeps the book above the satire.</item>
/// </list>
///
/// <b>An edition with a level above the dictionary's.</b> Some editions put a
/// grouping above what the dictionaries cite: Juvenal's five books above his
/// sixteen satires, and one of Livy's Perseus editions a volume above the
/// book. There the dictionary's reference names a different passage at the
/// edition's own top level - "Liv. 2, 24" is book 2, chapter 24, but "2.24"
/// in that edition is the twenty-fourth line of an index of sigla. Such an
/// edition is recognised by its second level never repeating under two
/// different first levels (satires are numbered straight through the books;
/// chapters restart in every book, so Herodotus is not one), and for it the
/// last step is tried first, with the first passage beneath it as well.
///
/// Passages that are not text - headings, speaker names, labels - are never
/// opened by a reference that only reaches them through a milestone or one of
/// the looser steps: Plato's editions give the speaker's name the milestone
/// of the speech that follows it.
///
/// Not tried, because each was measured and found to land on the wrong line
/// more often than the right one: walking up to a passage that contains the
/// reference (it sends a citation of an Ode to a line of the Carmen
/// Saeculare), and taking the numbers printed in the citation in place of
/// its URN.
/// </summary>
public sealed class PassageIndex
{
    private static readonly Regex StephanusPattern = new(@"^(\d+)([a-e])(\d*)$", RegexOptions.CultureInvariant);

    private static readonly string[] NotPassages = { "head", "speaker", "label", "arg", "sigla" };

    private readonly Dictionary<string, long> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _firstBeneath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Id, int Count)> _suffix = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _firstBeneathSuffix = new(StringComparer.Ordinal);
    private readonly List<(Stephanus Start, Stephanus End, long Id)> _milestones = new();
    private readonly bool _sectionsRunThrough;
    private readonly bool _levelAbove;

    /// <param name="passages">Every passage of the edition, in reading order: its node id, stored reference and milestone.</param>
    /// <param name="sectionsRunThrough">Whether the work's sections run straight through: <see cref="CitationConventions.SectionsRunThrough"/>.</param>
    public PassageIndex(IEnumerable<(long TextNodeId, string CitationRef, string? Milestone)> passages, bool sectionsRunThrough = false)
    {
        _sectionsRunThrough = sectionsRunThrough;

        // Second-level number to the one first level it appears under, or
        // null once it has appeared under two. Read from references three
        // levels deep or more, so an index or a list of sigla numbered under a
        // volume ("2.24") does not count.
        var above = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (id, citationRef, milestone) in passages)
        {
            var key = PassageAligner.ExtractPassageRef(citationRef);
            if (key.Length > 0) _exact.TryAdd(key, id);

            var parts = key.Split('.');
            if (parts.Any(p => NotPassages.Any(n => p.StartsWith(n, StringComparison.OrdinalIgnoreCase)))) continue;

            if (milestone is { Length: > 0 } && ReadRange(milestone) is { } range)
                _milestones.Add((range.Start, range.End, id));

            for (var take = 1; take < parts.Length; take++)
                _firstBeneath.TryAdd(string.Join('.', parts, 0, take), id);

            // One level down: only beneath a numbered level, not a named part
            // of the edition ("similes.5", an appendix of Homeric parallels).
            if (parts.Length >= 2 && parts[0].Length > 0 && char.IsDigit(parts[0][0]))
            {
                var suffix = string.Join('.', parts, 1, parts.Length - 1);
                _suffix[suffix] = _suffix.TryGetValue(suffix, out var seen) ? (seen.Id, seen.Count + 1) : (id, 1);
                for (var take = 1; take < parts.Length - 1; take++)
                    _firstBeneathSuffix.TryAdd(string.Join('.', parts, 1, take), id);

                if (parts.Length >= 3 && parts[1].Length > 0 && char.IsDigit(parts[1][0]))
                    above[parts[1]] = above.TryGetValue(parts[1], out var first) && first != parts[0] ? null : parts[0];
            }
        }

        // Two groups at least: an edition with one chapter divided a level
        // further than the rest (Iamblichus's Protrepticus, whose last chapter
        // numbers the Pythagorean symbols) has nothing above its chapters.
        _levelAbove = above.Count >= 2 && above.Values.All(v => v != null) && above.Values.Distinct(StringComparer.Ordinal).Count() >= 2;
    }

    /// <summary>How many passages the edition holds under a reference of its own.</summary>
    public int Count => _exact.Count;

    /// <summary>The passage a dictionary's reference names in this edition, and how it was found; null where none fits.</summary>
    /// <param name="passage">The reference, one level to an item.</param>
    /// <param name="mayStopAtChapter">
    /// Whether the reference may name a chapter the edition does not number
    /// (<see cref="CitationConventions.MayStopAtChapter"/>), so that only the
    /// continuous-section step is trusted with it.
    /// </param>
    public (long TextNodeId, CitationMatch How)? Find(IReadOnlyList<string> passage, bool mayStopAtChapter = false)
    {
        if (passage.Count == 0) return null;

        var reference = string.Join('.', passage);
        if (mayStopAtChapter) return ContinuousSection(passage) is { } only ? (only, CitationMatch.ContinuousSection) : null;

        if (_levelAbove)
        {
            if (Suffix(reference) is { } below) return (below, CitationMatch.Suffix);
            if (_firstBeneathSuffix.TryGetValue(reference, out var first)) return (first, CitationMatch.Section);
        }

        if (_exact.TryGetValue(reference, out var exact)) return (exact, CitationMatch.Exact);
        if (_exact.TryGetValue(reference + ".1", out var paragraph)) return (paragraph, CitationMatch.Paragraph);
        if (_firstBeneath.TryGetValue(reference, out var beneath)) return (beneath, CitationMatch.Section);

        if (_milestones.Count > 0 && ReadStephanus(passage[^1]) is { } wanted)
        {
            // The first passage whose range holds the page and section; with a
            // Bekker line, the last of those that starts at or before it.
            long? found = null;
            foreach (var (start, end, id) in _milestones)
            {
                if (Compare(start, wanted) > 0 || Compare(wanted, end) > 0) continue;
                if (wanted.Line == 0) return (id, CitationMatch.Milestone);

                var startsInColumn = start.Page == wanted.Page && start.Column == wanted.Column;
                if (found == null || (startsInColumn && start.Line <= wanted.Line)) found = id;
            }

            if (found is { } milestone) return (milestone, CitationMatch.Milestone);
        }

        if (ContinuousSection(passage) is { } continuous) return (continuous, CitationMatch.ContinuousSection);

        if (Suffix(reference) is { } suffix) return (suffix, CitationMatch.Suffix);

        return null;
    }

    /// <summary>Chapter and section read as the section alone, in a work whose sections run straight through.</summary>
    private long? ContinuousSection(IReadOnlyList<string> passage)
    {
        if (!_sectionsRunThrough || passage.Count < 2) return null;
        if (!int.TryParse(passage[^1], out var section) || !int.TryParse(passage[^2], out var chapter) || section <= chapter) return null;

        var straight = string.Join('.', passage.Take(passage.Count - 2).Append(passage[^1]));
        if (_exact.TryGetValue(straight, out var exact)) return exact;
        if (_exact.TryGetValue(straight + ".1", out var paragraph)) return paragraph;
        return null;
    }

    /// <summary>Whether the edition keeps a level above the one the dictionaries cite - see the remarks.</summary>
    public bool HasLevelAbove => _levelAbove;

    /// <summary>The one passage whose reference, less its first level, is this one or this one's first paragraph.</summary>
    private long? Suffix(string reference)
    {
        foreach (var candidate in new[] { reference, reference + ".1" })
            if (_suffix.TryGetValue(candidate, out var hit) && hit.Count == 1) return hit.Id;
        return null;
    }

    /// <summary>A Stephanus page and section, or a Bekker page, column and line.</summary>
    private readonly record struct Stephanus(int Page, char Column, int Line);

    private static int Compare(Stephanus a, Stephanus b)
    {
        var byPage = a.Page.CompareTo(b.Page);
        return byPage != 0 ? byPage : a.Column.CompareTo(b.Column);
    }

    private static Stephanus? ReadStephanus(string text)
    {
        var match = StephanusPattern.Match(text.Trim());
        if (!match.Success) return null;
        return new Stephanus(
            int.Parse(match.Groups[1].Value), match.Groups[2].Value[0],
            match.Groups[3].Length > 0 ? int.Parse(match.Groups[3].Value) : 0);
    }

    /// <summary>"327a–c", "329e–330e", "2a": a single reference or a range, the second half taking the first's page when it gives only a letter.</summary>
    private static (Stephanus Start, Stephanus End)? ReadRange(string milestone)
    {
        var halves = milestone.Replace(" ", string.Empty).Split('–', '-');
        if (ReadStephanus(halves[0]) is not { } start) return null;
        if (halves.Length == 1) return (start, start);

        var last = halves[^1];
        if (ReadStephanus(last) is { } end) return (start, end);
        if (last.Length == 1 && last[0] is >= 'a' and <= 'e') return (start, new Stephanus(start.Page, last[0], 0));
        return null;
    }
}
