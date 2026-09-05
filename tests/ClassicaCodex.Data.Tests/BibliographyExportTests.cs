using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Writing a passage of an ancient text out as BibTeX or RIS.
///
/// The application could already export the modern sources attached to a
/// research project, and had no way to export the thing it is actually for:
/// a passage found in the library. Anyone citing one was retyping it.
///
/// It goes out through the same writer those sources use, so there is one
/// implementation of each format rather than two that drift, and what is
/// written is what this application's own importer can read back.
///
/// The assertions worth having are the ones about restraint - no invented
/// publication year, and no link that does not resolve.
/// </summary>
public class BibliographyExportTests
{
    private static readonly PassageReference Aeneid = new(
        AuthorName: "Vergil",
        WorkTitle: "Aeneid",
        PassageRef: "6.851",
        EditionUrn: "urn:cts:latinLit:phi0690.phi003.perseus-lat1",
        Translator: null,
        CollectionName: "Ancient Latin (Perseus)",
        AuthorFloruit: "70 BCE-19 BCE");

    private static string Bib(PassageReference r) => BibliographyExport.ToBibTeX(new[] { r });
    private static string Ris(PassageReference r) => BibliographyExport.ToRis(new[] { r });

    // ---- the facts get across --------------------------------------------

    [Fact]
    public void BibTeXCarriesTheAuthorTitleAndLocator()
    {
        var bib = Bib(Aeneid);

        Assert.Contains("author = {Vergil}", bib);
        Assert.Contains("title = {Aeneid}", bib);
        Assert.Contains("pages = {6.851}", bib);
    }

    [Fact]
    public void RisCarriesTheSameFactsAndTerminates()
    {
        var ris = Ris(Aeneid);

        Assert.Contains("TY  - CHAP", ris);
        Assert.Contains("AU  - Vergil", ris);
        Assert.Contains("TI  - Aeneid", ris);
        Assert.Contains("SP  - 6.851", ris);
        Assert.Contains("ER  -", ris);
    }

    [Fact]
    public void TheCollectionIsRecordedSoTwoEditionsOfOneWorkAreTellableApart()
    {
        var csel = Aeneid with { CollectionName = "Latin Church Fathers (CSEL)" };
        var migne = Aeneid with { CollectionName = "Patrologia Latina (Migne)" };

        Assert.Contains("CSEL", Bib(csel));
        Assert.Contains("Migne", Bib(migne));
    }

    [Fact]
    public void ATranslatorIsNamed() =>
        Assert.Contains("trans. Samuel Butler", Bib(Aeneid with { Translator = "Samuel Butler" }));

    // ---- the two rules about not inventing anything -----------------------

    /// <summary>
    /// A reference manager wants a year and an ancient work does not have
    /// one. The floruit goes in the note, where it reads as what it is.
    /// </summary>
    [Fact]
    public void NoPublicationYearIsInvented()
    {
        var bib = Bib(Aeneid);

        Assert.DoesNotContain("year = {", bib);
        Assert.Contains("70 BCE-19 BCE", bib);
    }

    [Fact]
    public void ACtsUrnGetsALinkThatResolves() =>
        Assert.Contains("https://scaife.perseus.org/reader/urn:cts:latinLit:phi0690.phi003.perseus-lat1",
            Bib(Aeneid));

    /// <summary>
    /// Menota and the Renaissance collection are not CTS and do not resolve
    /// at Scaife. A link that goes nowhere is worse than none, because it
    /// looks like one.
    /// </summary>
    [Theory]
    [InlineData("urn:menota:anonymous:eddic-poems:gks-2365-4to")]
    [InlineData("engLit:renaissance:sidney:sidney:opensource")]
    [InlineData(null)]
    public void ANonResolvingIdentifierGetsNoLink(string? urn)
    {
        var reference = Aeneid with { EditionUrn = urn };

        Assert.Null(reference.ResolvableUrl());
        Assert.DoesNotContain("scaife", Bib(reference));
    }

    /// <summary>But it is still written down, because it is the durable identity.</summary>
    [Fact]
    public void ANonResolvingIdentifierIsStillRecorded() =>
        Assert.Contains("urn:menota:anonymous:eddic-poems:gks-2365-4to",
            Bib(Aeneid with { EditionUrn = "urn:menota:anonymous:eddic-poems:gks-2365-4to" }));

    // ---- cite keys --------------------------------------------------------

    [Fact]
    public void TheCiteKeyNamesTheAuthorWorkAndPassage() =>
        Assert.Equal("Vergil:Aeneid:6.851", Aeneid.CiteKey());

    /// <summary>
    /// A locator without its dots is a different locator, so those survive
    /// where the rest of the punctuation does not.
    /// </summary>
    [Fact]
    public void SpacesBecomeHyphensAndTheDotsInALocatorSurvive() =>
        Assert.Equal("Julius-Caesar:Gallic-War:1.1.1",
            new PassageReference("Julius Caesar", "Gallic War", "1.1.1").CiteKey());

    [Fact]
    public void AKeyIsNeverEmpty() =>
        Assert.Equal("ClassicaCodex", new PassageReference("", "", "").CiteKey());

    /// <summary>
    /// A Greek title sanitizes to nothing usable, and the author still has to
    /// carry the key.
    /// </summary>
    [Fact]
    public void AGreekTitleDoesNotProduceAnEmptyKey()
    {
        var key = new PassageReference("Julian", "Κατὰ Γαλιλαίων", "1.1").CiteKey();

        Assert.StartsWith("Julian", key);
        Assert.DoesNotContain(" ", key);
    }

    // ---- several at once, and the round trip ------------------------------

    [Fact]
    public void SeveralPassagesMakeOneFile()
    {
        var many = new[] { Aeneid, Aeneid with { PassageRef = "1.1" } };

        Assert.Equal(2, BibliographyExport.ToBibTeX(many).Split('@').Length - 1);
        Assert.Equal(2, BibliographyExport.ToRis(many).Split("ER  -").Length - 1);
    }

    /// <summary>
    /// Two passages of the same work must not collide on one cite key, or a
    /// .bib file carrying both is invalid.
    /// </summary>
    [Fact]
    public void TwoPassagesOfOneWorkGetDistinctKeys()
    {
        var bib = BibliographyExport.ToBibTeX(new[] { Aeneid, Aeneid with { PassageRef = "1.1" } });

        Assert.Contains("Vergil:Aeneid:6.851", bib);
        Assert.Contains("Vergil:Aeneid:1.1", bib);
    }

    /// <summary>
    /// What is written has to be readable by the parser on the other side of
    /// this same application.
    /// </summary>
    [Fact]
    public void WhatIsWrittenCanBeReadBackByTheImporter()
    {
        var parsedBib = BibliographyImport.Parse(Bib(Aeneid), "out.bib");
        var parsedRis = BibliographyImport.Parse(Ris(Aeneid), "out.ris");

        Assert.Single(parsedBib);
        Assert.Single(parsedRis);
        Assert.Contains("Aeneid", parsedBib[0].Title);
        Assert.Contains("Aeneid", parsedRis[0].Title);
        Assert.Contains("Vergil", string.Join(" ", parsedBib[0].Authors));
    }

    /// <summary>
    /// Nothing here may throw on a passage the library could genuinely hold -
    /// an anonymous work, an untitled fragment, a text with no edition URN.
    /// </summary>
    [Fact]
    public void ASparsePassageStillProducesSomething()
    {
        var bare = new PassageReference("Anonymous", "Fragmenta");

        Assert.Contains("Fragmenta", Bib(bare));
        Assert.Contains("Fragmenta", Ris(bare));
    }
}
