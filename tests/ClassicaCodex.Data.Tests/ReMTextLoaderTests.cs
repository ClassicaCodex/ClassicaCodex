using System.Xml.Linq;
using ClassicaCodex.Ingestion.ReM;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the Reference Corpus of Middle High German.
///
/// ReM's TEI is a linguistic export rather than an edition: one &lt;ab&gt; per
/// text, every token its own &lt;w&gt;, and the only structure a stream of empty
/// &lt;pb/&gt;, &lt;cb/&gt; and &lt;lb/&gt; milestones between them. There is no
/// &lt;div&gt;, &lt;l&gt; or &lt;p&gt; anywhere in the corpus.
///
/// That matters more than it sounds, because the shared TeiParser does not
/// refuse it. &lt;ab&gt; is neither one of its leaf elements nor a container of
/// them, so it emits the whole work as a single passage cited "ab1" and reports
/// a successful import - the Rolandslied's 44,051 tokens as one line. These
/// tests exist around a loader written because failing loudly was not on offer.
///
/// Every figure quoted below was measured against the real v2.1 distribution,
/// 406 files and 2,293,068 tokens.
/// </summary>
public class ReMTextLoaderTests
{
    private const string Tei = "http://www.tei-c.org/ns/1.0";

    /// <summary>
    /// A ReM file with the given body, and the header shape the real files have -
    /// which is not the obvious one. The author is in profileDesc/creation, not
    /// in titleStmt; titleStmt holds only the annotation team's credits.
    /// </summary>
    private static ReMText Load(
        string body, string author = "-", string title = "Ein Text", string scheme = "Hs.: Blatt (r/v), Zeile")
    {
        var xml = $@"<TEI xmlns=""{Tei}"" version=""4.6.0"">
  <teiHeader>
    <fileDesc xml:id=""M999"">
      <titleStmt>
        <title>{title}</title>
        <respStmt><resp>annotation by</resp><name>Bochum</name></respStmt>
      </titleStmt>
      <sourceDesc>
        <msDesc>
          <msIdentifier>
            <repository>Wien, Österr. Nationalbibl.</repository>
            <idno>Cod. 160</idno>
          </msIdentifier>
        </msDesc>
      </sourceDesc>
    </fileDesc>
    <encodingDesc><p>Primary line breaks: {scheme}</p></encodingDesc>
    <profileDesc>
      <creation><persName>{author}</persName></creation>
      <langUsage>
        <language ident=""gmh"">mhd</language>
        <language ident=""gmh"">bairisch</language>
      </langUsage>
      <textClass><keywords><term type=""text-type"">Spruchdichtung</term></keywords></textClass>
    </profileDesc>
  </teiHeader>
  <text><body><ab>{body}</ab></body></text>
</TEI>";

        return ReMTextLoader.Parse(XDocument.Parse(xml), "fallback");
    }

    /// <summary>
    /// <b>The bug this loader nearly shipped with.</b> ReM uses &lt;q&gt; as a
    /// container spanning many words, not as markup inside one - 458,891 tokens
    /// sit inside a &lt;q&gt; and 7,672 inside a &lt;hi&gt;, which is 466,563 of
    /// the corpus's 2,293,068.
    ///
    /// A first version walked the direct children of &lt;ab&gt; and dropped
    /// every one of them. Nothing threw, no citation was missing and no line was
    /// absent - a fifth of the text simply was not in the lines that had any.
    /// It was found by counting words in against words out, not by reading the
    /// output, which looked perfectly good.
    /// </summary>
    [Fact]
    public void TokensInsideAQuotationAreRead()
    {
        var text = Load(
            @"<lb n=""1"" ed=""1""/>
              <w norm=""do"" lemma=""dô"">do</w>
              <q><w norm=""sprach"" lemma=""sprach"">ſprach</w>
                 <w norm=""er"" lemma=""er"">er</w></q>
              <w norm=""zuo"" lemma=""zuo"">zvo</w>
              <lb n=""2"" ed=""1""/>");

        Assert.Equal("dô sprach er zuo", Assert.Single(text.Lines).Normalised);
    }

    /// <summary>And the same for the other container ReM spans words with.</summary>
    [Fact]
    public void TokensInsideAHighlightAreRead()
    {
        var text = Load(
            @"<lb n=""1"" ed=""1""/>
              <w norm=""daz"" lemma=""daz"">daz</w>
              <hi><w norm=""buoch"" lemma=""buoch"">bvoch</w></hi>
              <lb n=""2"" ed=""1""/>");

        Assert.Equal("daz buoch", Assert.Single(text.Lines).Normalised);
    }

    /// <summary>
    /// Both readings of every line: the manuscript's own spelling, and the
    /// normalised one a printed edition would use. They become two editions of
    /// the work, which is the point of having ReM rather than an edition of it.
    /// </summary>
    [Fact]
    public void EveryLineIsReadTwice()
    {
        var line = Assert.Single(Load(
            @"<pb n=""100v"" ed=""1""/><lb n=""5"" ed=""1""/>
              <w norm=""welt"" lemma=""werelt"">welt</w>
              <w norm=""muozic"" lemma=""müezic"">muͦzic</w>
              <lb n=""6"" ed=""1""/>").Lines);

        Assert.Equal("welt muͦzic", line.Diplomatic);
        Assert.Equal("werelt müezic", line.Normalised);
        Assert.Equal("100v.5", line.CitationRef);
    }

    /// <summary>
    /// join says the manuscript wrote as one word what the reference splits in
    /// two, or the reverse. "dar" + "undir" is one word on the page.
    /// </summary>
    [Fact]
    public void JoinedTokensCloseUpWithNoSpace()
    {
        var line = Assert.Single(Load(
            @"<lb n=""1"" ed=""1""/>
              <w norm=""der"" lemma=""der"">der</w>
              <w norm=""dar"" lemma=""dâr"" join=""right"">dar</w>
              <w norm=""undir"" lemma=""under"" join=""left"">undir</w>
              <lb n=""2"" ed=""1""/>").Lines);

        Assert.Equal("der darundir", line.Diplomatic);
        Assert.Equal("der dârunder", line.Normalised);
    }

    /// <summary>
    /// Punctuation attaches to the word before it rather than standing off, and
    /// keeps its mark in the normalised reading even though ReM gives it
    /// lemma="--" - an unpunctuated reading text where the manuscript is
    /// punctuated would be a worse reading, not a purer one.
    /// </summary>
    [Fact]
    public void PunctuationAttachesAndSurvivesNormalisation()
    {
        var line = Assert.Single(Load(
            @"<lb n=""1"" ed=""1""/>
              <w norm=""stet"" lemma=""stêt"" join=""right"">ſtet</w>
              <pc norm=""."" lemma=""--"" join=""left"">.</pc>
              <lb n=""2"" ed=""1""/>").Lines);

        Assert.Equal("ſtet.", line.Diplomatic);
        Assert.Equal("stêt.", line.Normalised);
    }

    /// <summary>
    /// What the scribe struck out is not read. The opposite of TeiParser's rule
    /// for printed editions, where &lt;del&gt; is an editor's athetesis and
    /// belongs in the text with a mark against it; here it is a manuscript
    /// correction, and Menota's loader makes the same choice. 881 tokens across
    /// the corpus are wholly deleted.
    /// </summary>
    [Fact]
    public void ScribalDeletionsAreNotRead()
    {
        var line = Assert.Single(Load(
            @"<lb n=""1"" ed=""1""/>
              <w norm=""daz"" lemma=""daz"">daz</w>
              <w norm=""wort"" lemma=""wort""><del>fal</del>wort</w>
              <lb n=""2"" ed=""1""/>").Lines);

        Assert.Equal("daz wort", line.Diplomatic);
    }

    /// <summary>
    /// What the editor supplied, and what is hard to make out, ARE read - both
    /// are text a reader should see.
    /// </summary>
    [Fact]
    public void SuppliedAndUnclearTextIsRead()
    {
        var line = Assert.Single(Load(
            @"<lb n=""1"" ed=""1""/>
              <w norm=""ginge"" lemma=""gienge""><supplied reason=""missing"">gin</supplied>g<unclear>e</unclear></w>
              <lb n=""2"" ed=""1""/>").Lines);

        Assert.Equal("ginge", line.Diplomatic);
    }

    /// <summary>
    /// A page numbered "0" is not a page. 163 texts are cited by an editor's
    /// line numbers and still carry one &lt;pb&gt; holding "0"; left in, every
    /// line of them would be cited "0.1", "0.2" against an edition that says
    /// 1, 2.
    /// </summary>
    [Fact]
    public void APlaceholderPageIsNotPartOfTheCitation()
    {
        var text = Load(
            @"<pb n=""0"" ed=""1""/><lb n=""1"" ed=""1""/>
              <w norm=""daz"" lemma=""daz"">daz</w>
              <lb n=""2"" ed=""1""/>
              <w norm=""wort"" lemma=""wort"">wort</w>
              <lb n=""3"" ed=""1""/>");

        Assert.Equal(new[] { "1", "2" }, text.Lines.Select(l => l.CitationRef));
    }

    /// <summary>
    /// The two numbering systems interleave, and mixing them produces lines that
    /// are neither. Only the primary one cuts a line.
    /// </summary>
    [Fact]
    public void TheSecondaryNumberingDoesNotCutLines()
    {
        var text = Load(
            @"<lb n=""1"" ed=""1""/>
              <w norm=""daz"" lemma=""daz"">daz</w>
              <lb n=""99"" ed=""2""/>
              <w norm=""wort"" lemma=""wort"">wort</w>
              <lb n=""2"" ed=""1""/>");

        var line = Assert.Single(text.Lines);
        Assert.Equal("1", line.CitationRef);
        Assert.Equal("daz wort", line.Normalised);
    }

    /// <summary>
    /// A text carrying only a secondary numbering is read by it rather than
    /// returned as one enormous line. An oddly cited text beats an unreadable
    /// one.
    /// </summary>
    [Fact]
    public void ATextWithNoPrimaryNumberingFallsBackToWhatItHas()
    {
        var text = Load(
            @"<lb n=""1"" ed=""2""/>
              <w norm=""daz"" lemma=""daz"">daz</w>
              <lb n=""2"" ed=""2""/>
              <w norm=""wort"" lemma=""wort"">wort</w>
              <lb n=""3"" ed=""2""/>");

        Assert.Equal(2, text.Lines.Count);
    }

    /// <summary>
    /// A line break with nothing before the next one is a blank line of the
    /// manuscript, not a passage. 355,623 primary markers across the corpus
    /// produce 355,454 lines.
    /// </summary>
    [Fact]
    public void BlankLinesAreNotPassages()
    {
        var text = Load(
            @"<lb n=""1"" ed=""1""/>
              <w norm=""daz"" lemma=""daz"">daz</w>
              <lb n=""2"" ed=""1""/>
              <lb n=""3"" ed=""1""/>
              <w norm=""wort"" lemma=""wort"">wort</w>
              <lb n=""4"" ed=""1""/>");

        Assert.Equal(new[] { "1", "3" }, text.Lines.Select(l => l.CitationRef));
    }

    /// <summary>
    /// The author is in profileDesc/creation. Reading titleStmt instead finds
    /// the five respStmt credits for the annotation team and no author at all -
    /// which is how a survey of this corpus concluded it names none, when 39 of
    /// the 406 texts do.
    /// </summary>
    [Fact]
    public void TheAuthorIsReadFromTheCreationStatement()
    {
        Assert.Equal("Priester Adelbrecht", Load("<lb n=\"1\" ed=\"1\"/>", author: "Priester Adelbrecht").Author);
    }

    /// <summary>
    /// "-" and "--" are what ReM writes where a field was not filled in. They
    /// are values, not absences, and a library that took them literally would
    /// fill up with authors called "-".
    /// </summary>
    [Theory]
    [InlineData("-")]
    [InlineData("--")]
    [InlineData("")]
    public void ThePlaceholderAuthorIsReadAsAnonymous(string author)
    {
        Assert.Null(Load("<lb n=\"1\" ed=\"1\"/>", author: author).Author);
    }

    /// <summary>
    /// The manuscript, which is what distinguishes two texts that are otherwise
    /// both called "Predigten".
    /// </summary>
    [Fact]
    public void TheManuscriptIsRead()
    {
        var text = Load("<lb n=\"1\" ed=\"1\"/>");

        Assert.Equal("Wien, Österr. Nationalbibl.", text.Repository);
        Assert.Equal("Cod. 160", text.Shelfmark);
        Assert.Equal("bairisch", text.Dialect);
        Assert.Equal("Spruchdichtung", text.Genre);
    }

    /// <summary>
    /// Each file says in prose what its primary numbering counts, and a reader
    /// looking at "100v.5" should be able to find out what it means. The
    /// "Primary line breaks:" label is stripped; the answer is kept.
    /// </summary>
    [Theory]
    [InlineData("Hs.: Blatt (r/v), Zeile", "Hs.: Blatt (r/v), Zeile")]
    [InlineData("Edition", "Edition")]
    [InlineData("Handschrift", "Handschrift")]
    public void TheCitationSchemeIsReadFromTheFile(string declared, string expected)
    {
        Assert.Equal(expected, Load("<lb n=\"1\" ed=\"1\"/>", scheme: declared).CitationScheme);
    }
}
