using System.Xml.Linq;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Two ways Holm perg 4 fol came out wrong, both from the same encoding
/// habit: it transcribes at the diplomatic level and leaves the other two
/// levels as empty placeholders - &lt;me:facs/&gt;&lt;me:norm/&gt; - on nearly
/// every word.
///
/// Counted as readings, those placeholders made ChooseReadingLevel see 100%
/// normalised coverage and pick "norm"; the level fallbacks then stopped at
/// the empty element rather than falling through, because "" is not null; and
/// WordText returned nothing for the 106,989 words whose &lt;me:norm/&gt; was
/// empty. The manuscript ingested 44,697 characters of the 605,058 it
/// contains - 7.4% of itself - and the corpus report said its coverage was
/// complete.
///
/// The second is narrower. Both node walks are flat Descendants() passes, so a
/// &lt;pc&gt; sitting inside a &lt;w&gt; is visited twice: once as part of the
/// word's reading and again on its own. Holm perg 4 fol writes its Roman
/// numerals that way and doubled 1,562 marks; AM 619 4to doubled 40.
///
/// Measured over the ten manuscripts, with the level chosen by
/// ChooseReadingLevel in each case:
///
///   Holm perg 4 fol   44,697 -> 605,058 chars   (norm -> dipl, +1,254%)
///   AM 132 Laxdæla   299,378 -> 303,100        (3,722 marks restored)
///   AM 132 Bandamanna 52,564 ->  52,758        (194 marks)
///   AM 619 4to       330,102 -> 330,062        (40 doubled marks removed)
///   AM 242 fol, Holm D 4                        (+14, +6)
///   AM 28 8vo, AM 36 fol, AM 63 fol, GKS 2365   unchanged
/// </summary>
public class MenotaLevelAndPunctuationTests
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";
    private static readonly XNamespace Me = "http://www.menota.org/ns/1.0";

    private static XElement Word(params object[] levels) => new(Tei + "w", levels);
    private static XElement L(string level, string? text = null) =>
        text == null ? new XElement(Me + level) : new XElement(Me + level, text);

    private static XDocument Doc(params object[] content) =>
        new(new XElement(Tei + "TEI",
            new XElement(Tei + "text", new XElement(Tei + "body", content))));

    // ------------------------------------------------ empty level elements

    /// <summary>
    /// A placeholder is not a reading. This is the whole of Holm perg 4 fol's
    /// encoding in one word.
    /// </summary>
    [Fact]
    public void AnEmptyLevelElementDoesNotCountAsCarryingThatLevel()
    {
        var word = Word(new XElement(Tei + "choice",
            L("dipl", "konungr"), L("facs"), L("norm")));

        Assert.Null(MenotaXmlLoader.Level(word, "norm"));
        Assert.Null(MenotaXmlLoader.Level(word, "facs"));
        Assert.Equal("konungr", MenotaXmlLoader.Level(word, "dipl"));
    }

    /// <summary>
    /// The consequence for the fallback chain. Asking for the normalised
    /// reading of a word that only has a diplomatic one has always meant
    /// reading it at the level it does carry - but an empty &lt;me:norm/&gt;
    /// used to satisfy the first link and return nothing.
    /// </summary>
    [Fact]
    public void AskingForAMissingLevelFallsThroughPastAnEmptyPlaceholder()
    {
        var body = new XElement(Tei + "p",
            Word(new XElement(Tei + "choice", L("dipl", "konungr"), L("norm"))),
            Word(new XElement(Tei + "choice", L("dipl", "hefir"), L("norm"))));

        Assert.Equal("konungr hefir", MenotaXmlLoader.WordsText(body, "norm"));
    }

    /// <summary>
    /// The reading level a manuscript is ingested at. Nine words with only a
    /// diplomatic reading and an empty normalised placeholder, one with both:
    /// counting placeholders gives 100% normalised coverage and picks the
    /// level that yields one word in ten.
    /// </summary>
    [Fact]
    public void PlaceholderLevelsDoNotDecideTheReadingLevel()
    {
        var words = new List<object>();
        for (var i = 0; i < 9; i++)
            words.Add(Word(new XElement(Tei + "choice", L("dipl", $"ord{i}"), L("norm"))));
        words.Add(Word(new XElement(Tei + "choice", L("dipl", "ord9"), L("norm", "orð9"))));

        var level = MenotaXmlLoader.ChooseReadingLevel(
            Doc(new XElement(Tei + "p", words)), out var coverage, out var missing);

        Assert.Equal("dipl", level);
        Assert.Equal(1.0, coverage);
        Assert.Equal(0, missing);
    }

    /// <summary>
    /// A genuinely normalised manuscript still chooses norm. The placeholder
    /// rule must not cost the corpus the level Delta actually wants.
    /// </summary>
    [Fact]
    public void ANormalisedManuscriptStillChoosesNorm()
    {
        var words = new List<object>();
        for (var i = 0; i < 10; i++)
            words.Add(Word(L("dipl", $"ord{i}"), L("norm", $"orð{i}")));

        var level = MenotaXmlLoader.ChooseReadingLevel(
            Doc(new XElement(Tei + "p", words)), out _, out _);

        Assert.Equal("norm", level);
    }

    /// <summary>
    /// Punctuation carries the levels too, and the same placeholder appears on
    /// it. 3,722 marks in Laxdæla saga alone were dropped from the normalised
    /// reading this way, so the saga read without sentence punctuation while
    /// the diplomatic reading of the same manuscript had it.
    /// </summary>
    [Fact]
    public void PunctuationWithAnEmptyNormalisedLevelIsStillRead()
    {
        var line = new XElement(Tei + "p",
            Word(L("dipl", "hefir"), L("norm", "hefir")),
            new XElement(Tei + "pc", L("facs", "."), L("dipl", "."), L("norm")));

        Assert.Equal("hefir.", MenotaIngestService.LineText(line, "norm", false));
    }

    // -------------------------------------------- punctuation inside a word

    /// <summary>
    /// Holm perg 4 fol's Roman numerals, verbatim: the periods belong to the
    /// diplomatic reading of the word and the flat walk reached them twice, so
    /// the line stored ".íí." followed by ".." - 1,562 of them in that
    /// manuscript and 40 in AM 619 4to.
    /// </summary>
    [Fact]
    public void PunctuationInsideAWordIsNotEmittedTwice()
    {
        var numeral = Word(new XElement(Tei + "choice",
            new XElement(Me + "dipl",
                new XElement(Tei + "pc", "."), "íí", new XElement(Tei + "pc", ".")),
            L("facs"), L("norm")));

        Assert.Equal(".íí.", MenotaIngestService.LineText(
            new XElement(Tei + "p", numeral), "dipl", false));
    }

    /// <summary>
    /// Punctuation between words is untouched by that guard - it is the case
    /// the branch exists for.
    /// </summary>
    [Fact]
    public void PunctuationBetweenWordsStillJoinsThePrecedingWord()
    {
        var line = new XElement(Tei + "p",
            Word(L("dipl", "konungr")),
            new XElement(Tei + "pc", L("dipl", ".")),
            Word(L("dipl", "hefir")));

        Assert.Equal("konungr. hefir", MenotaIngestService.LineText(line, "dipl", false));
    }

    /// <summary>
    /// Codex Runicus separates words with a two-dot mark rather than
    /// punctuating with it, at 0.997 marks per word, and those are dropped.
    /// The density that decides this now counts only the marks that would
    /// actually be emitted, so it is measured over the same population it
    /// governs. No manuscript in the corpus is near the threshold either way -
    /// Holm perg 4 fol moves from 0.101 to 0.088.
    /// </summary>
    [Fact]
    public void WordDividerDensityIgnoresMarksInsideWords()
    {
        // Two words, each carrying two marks of its own: 4 marks, 2 words,
        // a ratio of 2.0 if the nested marks are counted and 0 if they are not.
        var doc = Doc(new XElement(Tei + "p",
            Word(new XElement(Me + "dipl",
                new XElement(Tei + "pc", "."), "íí", new XElement(Tei + "pc", "."))),
            Word(new XElement(Me + "dipl",
                new XElement(Tei + "pc", "."), "ííí", new XElement(Tei + "pc", ".")))));

        Assert.False(MenotaIngestService.PunctuationIsWordDivider(doc));
    }

    /// <summary>
    /// And a real word divider is still recognised.
    /// </summary>
    [Fact]
    public void RealWordDividersAreStillDetected()
    {
        var content = new List<object>();
        for (var i = 0; i < 10; i++)
        {
            content.Add(Word(L("dipl", $"ord{i}")));
            content.Add(new XElement(Tei + "pc", new XAttribute("type", "runic"), L("dipl", ":")));
        }

        Assert.True(MenotaIngestService.PunctuationIsWordDivider(
            Doc(new XElement(Tei + "p", content))));
    }
}
