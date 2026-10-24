using ClassicaCodex.Ingestion.Goetia;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the 1904 Goetia out of Project Gutenberg's HTML of de Laurence's
/// 1916 reprint. The sample keeps the book's shapes: de Laurence's title page,
/// Crowley's Greek invocation before the first heading, headings printed
/// under the running titles, linked footnotes (one on a heading), a verse
/// group, the transcriber's note in the list of spirits, Paimon's label
/// without its stop, and Crowley's conjurations after the end of the first
/// book, which have no headings of their own.
/// </summary>
public class GoetiaTests
{
    private const string Html = """
        <!DOCTYPE html>
        <html><head><title>Goetia</title></head>
        <body>
        <div style='text-align:center'>*** START OF THE PROJECT GUTENBERG EBOOK THE LESSER KEY OF SOLOMON ***</div>
        <div class='figcenter id001'><img src='images/cover.jpg' alt='' class='ig001'></div>
        <div class='nf-center-c1'><div class='nf-center c002'><div>Copyright, 1916</div><div>de LAURENCE, SCOTT &amp; CO.</div></div></div>
        <p class='c004'>ΕΠΙΚΑΛΟΥΜΑΙ ΣΕ ΤΟΝ ΕΝ ΤΩ ΚΕΝΕΩ ΠΝΕΥΜΑΤΙ.</p>
        <div class='chapter'><a id='Page_3'></a><h2 class='c006'>Preface.</h2></div>
        <p class='c007'>This translation of the <span class='sc'>First Book</span> of the Rosicrucian
        Order.<a id='r1'></a><a href='#f1' class='c008'><sup>[1]</sup></a> The G. H. <i>Fra.</i> succumbed.</p>
        <div class='chapter'><h2 class='c006'>PRELIMINARY INVOCATION.</h2></div>
        <div class='lg-container-b c010'><div class='linegroup'><div class='group'>
          <div class='line'><i>Thee I invoke, the Bornless one.</i></div>
          <div class='line'><i>Thee, that didst create the Earth and the Heavens:</i></div>
        </div></div></div>
        <div class='chapter'><h2 class='c006'>Goetia <br> The Lesser Key Of Solomon <br> THE INITIATED INTERPRETATION OF CEREMONIAL MAGIC.</h2></div>
        <p class='c007'>It is loftily amusing to the student of <i>Magical</i> literature.</p>
        <p class='c005'>The spirits of the Goetia are portions of the human brain.<a id='r2'></a><a href='#f2' class='c008'><sup>[2]</sup></a></p>
        <div class='chapter'><h2 class='c006'>PRELIMINARY DEFINITION OF MAGIC. <br> <span class='sc'>Lemegeton Vel Clavicula Salomonis Regis.</span></h2></div>
        <p class='c007'>Magic is the Highest Knowledge of Natural Philosophy,<a id='r3'></a><a href='#f3' class='c008'><sup>[3]</sup></a> advanced in its works.</p>
        <div class='chapter'><h2 class='c006'>SHEMHAMPHORASH.</h2></div>
        <div class='tnote'><p class='c007'>[Transcriber’s Note: The references below to seals are to the images.]</p></div>
        <p class='c007'>(1.) <span class='sc'>Bael.</span>—The First Principal Spirit is a King ruling in the East, called Bael.</p>
        <p class='c005'>(2.) <span class='sc'>Agares.</span>—The Second Spirit is a Duke called Agreas, or Agares.<a id='r4'></a><a href='#f4' class='c008'><sup>[4]</sup></a></p>
        <div class='figcenter id001'><img src='images/i_024.jpg' alt='' class='ig002'></div>
        <p class='c005'>His Seal is this.</p>
        SPIRITS
        <p class='c005'><span class='sc'>These</span> be the 72 Mighty Kings and Princes which King
        <a id='Page_46'></a>Solomon Commanded into a Vessel of Brass.</p>
        <div class='chapter'><h2 class='c006'>OBSERVATIONS.</h2></div>
        <p class='c007'>First, thou shalt know and observe the Moon’s Age for thy working.</p>
        <h3 class='c016'>THE GREATER CURSE.<a id='r5'></a><a href='#f5' class='c008'><sup>[5]</sup></a></h3>
        <p class='c005'>(<span class='sc'>Here endeth this First Book of the Lemegeton, which is called the Goetia.</span>)</p>
        <div class='nf-center-c1'><div class='nf-center c002'>
            <div><a id='Page_71'></a><i>Ye Conjuratiouns of ye Booke Goetia in ye</i></div>
            <div><i>Lemegeton which Solomoun ye Kynge did give</i></div>
        </div></div>
        <p class='c007'><i>YE FYRSTE CONJOURATIOUN.</i></p>
        <p class='c007'>I invoke and move thee, O thou, Spirit N.</p>
        <div class='nf-center-c1'><div class='nf-center c002'><div><span class='large'>Footnotes</span></div></div></div>
        <div class='footnote' id='f1'><p class='c005'><span class='label'><a href='#r1'>1</a>.&#160;&#160;</span><i>Mr. A. E. Waite</i> writes.—<span class='sc'>Ed.</span></p></div>
        <div class='footnote' id='f2'><p class='c005'><span class='label'><a href='#r2'>2</a>.&#160;&#160;</span>Thought is a secretion of the brain.</p></div>
        <div class='footnote' id='f3'><p class='c005'><span class='label'><a href='#r3'>3</a>.&#160;&#160;</span>This Preliminary Definition of Magic is found in very few Codices.</p></div>
        <div class='footnote' id='f4'><p class='c005'><span class='label'><a href='#r4'>4</a>.&#160;&#160;</span>Or Agreas.</p></div>
        <div class='footnote' id='f5'><p class='c005'><span class='label'><a href='#r5'>5</a>.&#160;&#160;</span>Found in very few Codices.</p></div>
        <div class='tnotes'><ul class='ul_1 c002'><li>Transcriber’s Notes: Footnotes have been collected.</li></ul></div>
        <div style='text-align:center'>*** END OF THE PROJECT GUTENBERG EBOOK THE LESSER KEY OF SOLOMON ***</div>
        </body></html>
        """;

    /// <summary>The sample stops at spirit 2, where the real list goes on to 72; Paimon's label has no stop.</summary>
    private static string Book(int last = 72) => Html.Replace("SPIRITS", string.Concat(
        Enumerable.Range(3, last - 2).Select(n => n == 9
            ? "<p class='c005'>(9) <span class='sc'>Paimon.</span>—The Ninth Spirit is Paimon.</p>\n"
            : $"<p class='c005'>({n}.) <span class='sc'>Spirit.</span>—The spirit numbered {n}.</p>\n")));

    private static IReadOnlyList<GoetiaWork> Works() => GoetiaLoader.Parse(Book());

    private static Dictionary<string, string> Passages(string slug) =>
        Works().Single(w => w.Slug == slug).Passages.ToDictionary(p => p.Citation, p => p.Text);

    [Fact]
    public void TheBookIsFiveWorks()
    {
        Assert.Equal(
            new[] { "spirits", "art", "preface", "initiated-interpretation", "conjurations" },
            Works().Select(w => w.Slug));
        Assert.Equal(new[] { false, false, true, true, true }, Works().Select(w => w.ByCrowley));
    }

    [Fact]
    public void ASpiritIsCitedByItsNumberAndKeepsItsWholeEntry()
    {
        var spirits = Passages("spirits");

        Assert.Equal("(1.) Bael.—The First Principal Spirit is a King ruling in the East, called Bael.", spirits["1"]);
        Assert.Equal("(2.) Agares.—The Second Spirit is a Duke called Agreas, or Agares. His Seal is this.", spirits["2"]);
        Assert.StartsWith("(9) Paimon.", spirits["9"]);
        Assert.Equal("These be the 72 Mighty Kings and Princes which King Solomon Commanded into a Vessel of Brass.", spirits["end"]);
        Assert.DoesNotContain(spirits.Values, t => t.Contains("Transcriber"));
    }

    [Fact]
    public void FootnotesFollowThePassageOrHeadingTheirMarkerIsIn()
    {
        var spirits = Passages("spirits");
        Assert.Equal("Note: Or Agreas.", spirits["2.n1"]);

        var art = Passages("art");
        Assert.Equal("Magic is the Highest Knowledge of Natural Philosophy, advanced in its works.", art["1.1"]);
        Assert.Equal("Note: This Preliminary Definition of Magic is found in very few Codices.", art["1.1.n1"]);
        Assert.Equal("THE GREATER CURSE.", art["3.head"]);
        Assert.Equal("Note: Found in very few Codices.", art["3.head.n1"]);

        Assert.Equal("Note: Mr. A. E. Waite writes.—Ed.", Passages("preface")["2.1.n1"]);
    }

    [Fact]
    public void TheArtIsCitedBySectionAndParagraphWithoutTheRunningTitles()
    {
        var art = Passages("art");

        Assert.Equal("PRELIMINARY DEFINITION OF MAGIC. Lemegeton Vel Clavicula Salomonis Regis.", art["1.head"]);
        Assert.Equal("OBSERVATIONS.", art["2.head"]);
        Assert.StartsWith("(Here endeth this First Book", art["3.1"]);
    }

    [Fact]
    public void CrowleysGreekPrefaceAndInvocationOpenTheBook()
    {
        var preface = Passages("preface");

        Assert.Equal("ΕΠΙΚΑΛΟΥΜΑΙ ΣΕ ΤΟΝ ΕΝ ΤΩ ΚΕΝΕΩ ΠΝΕΥΜΑΤΙ.", preface["1.1"]);
        Assert.False(preface.ContainsKey("1.head"));
        Assert.Equal("Preface.", preface["2.head"]);
        Assert.Equal("This translation of the First Book of the Rosicrucian Order. The G. H. Fra. succumbed.", preface["2.1"]);
        Assert.Equal("Thee I invoke, the Bornless one. / Thee, that didst create the Earth and the Heavens:", preface["3.1"]);
        Assert.DoesNotContain(preface.Values, t => t.Contains("Copyright") || t.Contains("LAURENCE"));
    }

    [Fact]
    public void CrowleysEssayIsCitedByParagraph()
    {
        var essay = Passages("initiated-interpretation");

        Assert.Equal("The spirits of the Goetia are portions of the human brain.", essay["2"]);
        Assert.Equal("Note: Thought is a secretion of the brain.", essay["2.n1"]);
    }

    [Fact]
    public void CrowleysConjurationsTakeTheirHeadingsFromTheirCapitals()
    {
        var conjurations = Passages("conjurations");

        Assert.Equal("Ye Conjuratiouns of ye Booke Goetia in ye Lemegeton which Solomoun ye Kynge did give", conjurations["1.head"]);
        Assert.Equal("YE FYRSTE CONJOURATIOUN.", conjurations["2.head"]);
        Assert.Equal("I invoke and move thee, O thou, Spirit N.", conjurations["2.1"]);
        Assert.DoesNotContain(conjurations.Values, t => t.Contains("Footnotes") || t.Contains("Transcriber"));
    }

    [Fact]
    public void AListThatStopsShortIsRefused() =>
        Assert.Throws<InvalidDataException>(() => GoetiaLoader.Parse(Book(last: 40)));

    [Fact]
    public void AFootnoteWithNoMarkerIsRefused() =>
        Assert.Throws<InvalidDataException>(() => GoetiaLoader.Parse(Book().Replace("<a href='#f4' class='c008'><sup>[4]</sup></a>", "")));
}
