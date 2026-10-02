using ClassicaCodex.Ingestion.Egyptian;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the Pyramid Texts out of the Universal Dependencies Egyptian
/// treebank.
///
/// Every case here is taken from the real files, and most were found by
/// running the loader over all 3,089 sentences and looking at what came out.
/// The rows are shortened but their shape is not changed.
/// </summary>
public class PyramidTextLoaderTests
{
    private static readonly string Tab = "\t";

    /// <summary>A CoNLL-U row: id, form, lemma, upos, xpos, feats, head, deprel, deps, misc.</summary>
    private static string Row(string id, string form, string lemma, string upos, string feats, string misc) =>
        string.Join(Tab, id, form, lemma, upos, "_", feats, "0", "root", "_", misc);

    private static List<PyramidWitness> Parse(params string[] lines)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, lines);
            return PyramidTextLoader.Load(new[] { path });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string[] Sentence(
        string spell, string section, string king, string text, params string[] rows) =>
        new[]
        {
            $"# sent_id = PT_Sethe_{spell}_{section}_{king}",
            $"# spell = {spell}",
            $"# section = {section}",
            $"# king = {king}",
            $"# text = {text}"
        }.Concat(rows).Append(string.Empty).ToArray();

    /// <summary>
    /// <b>The signs and the transliteration are two scripts, not two
    /// spellings.</b> Both have to come out of one pass, and from different
    /// places: the transliteration is the treebank's own assembled line and
    /// the hieroglyphs are built from the token annotation, because there is
    /// no whole-sentence equivalent for them.
    /// </summary>
    [Fact]
    public void BothScriptsComeOutOfOnePass()
    {
        var pyramids = Parse(Sentence("23", "16a", "Unas", "Wśr(.w) ꞽč",
            Row("1", "Wśr(.w)", "Wśr.w", "PROPN", "Gender=Masc", "Hiero=𓊨𓁹|ID=49460"),
            Row("2", "ꞽč", "ꞽčꞽ", "VERB", "VerbForm=Fin", "Hiero=𓇋𓎁|ID=25710")));

        var utterance = Assert.Single(Assert.Single(pyramids).Utterances);

        Assert.Equal("𓊨𓁹 𓇋𓎁", utterance.Hieroglyphs);
        Assert.Equal("Wśr(.w) ꞽč", utterance.Transliteration);
        Assert.Equal("23.16a", utterance.Citation);
    }

    /// <summary>
    /// <b>A row is not always a word.</b> A multiword token is written three
    /// times: a range row with the surface form, then one row per component.
    /// The range row carries the signs as the group is written and the
    /// components carry the grammar, so the text takes the first and the
    /// dictionary takes the rest. Counting the components as text as well
    /// would write every one of these 1,224 groups into the line twice.
    /// </summary>
    [Fact]
    public void AMultiwordTokenIsWrittenOnceAndParsedTwice()
    {
        var pyramids = Parse(Sentence("1", "1a", "Unas", "č̣ꜣ-t(ꞽ) ꞽm",
            Row("1-2", "č̣ꜣ-t(ꞽ)", "_", "_", "_", "Hiero=𓍒𓄿(𓏏:𓏏)"),
            Row("1", "č̣ꜣ", "č̣ꜣi̯", "VERB", "VerbForm=Fin", "Hiero=𓍒𓄿|ID=181780"),
            Row("2", "t(ꞽ)", "tꞽ", "NOUN", "Gender=Masc", "Hiero=𓏏|ID=851185"),
            Row("3", "ꞽm", "ꞽm", "ADV", "AdvType=Loc", "Hiero=𓇋𓅓|ID=24640")));

        var utterance = Assert.Single(Assert.Single(pyramids).Utterances);

        // The group's own signs, once - not its two components' signs as well.
        Assert.Equal("𓍒𓄿𓏏𓏏 𓇋𓅓", utterance.Hieroglyphs);

        // But all three words are in the dictionary, and the range row, whose
        // lemma column is empty, is not.
        Assert.Equal(new[] { "č̣ꜣi̯", "tꞽ", "ꞽm" }, utterance.Words.Select(w => w.Headword));
    }

    /// <summary>
    /// <b>Some words were never carved.</b> Hiero=No marks a word the grammar
    /// requires and the wall does not show - a suffix pronoun the Egyptians
    /// left off, which the editors bracket in the transliteration. It must add
    /// no signs and no gap between the signs on either side of it, while still
    /// being a word for Word Study.
    /// </summary>
    [Fact]
    public void AWordThatWasNeverCarvedAddsNoSigns()
    {
        var pyramids = Parse(Sentence("1", "1a", "Unas", "sꜣ (⸗ꞽ) pw",
            Row("1", "sꜣ", "sꜣ", "NOUN", "Gender=Masc", "Hiero=𓅭|ID=125510"),
            Row("2", "(⸗ꞽ)", "ꞽ", "PRON", "Person=1", "Hiero=No|ID=10030_Add"),
            Row("3", "pw", "pw", "DET", "PronType=Dem", "Hiero=𓊪𓅱|ID=59741")));

        var utterance = Assert.Single(Assert.Single(pyramids).Utterances);

        Assert.Equal("𓅭 𓊪𓅱", utterance.Hieroglyphs);
        Assert.Equal(3, utterance.Words.Count);
    }

    /// <summary>
    /// <b>Quadrat layout is notation, not Unicode.</b> The treebank writes the
    /// square clusters hieroglyphic is set in with parentheses and colons, and
    /// restorations with square brackets. None of it is a sign. Keeping what
    /// is a hieroglyph rather than stripping what is not is what makes this
    /// safe: a list of characters to remove would have to be complete to be
    /// right, and a new one appearing upstream would arrive in the text.
    /// </summary>
    [Theory]
    [InlineData("(𓇋:𓈖)", "𓇋𓈖")]
    [InlineData("𓅜(𓐍:𓏏)", "𓅜𓐍𓏏")]
    [InlineData("[𓍹𓇳𓄤𓂓𓍺]", "𓍹𓇳𓄤𓂓𓍺")]
    [InlineData("𓄡:𓏏𓏤_", "𓄡𓏏𓏤")]
    [InlineData("No", "")]
    public void LayoutNotationNeverReachesTheText(string annotation, string expected)
    {
        Assert.Equal(expected, PyramidTextLoader.SignsOnly(annotation));
    }

    /// <summary>
    /// Unicode 15.1 added four thousand more hieroglyphs in a second block,
    /// and 1.35% of the signs here are in it. No font on Windows draws them
    /// yet, so they show as an empty box - but they are signs and they stay in
    /// the text. Dropping them would make the line look complete while being
    /// quietly wrong.
    /// </summary>
    [Fact]
    public void SignsFromTheNewerBlockAreKeptEvenThoughNothingDrawsThem()
    {
        Assert.True(PyramidTextLoader.IsHieroglyph(0x1401F));
        Assert.Equal("\U0001401F", PyramidTextLoader.SignsOnly("(\U0001401F)"));
    }

    /// <summary>
    /// Three rows in 35,458 have a malformed MISC - a missing equals sign that
    /// leaves "Hiero(𓏅:𓏏)". Read as absent rather than guessed at: the loss
    /// is three groups of signs, and the alternative is putting the wrong
    /// signs somewhere.
    /// </summary>
    [Fact]
    public void AMalformedAnnotationIsReadAsAbsentRatherThanGuessedAt()
    {
        var pyramids = Parse(Sentence("1", "1a", "Unas", "a b",
            Row("1", "a", "a", "NOUN", "_", "Hiero(𓏅:𓏏)|ID=1"),
            Row("2", "b", "b", "NOUN", "_", "Hiero=𓅓|ID=2")));

        Assert.Equal("𓅓", Assert.Single(Assert.Single(pyramids).Utterances).Hieroglyphs);
    }

    /// <summary>
    /// <b>Ordered by spell, and this is why.</b> Sethe's section numbers run
    /// continuously through his edition, so for his sentences either key
    /// works. Allen's do not: his sections count from 1 again inside each
    /// spell. Sorting by section would put spell 502C's section 3 near the
    /// front of the pyramid, among the opening offering formulae, hundreds of
    /// passages from where it belongs.
    /// </summary>
    [Fact]
    public void AllenNumberedSpellsStayBesideTheirSetheNeighbours()
    {
        var pyramids = Parse(
            Sentence("493", "1059a", "Neferkare", "first").Concat(
            Sentence("502C", "3", "Neferkare", "second")).Concat(
            Sentence("510", "1143a", "Neferkare", "third")).ToArray());

        Assert.Equal(
            new[] { "493.1059a", "502C.3", "510.1143a" },
            Assert.Single(pyramids).Utterances.Select(u => u.Citation));
    }

    /// <summary>
    /// Spell and section are numbers with letters attached, so they sort as
    /// neither strings nor integers: as strings "10" comes before "2", and as
    /// integers "502C" does not parse at all.
    /// </summary>
    [Fact]
    public void NumbersSortAsNumbersAndLettersBreakTheTie()
    {
        var pyramids = Parse(
            Sentence("10", "20a", "Unas", "ten").Concat(
            Sentence("2", "17a", "Unas", "two")).Concat(
            Sentence("2", "17b", "Unas", "two b")).ToArray());

        Assert.Equal(
            new[] { "2.17a", "2.17b", "10.20a" },
            Assert.Single(pyramids).Utterances.Select(u => u.Citation));
    }

    /// <summary>
    /// A section is usually one sentence and sometimes four. A bare "213.135a"
    /// would then name four different passages in Unas, which breaks a
    /// bookmark and makes a citation ambiguous; a position is appended only
    /// where there is something to disambiguate.
    /// </summary>
    [Fact]
    public void ASectionWithSeveralSentencesNumbersThemAndOneDoesNot()
    {
        var pyramids = Parse(
            Sentence("23", "16a", "Unas", "alone").Concat(
            Sentence("23", "16b", "Unas", "first of two")).Concat(
            Sentence("23", "16b", "Unas", "second of two")).ToArray());

        Assert.Equal(
            new[] { "23.16a", "23.16b.1", "23.16b.2" },
            Assert.Single(pyramids).Utterances.Select(u => u.Citation));
    }

    /// <summary>
    /// The six pyramids come out oldest first, with the names a reader would
    /// look for. The treebank gives the owner as a bare first name, and two of
    /// those are ambiguous: "Pepi" is Pepi I and "Neferkare" is his son Pepi
    /// II. Neith is not a king - she is Pepi II's queen, and the only woman
    /// whose pyramid carries these texts.
    /// </summary>
    [Fact]
    public void ThePyramidsAreOrderedAndNamedAsAReaderWouldLookForThem()
    {
        var pyramids = Parse(
            Sentence("1", "1a", "Neith", "last").Concat(
            Sentence("1", "1a", "Unas", "first")).Concat(
            Sentence("1", "1a", "Neferkare", "fifth")).Concat(
            Sentence("1", "1a", "Pepi", "third")).ToArray());

        Assert.Equal(
            new[]
            {
                "Pyramid Texts I: Unas",
                "Pyramid Texts III: Pepi I",
                "Pyramid Texts V: Pepi II",
                "Pyramid Texts VI: Queen Neith"
            },
            pyramids.Select(p => p.Title));
    }

    /// <summary>
    /// An empty node is an editor's reconstruction of something left out, not
    /// anything on the wall. This treebank has none today; the guard is here
    /// because a later release that adds them would otherwise interpolate
    /// words into the oldest text in the library.
    /// </summary>
    [Fact]
    public void AReconstructedWordIsNotPartOfTheText()
    {
        var pyramids = Parse(Sentence("1", "1a", "Unas", "𓅓 only",
            Row("1", "ꞽm", "ꞽm", "ADV", "_", "Hiero=𓅓|ID=1"),
            Row("1.1", "supplied", "supplied", "VERB", "_", "Hiero=𓏏|ID=2")));

        var utterance = Assert.Single(Assert.Single(pyramids).Utterances);

        Assert.Equal("𓅓", utterance.Hieroglyphs);
        Assert.Single(utterance.Words);
    }

    /// <summary>
    /// <b>Not every .conllu in the archive is the corpus.</b> The treebank
    /// ships a not-to-release folder holding the editors' working file: 2,182
    /// more sentences, overlapping the released ones. Reading every .conllu
    /// under the download folder would give 5,271 sentences instead of 3,089 -
    /// a corpus 71% larger than the published one, with duplicates in it, and
    /// no error anywhere to say so.
    /// </summary>
    [Theory]
    [InlineData(@"C:\data\UD_Egyptian-PC-r2.18\egy_pc-ud-train.conllu", true)]
    [InlineData(@"C:\data\UD_Egyptian-PC-r2.18\not-to-release\master.conllu", false)]
    [InlineData("/data/UD_Egyptian-PC-r2.18/not-to-release/master.conllu", false)]
    public void TheEditorsWorkingFileIsNotPartOfTheCorpus(string path, bool released)
    {
        Assert.Equal(released, PyramidTextsIngestService.IsReleased(path));
    }

    /// <summary>
    /// A sentence missing the fields that place it is dropped rather than
    /// filed somewhere arbitrary. There are none in the release; a sentence
    /// with no king would otherwise land in whichever pyramid was read last.
    /// </summary>
    [Fact]
    public void ASentenceThatCannotBePlacedIsNotGuessedAt()
    {
        var pyramids = Parse(
            "# sent_id = PT_Sethe_1_1a",
            "# text = unplaceable",
            Row("1", "a", "a", "NOUN", "_", "Hiero=𓅓|ID=1"),
            string.Empty);

        Assert.Empty(pyramids);
    }
}
