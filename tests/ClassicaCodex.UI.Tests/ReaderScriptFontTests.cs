using ClassicaCodex.Core.Models;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Which font a reader pane needs to draw what is going into it.
///
/// This exists because a cuneiform edition rendered as a pane of empty
/// boxes, one per sign. The text was correct, the index was correct, and
/// Palatino Linotype contains no cuneiform - and nothing on screen
/// distinguished that from the import having failed.
///
/// The rule has to be wrong in neither direction. Too eager and a Greek
/// edition loses its reading font because one odd character appeared; too
/// shy and the boxes come back.
/// </summary>
public class ReaderScriptFontTests
{
    private static List<TextNode> Lines(params string[] text) =>
        text.Select(t => new TextNode { Text = t }).ToList();

    private static string Sign(int codePoint) => char.ConvertFromUtf32(codePoint);

    /// <summary>
    /// Everything that reads correctly today must keep its own font. Greek,
    /// Latin, Old Norse and English all live inside the Basic Multilingual
    /// Plane, so none of them trips this.
    /// </summary>
    [Theory]
    [InlineData("arma virumque cano")]
    [InlineData("μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος")]
    [InlineData("Hwæt! We Gardena in geardagum")]
    [InlineData("Vǫluspá")]
    [InlineData("")]
    public void OrdinaryTextKeepsThePanesOwnFont(string text)
    {
        Assert.Equal("Palatino Linotype",
            ReaderScriptFont.FamilyFor("Palatino Linotype", Lines(text)));
    }

    /// <summary>
    /// The case that started this: cuneiform needs the historic font.
    /// </summary>
    [Fact]
    public void CuneiformAsksForTheHistoricFont()
    {
        var line = Lines(Sign(0x12000) + Sign(0x12038));

        Assert.Equal(ReaderScriptFont.HistoricScripts,
            ReaderScriptFont.FamilyFor("Palatino Linotype", line));
    }

    /// <summary>
    /// And Egyptian, which the same font also covers - so hieroglyphs will
    /// not need this solving twice.
    /// </summary>
    [Fact]
    public void HieroglyphsAskForTheSameFont()
    {
        Assert.Equal(ReaderScriptFont.HistoricScripts,
            ReaderScriptFont.FamilyFor("Palatino Linotype", Lines(Sign(0x13000))));
    }

    /// <summary>
    /// One cuneiform line among many is still a pane that needs the font -
    /// the alternative is one row of boxes inside a readable text.
    /// </summary>
    [Fact]
    public void OneAstralLineAmongManyIsEnough()
    {
        var mixed = Lines("ordinary latin", "more latin", Sign(0x12000), "and more");

        Assert.Equal(ReaderScriptFont.HistoricScripts,
            ReaderScriptFont.FamilyFor("Palatino Linotype", mixed));
    }

    /// <summary>
    /// An astral character that is not a letter must NOT drag the whole pane
    /// into a historic font. Emoji and symbols live out there too, and
    /// swapping the font for one stray symbol would make the surrounding
    /// prose worse to read for no gain.
    /// </summary>
    [Theory]
    [InlineData(0x1F600)]   // emoji
    [InlineData(0x1D11E)]   // musical symbol G clef
    [InlineData(0x1D7CE)]   // mathematical bold digit zero
    public void AnAstralNonLetterDoesNotSwapTheFont(int codePoint)
    {
        Assert.Equal("Palatino Linotype",
            ReaderScriptFont.FamilyFor("Palatino Linotype", Lines("text " + Sign(codePoint))));
    }

    /// <summary>
    /// The two panes do not share a reading font, so each keeps its own when
    /// nothing special is needed.
    /// </summary>
    [Fact]
    public void EachPaneKeepsItsOwnDefault()
    {
        var plain = Lines("nothing unusual here");

        Assert.Equal("Palatino Linotype", ReaderScriptFont.FamilyFor("Palatino Linotype", plain));
        Assert.Equal("Georgia", ReaderScriptFont.FamilyFor("Georgia", plain));
    }

    /// <summary>
    /// And both give way to the historic font when the text needs it, so a
    /// cuneiform edition is legible in whichever pane it is opened in - which
    /// it now can be in either, since a second original fills the right-hand
    /// pane.
    /// </summary>
    [Fact]
    public void EitherPaneWillTakeTheHistoricFont()
    {
        var signs = Lines(Sign(0x12000));

        Assert.Equal(ReaderScriptFont.HistoricScripts, ReaderScriptFont.FamilyFor("Palatino Linotype", signs));
        Assert.Equal(ReaderScriptFont.HistoricScripts, ReaderScriptFont.FamilyFor("Georgia", signs));
    }

    /// <summary>
    /// An empty pane keeps its own font rather than throwing on the way.
    /// </summary>
    [Fact]
    public void NoNodesIsNotAnError()
    {
        Assert.False(ReaderScriptFont.IsNeededFor(null));
        Assert.False(ReaderScriptFont.IsNeededFor(Array.Empty<TextNode>()));
        Assert.Equal("Georgia", ReaderScriptFont.FamilyFor("Georgia", null));
    }
}
