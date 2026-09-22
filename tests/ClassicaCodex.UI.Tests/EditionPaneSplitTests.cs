using ClassicaCodex.Core.Models;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Which edition of a work lands in which reader pane.
///
/// Getting this wrong does not throw. It leaves a pane empty, or puts a text
/// somewhere a reader will not look for it, and the app reads as though the
/// text is missing rather than misfiled - which is how an Unknown-kind
/// edition once became searchable but unopenable.
/// </summary>
public class EditionPaneSplitTests
{
    private static Edition Of(EditionKind kind, int id, string? language = null) =>
        new() { EditionId = id, Kind = kind, Language = language, CtsUrn = $"urn:{id}" };

    /// <summary>
    /// The ordinary case, and it must not change: original left, translation
    /// right.
    /// </summary>
    [Fact]
    public void OriginalGoesLeftAndTranslationRight()
    {
        var (left, right) = MainForm.SplitEditionsBetweenPanes(new[]
        {
            Of(EditionKind.Original, 1, "grc"),
            Of(EditionKind.Translation, 2, "eng"),
        });

        Assert.Equal(new[] { 1 }, left.Select(e => e.EditionId));
        Assert.Equal(new[] { 2 }, right.Select(e => e.EditionId));
    }

    /// <summary>
    /// An edition ingest could not classify still has to be openable. It goes
    /// right, because whatever it is, it is a text of this work and the second
    /// pane is where a text that is not the original belongs.
    /// </summary>
    [Fact]
    public void UnknownKindIsStillReachable()
    {
        var (left, right) = MainForm.SplitEditionsBetweenPanes(new[]
        {
            Of(EditionKind.Original, 1),
            Of(EditionKind.Unknown, 2),
        });

        Assert.Single(left);
        Assert.Equal(new[] { 2 }, right.Select(e => e.EditionId));
    }

    /// <summary>
    /// Two originals and no translation is the cuneiform case: Oracc gives
    /// every text in signs and in transliteration, and both are the original
    /// because a transliteration renders the script rather than translating
    /// the language.
    ///
    /// Before this, both queued up in the left dropdown and the right pane sat
    /// empty - so the one thing two panes exist for was the one thing you
    /// could not do with the text that most needs it.
    /// </summary>
    [Fact]
    public void TwoOriginalsAndNoTranslationFillBothPanes()
    {
        var (left, right) = MainForm.SplitEditionsBetweenPanes(new[]
        {
            Of(EditionKind.Original, 1, "akk"),   // transliteration
            Of(EditionKind.Original, 2, "akk"),   // cuneiform signs
        });

        Assert.Equal(new[] { 1 }, left.Select(e => e.EditionId));
        Assert.Equal(new[] { 2 }, right.Select(e => e.EditionId));
    }

    /// <summary>
    /// A real translation always outranks a second original for the right-hand
    /// pane. If a cuneiform work ever gains an English text, that English is
    /// what a reader wants beside it - the signs do not get to keep the seat.
    /// </summary>
    [Fact]
    public void ATranslationOutranksASecondOriginal()
    {
        var (left, right) = MainForm.SplitEditionsBetweenPanes(new[]
        {
            Of(EditionKind.Original, 1, "akk"),
            Of(EditionKind.Original, 2, "akk"),
            Of(EditionKind.Translation, 3, "eng"),
        });

        Assert.Equal(new[] { 1, 2 }, left.Select(e => e.EditionId));
        Assert.Equal(new[] { 3 }, right.Select(e => e.EditionId));
    }

    /// <summary>
    /// The cuneiform reading arrangement, which is what an Oracc work has to
    /// land in: the signs on the left, the word-by-word meanings on the
    /// right, and the transliteration a click away in the left dropdown.
    ///
    /// The order matters and is set by ingest writing the signs first. Get it
    /// wrong and the reader opens on transliteration with the meanings beside
    /// it, which is a different and worse pairing - the text itself would not
    /// be on screen at all.
    /// </summary>
    [Fact]
    public void ACuneiformWorkOpensOnSignsWithMeaningsBeside()
    {
        var (left, right) = MainForm.SplitEditionsBetweenPanes(new[]
        {
            Of(EditionKind.Original, 1, "akk"),      // cuneiform, written first
            Of(EditionKind.Original, 2, "akk"),      // transliteration
            Of(EditionKind.Translation, 3, "eng"),   // the gloss
        });

        // The signs open by default, the transliteration is reachable.
        Assert.Equal(new[] { 1, 2 }, left.Select(e => e.EditionId));

        // The meanings sit opposite, where a translation would.
        Assert.Equal(new[] { 3 }, right.Select(e => e.EditionId));
    }

    /// <summary>
    /// One original on its own must not be split away from itself, leaving the
    /// left pane empty and the text on the right where nothing looks for it.
    /// </summary>
    [Fact]
    public void ASingleOriginalStaysOnTheLeft()
    {
        var (left, right) = MainForm.SplitEditionsBetweenPanes(new[] { Of(EditionKind.Original, 1) });

        Assert.Single(left);
        Assert.Empty(right);
    }

    /// <summary>
    /// No editions at all is a work whose text failed to import. It must not
    /// throw on the way to the reader reporting it.
    /// </summary>
    [Fact]
    public void NoEditionsIsNotAnError()
    {
        var (left, right) = MainForm.SplitEditionsBetweenPanes(Array.Empty<Edition>());

        Assert.Empty(left);
        Assert.Empty(right);
    }

    /// <summary>
    /// Every edition ends up in exactly one pane, whatever the mix. A text
    /// that lands in neither is invisible in the reader while still being
    /// returned by search.
    /// </summary>
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(0, 2, 0)]
    [InlineData(2, 0, 1)]
    public void NoEditionIsEverLost(int originals, int translations, int unknowns)
    {
        var id = 0;
        var editions = Enumerable.Range(0, originals).Select(_ => Of(EditionKind.Original, ++id))
            .Concat(Enumerable.Range(0, translations).Select(_ => Of(EditionKind.Translation, ++id)))
            .Concat(Enumerable.Range(0, unknowns).Select(_ => Of(EditionKind.Unknown, ++id)))
            .ToList();

        var (left, right) = MainForm.SplitEditionsBetweenPanes(editions);

        var placed = left.Concat(right).Select(e => e.EditionId).ToList();

        Assert.Equal(editions.Count, placed.Count);
        Assert.Equal(editions.Select(e => e.EditionId).OrderBy(i => i), placed.OrderBy(i => i));
    }
}
