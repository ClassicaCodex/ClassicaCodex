using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// Create Translation asks the model to echo each passage's citation
/// reference back alongside its translation. It mostly does. When it didn't,
/// the returned reference was stored as-is, which meant it counted as a
/// translated line in every progress figure while matching no actual line -
/// so the dialog announced "all lines translated and saved" over an empty
/// preview, and wrote an edition containing no text at all.
///
/// Reconcile is what stops a reply being trusted about which passage it
/// belongs to. These cases are the ways a model can get that wrong.
/// </summary>
public class GeminiBatchReconcileTests
{
    private static List<(string CitationRef, string Text)> Passages(params string[] refs) =>
        refs.Select(r => (r, $"source {r}")).ToList();

    private static List<(string CitationRef, string TranslatedText)> Returned(
        params (string Ref, string Text)[] items) =>
        items.Select(i => (i.Ref, i.Text)).ToList();

    [Fact]
    public void ExactRefsAreMatchedStraightThrough()
    {
        var result = GeminiTranslationService.Reconcile(
            Returned(("1.1", "first"), ("1.2", "second")),
            Passages("1.1", "1.2"));

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.CitationRef == "1.1" && r.TranslatedText == "first");
        Assert.Contains(result, r => r.CitationRef == "1.2" && r.TranslatedText == "second");
    }

    /// <summary>
    /// The prompt tags each passage as "[1.1] ...", so the model frequently
    /// echoes the brackets as part of the reference.
    /// </summary>
    [Theory]
    [InlineData("[1.1]")]
    [InlineData(" 1.1 ")]
    [InlineData("[ 1.1 ]")]
    public void RefsAreMatchedThroughFormattingNoise(string returnedRef)
    {
        var result = GeminiTranslationService.Reconcile(
            Returned((returnedRef, "translated")),
            Passages("1.1"));

        var hit = Assert.Single(result);
        Assert.Equal("1.1", hit.CitationRef);   // keyed by OUR ref, not the model's
        Assert.Equal("translated", hit.TranslatedText);
    }

    /// <summary>
    /// The observed failure: one passage, one translation, and a reference
    /// that resembles nothing that was sent. With the counts equal there is
    /// only one passage it can belong to, so position resolves it.
    /// </summary>
    [Fact]
    public void UnrecognisedRefFallsBackToPositionWhenCountsMatch()
    {
        var result = GeminiTranslationService.Reconcile(
            Returned(("passage 1", "the translation")),
            Passages("1.1"));

        var hit = Assert.Single(result);
        Assert.Equal("1.1", hit.CitationRef);
        Assert.Equal("the translation", hit.TranslatedText);
    }

    [Fact]
    public void PositionalFallbackFillsOnlyTheGaps()
    {
        // Middle entry came back with a ref that matches nothing.
        var result = GeminiTranslationService.Reconcile(
            Returned(("1.1", "first"), ("???", "second"), ("1.3", "third")),
            Passages("1.1", "1.2", "1.3"));

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.CitationRef == "1.2" && r.TranslatedText == "second");
    }

    /// <summary>
    /// The important limit. If the reply is short, position can't be trusted -
    /// lining up two translations against three passages would attach one of
    /// them to the wrong line, and a confidently wrong translation is worse
    /// than a missing one. The unmatched passage is left for a retry instead.
    /// </summary>
    [Fact]
    public void NoPositionalGuessWhenTheReplyIsIncomplete()
    {
        var result = GeminiTranslationService.Reconcile(
            Returned(("1.1", "first"), ("???", "mystery")),
            Passages("1.1", "1.2", "1.3"));

        var hit = Assert.Single(result);
        Assert.Equal("1.1", hit.CitationRef);
    }

    [Fact]
    public void DuplicateRefsDoNotOverwriteEachOther()
    {
        // Both claim 1.1; the second can't take a ref already spoken for, and
        // with counts equal it lands on the passage still unclaimed.
        var result = GeminiTranslationService.Reconcile(
            Returned(("1.1", "first"), ("1.1", "second")),
            Passages("1.1", "1.2"));

        Assert.Equal(2, result.Count);
        Assert.Equal("first", result.Single(r => r.CitationRef == "1.1").TranslatedText);
        Assert.Equal("second", result.Single(r => r.CitationRef == "1.2").TranslatedText);
    }

    /// <summary>
    /// Nothing attributable means nothing returned - never a key invented
    /// from the model's reply. That invented key is what made an empty
    /// translation look like a finished one.
    /// </summary>
    [Fact]
    public void NothingIsInventedWhenTheReplyCannotBeAttributed()
    {
        var result = GeminiTranslationService.Reconcile(
            Returned(("nonsense", "a"), ("also nonsense", "b"), ("more", "c")),
            Passages("1.1", "1.2"));

        Assert.Empty(result);
    }

    [Fact]
    public void EmptyReplyReturnsNothing()
    {
        Assert.Empty(GeminiTranslationService.Reconcile(Returned(), Passages("1.1")));
    }

    /// <summary>
    /// Citation refs in the Perseus corpus aren't always numeric - some
    /// orations use whole descriptive phrases as a div's @n.
    /// </summary>
    [Fact]
    public void NonNumericCitationRefsMatch()
    {
        var result = GeminiTranslationService.Reconcile(
            Returned(("Against Timarchus", "translated")),
            Passages("Against Timarchus"));

        Assert.Equal("Against Timarchus", Assert.Single(result).CitationRef);
    }
}
