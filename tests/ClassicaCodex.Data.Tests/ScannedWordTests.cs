using ClassicaCodex.Core.Meter;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading a scansion back as words, which is what Word Study shows.
///
/// The line's feet are the scanner's answer; this is the reader's. Latin
/// editions print no macrons, so the letters of "cano" do not say whether the
/// final o is long, and the metre does - which makes it the first person of a
/// verb rather than anything else. Measured over Virgil, Ovid, Lucretius and
/// Juvenal, 33,114 verse lines: the metre settles 75.1% of the syllables the
/// spelling leaves open, and 74.8% of words come out with every syllable
/// determined.
/// </summary>
public class ScannedWordTests
{
    private const string Aeneid1 = "Arma virumque cano, Troiae qui primus ab oris";

    private static ScannedWord Word(string line, string word) =>
        Assert.Single(ScannedWords.Matching(HexameterScanner.Scan(line), word));

    // --------------------------------------------------- what the metre says

    /// <summary>
    /// The first word of the Aeneid. Long, short - and the first syllable is
    /// long by POSITION, its a standing before rm. This is why the marks are
    /// on syllables and not on vowels: printed as a macron over the a it would
    /// tell a reader that arma has a long first vowel, which it does not.
    /// </summary>
    [Fact]
    public void ALongSyllableIsNotALongVowel() =>
        Assert.Equal("¯ ˘", Word(Aeneid1, "arma").Pattern);

    /// <summary>
    /// The case the feature exists for. Nothing in the spelling of "cano"
    /// distinguishes the two quantities of its final o; the metre requires a
    /// long one.
    /// </summary>
    [Fact]
    public void TheMetreSettlesAVowelTheSpellingCannot()
    {
        var cano = Word(Aeneid1, "cano");

        Assert.Equal("˘ ¯", cano.Pattern);
        Assert.True(cano.FullyResolved);
    }

    [Fact]
    public void AThreeSyllableWordComesBackInOrder() =>
        Assert.Equal("˘ ¯ ˘", Word(Aeneid1, "virumque").Pattern);

    /// <summary>
    /// Ovid's first line. "animus" is three short vowels, and its last
    /// syllable is still long - it stands before the m of mutatas and is
    /// closed by two consonants. Same lesson as arma, one word further on.
    /// </summary>
    [Fact]
    public void AShortVowelInAClosedSyllableIsStillALongSyllable() =>
        Assert.Equal("˘ ˘ ¯",
            Word("In nova fert animus mutatas dicere formas", "animus").Pattern);

    // ------------------------------------------------ what it declines to say

    /// <summary>
    /// The last syllable of a line is free - brevis in longo - so the metre
    /// does not settle it however the shapes are written, and the mark says
    /// so rather than reporting the convention as a measurement.
    /// </summary>
    [Fact]
    public void TheLastSyllableOfTheLineIsNotClaimed()
    {
        var oris = Word(Aeneid1, "oris");

        Assert.Equal("¯ ×", oris.Pattern);
        Assert.False(oris.FullyResolved);
        Assert.True(oris.SaysAnything);
    }

    /// <summary>
    /// Where two shapes of the line survive and disagree about a syllable, it
    /// is unresolved - not resolved by picking the likelier shape.
    ///
    /// "primus" has a long first vowel by nature, and the scanner has no way
    /// to know that: it keeps no table of vowel quantities, deliberately, so
    /// both a dactyl and a spondee fit the fourth foot and the word goes
    /// unsettled. That is the honest answer and the cost of not guessing.
    /// </summary>
    [Fact]
    public void AWordTheSurvivingShapesDisagreeAboutIsUnresolved()
    {
        var primus = Word(Aeneid1, "primus");

        Assert.Equal("× ×", primus.Pattern);
        Assert.False(primus.SaysAnything);
    }

    // ----------------------------------------------------------- elision

    /// <summary>
    /// Aeneid 3.658, three elisions in one line. An elided syllable is marked
    /// as elided rather than given a quantity: it is not in a foot.
    /// </summary>
    [Fact]
    public void AnElidedSyllableIsMarkedRatherThanMeasured()
    {
        const string line = "monstrum horrendum, informe, ingens, cui lumen ademptum";
        var scansion = HexameterScanner.Scan(line);

        Assert.True(scansion.Scans);
        Assert.Equal(3, scansion.Elisions);

        var monstrum = Assert.Single(ScannedWords.Matching(scansion, "monstrum"));
        Assert.Collection(monstrum.Syllables,
            first => Assert.False(first.Elided),
            second => Assert.True(second.Elided));
    }

    // ------------------------------------------------------ matching words

    /// <summary>
    /// Words are matched on letters, not on position, because the word list a
    /// reader picks from splits the line on whitespace while the scanner
    /// splits on any non-letter. A word this line does not contain matches
    /// nothing rather than matching whatever sits at some index.
    /// </summary>
    [Fact]
    public void AWordNotInTheLineMatchesNothing() =>
        Assert.Empty(ScannedWords.Matching(HexameterScanner.Scan(Aeneid1), "Karthago"));

    /// <summary>
    /// u and v are one letter, so a reader picking "virumque" out of a text
    /// that prints "uirumque" still gets its quantities.
    /// </summary>
    [Fact]
    public void UAndVAreTheSameLetterWhenMatching() =>
        Assert.Equal("˘ ¯ ˘", Word(Aeneid1, "uirumque").Pattern);

    /// <summary>
    /// A line that does not scan still names its words, and says nothing about
    /// their quantities rather than saying something wrong. Horace's
    /// Archilochian is the case Word Study will meet - it is Latin verse, and
    /// it is not a hexameter.
    /// </summary>
    [Fact]
    public void ALineThatDoesNotScanReportsNoQuantities()
    {
        var scansion = HexameterScanner.Scan("Solvitur acris hiems grata vice veris et Favoni");

        Assert.False(scansion.Scans);

        var acris = Assert.Single(ScannedWords.Matching(scansion, "acris"));
        Assert.False(acris.SaysAnything);
        Assert.Equal("× ×", acris.Pattern);
    }

    /// <summary>
    /// Every word of the line comes back, in the order it was written, so a
    /// caller walking them is walking the line.
    ///
    /// Lowercased, and otherwise as the edition spells them: the prosody
    /// tokeniser reads a CAPITAL V as the u it was in Roman capitals and
    /// leaves a lowercase v alone, so "virumque" stays spelt with a v here.
    /// Matching folds the two either way - see the u/v test above - and this
    /// is only about what the letters come back as.
    /// </summary>
    [Fact]
    public void WordsComeBackInLineOrder()
    {
        var words = ScannedWords.From(HexameterScanner.Scan(Aeneid1));

        Assert.Equal(
            new[] { "arma", "virumque", "cano", "troiae", "qui", "primus", "ab", "oris" },
            words.Select(w => w.Text).ToArray());
    }
}
