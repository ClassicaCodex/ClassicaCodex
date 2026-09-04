using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// WordNormalizer decides what counts as "the same word" for every search,
/// concordance, and word-study lookup in the app. A regression here doesn't
/// throw or crash - it just quietly makes searches return nothing, or return
/// the wrong thing, which is exactly the kind of failure that survives
/// manual testing because you have to already know the right answer to spot
/// it. Hence pinning the rules rather than trusting them.
/// </summary>
public class WordNormalizerTests
{
    [Theory]
    [InlineData("λόγος", "λογοσ")]
    [InlineData("λόγου", "λογου")]
    [InlineData("ἄνθρωπος", "ανθρωποσ")]
    public void Normalize_StripsAccentsAndBreathings(string input, string expected)
    {
        Assert.Equal(expected, WordNormalizer.Normalize(input));
    }

    /// <summary>
    /// The whole point of the lemma system: every inflection of a headword
    /// has to normalize to something the index can match on. These four
    /// differ only in ending, and all four must survive normalization as
    /// distinct forms - normalization strips accents, it does not stem.
    /// </summary>
    [Fact]
    public void Normalize_KeepsInflectionsDistinct()
    {
        var forms = new[] { "λόγος", "λόγου", "λόγῳ", "λόγον" }
            .Select(WordNormalizer.Normalize)
            .ToList();

        Assert.Equal(forms.Count, forms.Distinct().Count());
    }

    /// <summary>
    /// Final sigma and medial sigma are the same letter in different
    /// positions. If these stop folding together, every Greek search for a
    /// word ending in sigma silently misses the lemma data's spelling of it.
    /// </summary>
    [Fact]
    public void Normalize_FoldsFinalSigmaToMedial()
    {
        Assert.Equal(WordNormalizer.Normalize("λόγοσ"), WordNormalizer.Normalize("λόγος"));
        Assert.EndsWith("σ", WordNormalizer.Normalize("λόγος"));
    }

    /// <summary>
    /// Perseus files are inconsistent about precomposed vs combining Unicode
    /// for the same character - the reason normalization exists at all.
    /// U+1F04 (alpha with psili and oxia) must match the decomposed spelling
    /// of the same letter.
    /// </summary>
    [Fact]
    public void Normalize_TreatsPrecomposedAndCombiningAlike()
    {
        const string precomposed = "\u1f04";              // ἄ as one codepoint
        const string combining = "\u03b1\u0313\u0301";    // α + psili + oxia

        Assert.Equal(WordNormalizer.Normalize(precomposed), WordNormalizer.Normalize(combining));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("123")]
    public void Normalize_DropsAnythingWithNoLetters(string input)
    {
        Assert.Equal(string.Empty, WordNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_LowercasesLatin()
    {
        Assert.Equal("arma", WordNormalizer.Normalize("ARMA"));
    }

    /// <summary>
    /// Normalize used to end with an explicit .Normalize(FormC) call. Once
    /// every combining mark has been stripped from an NFD-decomposed word,
    /// there is nothing left for FormC to recompose - canonical
    /// decomposition never splits a character into more than one base
    /// letter, only into a base letter plus marks - so the call was
    /// provably a no-op, run once per word on every line of a
    /// multi-million-line index build. This pins that removing it changed
    /// nothing: applying FormC to the actual output must still be an
    /// identity operation, across the trickiest inputs this file already
    /// exercises.
    /// </summary>
    [Theory]
    [InlineData("λόγος")]
    [InlineData("ἄνθρωπος")]
    [InlineData("λόγοσ")]
    [InlineData("\u1f04")]
    [InlineData("\u03b1\u0313\u0301")]
    [InlineData("ARMA")]
    public void Normalize_OutputIsAlreadyFullyComposed(string input)
    {
        var result = WordNormalizer.Normalize(input);
        Assert.Equal(result, result.Normalize(System.Text.NormalizationForm.FormC));
    }

    /// <summary>
    /// u/v and i/j were one letter each in antiquity and editions disagree,
    /// so headword lookup folds them. Without this, a lemma dataset spelling
    /// "uos" never finds a lexicon entry filed under "vos".
    /// </summary>
    [Theory]
    [InlineData("vos", "uos")]
    [InlineData("iam", "jam")]
    public void NormalizeHeadword_FoldsLatinLetterVariants(string a, string b)
    {
        Assert.Equal(
            WordNormalizer.NormalizeHeadword(a, "lat"),
            WordNormalizer.NormalizeHeadword(b, "lat"));
    }

    /// <summary>
    /// That folding is Latin-only. Greek has no u/v question, and applying
    /// the rule to Greek headwords would corrupt them.
    /// </summary>
    [Fact]
    public void NormalizeHeadword_DoesNotFoldLetterVariantsForGreek()
    {
        Assert.Equal("λογοσ", WordNormalizer.NormalizeHeadword("λόγος", "grc"));
    }

    /// <summary>
    /// Homograph numbering (liber1 / liber2) differs between the lemma data
    /// and the lexicon with no reliable mapping, so the digits are stripped
    /// and a lookup may legitimately return several entries.
    /// </summary>
    [Fact]
    public void NormalizeHeadword_StripsHomographNumbering()
    {
        Assert.Equal(
            WordNormalizer.NormalizeHeadword("liber", "lat"),
            WordNormalizer.NormalizeHeadword("liber1", "lat"));
    }

    /// <summary>
    /// A headword that is nothing but digits would otherwise strip to an
    /// empty string and match everything or nothing depending on the caller.
    /// </summary>
    [Fact]
    public void NormalizeHeadword_HandlesAllDigitInput()
    {
        Assert.Equal(string.Empty, WordNormalizer.NormalizeHeadword("123", "lat"));
    }

    /// <summary>
    /// Lunate sigma is sigma - the rounded shape papyri and inscriptions use,
    /// which some editors keep in print. 87 editions in this corpus are set
    /// in it throughout, among them the Suda, Herodian and Apollonius
    /// Dyscolus, and before this fold none of them could be reached by anyone
    /// typing an ordinary sigma: 349,421 index entries across 84,799 distinct
    /// words, and 22.2% of every line containing πόλις.
    /// </summary>
    [Theory]
    [InlineData("ϲοφία", "σοφια")]
    [InlineData("σοφία", "σοφια")]
    [InlineData("Σωκράτης", "σωκρατησ")]
    [InlineData("Ϲωκράτηϲ", "σωκρατησ")]
    [InlineData("πόλιϲ", "πολισ")]
    public void EveryShapeOfSigmaFoldsTogether(string word, string expected) =>
        Assert.Equal(expected, WordNormalizer.Normalize(word));

    /// <summary>
    /// Three lowercase shapes and two capitals, all one letter.
    /// </summary>
    [Fact]
    public void TheSigmasAreAllTheSameLetter()
    {
        var shapes = new[] { "σ", "ς", "ϲ", "Σ", "Ϲ" };

        Assert.Single(shapes.Select(WordNormalizer.Normalize).Distinct());
    }
}
