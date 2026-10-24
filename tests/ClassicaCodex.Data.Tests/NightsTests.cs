using ClassicaCodex.Ingestion.Nights;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading Burton's Nights out of Project Gutenberg's HTML. The samples are
/// cut from the real volumes: the boilerplate header, the title page and
/// contents, footnote references and footnotes, page numbers, a verse
/// linegroup, the night markers in each of the ways Burton's transcription
/// writes them, and the index that ends a volume.
/// </summary>
public class NightsTests
{
    private const string Volume1 = """
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>Vol 1</title></head>
        <body>
        <header class="pg-boilerplate pgheader"><h2>The Project Gutenberg eBook of the Nights</h2></header>
        <div><h1>THE BOOK OF THE Thousand Nights and a Night</h1></div>
        <h2>THE TRANSLATOR'S FOREWORD.</h2>
        <p>This work, laborious as it may appear, has been to me a labour of love.</p>
        <h2>CONTENTS OF THE FIRST VOLUME.</h2>
        <table><tr><td>TALE OF THE TRADER AND THE JINNI</td></tr></table>
        <h2>THE BOOK OF THE THOUSAND NIGHTS AND A NIGHT.</h2>
        <p>In tide of yore there was a King.&nbsp;He had two sons.</p>
        <h3>TALE OF THE BULL<a class="pginternal" href="#f23"><sup>[23]</sup></a> AND THE ASS.</h3>
        <p>There was a merchant who owned much money.</p>
        <div class="figcenter"><img alt="" src="images/i_001.jpg"></div>
        <div class="chapter"><h2>TALE OF THE TRADER AND THE JINNI.</h2></div>
        <p>It is related, O auspicious King, that lo! an Ifrit appeared, huge of stature<a class="c015 pginternal" href="#f40" id="r40"><sup>[40]</sup></a> and brandishing a drawn sword.</p>
        <div class="footnote" id="f40"><p><span class="label">40.</span> Travellers tell of a knack of jerking the date-stone.</p></div>
        <div class="lg-container-b"><div class="linegroup"><div class="group">
          <div class="line">Containeth Time a twain of days ✿ this of blessing that of bane</div>
          <div class="line">And holdeth Life a twain of halves ✿ this of pleasure that of pain.</div>
        </div></div></div>
        <div class="nf-center-c1"><div class="nf-center c021"><div>Now when it was the Second Night,</div></div></div>
        <p>said Dunyazad to her sister <span class="pageno" id="Page_30">30</span>Shahrazad, "Finish for us that story."</p>
        <div class="nf-center-c1"><div class="nf-center"><div>Now when it as the Third Night.</div></div></div>
        <h2>THE FISHERMAN AND THE JINNI.</h2>
        <p>It hath reached me that there was a Fisherman well stricken in years.</p>
        <h2>INDEX.</h2>
        <p>Abraham, 12</p>
        </body></html>
        """;

    private static List<(string Citation, string Text, bool IsHead)> Read() => NightsLoader.Main(new[] { Volume1 }, lastNight: 3);

    private static Dictionary<string, string> Passages() => Read().ToDictionary(p => p.Citation, p => p.Text);

    [Fact]
    public void TheFrameIsNightZeroAndTheFirstTaleOpensTheFirstNight()
    {
        var passages = Read();

        Assert.Equal(
            new[] { "0.t1", "0.1", "0.t2", "0.2", "1.t1", "1.1", "1.2", "2.head", "2.1", "3.head", "3.t1", "3.1" },
            passages.Select(p => p.Citation));
        Assert.Equal("TALE OF THE TRADER AND THE JINNI.", passages[4].Text);
        Assert.True(passages[4].IsHead);
    }

    [Fact]
    public void FootnotesPageNumbersAndTheApparatusAreLeftOut()
    {
        var passages = Passages();

        Assert.Equal("It is related, O auspicious King, that lo! an Ifrit appeared, huge of stature and brandishing a drawn sword.", passages["1.1"]);
        Assert.Equal("said Dunyazad to her sister Shahrazad, \"Finish for us that story.\"", passages["2.1"]);
        Assert.Equal("TALE OF THE BULL AND THE ASS.", passages["0.t2"]);
        Assert.DoesNotContain(passages.Values, t => t.Contains("Travellers tell") || t.Contains("labour of love") || t.Contains("Abraham, 12"));
    }

    [Fact]
    public void VerseIsALineToEachSlash() =>
        Assert.Equal(
            "Containeth Time a twain of days ✿ this of blessing that of bane / And holdeth Life a twain of halves ✿ this of pleasure that of pain.",
            Passages()["1.2"]);

    [Fact]
    public void ANightOutOfOrderIsRefused() =>
        Assert.Throws<InvalidDataException>(() =>
            NightsLoader.Main(new[] { Volume1.Replace("the Second Night", "the Fourth Night") }, lastNight: 3));

    [Theory]
    [InlineData("Second", 2)]
    [InlineData("Hundred and Twenty-fourth", 124)]
    [InlineData("Two Hundred and Thirty-Seventh", 237)]
    [InlineData("Five-and-Fiftieth", 55)]
    [InlineData("full Sixtieth", 60)]
    [InlineData("HUNDRED AND FIFTIETH", 150)]
    [InlineData("Thousand and First", 1001)]
    public void BurtonsOrdinalsAreRead(string words, int number) =>
        Assert.Equal(number, NightsLoader.Ordinal(words));

    private const string Supplemental = """
        <html><body>
        <h2>CONTENTS OF THE THIRD VOLUME.</h2>
        <h2>THE TALE OF ZAYN AL-ASNAM.</h2>
        <div><div>Now when it was the Four Hundred and Ninety-seventh Night,</div></div>
        <p>She resumed, It hath reached me that there was a Sultan.</p>
        <h2>ALAEDDIN; OR, THE WONDERFUL LAMP.</h2>
        <p>Then the Slave of the Lamp appeared and said, "Here am I."</p>
        <h3>ALADDIN (ENGLISH TRANSLATION OF GALLAND.)</h3>
        <p>In the capital of one of the richest provinces of China.</p>
        <h2>Appendix. VARIANTS AND ANALOGUES.</h2>
        <p>Clouston's note.</p>
        </body></html>
        """;

    [Fact]
    public void TheSupplementalTalesAreCitedByTale()
    {
        var passages = NightsLoader.Supplemental(new[] { Supplemental });

        Assert.Equal(new[] { "1.head", "1.t1", "1.1", "2.head", "2.1", "2.t1", "2.2" }, passages.Select(p => p.Citation));
        Assert.Equal("ALAEDDIN; OR, THE WONDERFUL LAMP.", passages[3].Text);
        Assert.DoesNotContain(passages, p => p.Text.Contains("Clouston"));
    }
}
