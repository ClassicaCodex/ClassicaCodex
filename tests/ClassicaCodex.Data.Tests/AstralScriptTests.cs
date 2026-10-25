using System.Text;
using System.Text.RegularExpressions;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Stylometry;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// Pins that the word pipeline can see a letter that lives outside the Basic
/// Multilingual Plane.
///
/// This is the same bug the corpus has already grown into once. The word
/// scanners used to match [a-zA-Z], which silently made them English-only -
/// Greek text contains no ASCII letters, so nothing was ever extracted and
/// the features built on top returned nothing for every Greek passage. The
/// repair was \p{L}, and \p{L} has the identical hole one plane up: .NET's
/// regex engine and char methods both work on 16-bit code units, and every
/// Egyptian hieroglyph and cuneiform sign is a surrogate pair.
///
/// The reason this needs pinning rather than trusting is the failure mode.
/// Nothing throws. The index builds, reports success, and is empty. You have
/// to already know the right answer to notice it is missing - which is
/// exactly the class of failure that survives manual testing.
///
/// Signs are written as code points rather than as literals, because a
/// literal astral character does not survive every tool that touches a source
/// file on the way here.
/// </summary>
public class AstralScriptTests
{
    // EGYPTIAN HIEROGLYPH A001, D021, G017 - a three-sign group. No claim is
    // made here about what it spells; the test is about whether the pipeline
    // can see signs at all.
    private static readonly string Hieroglyphs =
        char.ConvertFromUtf32(0x13000) + char.ConvertFromUtf32(0x13021) + char.ConvertFromUtf32(0x13017);

    // CUNEIFORM SIGN A, followed by two of its neighbours.
    private static readonly string Cuneiform =
        char.ConvertFromUtf32(0x12000) + char.ConvertFromUtf32(0x12001) + char.ConvertFromUtf32(0x12002);

    /// <summary>
    /// The regression, stated plainly. Before the rune fix this returned the
    /// empty string for both scripts, and every caller downstream - the word
    /// index, word study, the concordance - therefore had nothing to store.
    /// </summary>
    [Fact]
    public void Normalize_KeepsAstralLetters()
    {
        Assert.Equal(Hieroglyphs, WordNormalizer.Normalize(Hieroglyphs));
        Assert.Equal(Cuneiform, WordNormalizer.Normalize(Cuneiform));
    }

    /// <summary>
    /// Both scripts are letters - Unicode category OtherLetter, the same
    /// category as a Chinese character. They were never exotic; .NET simply
    /// could not see them through a 16-bit char.
    /// </summary>
    [Fact]
    public void AstralSigns_AreLetters()
    {
        Assert.True(Rune.IsLetter(new Rune(0x13000)));
        Assert.True(Rune.IsLetter(new Rune(0x12000)));
    }

    /// <summary>
    /// A line mixing scripts yields a token for each word in each of them.
    /// Tokenising is where stylometry, echo finding and the index all begin,
    /// so a script invisible here is invisible everywhere.
    /// </summary>
    [Fact]
    public void Tokenize_FindsWordsInEveryScript()
    {
        var line = $"logos {Hieroglyphs} kai {Cuneiform}";

        var tokens = StylometryTokenizer.Tokenize(line, foldAccents: true);

        Assert.Equal(4, tokens.Count);
        Assert.Contains(Hieroglyphs, tokens);
        Assert.Contains(Cuneiform, tokens);
    }

    /// <summary>
    /// The safety claim behind the whole change: for text that already worked,
    /// nothing moves. Rune.IsLetter accepts exactly the categories \p{L} does
    /// - Lu, Ll, Lt, Lm, Lo - so this compares the new scanner against the
    /// regex it replaced, over text in the scripts the corpus actually holds.
    ///
    /// If this ever fails, the fix has changed Greek or Latin tokenisation and
    /// every stylometric result in the app moved with it.
    /// </summary>
    [Theory]
    [InlineData("arma virumque cano, Troiae qui primus ab oris")]
    [InlineData("μῆνιν ἄειδε θεὰ Πηληϊάδεω Ἀχιλῆος")]
    [InlineData("Hwæt! We Gardena in geardagum")]
    [InlineData("To be, or not to be, that is the question")]
    [InlineData("δ' ἄρα — τῶν; (ἐν) [τῇ] πόλει· 1234")]
    public void LetterRuns_MatchesTheRegexItReplaced_OnBmpText(string text)
    {
        var fromRegex = Regex.Matches(text, @"\p{L}+").Select(m => m.Value).ToList();
        var fromScanner = LetterRuns.Words(text).ToList();

        Assert.Equal(fromRegex, fromScanner);
    }

    /// <summary>
    /// Length is not a letter count once a script leaves the BMP. Two signs
    /// measure 4, so any rule phrased as "at least four letters" - the echo
    /// finder's candidate filter, for one - lets two-sign words through as if
    /// they were four-letter words unless it counts properly.
    /// </summary>
    [Fact]
    public void CountLetters_IsNotStringLength()
    {
        var twoSigns = char.ConvertFromUtf32(0x13000) + char.ConvertFromUtf32(0x13001);

        Assert.Equal(4, twoSigns.Length);
        Assert.Equal(2, LetterRuns.CountLetters(twoSigns));
        Assert.Equal(5, LetterRuns.CountLetters("λόγος"));
    }

    /// <summary>
    /// Scan reports the letter count separately from the span, so a caller can
    /// index back into the string with one and apply a length rule with the
    /// other.
    /// </summary>
    [Fact]
    public void Scan_ReportsSpanAndLetterCountSeparately()
    {
        var text = $"({Hieroglyphs})";

        var runs = LetterRuns.Scan(text).ToList();

        var run = Assert.Single(runs);
        Assert.Equal(1, run.Start);
        Assert.Equal(6, run.Length);        // three signs, two code units each
        Assert.Equal(3, run.LetterCount);
        Assert.Equal(Hieroglyphs, text.Substring(run.Start, run.Length));
    }

    /// <summary>
    /// The highlight span. Walking a match inward by char loses an astral word
    /// completely: both halves of every pair fail char.IsLetter, so the two
    /// ends meet in the middle and the passage is found by search but never
    /// marked in the reader.
    /// </summary>
    [Fact]
    public void TrimToLetters_KeepsAnAstralWordWhole()
    {
        var text = $"«{Cuneiform}»";

        var trimmed = LetterRuns.TrimToLetters(text, 0, text.Length - 1, out var start, out var end);

        Assert.True(trimmed);
        Assert.Equal(Cuneiform, text.Substring(start, end - start + 1));
    }

    /// <summary>
    /// A span of pure punctuation still reports that it holds no letters,
    /// rather than returning a reversed or negative span to the caller.
    /// </summary>
    [Fact]
    public void TrimToLetters_ReportsWhenThereAreNoLetters()
    {
        const string text = "-- ... --";

        Assert.False(LetterRuns.TrimToLetters(text, 0, text.Length - 1, out _, out _));
    }

    /// <summary>
    /// Real corpora contain broken pairs - a passage cut mid-character by an
    /// exporter, or a file that lost its last byte. A lone surrogate must not
    /// throw and must not leave the walk standing on half a character, which
    /// would spin forever during an index build over millions of lines.
    /// </summary>
    [Fact]
    public void Scan_SurvivesAnUnpairedSurrogate()
    {
        // Written as a code point, not an escape: a \u escape for a lone
        // surrogate does not survive every editor that touches this file.
        var broken = "alpha " + ((char)0xD80C) + " beta";   // high surrogate, no partner

        var words = LetterRuns.Words(broken).ToList();

        Assert.Equal(new[] { "alpha", "beta" }, words);
    }

    /// <summary>
    /// The sigma fold has to survive the move from chars to runes, because
    /// every Greek search for a word ending in sigma depends on it and the
    /// fold is now written against Rune.Value rather than a char.
    /// </summary>
    [Theory]
    [InlineData("λόγος", "λογοσ")]
    [InlineData("ΛΌΓΟΣ", "λογοσ")]
    public void Normalize_StillFoldsSigma_AfterTheRuneChange(string input, string expected)
    {
        Assert.Equal(expected, WordNormalizer.Normalize(input));
    }
}
