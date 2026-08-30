namespace ClassicaCodex.Core;

/// <summary>
/// What the two editions do at one passage.
///
/// Ordered by how much it matters, so a caller can filter with a comparison
/// rather than a set of cases: everything from OrthographyDiffers upward is
/// something an editor chose, and TextDiffers is the only one that is a
/// difference in what the text says.
/// </summary>
public enum CollationStatus
{
    /// <summary>Character for character the same.</summary>
    Identical,

    /// <summary>Same words; different spacing, case, punctuation or brackets.</summary>
    PresentationDiffers,

    /// <summary>Same words; different accents, breathings, or u/v and i/j.</summary>
    OrthographyDiffers,

    /// <summary>
    /// Same words, broken across the two lines at a different point - one
    /// edition ending a line mid-word and hyphenating it, most often.
    /// </summary>
    LineationDiffers,

    /// <summary>Different words. The thing a collation is for.</summary>
    TextDiffers,

    /// <summary>Present in the first edition and absent from the second.</summary>
    OnlyInLeft,

    /// <summary>Present in the second edition and absent from the first.</summary>
    OnlyInRight
}

/// <summary>One passage as the two editions print it.</summary>
public sealed record CollationRow(
    string PassageRef,
    string? Left,
    string? Right,
    CollationStatus Status);

/// <summary>
/// The whole comparison, with the counts that say whether it is worth reading.
///
/// Substantive is deliberately separate from the rest. A collation that
/// reports two thousand differences is telling you nothing; one that reports
/// two thousand differences of which forty are in the words is telling you
/// where to look.
/// </summary>
public sealed record CollationResult(
    IReadOnlyList<CollationRow> Rows,
    int Identical,
    int PresentationDiffers,
    int OrthographyDiffers,
    int LineationDiffers,
    int TextDiffers,
    int OnlyInLeft,
    int OnlyInRight)
{
    /// <summary>Passages both editions have, however they print them.</summary>
    public int Shared =>
        Identical + PresentationDiffers + OrthographyDiffers + LineationDiffers + TextDiffers;

    /// <summary>Shared passages where the two print the same words.</summary>
    public int Agreeing => Identical + PresentationDiffers + OrthographyDiffers + LineationDiffers;

    /// <summary>
    /// Whether the two can be compared at all.
    ///
    /// Sharing references is not enough, and assuming it was produced the
    /// worst results in the first run against the real library. Two editions
    /// that divide a work differently still collide on plain numeric
    /// references - both number their passages 1, 2, 3 - so they appear to
    /// align perfectly and then disagree at every single one. Several of the
    /// CSEL and Patrologia Latina pairings do exactly this.
    ///
    /// The guard is that some of the aligned passages have to actually agree.
    /// Two printings of the same work disagreeing about every line of it is
    /// not a collation with a great many variants; it is two things that were
    /// never lined up. The threshold is a judgement rather than a fact, and is
    /// set low deliberately - it exists to catch total mismatch, not to rule
    /// on how divergent two genuine editions may be.
    /// </summary>
    public bool IsAlignable => Shared > 0 && Agreeing * 10 >= Shared;
}

/// <summary>
/// Lines up two editions of one work by citation reference and says where they
/// disagree.
///
/// Aligning on the reference rather than by sequence is what makes this
/// tractable. The two editions are independent printings, and their line
/// counts differ - one Aeschylus here has 1,884 lines against the other's
/// 1,881 - so walking them in parallel would slip by three somewhere in the
/// first act and report every line after that as a variant. The citation
/// reference is the editors' own statement about which line is which, and it
/// is the same identity the annotations use for the same reason.
///
/// What it does not do is align within a line. Knowing that two printings of
/// Agamemnon 42 differ, and being able to read both, is the useful part; which
/// word inside it moved is a question for the reader looking at them.
/// </summary>
public static class Collation
{
    /// <summary>
    /// Compares two editions given their passages as (reference, text) pairs.
    ///
    /// A reference appearing more than once in one edition has its texts joined
    /// in the order given. That happens where an edition splits a citation
    /// across several elements, and treating the pieces as rival readings of
    /// each other would be nonsense.
    ///
    /// The language is the editions', used to decide whether u/v and i/j are
    /// one letter. Passing null simply applies every fold, which costs nothing:
    /// neither alphabet contains the other's letters.
    /// </summary>
    public static CollationResult Compare(
        IEnumerable<(string PassageRef, string Text)> left,
        IEnumerable<(string PassageRef, string Text)> right,
        string? language = null)
    {
        var (leftByRef, leftOrder) = Combine(left);
        var (rightByRef, rightOrder) = Combine(right);

        var rows = new List<CollationRow>(Math.Max(leftByRef.Count, rightByRef.Count));

        int identical = 0, presentation = 0, orthography = 0, text = 0, onlyLeft = 0, onlyRight = 0;

        foreach (var passageRef in leftOrder)
        {
            var leftText = leftByRef[passageRef];

            if (!rightByRef.TryGetValue(passageRef, out var rightText))
            {
                rows.Add(new CollationRow(passageRef, leftText, null, CollationStatus.OnlyInLeft));
                onlyLeft++;
                continue;
            }

            var status = CollationNormalizer.FirstAgreement(leftText, rightText, language) switch
            {
                CollationLevel.Raw => CollationStatus.Identical,
                CollationLevel.Presentation => CollationStatus.PresentationDiffers,
                CollationLevel.Orthography => CollationStatus.OrthographyDiffers,
                _ => CollationStatus.TextDiffers
            };

            switch (status)
            {
                case CollationStatus.Identical: identical++; break;
                case CollationStatus.PresentationDiffers: presentation++; break;
                case CollationStatus.OrthographyDiffers: orthography++; break;
                default: text++; break;
            }

            rows.Add(new CollationRow(passageRef, leftText, rightText, status));
        }

        // Appended after, rather than interleaved. The left edition's own
        // sequence is the spine of the list - it is the order that edition
        // prints - and there is no reliable way to place a reference the left
        // does not have inside it, since citation references sort by neither
        // string nor number ("10.1" belongs after "9.1", not before it).
        foreach (var passageRef in rightOrder)
        {
            if (leftByRef.ContainsKey(passageRef)) continue;

            rows.Add(new CollationRow(passageRef, null, rightByRef[passageRef], CollationStatus.OnlyInRight));
            onlyRight++;
        }

        var lineation = ReclassifyLineation(rows, language);
        text -= lineation;

        return new CollationResult(
            rows, identical, presentation, orthography, lineation, text, onlyLeft, onlyRight);
    }

    /// <summary>
    /// Finds pairs of adjacent rows that differ only in where the line break
    /// falls, and downgrades both out of TextDiffers.
    ///
    /// One edition ends a line mid-word and hyphenates it - Aeschylus' lyric
    /// passages are full of this, "Ἀχαι-" then "ῶν" against the other's
    /// "Ἀχαιῶν" - which makes two adjacent lines differ where the text does
    /// not. Left alone it is the second-largest source of false variants after
    /// the elision mark, and unlike a real reading it always comes in pairs.
    ///
    /// Detected by joining each side's two lines and comparing them with the
    /// spaces removed, which covers the hyphen (already gone as punctuation)
    /// and any word that simply moved across the break.
    ///
    /// Deliberately only adjacent pairs. A displacement running over more
    /// lines than that is a systematic difference in how the two editions
    /// divide the text, and calling it a lineation detail would be a claim
    /// about the editions this cannot support.
    /// </summary>
    private static int ReclassifyLineation(List<CollationRow> rows, string? language)
    {
        var found = 0;

        for (var i = 0; i < rows.Count - 1; i++)
        {
            if (rows[i].Status != CollationStatus.TextDiffers ||
                rows[i + 1].Status != CollationStatus.TextDiffers)
            {
                continue;
            }

            var left = Join(rows[i].Left, rows[i + 1].Left);
            var right = Join(rows[i].Right, rows[i + 1].Right);

            if (!string.Equals(Squash(left, language), Squash(right, language), StringComparison.Ordinal))
            {
                continue;
            }

            rows[i] = rows[i] with { Status = CollationStatus.LineationDiffers };
            rows[i + 1] = rows[i + 1] with { Status = CollationStatus.LineationDiffers };
            found += 2;

            // Past the pair just claimed, so one line cannot be read as
            // rejoining both its neighbours.
            i++;
        }

        return found;
    }

    private static string Join(string? first, string? second) =>
        (first ?? string.Empty) + " " + (second ?? string.Empty);

    /// <summary>
    /// Folded as far as it goes and then stripped of spaces, so that where the
    /// words were divided stops mattering while which words they are does not.
    /// </summary>
    private static string Squash(string text, string? language) =>
        CollationNormalizer.Normalize(text, CollationLevel.Orthography, language).Replace(" ", "");

    /// <summary>
    /// Groups an edition's passages by reference, preserving the order they
    /// arrived in - which is the order the caller read them out of the
    /// edition, and the order the reader sees them.
    /// </summary>
    private static (Dictionary<string, string> ByRef, List<string> Order) Combine(
        IEnumerable<(string PassageRef, string Text)> passages)
    {
        var combined = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var (passageRef, text) in passages)
        {
            if (combined.TryGetValue(passageRef, out var existing))
            {
                combined[passageRef] = existing + " " + text;
                continue;
            }

            combined[passageRef] = text;
            order.Add(passageRef);
        }

        return (combined, order);
    }
}
