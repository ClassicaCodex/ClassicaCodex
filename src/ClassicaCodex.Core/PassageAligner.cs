using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Core;

/// <summary>
/// Aligns text between two editions of the same work by citation reference,
/// for when the two editions divide their text at different depths - a
/// Latin original citing book.chapter.section (1.1.1) while its English
/// translation stops at book.chapter (1.1). That's common and not a data
/// defect, so exact-only matching would miss real, valid alignments.
///
/// Originally lived as private methods inside PassageExportForm (bilingual
/// export mode). Pulled out here once Translate needed the identical
/// behavior - "find the matching passage in another edition" shouldn't have
/// two separately-maintained implementations that could quietly drift apart,
/// the way the classical/Renaissance entity tables once did.
/// </summary>
public class PassageAligner
{
    private readonly List<(string Key, string RawCitationRef, string Text)> _ordered = new();
    private readonly Dictionary<string, int> _indexByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<int>> _indicesByPrefix = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="counterpartNodes">
    /// Every TextNode of the edition being aligned *against* - typically a
    /// whole translation edition, loaded once up front since a lookup by
    /// citation needs the full map anyway and an edition is at most a few
    /// thousand rows.
    /// </param>
    public PassageAligner(IEnumerable<TextNode> counterpartNodes)
    {
        foreach (var n in counterpartNodes)
        {
            if (string.IsNullOrWhiteSpace(n.Text)) continue;

            // Keyed by passage ref, not the raw stored ref - see
            // ExtractPassageRef for why those can't be compared directly.
            var key = ExtractPassageRef(n.CitationRef);
            var index = _ordered.Count;
            _ordered.Add((key, n.CitationRef, n.Text));

            if (key.Length == 0) continue;
            _indexByKey.TryAdd(key, index);

            // Also file this position under every ancestor prefix, so a
            // coarser primary ref can pick up all the finer counterpart
            // pieces sitting beneath it.
            var parts = key.Split('.');
            for (var take = 1; take < parts.Length; take++)
            {
                var prefix = string.Join(".", parts.Take(take));
                if (!_indicesByPrefix.TryGetValue(prefix, out var list))
                {
                    list = new List<int>();
                    _indicesByPrefix[prefix] = list;
                }
                list.Add(index);
            }
        }
    }

    /// <summary>
    /// Every counterpart passage in reading order, alongside the key it was
    /// indexed under. Exposed for callers (bilingual export) that need to
    /// walk or dump the whole counterpart edition, not just resolve one
    /// citation at a time.
    /// </summary>
    public IReadOnlyList<(string Key, string RawCitationRef, string Text)> Ordered => _ordered;

    /// <summary>
    /// Finds the counterpart passage positions for a citation ref, allowing
    /// for the two editions dividing their text at different depths.
    ///
    /// Three passes, in order of precision: an exact hit; then walking *up*
    /// the primary ref, so a fine-grained original finds the coarser
    /// translated unit containing it; then walking *down*, so a coarse
    /// original picks up the finer translated pieces beneath it.
    /// </summary>
    public List<int>? ResolveIndices(string citationRef)
    {
        var passage = ExtractPassageRef(citationRef);
        if (passage.Length == 0) return null;

        if (_indexByKey.TryGetValue(passage, out var exact))
            return new List<int> { exact };

        var parts = passage.Split('.');
        for (var take = parts.Length - 1; take >= 1; take--)
        {
            var prefix = string.Join(".", parts.Take(take));
            if (_indexByKey.TryGetValue(prefix, out var coarser))
                return new List<int> { coarser };
        }

        if (_indicesByPrefix.TryGetValue(passage, out var finer))
            return finer;

        return null;
    }

    /// <summary>
    /// Convenience for callers that just want text, not positions - the
    /// matched passage(s) joined with a space. Null when nothing aligns.
    /// </summary>
    public string? ResolveText(string citationRef)
    {
        var indices = ResolveIndices(citationRef);
        if (indices == null || indices.Count == 0) return null;
        return string.Join(" ", indices.Select(i => _ordered[i].Text));
    }

    /// <summary>
    /// Same match as ResolveText, but also returns the counterpart's own
    /// citation ref - not for computing anything, purely so a caller can
    /// show it to a person alongside the text. That matters because this
    /// alignment is confident, not certain: two editions can label
    /// genuinely different things with the same coincidental key (a cast
    /// list numbered 1, 2, 3... in one edition can collide with unrelated
    /// spoken lines numbered 1, 2, 3... in another), and no amount of
    /// string-matching can detect that from the refs alone. Showing the
    /// matched ref next to the original's is what actually lets someone
    /// catch a bad alignment - a length check can flag the obvious cases,
    /// but seeing "this came from 1.3, not near where I am" is what confirms
    /// it. When a coarse ref matches several finer counterpart passages, the
    /// ref shown is the first one, where the passage begins.
    /// </summary>
    /// <summary>
    /// Same match as ResolveText, plus the counterpart's own citation ref
    /// and how many counterpart passages were assembled into it. The count
    /// matters for more than curiosity: an exact or coarser hit (count == 1)
    /// is the case where two editions could be numbering genuinely different
    /// things the same way by coincidence - worth treating with suspicion if
    /// the lengths don't match. A finer hit (count > 1, several passages
    /// walked *down* from a coarser query and joined) isn't a coincidence at
    /// all - every one of those pieces really does sit under the citation
    /// asked for, so a big combined length is exactly what should happen,
    /// not a warning sign.
    /// </summary>
    public (string CitationRef, string Text, int MatchedPassageCount)? ResolveMatch(string citationRef)
    {
        var indices = ResolveIndices(citationRef);
        if (indices == null || indices.Count == 0) return null;

        var text = string.Join(" ", indices.Select(i => _ordered[i].Text));
        var firstRef = _ordered[indices[0]].RawCitationRef;
        return (firstRef, text, indices.Count);
    }

    /// <summary>
    /// The passage portion of a citation ref - "1.1.1" out of
    /// "urn:cts:latinLit:phi0448.phi002.perseus-lat2.1.1.1".
    ///
    /// Perseus's TEI puts the full CTS URN in a div's @n attribute for many
    /// works, so that's what ends up stored. The URN's last identifier
    /// before the passage is the *version* - perseus-lat2 for the Latin,
    /// perseus-eng1 for its translation - which differs between editions by
    /// design, so whole-ref comparison never matches even on identical
    /// lines.
    ///
    /// Perseus version identifiers always contain a hyphen (perseus-grc2,
    /// perseus-eng1, perseus-lat2) while the passage segments after them
    /// never do, which makes the hyphen a reliable boundary to cut at.
    /// Everything after it is the passage reference both editions share.
    ///
    /// Refs that aren't URNs (a plain "1.1", or a scene-structured
    /// "prologue.pr.1") are returned untouched. That matters: an earlier
    /// version of this kept only trailing numeric segments, which turned
    /// both "act1.scene1.1" and "act2.scene1.1" into plain "1" and silently
    /// collided them in the lookup.
    /// </summary>
    public static string ExtractPassageRef(string citationRef)
    {
        if (string.IsNullOrWhiteSpace(citationRef)) return string.Empty;

        if (citationRef.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            var segments = citationRef.Split('.');
            for (var i = 0; i < segments.Length; i++)
            {
                if (!segments[i].Contains('-')) continue;

                var passage = string.Join(".", segments.Skip(i + 1));
                return passage.Length > 0 ? passage : citationRef;
            }
        }

        return citationRef;
    }
}
