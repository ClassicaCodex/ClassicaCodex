using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Which right-click tools a pane offers.
///
/// Getting this wrong is quiet in both directions and neither is harmless.
/// Too generous and a reader picks Word Study on a cuneiform line and gets
/// an empty window, which reads as the app being broken rather than the data
/// being absent. Too strict and a tool disappears from a text that could
/// have used it, and nothing on screen says why.
///
/// The rule is about DATA, never about language or collection, so that it
/// corrects itself when the data changes. These pin that.
/// </summary>
public class ReaderPaneCapabilitiesTests
{
    /// <summary>
    /// The cuneiform case. Oracc ships no lexicon into this app's tables and
    /// no apparatus, so both tools open on nothing and both stay hidden.
    /// </summary>
    [Fact]
    public void AkkadianWithNoLexiconOffersNeitherTool()
    {
        var capabilities = ReaderPaneCapabilities.For("akk", lemmaCount: 0, apparatusCount: 0);

        Assert.False(capabilities.HasLexicon);
        Assert.False(capabilities.HasApparatus);
    }

    /// <summary>
    /// And the reason the gate is on the data: the day somebody imports
    /// Oracc's Akkadian glossaries, Word Study comes back on its own. Nothing
    /// here names a language or a collection, so nothing has to be edited.
    /// </summary>
    [Fact]
    public void AkkadianWithALexiconOffersWordStudy()
    {
        var capabilities = ReaderPaneCapabilities.For("akk", lemmaCount: 40_000, apparatusCount: 0);

        Assert.True(capabilities.HasLexicon);
    }

    /// <summary>
    /// The ordinary case must not regress: Greek keeps both tools.
    /// </summary>
    [Fact]
    public void GreekWithBothKeepsBothTools()
    {
        var capabilities = ReaderPaneCapabilities.For("grc", lemmaCount: 900_000, apparatusCount: 12);

        Assert.True(capabilities.HasLexicon);
        Assert.True(capabilities.HasApparatus);
    }

    /// <summary>
    /// A Greek edition with no apparatus loses Editor's Notes too. This is
    /// the half of the rule that proves it is not a cuneiform special case -
    /// the tool follows the data, whatever the text is written in.
    /// </summary>
    [Fact]
    public void AnyEditionWithoutAnApparatusLosesEditorsNotes()
    {
        var capabilities = ReaderPaneCapabilities.For("grc", lemmaCount: 900_000, apparatusCount: 0);

        Assert.True(capabilities.HasLexicon);
        Assert.False(capabilities.HasApparatus);
    }

    /// <summary>
    /// No language means no lookup, however much lemma data the library
    /// holds: WordStudyForm is given the language, and it has to be, because
    /// an English translation and a Latin original are both written in the
    /// Latin alphabet and the word itself cannot say which it is.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoLanguageMeansNoWordStudy(string? language)
    {
        var capabilities = ReaderPaneCapabilities.For(language, lemmaCount: 900_000, apparatusCount: 0);

        Assert.False(capabilities.HasLexicon);
    }

    /// <summary>
    /// An apparatus belongs to an edition rather than to a language, so it
    /// survives a text whose language nobody recorded.
    /// </summary>
    [Fact]
    public void AnApparatusDoesNotDependOnTheLanguage()
    {
        var capabilities = ReaderPaneCapabilities.For(null, lemmaCount: 0, apparatusCount: 3);

        Assert.False(capabilities.HasLexicon);
        Assert.True(capabilities.HasApparatus);
    }

    /// <summary>
    /// A pane that has never been filled offers nothing rather than
    /// everything. The menu can be opened before any edition has loaded, and
    /// the safe default there is the empty one - an absent tool is a smaller
    /// wrong than a tool that opens on no text at all.
    /// </summary>
    [Fact]
    public void AnEmptyPaneOffersNothing()
    {
        var capabilities = ReaderPaneCapabilities.Empty;

        Assert.False(capabilities.HasLexicon);
        Assert.False(capabilities.HasApparatus);
    }
}
