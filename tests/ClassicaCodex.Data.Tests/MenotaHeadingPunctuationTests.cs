using System.Xml.Linq;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Headings read unpunctuated while the lines beneath them did not.
///
/// WordsText collected &lt;w&gt; descendants and joined them with spaces, which
/// is right for the callers that want words and wrong for the one that
/// produces a heading a reader sees. 1,191 free-standing &lt;pc&gt; sit inside
/// headings across the ten manuscripts - 628 in AM 36 fol, 315 in Holm perg 4
/// fol, 152 in AM 63 fol, 90 in AM 619 4to - and every one was dropped.
///
/// The two guards LineText needs apply here too, for the same reasons: a mark
/// inside a &lt;w&gt; belongs to that word's reading and would double (Holm
/// perg has 28 of those in headings alone), and a manuscript whose marks are
/// word dividers rather than punctuation must not have them appended at all.
/// </summary>
public class MenotaHeadingPunctuationTests
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";
    private static readonly XNamespace Me = "http://www.menota.org/ns/1.0";

    private static XElement W(string dipl) => new(Tei + "w", new XElement(Me + "dipl", dipl));
    private static XElement Pc(string mark) => new(Tei + "pc", new XElement(Me + "dipl", mark));

    /// <summary>
    /// AM 36 fol's chapter headings, which carry a mark after almost every
    /// word: "XXXIX . Cap . Ferd Sveins Jarls ." in the source.
    /// </summary>
    [Fact]
    public void AHeadingKeepsItsPunctuation()
    {
        var head = new XElement(Tei + "head",
            W("Ferd"), W("Sveins"), W("Jarls"), Pc("."));

        Assert.Equal("Ferd Sveins Jarls.",
            MenotaXmlLoader.WordsText(head, "dipl", includePunctuation: true));
    }

    /// <summary>
    /// The mark lands after the word it follows, not at the end - which is why
    /// this walks in document order rather than taking the words and then the
    /// marks.
    /// </summary>
    [Fact]
    public void MarksLandWhereTheySitRatherThanAtTheEnd()
    {
        var head = new XElement(Tei + "head",
            W("Sætt"), Pc(":"), W("Vilcinus"), W("konongs"), Pc("."));

        Assert.Equal("Sætt: Vilcinus konongs.",
            MenotaXmlLoader.WordsText(head, "dipl", includePunctuation: true));
    }

    /// <summary>
    /// A mark inside a word was already taken with the word's reading. Holm
    /// perg 4 fol writes its Roman numerals that way.
    /// </summary>
    [Fact]
    public void AMarkInsideAWordIsNotAppendedAgain()
    {
        var head = new XElement(Tei + "head",
            new XElement(Tei + "w",
                new XElement(Me + "dipl", Pc("."), "íí", Pc("."))),
            W("Cap"));

        Assert.Equal(".íí. Cap",
            MenotaXmlLoader.WordsText(head, "dipl", includePunctuation: true));
    }

    /// <summary>
    /// AM 28 8vo separates its words with a two-dot mark at 0.997 marks per
    /// word and has 5 of them in its 2 headings. Taken as punctuation they
    /// render "om: konæ: iordh:", so the caller passes false for that
    /// manuscript. See MenotaIngestService.PunctuationIsWordDivider.
    /// </summary>
    [Fact]
    public void WordDividersAreNotAppendedToAHeading()
    {
        var head = new XElement(Tei + "head",
            W("om"), Pc(":"), W("konæ"), Pc(":"), W("iordh"), Pc(":"));

        Assert.Equal("om konæ iordh",
            MenotaXmlLoader.WordsText(head, "dipl", includePunctuation: false));
    }

    /// <summary>
    /// The default is off, so the plan's titles and MenotaPlanForm's preview
    /// are untouched. They are matched against catalogue entries through a
    /// Normalise that keeps only letters and digits - punctuation could not
    /// help them - and they are written into .plan.json files that already
    /// exist, where changing the string would change titles a user has
    /// reviewed.
    /// </summary>
    [Fact]
    public void PunctuationIsOffByDefault()
    {
        var head = new XElement(Tei + "head", W("Ferd"), W("Sveins"), Pc("."));

        Assert.Equal("Ferd Sveins", MenotaXmlLoader.WordsText(head, "dipl"));
    }

    /// <summary>
    /// Callers that pass a &lt;w&gt; as the container - AddApparatus, NoteText -
    /// read the same either way, since every mark inside a word is nested by
    /// definition and skipped by the guard above.
    /// </summary>
    [Fact]
    public void AWordAsContainerReadsTheSameEitherWay()
    {
        var word = new XElement(Tei + "w",
            new XElement(Me + "dipl", "konungr", Pc(".")));

        Assert.Equal(
            MenotaXmlLoader.WordsText(word, "dipl"),
            MenotaXmlLoader.WordsText(word, "dipl", includePunctuation: true));
    }

    /// <summary>
    /// A heading with no word markup at all is the editor's, and still falls
    /// back to its plain text.
    /// </summary>
    [Fact]
    public void AnEditorialHeadingWithNoWordMarkupIsUnchanged()
    {
        var head = new XElement(Tei + "head", "Vpphaf sogunnar.");

        Assert.Equal("Vpphaf sogunnar.",
            MenotaXmlLoader.WordsText(head, "dipl", includePunctuation: true));
    }
}
