using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The fold that decides what counts as a difference between two printings of
/// the same line.
///
/// This is where the collation feature is won or lost. Two editions compared
/// byte for byte disagree almost everywhere - Perseus and First1KGreek agree on
/// barely half the Agamemnon and on none of the Historia Ecclesiastica, which
/// cannot be a thousand textual variants and is obviously house style. Getting
/// this wrong in the permissive direction hides real readings; getting it wrong
/// in the strict direction buries them under noise, which is worse, because a
/// list of two thousand differences looks like evidence.
/// </summary>
public class CollationNormalizerTests
{
    private static CollationLevel? Agreement(string a, string b, string? language = null) =>
        CollationNormalizer.FirstAgreement(a, b, language);

    [Fact]
    public void IdenticalLinesAgreeBeforeAnythingIsFolded()
    {
        Assert.Equal(CollationLevel.Raw, Agreement("μῆνιν ἄειδε θεά", "μῆνιν ἄειδε θεά"));
    }

    /// <summary>
    /// Spacing, case and punctuation are the typesetter's, not the editor's.
    /// </summary>
    [Theory]
    [InlineData("Arma virumque cano", "arma  virumque cano")]
    [InlineData("Arma virumque cano,", "Arma virumque cano")]
    [InlineData("μῆνιν ἄειδε, θεά·", "μῆνιν ἄειδε θεά")]
    public void SpacingCaseAndPunctuationAreNotReadings(string a, string b)
    {
        Assert.Equal(CollationLevel.Presentation, Agreement(a, b));
    }

    /// <summary>
    /// One editor bracketing a word another prints plainly is a disagreement
    /// about the word's standing, not about whether the word is there - and the
    /// word is in both. Marking it as a variant would bury the places where the
    /// editors actually print different words.
    /// </summary>
    [Theory]
    [InlineData("ἦλθε [δὲ] πρῶτος", "ἦλθε δὲ πρῶτος")]
    [InlineData("uenit ⟨et⟩ uidit", "uenit et uidit")]
    [InlineData("†corrupta† uox", "corrupta uox")]
    public void EditorialBracketsAndCruxesAreNotReadings(string a, string b)
    {
        Assert.Equal(CollationLevel.Presentation, Agreement(a, b));
    }

    /// <summary>
    /// The same letter typed by two programs. One file stores the accent baked
    /// into the character, the other stores it as its own code point.
    /// </summary>
    [Fact]
    public void PrecomposedAndDecomposedAccentsAreTheSameLetter()
    {
        // Spelled in escapes rather than typed. The two forms look identical
        // on screen, so literals would leave this asserting nothing the day an
        // editor normalised the file - and it would not say so.
        const string composed = "\u1F04\u03B5\u03B9\u03B4\u03B5";   // precomposed
        const string decomposed = "\u03B1\u0313\u0301\u03B5\u03B9\u03B4\u03B5"; // alpha + breathing + acute

        Assert.NotEqual(composed, decomposed);
        Assert.Equal(CollationLevel.Presentation, Agreement(composed, decomposed));
    }

    /// <summary>
    /// Greek accents and breathings are editorial - the manuscripts largely do
    /// not have them - so two houses accenting differently have not printed
    /// different words.
    /// </summary>
    [Theory]
    [InlineData("μῆνιν ἄειδε", "μηνιν αειδε")]
    [InlineData("οὗτος", "ουτος")]
    public void GreekAccentsAndBreathingsAreOrthography(string a, string b)
    {
        Assert.Equal(CollationLevel.Orthography, Agreement(a, b));
    }

    /// <summary>Final sigma is a position, not a letter.</summary>
    [Fact]
    public void FinalSigmaIsTheSameLetterAsMedialSigma()
    {
        Assert.Equal(CollationLevel.Orthography, Agreement("λογος", "λογοσ"));
    }

    /// <summary>
    /// u and v are one letter, and i and j are one letter, split by printers
    /// long after these texts were written. Every edition picks a side.
    /// </summary>
    [Theory]
    [InlineData("uenit uidit uicit", "venit vidit vicit")]
    [InlineData("iam iustus", "jam justus")]
    public void LatinConsonantalUAndIAreOrthography(string a, string b)
    {
        Assert.Equal(CollationLevel.Orthography, Agreement(a, b, "lat"));
    }

    /// <summary>
    /// The e-caudata a medieval scribe wrote for the ae digraph, and the
    /// ligature a printer set for it, are both that digraph.
    /// </summary>
    [Theory]
    [InlineData("cęlum", "caelum")]
    [InlineData("cælum", "caelum")]
    public void TheAeDigraphIsTheSameHoweverItIsWritten(string a, string b)
    {
        Assert.Equal(CollationLevel.Orthography, Agreement(a, b, "lat"));
    }

    /// <summary>
    /// The regression guarding the fold that writes e-caudata out as two
    /// letters. An earlier version repaired it afterwards by rewriting "ea" as
    /// "ae", which silently corrupted every ordinary word containing "ea" -
    /// and would have reported "mea terrea" as a variant of itself.
    /// </summary>
    [Fact]
    public void ExpandingECaudataLeavesOrdinaryEaAlone()
    {
        Assert.Equal(CollationLevel.Raw, Agreement("mea terrea creaui", "mea terrea creaui", "lat"));
        Assert.Null(Agreement("mea", "mae", "lat"));
    }

    /// <summary>
    /// The point of the whole exercise: different words stay different, at
    /// every level of folding.
    /// </summary>
    [Theory]
    [InlineData("μῆνιν ἄειδε θεά", "μῆνιν ἄειδε μοῦσα")]
    [InlineData("arma uirumque cano", "arma uirosque cano")]
    [InlineData("ἦλθε πρῶτος", "ἦλθε πρῶτον")]
    public void DifferentWordsAreNeverFoldedTogether(string a, string b)
    {
        Assert.Null(Agreement(a, b, "lat"));
        Assert.Null(Agreement(a, b));
    }

    /// <summary>
    /// Elision is an editor's decision about the text, so an edition that
    /// elides and one that does not have printed different things - and the
    /// apostrophe being stripped as punctuation must not hide that.
    /// </summary>
    [Fact]
    public void AnElidedWordIsNotTheSameAsAnUnelidedOne()
    {
        Assert.Null(Agreement("δ᾽ ἐγένετο", "δὲ ἐγένετο"));
    }

    /// <summary>
    /// The language only decides whether u/v and i/j are folded. Applying
    /// Greek rules to Latin or the reverse does nothing, because neither
    /// alphabet contains the other's letters - so an edition whose language
    /// was never recorded still collates.
    /// </summary>
    [Fact]
    public void GreekCollatesWithoutBeingToldItIsGreek()
    {
        Assert.Equal(CollationLevel.Orthography, Agreement("μῆνιν", "μηνιν", language: null));
        Assert.Equal(CollationLevel.Orthography, Agreement("μῆνιν", "μηνιν", "lat"));
    }

    /// <summary>
    /// Latin u/v needs the language, and says so. Folding v to u in Greek text
    /// would be harmless, but claiming to fold it without being told the text
    /// is Latin would be a guess.
    /// </summary>
    [Fact]
    public void LatinConsonantalUNeedsTheLanguage()
    {
        Assert.Equal(CollationLevel.Orthography, Agreement("uenit", "venit", "lat"));
        Assert.Equal(CollationLevel.Orthography, Agreement("uenit", "venit"));
    }

    [Fact]
    public void RawLevelReturnsTheTextUntouched()
    {
        const string line = "  Arma  virumque,  cano  ";
        Assert.Equal(line, CollationNormalizer.Normalize(line, CollationLevel.Raw));
    }

    [Fact]
    public void FoldingIsStableWhenAppliedTwice()
    {
        const string line = "Μῆνιν ἄειδε, θεά, Πηληϊάδεω Ἀχιλῆος";

        var once = CollationNormalizer.Normalize(line, CollationLevel.Orthography);
        var twice = CollationNormalizer.Normalize(once, CollationLevel.Orthography);

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// The regression that cost most of the signal. Perseus writes the elision
    /// mark as U+1FBD (a Unicode symbol), First1KGreek as U+02BC (a modifier
    /// LETTER), and only the first fell out of the punctuation and symbol
    /// tests - so every elided word in seven plays of Aeschylus read as a
    /// textual variant. Measured against the real library it was the single
    /// largest source of false variants by a wide margin.
    /// </summary>
    [Fact]
    public void TheElisionMarkIsTheSameWhicheverCharacterWasUsedForIt()
    {
        const string koronis = "\u03C4\u1FBD";        // Perseus
        const string modifierLetter = "\u03C4\u02BC"; // First1KGreek

        Assert.NotEqual(koronis, modifierLetter);
        Assert.Equal(CollationLevel.Presentation, Agreement(koronis, modifierLetter));
    }

    /// <summary>
    /// And the distinction that must survive it. Stripping the mark is not
    /// ignoring elision: an edition printing the elided form and one printing
    /// the full word still differ, because the letters differ.
    /// </summary>
    [Fact]
    public void StrippingTheElisionMarkDoesNotHideTheElisionItself()
    {
        Assert.Null(Agreement("\u03B4\u02BC", "\u03B4\u1F72"));
    }
}
