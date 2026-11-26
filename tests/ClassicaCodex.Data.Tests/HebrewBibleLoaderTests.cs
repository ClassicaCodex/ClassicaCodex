using System.Xml.Linq;
using ClassicaCodex.Core;
using ClassicaCodex.Ingestion.Hebrew;
using ClassicaCodex.Ingestion.Sefaria;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the Hebrew Bible out of the Open Scriptures OSIS files, and
/// putting the JPS translation of 1917 on the same verse numbers.
///
/// The verses here are real ones, shortened where the rule being tested
/// allows; the markup is OSHB's own. Most of these rules were found by
/// running the importer over all 23,213 verses and comparing the two panes.
/// </summary>
public class HebrewBibleLoaderTests
{
    private const string Osis = "http://www.bibletechnologies.net/2003/OSIS/namespace";

    private static HebrewVerse Verse(string body) =>
        HebrewBibleLoader.ReadVerse(1, 1, XElement.Parse($"<verse xmlns=\"{Osis}\" osisID=\"Gen.1.1\">{body}</verse>"));

    /// <summary>
    /// The slashes are OSHB's division of a word into prefix and stem for
    /// parsing. The scribe wrote one word, and so does the reading pane.
    /// </summary>
    [Fact]
    public void ParsingSlashesAreNotWritten()
    {
        var verse = Verse("<w lemma=\"b/7225\" morph=\"HR/Ncfsa\">בְּ/רֵאשִׁ֖ית</w>"
                          + "<w lemma=\"1254 a\" morph=\"HVqp3ms\">בָּרָ֣א</w>");

        Assert.Equal("בְּרֵאשִׁ֖ית בָּרָ֣א", verse.Text);
        Assert.Equal("בְּרֵאשִׁ֖ית", verse.Words[0].Form);
    }

    /// <summary>
    /// Maqaf binds two words under one accent and takes no space either side;
    /// sof pasuq closes the verse against its last word; paseq stands apart.
    /// Isaiah 6.2, where the seraphim stand, has two paseqs.
    /// </summary>
    [Fact]
    public void MaqafSofPasuqAndPaseqAreSpacedAsPrinted()
    {
        var verse = Verse(
            "<w lemma=\"7714\" morph=\"HNcmpa\">שְׂרָפִ֨ים</w>"
            + "<w lemma=\"5975\" morph=\"HVqrmpa\">עֹמְדִ֤ים</w>"
            + "<seg type=\"x-paseq\">׀</seg>"
            + "<w lemma=\"853\" morph=\"HTo\">אֶת</w><seg type=\"x-maqqef\">־</seg>"
            + "<w lemma=\"d/776\" morph=\"HTd/Ncbsa\">הָ/אָֽרֶץ</w><seg type=\"x-sof-pasuq\">׃</seg>");

        Assert.Equal("שְׂרָפִ֨ים עֹמְדִ֤ים ׀ אֶת־הָאָֽרֶץ׃", verse.Text);
    }

    /// <summary>
    /// Where the written consonants and the marginal reading differ, both are
    /// kept the way printed Bibles keep them - ketiv in parentheses, qere in
    /// brackets - and both are words a reader may look up.
    /// </summary>
    [Fact]
    public void KetivAndQereAreBothKept()
    {
        var verse = Verse(
            "<w type=\"x-ketiv\" lemma=\"3318\" morph=\"HVhv2ms\">הוצא</w>"
            + "<note type=\"variant\"><catchWord>הוצא</catchWord>"
            + "<rdg type=\"x-qere\"><w lemma=\"3318\" morph=\"HVhv2ms\">הַיְצֵ֣א</w></rdg></note>"
            + "<w lemma=\"854\" morph=\"HR/Sp2fs\">אִתָּ֑/ךְ</w>");

        Assert.Equal("(הוצא) [הַיְצֵ֣א] אִתָּ֑ךְ", verse.Text);
        Assert.Equal(new[] { "הוצא", "הַיְצֵ֣א", "אִתָּ֑ךְ" }, verse.Words.Select(w => w.Form));
    }

    /// <summary>
    /// Every other note is OSHB commenting on the text - an accent read
    /// differently from BHS, a word divided for parsing - and is not the text.
    /// </summary>
    [Fact]
    public void EditorialNotesAreLeftOut()
    {
        var verse = Verse(
            "<w lemma=\"595\" morph=\"HPp1cs\">אָֽנֹכִ֖י</w>"
            + "<note type=\"alternative\"><catchWord>אָֽנֹכִ֖י</catchWord><rdg type=\"x-accent\">אָֽנֹכִי֙</rdg></note>"
            + "<note n=\"c\">We read one or more accents in L differently than BHS.</note>");

        Assert.Equal("אָֽנֹכִ֖י", verse.Text);
        Assert.Single(verse.Words);
    }

    /// <summary>
    /// A suspended or small letter is a seg nested inside the word, and the
    /// word is all of it: the suspended nun of Judges 18.30 is part of מְנַשֶּׁה.
    /// </summary>
    [Fact]
    public void ASuspendedLetterStaysInsideItsWord()
    {
        var verse = Verse("<w lemma=\"4519\" morph=\"HNp\">מְ<seg type=\"x-suspended\">נַ</seg>שֶּׁ֜ה</w>");

        Assert.Equal("מְנַשֶּׁ֜ה", verse.Text);
    }

    [Theory]
    [InlineData("b/7225", "7225")]
    [InlineData("c/d/776", "776")]
    [InlineData("1254 a", "1254 a")]
    [InlineData("d", "")]
    public void TheHeadwordIsTheStrongsEntryWithoutItsParticles(string lemma, string expected) =>
        Assert.Equal(expected, HebrewBibleLoader.HeadwordOf(lemma));

    /// <summary>OSHB's homograph letters split entries Strong's prints as one.</summary>
    [Fact]
    public void AnAugmentedNumberLooksUpItsStrongsEntry() =>
        Assert.Equal("1254", HebrewBibleIngestService.StrongsNumber("1254 a"));

    /// <summary>
    /// JPS 1917 prints "Thou shalt not murder ... bear false witness" as one
    /// verse, Exodus 20.13, where the Hebrew numbers four. Split back at the
    /// sentence ends, with the rest of the chapter moved up by three, both
    /// panes say the same thing at 20.16 and both end at 20.26.
    /// </summary>
    [Fact]
    public void TheCommandmentsAreSplitBackIntoTheHebrewsFourVerses()
    {
        var jps = new List<(string, string)>
        {
            ("20.12", "Honour thy father and thy mother."),
            ("20.13", "Thou shalt not murder. Thou shalt not commit adultery. Thou shalt not steal. "
                      + "Thou shalt not bear false witness against thy neighbour."),
            ("20.14", "Thou shalt not covet thy neighbour's house."),
            ("20.23", "Neither shalt thou go up by steps unto Mine altar."),
            ("21.1", "Now these are the ordinances.")
        };

        var aligned = HebrewBibleIngestService.AlignToHebrew("Exod", jps);

        Assert.Equal(
            new[] { "20.12", "20.13", "20.14", "20.15", "20.16", "20.17", "20.26", "21.1" },
            aligned.Select(p => p.Citation));
        Assert.Equal("Thou shalt not bear false witness against thy neighbour.", aligned[4].Text);
        Assert.Equal("Thou shalt not covet thy neighbour's house.", aligned[5].Text);
    }

    /// <summary>
    /// If the joined verse is ever not four sentences, guessing where to cut
    /// it would put words in the wrong verse. Left alone, the chapter is only
    /// misnumbered.
    /// </summary>
    [Fact]
    public void ACommandmentVerseThatIsNotFourSentencesIsLeftAlone()
    {
        var jps = new List<(string, string)> { ("5.17", "Thou shalt not murder, nor steal."), ("5.18", "x") };

        Assert.Same(jps, HebrewBibleIngestService.AlignToHebrew("Deut", jps));
    }

    [Fact]
    public void NumbersTwentyFiveNineteenIsDividedFromTheNextChapter()
    {
        var jps = new List<(string, string)>
        {
            ("25.18", "for they harass you."),
            ("26.1", "And it came to pass after the plague, that the LORD spoke unto Moses.")
        };

        var aligned = HebrewBibleIngestService.AlignToHebrew("Num", jps);

        Assert.Equal(new[] { "25.18", "25.19", "26.1" }, aligned.Select(p => p.Citation));
        Assert.Equal("And it came to pass after the plague,", aligned[1].Text);
        Assert.Equal("that the LORD spoke unto Moses.", aligned[2].Text);
    }

    /// <summary>
    /// Sefaria answers a whole book as one jagged array. Chapters and verses
    /// are numbered from one, and an empty verse is skipped without
    /// renumbering the ones after it.
    /// </summary>
    [Fact]
    public void ASefariaBookFlattensToChapterAndVerse()
    {
        const string json = """
            {"versions":[{"versionTitle":"The Holy Scriptures: A New Translation (JPS 1917)",
              "license":"Public Domain","language":"en",
              "text":[["In the beginning.","And the earth."],["", "Thus the heavens."]]}]}
            """;

        var version = SefariaTextLoader.Parse(json);

        Assert.Equal("Public Domain", version.License);
        Assert.Equal(new[] { "1.1", "1.2", "2.2" }, version.Passages.Select(p => p.Citation));
    }

    /// <summary>
    /// Translators' footnotes are notes about the text and come out with their
    /// markers; every other tag goes and keeps its words.
    /// </summary>
    [Fact]
    public void SefariaMarkupIsCleaned() =>
        Assert.Equal(
            "the LORD God made earth & heaven.",
            SefariaTextLoader.Clean(
                "the <small>LORD</small> God made earth &amp; heaven."
                + "<sup class=\"footnote-marker\">1</sup><i class=\"footnote\">Heb. the heavens.</i>"));

    /// <summary>
    /// A note with italics inside it comes out whole. Ending at the first
    /// close left the rest of the note in the text, which is half as many
    /// words again in Guggenheimer's Jerusalem Talmud.
    /// </summary>
    [Fact]
    public void AFootnoteHoldingItalicsComesOutWhole() =>
        Assert.Equal(
            "Others say a short prayer.",
            SefariaTextLoader.Clean(
                "Others say<sup class=\"footnote-marker\">206</sup><i class=\"footnote\">Reported in "
                + "<i>Babylonian</i> style, with <i>no</i> doxology.</i> a short prayer."));

    /// <summary>The Tosefta's translators hang their notes from a plain superscript.</summary>
    [Fact]
    public void APlainSuperscriptBeforeANoteGoesWithIt() =>
        Assert.Equal(
            "He recites it.",
            SefariaTextLoader.Clean("He recites<sup>1</sup><i class=\"footnote\">The Shema.</i> it."));

    [Fact]
    public void AMarkerWithNoNoteLeavesNoDigitBehind() =>
        Assert.Equal(
            "He recites it.",
            SefariaTextLoader.Clean("He recites<sup class=\"footnote-marker\">1</sup> it."));

    /// <summary>A superscript that belongs to no note is the author's, and stays.</summary>
    [Fact]
    public void ASuperscriptNotAttachedToANoteIsKept() =>
        Assert.Equal("Psalm 151a here.", SefariaTextLoader.Clean("Psalm 151<sup>a</sup> here."));

    /// <summary>
    /// A note that never closes cannot be measured, so nothing is taken: a
    /// stray note can be seen, and a lost passage cannot.
    /// </summary>
    [Fact]
    public void AnUnclosedNoteLosesNoText() =>
        Assert.Equal(
            "Before. the note and the rest",
            SefariaTextLoader.Clean("Before. <i class=\"footnote\">the note and the rest"));

    /// <summary>
    /// One unclosed note says nothing about the notes after it, and they still
    /// go. Goldschmidt's Kiddushin 20b, where the text reads on from the
    /// unclosed note.
    /// </summary>
    [Fact]
    public void ANoteAfterAnUnclosedOneStillGoes() =>
        Assert.Equal(
            "Lev. 27,19. dies lehrt, daß er es in Hälften einlösen könne.",
            SefariaTextLoader.Clean(
                "<i class=\"footnote\">Lev. 27,19. dies lehrt, daß er es"
                + "<sup class=\"footnote-marker\">721</sup><i class=\"footnote\">Das Feld.</i>"
                + " in Hälften einlösen könne."));

    /// <summary>Cohen's Kallah 5.3 and 5.4 write the class in curly quotes, inside the straight ones.</summary>
    [Fact]
    public void ANoteWithCurlyQuotesInItsClassStillGoes() =>
        Assert.Equal(
            "let him examine his actions; and speak.",
            SefariaTextLoader.Clean(
                "let him examine his actions;<sup class=\"“footnote-marker”\">89</sup>"
                + "<i class=\"“footnote”\">(89) This phrase occurs in Erub. 13b.</i> and speak."));

    /// <summary>
    /// Goldschmidt's Mishnah numbers carry the marker's class with no note after
    /// them. A pattern running from that class to the next note took the
    /// Mishnah's opening words with it, 19,000 words across the Talmud.
    /// </summary>
    [Fact]
    public void AMishnahNumberDoesNotTakeTheWordsAfterIt() =>
        Assert.Equal(
            "VON WANN LIEST MAN es.",
            SefariaTextLoader.Clean(
                "<sup class=\"footnote-marker\">i</sup><b>V</b>ON <small>WANN</small> LIEST MAN"
                + "<sup class=\"footnote-marker\">1</sup><i class=\"footnote\">Das Shema.</i> es."));

    /// <summary>
    /// Guggenheimer's English has the marker's class on superscripts that are
    /// no markers at all: the end of an ordinal, a transliterated vowel, the
    /// top of a fraction. Those are the author's words.
    /// </summary>
    [Theory]
    [InlineData("the 31<sup class=\"footnote-marker\">st</sup> day", "the 31st day")]
    [InlineData("the 32<sup class=\"footnote-marker\">nd</sup> day", "the 32nd day")]
    [InlineData("the 30<sup class=\"footnote-marker\">th</sup> day", "the 30th day")]
    [InlineData("Rav says y<sup class=\"footnote-marker\">e</sup>utu", "Rav says yeutu")]
    [InlineData("The as is <sup class=\"footnote-marker\">1</sup>/24 of a denar", "The as is 1/24 of a denar")]
    public void TheMarkerClassOnPartOfAWordKeepsTheWord(string markup, string expected) =>
        Assert.Equal(expected, SefariaTextLoader.Clean(markup));

    /// <summary>
    /// Hebrew's final letters are the same letters at the end of a word, so a
    /// search typed with the medial forms - the stem of every plural of
    /// "angel" - finds the singular.
    /// </summary>
    [Fact]
    public void FinalLettersFoldToTheirMedialForms() =>
        Assert.Equal(WordNormalizer.Normalize("מַלְאַכ"), WordNormalizer.Normalize("מַלְאָךְ"));

    /// <summary>Maqaf joins two words in writing and leaves them two words in the index.</summary>
    [Fact]
    public void MaqafSeparatesWordsInTheIndex() =>
        Assert.Equal(new[] { "את", "הארצ" }, WordNormalizer.TokenizeLine("אֶת־הָאָֽרֶץ׃"));
}
