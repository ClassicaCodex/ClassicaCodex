using System.Xml.Linq;
using ClassicaCodex.Core.Bfm;
using ClassicaCodex.Ingestion.Bfm;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the Base de Français Médiéval.
///
/// The corpus is TEI throughout but not one encoding: four shapes, two
/// punctuation tagsets, and a join attribute that means something different
/// in each of the two readings. Every case below is taken from a real file,
/// and most of them were found by running the loader over all 500 and looking
/// at what came out.
/// </summary>
public class BfmTextLoaderTests
{
    private static BfmText Parse(string body) => BfmTextLoader.Parse(
        XDocument.Parse($"""
        <TEI xmlns="http://www.tei-c.org/ns/1.0">
          <teiHeader>
            <fileDesc>
              <titleStmt><title>A Text</title><author>anonyme</author></titleStmt>
            </fileDesc>
            <profileDesc><langUsage><language ident="fro">ancien français</language></langUsage></profileDesc>
          </teiHeader>
          <text><body>{body}</body></text>
        </TEI>
        """), "test");

    /// <summary>
    /// The fabliaux: a verse line is an &lt;l&gt;, and the &lt;lb/&gt; inside
    /// it is where the manuscript broke the line, not where the verse did.
    /// </summary>
    [Fact]
    public void AVerseSpanningTwoManuscriptLinesIsOneLine()
    {
        var text = Parse("""
            <l n="3"><lb n="5"/><w>sempres</w><w>en</w><lb n="6"/><w>puet</w><w>oïr</w></l>
            <l n="4"><lb n="7"/><w>si</w><w>com</w></l>
            """);

        Assert.Equal(2, text.Lines.Count);
        Assert.Equal("3", text.Lines[0].Citation);
        Assert.Equal("sempres en puet oïr", text.Lines[0].Normalised);
        Assert.True(text.Lines[0].IsVerse);
    }

    /// <summary>
    /// Elsewhere there is no &lt;l&gt; at all and the numbered &lt;lb&gt; IS
    /// the verse line - which is how the Chanson de Roland is encoded.
    /// </summary>
    [Fact]
    public void ANumberedLineBreakIsTheVerseWhereThereIsNoLineElement()
    {
        var text = Parse("""
            <lb n="1"/><w>Carles</w><w>li</w><w>reis</w>
            <lb n="2"/><w>Set</w><w>anz</w>
            """);

        Assert.Equal(2, text.Lines.Count);
        Assert.Equal("1", text.Lines[0].Citation);
        Assert.Equal("Carles li reis", text.Lines[0].Normalised);
        Assert.Equal("Set anz", text.Lines[1].Normalised);
    }

    /// <summary>
    /// <b>An unnumbered line break is layout, and prose runs through it.</b>
    /// Reading every &lt;lb/&gt; as a line turned the Seigneur d'Anglure's
    /// prose travel diary into 2,652 numbered "verses" the first time this
    /// ran.
    /// </summary>
    [Fact]
    public void AnUnnumberedLineBreakDoesNotCutProse()
    {
        var text = Parse("""
            <p n="1">Au commencement<lb/> de ma matiere,<lb/> je pry</p>
            <p n="2">Mais ainsi comment il advient</p>
            """);

        Assert.Equal(2, text.Lines.Count);
        Assert.Equal("1", text.Lines[0].Citation);
        Assert.Equal("Au commencement de ma matiere, je pry", text.Lines[0].Normalised);
        Assert.False(text.Lines[0].IsVerse);
    }

    /// <summary>
    /// Plain prose with no tokens at all - 78 of the 500 files are encoded
    /// this way, and a loader that only reads &lt;w&gt; returns nothing for
    /// them.
    /// </summary>
    [Fact]
    public void ProseWithNoWordElementsStillReads()
    {
        var text = Parse("""<p n="1">Verités est que depuis que la cité de Romme fu fondee</p>""");

        var line = Assert.Single(text.Lines);
        Assert.Equal("Verités est que depuis que la cité de Romme fu fondee", line.Normalised);
    }

    /// <summary>
    /// <b>The two readings, and the reason for them.</b> &lt;ex&gt; is the
    /// editor spelling out an abbreviation the scribe wrote as a mark - 96,872
    /// times across the corpus - and it belongs to one reading only.
    /// "S|emp|&lt;ex&gt;re&lt;/ex&gt;|s" is <i>Semps</i> on the page and
    /// <i>sempres</i> in the edition.
    /// </summary>
    [Fact]
    public void AnExpandedAbbreviationIsInTheEditorsReadingOnly()
    {
        var text = Parse("""
            <l n="1"><w><choice><orig>S</orig><reg>s</reg></choice>emp<ex>re</ex>s</w></l>
            """);

        var line = Assert.Single(text.Lines);
        Assert.Equal("Semps", line.Diplomatic);
        Assert.Equal("sempres", line.Normalised);
        Assert.True(text.HasTwoReadings);
    }

    /// <summary>
    /// join="right" marks where the manuscript leaves no space - these scribes
    /// separate words irregularly. It belongs to the scribe's reading; putting
    /// it in the editor's would file 30,680 glued-together words in the word
    /// index.
    /// </summary>
    [Fact]
    public void TheManuscriptsMissingSpacesStayInTheScribesReading()
    {
        var text = Parse("""
            <l n="1"><w join="right">Le</w><w join="right">flabel</w><w>d’</w><w>Aloul</w></l>
            """);

        var line = Assert.Single(text.Lines);
        Assert.Equal("Leflabeld’Aloul", line.Diplomatic);
        Assert.Equal("Le flabel d’Aloul", line.Normalised);
    }

    /// <summary>An apostrophe elides in anybody's reading.</summary>
    [Fact]
    public void AWordEndingInAnApostropheClosesUpInBothReadings()
    {
        var text = Parse("""<lb n="1"/><w>Tresqu'</w><w>en</w><w>la</w><w>mer</w>""");

        var line = Assert.Single(text.Lines);
        Assert.Equal("Tresqu'en la mer", line.Diplomatic);
        Assert.Equal("Tresqu'en la mer", line.Normalised);
    }

    /// <summary>
    /// <b>Punctuation is a word here, under two different tagsets.</b> "pon"
    /// alone is the single commonest tag in the whole corpus at 307,126
    /// occurrences; PONfbl and PONfrt are the finer scheme. Spacing either
    /// like a word gives "vient , mene".
    /// </summary>
    [Theory]
    [InlineData("pon")]
    [InlineData("PONfbl")]
    [InlineData("PONfrt")]
    public void PunctuationAttachesToTheWordBeforeIt(string tag)
    {
        var text = Parse($"""<lb n="1"/><w>vient</w><w type="{tag}">,</w><w>mene</w>""");

        Assert.Equal("vient, mene", Assert.Single(text.Lines).Normalised);
    }

    [Fact]
    public void AnOpeningBracketAttachesToTheWordAfterIt()
    {
        var text = Parse("""<lb n="1"/><w>dist</w><w type="pon">(</w><w>ço</w><w type="pon">)</w>""");

        Assert.Equal("dist (ço)", Assert.Single(text.Lines).Normalised);
    }

    /// <summary>
    /// An editor's note is the editor talking about the text rather than any
    /// of it. Letting one through would put a modern French sentence inside an
    /// Old French line, where it would be indexed and counted as though a
    /// scribe had written it.
    /// </summary>
    [Fact]
    public void AnEditorsNoteIsNotPartOfTheText()
    {
        var text = Parse("""
            <lb n="1"/><w>Carles</w><note>Charlemagne, évidemment.</note><w>li</w><w>reis</w>
            """);

        Assert.Equal("Carles li reis", Assert.Single(text.Lines).Normalised);
    }

    [Fact]
    public void WordsCarryTheirHeadwordAndGrammarWhereTheCorpusGivesThem()
    {
        var text = Parse("""
            <lb n="1"/><w type="NOMpro" lemma="Charles">Carles</w><w type="DETdef" lemma="le">li</w>
            """);

        var line = Assert.Single(text.Lines);
        Assert.True(text.HasLemmas);
        Assert.Equal(2, line.Words.Count);
        Assert.Equal("Charles", line.Words[0].Headword);
        Assert.Equal("NOMpro", line.Words[0].Tag);
    }

    /// <summary>
    /// <b>Six of the 500 texts may not be redistributed.</b> The corpus marks
    /// each file in its own header, and this is what keeps them out.
    /// </summary>
    [Theory]
    [InlineData("libre_1a", true)]
    [InlineData("libre_1d", true)]
    [InlineData("restreint_2a", false)]
    [InlineData("restreint_3", false)]
    [InlineData("restreint_4", false)]
    [InlineData(null, true)]
    public void RestrictedTextsAreRefusedAndFreeOnesAreNot(string? availability, bool expected)
    {
        Assert.Equal(expected, BfmTextLoader.IsFreelyAvailable(availability));
    }

    [Fact]
    public void TheAvailabilityMarkIsReadFromTheFile()
    {
        var text = Parse("""
            <ab type="availability_bfm" subtype="restreint_4">Conditions</ab>
            <lb n="1"/><w>Carles</w>
            """);

        Assert.Equal("restreint_4", text.Availability);
        Assert.False(text.IsFree);
    }

    /// <summary>
    /// A chantefable alternates sung verse and spoken prose, so the question
    /// is per line rather than per text. Aucassin et Nicolette is the one that
    /// matters, and recording it either way round would be wrong half the time.
    /// </summary>
    [Fact]
    public void VerseAndProseAreDecidedLineByLine()
    {
        var text = Parse("""
            <lb n="1"/><w>et</w><w>des</w><w>proueces</w>
            <p n="2">Or dient et content et fablent</p>
            """);

        Assert.Equal(2, text.Lines.Count);
        Assert.True(text.Lines[0].IsVerse);
        Assert.False(text.Lines[1].IsVerse);
    }

    /// <summary>The corpus is largely anonymous and says so in a French adjective, which is not a person.</summary>
    [Fact]
    public void AnonymousIsNotAnAuthor()
    {
        Assert.Null(new BfmTextEntry { Author = "anonyme" }.NamedAuthor);
        Assert.Null(new BfmTextEntry { Author = "  " }.NamedAuthor);
        Assert.Equal("Marie de France", new BfmTextEntry { Author = "Marie de France" }.NamedAuthor);
    }
}
