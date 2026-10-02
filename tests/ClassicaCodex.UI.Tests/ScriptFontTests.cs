using ClassicaCodex.Core.Models;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Choosing a font that can draw the text.
///
/// <b>The surrogate pair is the whole point.</b> Every cuneiform codepoint is
/// above U+FFFF, so in a C# string each sign is two chars, both of them in the
/// D800-DFFF range. A detector written as a loop over chars compares those
/// halves against 0x12000, finds nothing, and reports a tablet as needing no
/// special font - which is exactly the state that shipped in 3.13.0 and showed
/// an Oracc tablet as rows of small circles.
/// </summary>
public class ScriptFontTests
{
    /// <summary>Four signs from the start of the block: A, BA, GA, cuneiform numeric one.</summary>
    private const string Cuneiform = "\U00012000\U00012040\U00012100\U00012415";

    private static TextNode Line(string text) => new() { Text = text };

    [Fact]
    public void CuneiformIsFoundEvenThoughEverySignIsASurrogatePair()
    {
        // If this ever stops holding, the test is no longer testing what it
        // was written for.
        Assert.True(Cuneiform.Length > Cuneiform.EnumerateRunes().Count(),
            "The sample is expected to be surrogate pairs - two chars per sign.");

        Assert.True(ScriptFonts.ContainsCuneiform(Cuneiform));
    }

    [Theory]
    [InlineData("in 2 day in")]                       // the English gloss pane
    [InlineData("a-na ṣi-bu-ti-šu")]                  // the transliteration edition
    [InlineData("μῆνιν ἄειδε θεά")]                   // Greek
    [InlineData("tẽ dñicalis t̾re nr̃e")]              // a medieval Latin line
    [InlineData("")]
    public void EverythingElseNeedsNoSpecialFont(string text)
    {
        Assert.False(ScriptFonts.ContainsCuneiform(text));
    }

    /// <summary>
    /// The two Oracc editions of one work are both akk/Original and differ
    /// only in script, so this is the distinction the whole fix rests on.
    /// </summary>
    [Fact]
    public void TheTwoOraccEditionsOfOneWorkAreToldApart()
    {
        var cuneiform = new[] { Line(Cuneiform), Line(Cuneiform) };
        var transliteration = new[] { Line("a-na ṣi-bu-ti-šu"), Line("ša₂ KUR-e") };

        Assert.True(ScriptFonts.NeedsCuneiform(cuneiform));
        Assert.False(ScriptFonts.NeedsCuneiform(transliteration));
    }

    /// <summary>
    /// One cuneiform line anywhere in the examined span is enough - an edition
    /// whose first lines are broken away and empty still gets its font.
    /// </summary>
    [Fact]
    public void AnEditionThatStartsWithEmptyLinesIsStillCuneiform()
    {
        var nodes = new[] { Line(""), Line(""), Line(Cuneiform) };

        Assert.True(ScriptFonts.NeedsCuneiform(nodes));
    }

    /// <summary>
    /// The scan stops. Migne is 286,531 lines and reading all of them to
    /// conclude it is Latin would be a pause on every work opened, so a
    /// cuneiform line far past the cap is deliberately not found.
    /// </summary>
    [Fact]
    public void TheScanStopsRatherThanReadingAWholeCorpus()
    {
        var nodes = Enumerable.Range(0, 4000)
            .Select(_ => Line(new string('a', 100)))
            .Append(Line(Cuneiform))
            .ToList();

        Assert.False(ScriptFonts.NeedsCuneiform(nodes));
    }

    /// <summary>
    /// A family nobody has resolves to null rather than to a font name that
    /// would then fail to construct.
    /// </summary>
    [Fact]
    public void AnUninstalledFamilyResolvesToNothing()
    {
        Assert.Null(ScriptFonts.FirstInstalled(new[] { "No Such Font 8831" }));
    }

    /// <summary>
    /// And on a machine that has one, it is chosen. Segoe UI Historic ships
    /// with Windows 10 and 11 and carries the cuneiform block, which is why
    /// the fix needs nothing installed; the assertion is conditional because
    /// the test must not fail on a Windows that does not have it.
    /// </summary>
    [Fact]
    public void CuneiformResolvesToAnInstalledFontWhereThereIsOne()
    {
        var chosen = ScriptFonts.FamilyFor(new[] { Line(Cuneiform) });
        var segoe = ScriptFonts.FirstInstalled(new[] { "Segoe UI Historic" });

        if (segoe == null) return;

        Assert.Equal("Segoe UI Historic", chosen);
    }

    [Fact]
    public void TextThatNeedsNothingSpecialAsksForNoFont()
    {
        Assert.Null(ScriptFonts.FamilyFor(new[] { Line("μῆνιν ἄειδε θεά") }));
    }
}
