using ClassicaCodex.Core.Meter;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Scanning a Latin hexameter from letters that do not say how long a vowel
/// is.
///
/// Every text in this library prints Latin without macrons, so about half of
/// every line's syllables are undecided by their spelling and no amount of
/// care with the remaining half will decide them. The scanner therefore does
/// not produce a scansion; it eliminates the shapes that contradict what the
/// spelling does force, and reports what is left. One survivor is a solved
/// line, several is an honest admission, and none means the line will not
/// scan at all.
///
/// These tests are about the two ways that goes wrong. It can be too
/// permissive, letting a shape through that the letters rule out - which
/// shows up as a line with more readings than it should have, or as prose
/// that scans. And it can be too strict, forcing a quantity the poet did not
/// observe - which shows up as a real hexameter that will not scan, and is
/// the failure that matters, because it looks exactly like a corrupt text.
///
/// Measured against the four hexameter poems in the CSEL corpus, 90-96% of
/// lines scan and prose scans at 0.7%, which is what makes the first number
/// mean anything. See the class comment on <see cref="HexameterScanner"/> for
/// the method.
/// </summary>
public class HexameterScansionTests
{
    /// <summary>
    /// Lines whose spelling happens to pin every foot. Rare enough to be
    /// worth naming, and the strongest check available: a wrong rule
    /// anywhere in the prosody either breaks these or lets a second reading
    /// through.
    /// </summary>
    [Theory]
    [InlineData("Tityre, tu patulae recubans sub tegmine fagi", "DDDSDS")]
    [InlineData("Aethera sidereum iusso moderamine uoluet.", "DDSDDS")]
    public void ALineTheLettersFullyDetermineHasOneReading(string line, string pattern)
    {
        var scansion = HexameterScanner.Scan(line);

        Assert.True(scansion.Scans);
        Assert.Equal(1, scansion.ReadingCount);
        Assert.Equal(pattern, scansion.Pattern);
    }

    /// <summary>
    /// The Aeneid's first line, which the letters do not fully determine.
    ///
    /// Four of its six feet are pinned; the other two are not, because "qui"
    /// and "pri-" are long by nature and nothing in the spelling says so -
    /// each stands before a single consonant, and each would be short in a
    /// word where the vowel happened to be short.
    ///
    /// Reporting the four and admitting the two is the whole design. A
    /// scanner that picked the likelier arrangement would be right here and
    /// silent about the fact that it guessed.
    /// </summary>
    [Fact]
    public void AnUnderdeterminedLineReportsTheFeetItsReadingsAgreeOn()
    {
        var scansion = HexameterScanner.Scan("Arma uirumque cano, Troiae qui primus ab oris");

        Assert.True(scansion.Scans);
        Assert.True(scansion.ReadingCount > 1);
        Assert.Equal("DDS??S", scansion.Pattern);
        Assert.Equal(Foot.Dactyl, scansion.Feet[0]);
        Assert.Null(scansion.Feet[3]);
    }

    /// <summary>
    /// Prose does not accidentally fit. This is the control that makes the
    /// hit rate on verse worth quoting - without it, a scanner that said yes
    /// to everything would score 100% on the Aeneid.
    /// </summary>
    [Theory]
    [InlineData("Gloriosissimam ciuitatem dei siue in hoc temporum cursu")]
    [InlineData("Magnum opus et arduum, sed deus adiutor noster est.")]
    public void ProseDoesNotScan(string line)
    {
        Assert.False(HexameterScanner.Scan(line).Scans);
    }

    /// <summary>
    /// A line too short to be a hexameter is reported as too short, not as a
    /// line that failed on its quantities. The distinction is the whole value
    /// of the failure: Virgil left dozens of half-lines in the Aeneid and
    /// they are printed as he left them, so a reader wants "this is half a
    /// line" and not "this does not scan".
    /// </summary>
    [Fact]
    public void AHalfLineIsReportedAsTooShort()
    {
        var scansion = HexameterScanner.Scan("hic cursus fuit");

        Assert.False(scansion.Scans);
        Assert.Equal(ScansionFailure.TooShort, scansion.Failure);
    }

    [Fact]
    public void AnEmptyLineIsReportedAsEmpty()
    {
        Assert.Equal(ScansionFailure.Empty, HexameterScanner.Scan("   -- 42 --  ").Failure);
    }

    /// <summary>
    /// What the scanner cannot do, recorded so it is not discovered later by
    /// someone trusting it.
    ///
    /// A pentameter is twelve to fourteen syllables and a hexameter thirteen
    /// to seventeen, so the two overlap, and about half the pentameters in an
    /// elegiac poem fit a hexameter shape without contradiction. Measured on
    /// Orientius' Commonitorium, which is elegiac couplets throughout: the
    /// hexameters scan at 95.8% and the pentameters at 49.8%.
    ///
    /// So this tells verse from prose almost perfectly and cannot be used to
    /// tell one metre from another. Anything built on it has to know what
    /// metre it is looking at.
    /// </summary>
    [Fact]
    public void APentameterCanFitAHexameterShape()
    {
        // Orientius, Commonitorium 1.16 - a pentameter, and two hexameter
        // shapes fit it without contradicting a single quantity.
        Assert.True(HexameterScanner.Scan("uita docenda mihi est, uita petenda tibi.").Scans);
    }

    /// <summary>
    /// Synizesis: two written vowels read as one syllable. Pentheus is
    /// Pen-theus where the letters say Pen-the-us, and so are Orpheus,
    /// Theseus and the rest of the Greek names in -eus - but not reliably,
    /// and not always twice in the same poem, so both readings are offered
    /// and the line chooses.
    ///
    /// Aeneid 4.469, which was two syllables too long before this.
    /// </summary>
    [Fact]
    public void AGreekNameCanBeReadWithSynizesis()
    {
        var scansion = HexameterScanner.Scan(
            "Eumenidum veluti demens videt agmina Pentheus,");

        Assert.True(scansion.Scans);
        Assert.Equal(16, scansion.MetricalSyllables);
    }

    /// <summary>
    /// Hiatus: an elision the poet declines to make. Aeneid 1.617, where the
    /// -o of Dardanio stands before the A- of Anchisae instead of vanishing
    /// into it.
    ///
    /// Worth asserting the whole pattern rather than merely that it scans.
    /// The line has one reading and only one, so the branch did not buy the
    /// scan with a shrug - it recovered the scansion exactly, hiatus and all.
    /// </summary>
    [Fact]
    public void APoetMayDeclineAnElision()
    {
        var scansion = HexameterScanner.Scan("Tune ille Aeneas, quem Dardanio Anchisae");

        Assert.Equal(1, scansion.ReadingCount);
        Assert.Equal("SSSDSS", scansion.Pattern);
    }

    /// <summary>
    /// An initial i before a vowel is a consonant in iam and a vowel in
    /// I-ulus, and Virgil uses the name both ways. Aeneid 1.709.
    /// </summary>
    [Fact]
    public void AnInitialIInANameCanBeAVowel()
    {
        Assert.True(HexameterScanner.Scan("Mirantur dona Aeneae, mirantur Iulum").Scans);
    }

    /// <summary>
    /// Three lines of Lucretius, one for each of the things that used to stop
    /// him scanning. Each comes back with exactly one reading, which is the
    /// part worth asserting: a fix that only made a line scan could have done
    /// it by being vague, and these recovered the scansion instead.
    ///
    /// 1.29 is the archaic genitive militiai; 1.7 the consonantal u of
    /// suauis; 1.12 the diaeresis of aeriae.
    /// </summary>
    [Theory]
    [InlineData("effice ut interea fera moenera militiai", "DDDDDS")]
    [InlineData("adventumque tuum, tibi suavis daedala tellus", "SDDSDS")]
    [InlineData("aeriae primum volucris te, diva, tuumque", "DSDSDS")]
    public void LucretiusScansUniquely(string line, string pattern)
    {
        var scansion = HexameterScanner.Scan(line);

        Assert.Equal(1, scansion.ReadingCount);
        Assert.Equal(pattern, scansion.Pattern);
    }
}

/// <summary>
/// The letters, before the metre gets to them.
///
/// Everything here is a rule about spelling that a scanner has to get right
/// before it can eliminate anything - and each of these was found by a real
/// line refusing to scan, not by reading a grammar.
/// </summary>
public class LatinProsodyTests
{
    private static int Syllables(string text) =>
        LatinProsody.Syllabify(text).Count(s => !s.Elided);

    private static Quantity First(string text) => LatinProsody.Syllabify(text)[0].Quantity;

    /// <summary>
    /// i and u are each written as a vowel and each sometimes a consonant.
    /// A critical edition writes no j and no v, so a scanner that reads every
    /// u as a vowel finds six syllables in uoluerunt, which has three.
    /// </summary>
    [Theory]
    [InlineData("iam", 1)]           // consonantal i, word-initial
    [InlineData("iuuenis", 3)]       // ju-ve-nis: initial i, then a vowel u, then a v
    [InlineData("nouus", 2)]         // no-vus: u between two vowels
    [InlineData("coniux", 2)]        // con-jux: i after a prefix behaves as word-initial
    [InlineData("deus", 2)]          // de-us: eu is not a diphthong here
    [InlineData("cui", 1)]           // and ui is one, in this word
    [InlineData("Troiae", 2)]        // Tro-iae, with the i counting double
    public void ConsonantalLettersAreNotCountedAsSyllables(string word, int expected)
    {
        Assert.Equal(expected, Syllables(word));
    }

    /// <summary>
    /// Roman capitals had no U. A text setting a word in capitals writes V
    /// for the vowel as readily as for the consonant, so "Vt" is ut and
    /// "DEVS" is deus - and read as a v they lose their only vowel and the
    /// word vanishes from the count entirely.
    /// </summary>
    [Theory]
    [InlineData("Vt", 1)]
    [InlineData("DEVS", 2)]
    [InlineData("Virtus", 2)]
    public void ACapitalVIsTheLetterU(string word, int expected)
    {
        Assert.Equal(expected, Syllables(word));
    }

    /// <summary>
    /// Elision takes the syllable and leaves the consonants in front of it:
    /// "multum ille" is mul-til-le, three syllables, and the t of multum is
    /// still there.
    /// </summary>
    [Theory]
    [InlineData("multum ille", 3)]
    [InlineData("atque ego", 3)]
    [InlineData("monstrum horrendum", 4)]   // h does not block it
    [InlineData("atque iam", 3)]            // consonantal i does
    public void ElisionRemovesTheSyllableAndNotTheConsonants(string text, int expected)
    {
        Assert.Equal(expected, Syllables(text));
    }

    /// <summary>
    /// The third place a written u is a consonant, after qu and ngu. A list
    /// rather than a rule, because nothing separates the swa- of suauis from
    /// the su-a of suus - and those are common enough that being wrong about
    /// them would cost far more than the handful of words being right about.
    /// </summary>
    [Theory]
    [InlineData("suavis", 2)]        // swa-vis
    [InlineData("persuadeo", 4)]     // per-swa-de-o, so a prefix comes free
    [InlineData("suus", 2)]          // and su-us is untouched
    [InlineData("sua", 2)]
    public void TheUOfSuauisIsAConsonantAndSuusIsNot(string word, int expected)
    {
        Assert.Equal(expected, Syllables(word));
    }

    /// <summary>
    /// It also has to count as one consonant rather than two, exactly as qu
    /// does - otherwise it closes the syllable in front of it, and "tibi
    /// suauis" makes the -bi long when the line needs it short.
    /// </summary>
    [Fact]
    public void TheSuClusterDoesNotCloseTheSyllableBeforeIt()
    {
        var syllables = LatinProsody.Syllabify("tibi suavis");
        Assert.Equal(Quantity.Unknown, syllables[1].Quantity);
    }

    /// <summary>
    /// A word-final -ai is the archaic genitive with a long a, or a Greek
    /// nominative plural with a short one, and the letters do not say which.
    /// So the rule that a vowel before a vowel is shortened is withheld here
    /// rather than applied - it was not a guess that sometimes missed, it was
    /// an assertion that ruled the correct scansion out.
    /// </summary>
    [Fact]
    public void TheAOfAnArchaicGenitiveIsNotShortened()
    {
        var syllables = LatinProsody.Syllabify("militiai");
        Assert.Equal(Quantity.Unknown, syllables[^2].Quantity);

        // Still shortened everywhere else it stands before a vowel.
        Assert.Equal(Quantity.Short, LatinProsody.Syllabify("aureus")[1].Quantity);
    }

    /// <summary>
    /// aer is two syllables and aerumna is one, poeta is two and poena is
    /// one, and the spellings agree for as far as any rule can see - so both
    /// readings are offered on exactly the prefixes where both words live,
    /// and nowhere else.
    /// </summary>
    [Fact]
    public void ADiaeresisIsOfferedWhereBothWordsAreSpelledAlike()
    {
        Assert.Equal(2, LatinProsody.Syllabifications("aer").Count);
        Assert.Equal(2, LatinProsody.Syllabifications("poeta").Count);
        Assert.Single(LatinProsody.Syllabifications("caelum"));
        Assert.Single(LatinProsody.Syllabifications("proelia"));
    }

    [Fact]
    public void ADiphthongIsLong() => Assert.Equal(Quantity.Long, First("aestas"));

    [Fact]
    public void TwoConsonantsMakeTheSyllableBeforeThemLong() =>
        Assert.Equal(Quantity.Long, First("ille"));

    [Fact]
    public void XCountsAsTwoConsonantsOnItsOwn() => Assert.Equal(Quantity.Long, First("rex"));

    [Fact]
    public void AVowelBeforeAVowelIsShort() => Assert.Equal(Quantity.Short, First("deus"));

    /// <summary>
    /// A vowel before a single consonant is the case nothing decides, and it
    /// is the common one. Saying so is what lets the metre do the work.
    /// </summary>
    [Fact]
    public void AVowelBeforeOneConsonantIsUndecided() =>
        Assert.Equal(Quantity.Unknown, First("amat"));

    /// <summary>
    /// Mute plus liquid need not close the syllable in front of it, and what
    /// matters is that the two consonants belong to one word rather than to
    /// the vowel's word. In "fonte fluentes" the -te stays open because fl-
    /// opens the next word whole.
    ///
    /// Getting this wrong was, on the Juvencus, the single largest cause of
    /// hexameters that would not scan: it forced a long syllable in front of
    /// every word beginning cl-, cr-, pr-, tr- or fl-, and the failure rate
    /// halved when it was corrected.
    /// </summary>
    [Fact]
    public void MuteAndLiquidLeaveThePrecedingSyllableOpenAcrossAWordBoundary()
    {
        var syllables = LatinProsody.Syllabify("fonte fluentes");
        Assert.Equal(Quantity.Unknown, syllables[1].Quantity);
    }

    /// <summary>
    /// A mute ending one word and a liquid beginning the next is two
    /// consonants like any other, and does close the syllable.
    /// </summary>
    [Fact]
    public void AMuteAndLiquidSplitAcrossWordsStillCloseTheSyllable()
    {
        var syllables = LatinProsody.Syllabify("ad ripam");
        Assert.Equal(Quantity.Long, syllables[0].Quantity);
    }

    /// <summary>
    /// A u before a vowel and after l, r or n is genuinely ambiguous - the v
    /// of sil-ua and the vowel of cru-or are spelled identically - so both
    /// readings are offered and the metre chooses. Nothing else in a line
    /// branches, so a line with no such letter has exactly one reading.
    /// </summary>
    [Fact]
    public void AnAmbiguousUIsOfferedBothWays()
    {
        var readings = LatinProsody.Syllabifications("silua");
        Assert.Equal(2, readings.Count);
        Assert.Contains(readings, r => r.Count == 3);   // si-lu-a
        Assert.Contains(readings, r => r.Count == 2);   // sil-ua
    }

    [Fact]
    public void ALineWithNothingAmbiguousHasOneReading()
    {
        Assert.Single(LatinProsody.Syllabifications("arma uirumque cano"));
    }

    /// <summary>
    /// Synizesis, hiatus and a vocalic initial i are offered in capitalised
    /// words and nowhere else - a capital being the only mark the text puts
    /// on a proper name, which is where all three of them live.
    ///
    /// The gate is what makes them affordable. "deus" and "meus" have the
    /// same letters in the same order as the -eus names and never contract,
    /// and they are common enough that branching on them would have cost
    /// more determinacy across the corpus than the branch recovers.
    /// </summary>
    [Fact]
    public void TheNameOnlyBranchesAreGatedOnACapital()
    {
        Assert.Single(LatinProsody.Syllabifications("deus"));
        Assert.Equal(2, LatinProsody.Syllabifications("Deus").Count);
    }

    /// <summary>
    /// And the plain reading stays first, so anything asking for one answer
    /// gets the letters as written rather than a contraction.
    /// </summary>
    [Fact]
    public void ThePlainReadingIsStillTheFirstOne()
    {
        Assert.Equal(2, LatinProsody.Syllabify("Deus").Count);
    }

    /// <summary>
    /// Where a text does mark quantity, the mark wins. Almost no text in this
    /// library does, but throwing away a macron in order to re-derive it from
    /// position would be perverse.
    /// </summary>
    [Fact]
    public void AMarkedQuantityIsBelieved()
    {
        Assert.Equal(Quantity.Long, LatinProsody.Syllabify("āmat")[0].Quantity);
        Assert.Equal(Quantity.Short, LatinProsody.Syllabify("ămat")[0].Quantity);
    }
}
