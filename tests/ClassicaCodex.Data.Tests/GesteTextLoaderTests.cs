using System.Xml.Linq;
using ClassicaCodex.Ingestion.Geste;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading Geste, the corpus of Old French chansons de geste.
///
/// Verse TEI, closer to something the shared TeiParser could read than ReM was -
/// it has real &lt;l&gt; elements - but not close enough. Every figure below was
/// measured against the pinned corpus (34 files, commit 71737a2), because the
/// corpus's published description differs from its files in four places, each of
/// which would have produced damage that looked like success.
/// </summary>
public class GesteTextLoaderTests
{
    private const string Tei = "http://www.tei-c.org/ns/1.0";

    private static GesteText Load(string body, string title = "Otinel", string language = "fro",
        string stem = "transcr_Otin_B")
    {
        var xml = $@"<TEI xmlns=""{Tei}"">
  <teiHeader>
    <fileDesc>
      <titleStmt><title>{title}</title><author>Paul Meyer</author></titleStmt>
    </fileDesc>
    <profileDesc><langUsage><language ident=""{language}"">francien</language></langUsage></profileDesc>
  </teiHeader>
  <text><body>{body}</body></text>
</TEI>";

        return GesteTextLoader.Parse(XDocument.Parse(xml), stem);
    }

    /// <summary>
    /// The two readings: the manuscript letter for letter, and the editor's.
    /// &lt;choice&gt;&lt;orig&gt;/&lt;reg&gt; carries allographs and
    /// &lt;abbr&gt;/&lt;expan&gt; carries abbreviations, and both belong on the
    /// same axis - what the scribe wrote against what it means.
    /// </summary>
    [Fact]
    public void BothReadingsOfALineAreProduced()
    {
        var line = Assert.Single(Load(
            @"<lg><l n=""1"">
                <w><choice><orig>KJ</orig><reg>ki</reg></choice></w>
                <w><choice><orig>uolt</orig><reg>volt</reg></choice></w>
                <w>ch<choice><abbr>ãcũ</abbr><expan>ancun</expan></choice></w>
              </l></lg>").Lines);

        Assert.Equal("KJ uolt chãcũ", line.Diplomatic);
        Assert.Equal("ki volt chancun", line.Normalised);
        Assert.Equal("1", line.CitationRef);
    }

    /// <summary>
    /// <b>sic/corr is not a manuscript reading and must not become one.</b> It is
    /// an editor correcting an error in the printed source they transcribed, so
    /// there is no sense in which the sic is "what the manuscript has". Measured:
    /// ed_FloovG carries 110 corr/sic pairs and no orig/reg at all, while
    /// transcr_Otin_B carries 12,154 orig/reg - the two kinds of markup belong to
    /// the editions and the transcriptions respectively. Presenting a sic as a
    /// diplomatic reading would invent a variant.
    /// </summary>
    [Fact]
    public void AnEditorsCorrectionIsTakenInBothReadings()
    {
        var line = Assert.Single(Load(
            @"<lg><l n=""1"">
                <w>Dés</w>
                <w><choice><sic>vso</sic><corr>vos</corr></choice></w>
              </l></lg>").Lines);

        Assert.Equal("Dés vos", line.Diplomatic);
        Assert.Equal("Dés vos", line.Normalised);
    }

    /// <summary>
    /// A fifth of ReM's corpus hid inside span containers and cost a day to
    /// find. Here it is 45%: 9,816 of 21,624 tokens in ed_FloovG and 18,989 of
    /// 37,774 in ed_GuiBourgG sit inside persName, forename, num, hi or choice.
    /// A loader reading only direct children produces text that reads perfectly
    /// and is missing half of itself.
    /// </summary>
    [Fact]
    public void TokensNestedInsideOtherElementsAreRead()
    {
        var line = Assert.Single(Load(
            @"<lg><l n=""1"">
                <w>Dou</w>
                <persName><w>premier</w> <w>roi</w></persName>
                <hi rend=""i""><w>de</w></hi>
                <num><w>.III.</w></num>
              </l></lg>").Lines);

        Assert.Equal("Dou premier roi de .III.", line.Diplomatic);
    }

    /// <summary>
    /// 17 of the 34 files carry no @n on any &lt;l&gt; - the fallback is the
    /// common case, not the exception, which is the opposite of what the corpus's
    /// own description suggests.
    /// </summary>
    [Fact]
    public void ALineWithNoNumberIsCitedByItsPosition()
    {
        var text = Load(
            @"<lg><l><w>premier</w></l><l><w>second</w></l></lg>");

        Assert.Equal(new[] { "1", "2" }, text.Lines.Select(l => l.CitationRef));
    }

    /// <summary>And the folio joins it where the file gives one.</summary>
    [Fact]
    public void AFolioPrefixesAnUnnumberedLine()
    {
        var text = Load(@"<pb n=""93v""/><lg><l><w>premier</w></l></lg>");

        Assert.Equal("93v.1", Assert.Single(text.Lines).CitationRef);
    }

    /// <summary>
    /// The annotation, which is the reason this corpus is worth having: a real
    /// Old French headword with Tobler-Lommatzsch homograph numbering, plus
    /// CATTEX2009 part of speech and morphology.
    /// </summary>
    [Fact]
    public void TheAnnotationIsReadFromEachWord()
    {
        var word = Assert.Single(Load(
            @"<lg><l n=""1""><w lemma=""estre1"" pos=""VERcjg"" msd=""MODE=ind|PERS.=3"">ſ̃t</w></l></lg>")
            .Lines).Words.Single();

        Assert.Equal("ſ̃t", word.Form);
        Assert.Equal("estre1", word.Headword);
        Assert.Equal("VERcjg MODE=ind|PERS.=3", word.Tag);
    }

    /// <summary>
    /// <b>Empty is not absent.</b> ed_OtinG_pos.xml carries lemma="" on all
    /// 15,908 of its tokens and no @pos or @msd at all. A non-null test would
    /// make the empty string the commonest headword in Old French, with 15,908
    /// attested forms behind it in Word Study.
    /// </summary>
    [Fact]
    public void AnEmptyLemmaIsNotAHeadword()
    {
        var line = Assert.Single(Load(
            @"<lg><l n=""1""><w lemma="""" type="""">KJ</w><w lemma=""voloir"">uolt</w></l></lg>").Lines);

        Assert.Equal("KJ uolt", line.Diplomatic);
        Assert.Equal("voloir", Assert.Single(line.Words).Headword);
    }

    /// <summary>
    /// A text with no &lt;w&gt; at all is still readable. ed_HuonG.xml is 10,495
    /// verse lines and zero tokens, and ed_CroisBaudriM2.xml is 145 lines with 37
    /// - a loader that only assembled lines out of &lt;w&gt; would install them
    /// empty.
    /// </summary>
    [Fact]
    public void AnUntokenisedTextIsStillRead()
    {
        var line = Assert.Single(Load(
            @"<lg><l n=""1"">Or oiez seignor que Dés vos beneïe</l></lg>").Lines);

        Assert.Equal("Or oiez seignor que Dés vos beneïe", line.Diplomatic);
        Assert.Empty(line.Words);
    }

    /// <summary>
    /// Anglo-Norman and Walloon are separate ISO 639-3 languages, not dialects of
    /// French as far as a language column goes - a search scoped to Old French
    /// should not silently return Anglo-Norman. Measured: fro on 27 files, xno on
    /// 7, wln on 5. The regional qualifier is dropped to its base code, because
    /// "fro-lorrain" is not a code anything else in the library understands.
    /// </summary>
    [Theory]
    [InlineData("fro", "fro")]
    [InlineData("xno", "xno")]
    [InlineData("wln", "wln")]
    [InlineData("fro-lorrain", "fro")]
    public void TheLanguageIsReadPerFile(string declared, string expected) =>
        Assert.Equal(expected, Load(@"<lg><l n=""1""><w>a</w></l></lg>", language: declared).Language);

    /// <summary>
    /// <b>No author is read, and that is the decision.</b> titleStmt/author holds
    /// four values across the 34 files: "Anonyme" three times, and "Batova
    /// Ekaterina" once - the modern encoder of that file, not the author of
    /// Girart de Vienne. Elsewhere the same element carries Paul Meyer, Giulio
    /// Bertoni, R. Menéndez Pidal and A. Scheler, the editors of the printed
    /// sources. Filing Paul Meyer as the author of a chanson de geste is wrong in
    /// a way a reader of this library would see at once.
    /// </summary>
    [Fact]
    public void TheAuthorFieldIsNeverBelieved() =>
        Assert.Null(Load(@"<lg><l n=""1""><w>a</w></l></lg>").Author);

    /// <summary>
    /// The witness, from the filename, because ten files are titled "Garin le
    /// Lorrain" and five "Otinel" and nothing else tells them apart in a library
    /// tree. ReM's titles carried it - "Rolandslied (P)" - and Geste's do not.
    /// </summary>
    [Theory]
    [InlineData("transcr_Otin_B", "B")]
    [InlineData("transcr_Asprem_P4", "P4")]
    [InlineData("ed_FloovG", "G")]
    [InlineData("ed_GarLorrMe1a", "Me1a")]
    [InlineData("ed_OtinG_pos", "G")]
    public void TheWitnessComesFromTheFilename(string stem, string expected) =>
        Assert.Equal(expected, GesteTextLoader.WitnessFrom(stem));
}
