using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// MorphologyDecoder existed in two copies - one in Core, one in UI, both
/// declaring ClassicaCodex.Core.MorphologyDecoder. C# resolves a
/// source-declared type over one from a referenced assembly without saying
/// anything, so the UI silently used its own copy, and the WordNet English
/// support added to the Core copy never reached Word Study. The duplicate is
/// gone; these tests are here so that if a second copy ever reappears, the
/// divergence shows up as a red test rather than as a feature that quietly
/// stopped working.
/// </summary>
public class MorphologyDecoderTests
{
    // --- Greek: the 9-character AGDT positional tag ------------------------

    [Fact]
    public void Decode_ReadsNineCharacterGreekTag()
    {
        var parse = MorphologyDecoder.Decode("n-s---mn-");

        Assert.True(parse.IsDecoded);
        Assert.Equal("noun", parse.PartOfSpeech);
        Assert.NotEmpty(parse.Description);
    }

    [Fact]
    public void Decode_ReadsVerbTagAsVerb()
    {
        var parse = MorphologyDecoder.Decode("v-sppemn-");

        Assert.True(parse.IsDecoded);
        Assert.Equal("verb", parse.PartOfSpeech);
    }

    // --- Latin: the coarser prefix labels ---------------------------------

    [Theory]
    [InlineData("NOMcom", "common noun")]
    [InlineData("NOMpro", "proper noun")]
    [InlineData("ADJ", "adjective")]
    public void Decode_ReadsLatinPrefixLabels(string tag, string expected)
    {
        var parse = MorphologyDecoder.Decode(tag);

        Assert.True(parse.IsDecoded);
        Assert.Equal(expected, parse.PartOfSpeech);
    }

    /// <summary>
    /// NOMcom must not be shadowed by a shorter NOM prefix - longest match
    /// wins, or every common noun reads as whatever NOM maps to.
    /// </summary>
    [Fact]
    public void Decode_PrefersLongestLatinPrefix()
    {
        Assert.Equal("common noun", MorphologyDecoder.Decode("NOMcom").PartOfSpeech);
        Assert.Equal("proper noun", MorphologyDecoder.Decode("NOMpro").PartOfSpeech);
    }

    // --- English / WordNet -------------------------------------------------

    /// <summary>
    /// This is the exact case the duplicate class broke. "verb" starts with
    /// the Latin code "VER", so without the English check running first it
    /// decodes by coincidence - and "noun" matches no Latin prefix at all, so
    /// it fell through and displayed raw. Two WordNet word classes, same
    /// data, inconsistent presentation.
    /// </summary>
    [Theory]
    [InlineData("noun")]
    [InlineData("verb")]
    [InlineData("adjective")]
    [InlineData("adverb")]
    public void Decode_ReadsWordNetWordClasses(string tag)
    {
        var parse = MorphologyDecoder.Decode(tag);

        Assert.True(parse.IsDecoded);
        Assert.Equal(tag, parse.PartOfSpeech);
        Assert.Equal(tag, parse.Description);
    }

    // --- Unrecognized input ------------------------------------------------

    /// <summary>
    /// The decoder's central rule: never invent a parse. An unknown tag comes
    /// back undecoded with RawTag intact, so the UI shows the raw string
    /// rather than a confidently wrong grammatical description.
    /// </summary>
    [Theory]
    [InlineData("zzz-unknown")]
    [InlineData("!!")]
    public void Decode_LeavesUnknownTagsUndecoded(string tag)
    {
        var parse = MorphologyDecoder.Decode(tag);

        Assert.False(parse.IsDecoded);
        Assert.Equal(tag, parse.RawTag);
        Assert.Equal(tag, parse.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decode_HandlesMissingTag(string? tag)
    {
        var parse = MorphologyDecoder.Decode(tag);

        Assert.False(parse.IsDecoded);
        Assert.Equal(string.Empty, parse.RawTag);
    }

    [Fact]
    public void Decode_TrimsSurroundingWhitespace()
    {
        Assert.Equal(
            MorphologyDecoder.Decode("n-s---mn-").Description,
            MorphologyDecoder.Decode("  n-s---mn-  ").Description);
    }

    // --- Glob patterns for morphology search -------------------------------

    /// <summary>
    /// Both corpora are searched at once, and their tags are different
    /// lengths, so a selection has to produce a pattern for each. A 9-char
    /// pattern silently applied to 10-char tags matches nothing.
    /// </summary>
    [Fact]
    public void BuildGlobPatterns_ProducesBothTagWidths()
    {
        var selections = new Dictionary<int, char> { [0] = 'v' };

        var (nine, ten) = MorphologyDecoder.BuildGlobPatterns(selections);

        Assert.Equal(MorphologyDecoder.AgdtTagLength, nine.Length);
        Assert.Equal(MorphologyDecoder.ExtendedTagLength, ten.Length);
    }

    [Fact]
    public void BuildGlobPatterns_WithNoSelectionsMatchesAnything()
    {
        var (nine, ten) = MorphologyDecoder.BuildGlobPatterns(new Dictionary<int, char>());

        Assert.All(nine, c => Assert.Equal('?', c));
        Assert.All(ten, c => Assert.Equal('?', c));
    }

    // ---- The Latin feature string ----
    //
    // Carried on every token of the Latin corpus and read by nothing until
    // now, so every Latin word in the library came out as a bare "verb" or
    // "common noun". These are real tags from the files on disk.

    [Fact]
    public void LatinFeatures_ReadANounsGenderCaseAndNumber()
    {
        var parse = MorphologyDecoder.Decode("NOMcom|Case=Gen|Numb=Sing");

        Assert.True(parse.IsDecoded);
        Assert.Equal("common noun", parse.PartOfSpeech);
        Assert.Contains("genitive", parse.Description);
        Assert.Contains("singular", parse.Description);
    }

    /// <summary>
    /// A finite verb reads tense-voice-mood then person and number, which is
    /// how a grammar states it and not the order the attributes arrive in.
    /// </summary>
    [Fact]
    public void LatinFeatures_ReadAFiniteVerbInGrammarOrder()
    {
        var parse = MorphologyDecoder.Decode("VER|Mood=Ind|Tense=Pres|Voice=Act|Pers=3|Numb=Sing");

        Assert.True(parse.IsDecoded);
        Assert.Equal("verb", parse.PartOfSpeech);
        Assert.Equal("verb: present active indicative 3rd person singular", parse.Description);
    }

    /// <summary>
    /// The textbook Latin ambiguity, and the one the README promises is shown
    /// rather than guessed at. Both of nostra's readings are in its own
    /// source file; before this they decoded to the same word, "pronoun",
    /// twice over, so the panel showed three identical lines.
    /// </summary>
    [Fact]
    public void LatinFeatures_TellNostrasTwoReadingsApart()
    {
        var ablative = MorphologyDecoder.Decode("ADJqua|Case=Abl|Numb=Sing|Gend=Fem");
        var neuterPlural = MorphologyDecoder.Decode("ADJqua|Case=Nom|Numb=Plur|Gend=Neut");

        Assert.NotEqual(ablative.Description, neuterPlural.Description);
        Assert.Contains("ablative", ablative.Description);
        Assert.Contains("feminine", ablative.Description);
        Assert.Contains("neuter", neuterPlural.Description);
        Assert.Contains("plural", neuterPlural.Description);
    }

    /// <summary>
    /// Positive degree is the unmarked case; saying it adds nothing, exactly
    /// as the positional decoder leaves it unsaid.
    /// </summary>
    [Fact]
    public void LatinFeatures_LeavePositiveDegreeUnsaid()
    {
        var parse = MorphologyDecoder.Decode("ADJqua|Case=Nom|Numb=Sing|Gend=Masc|Deg=Pos");

        Assert.DoesNotContain("positive", parse.Description);
        Assert.Contains("superlative", MorphologyDecoder.Decode("ADJqua|Case=Nom|Deg=Sup").Description);
    }

    /// <summary>
    /// A category with no features still decodes - that is what the corpus
    /// gives for indeclinables, and what every Latin word gave before.
    /// </summary>
    [Fact]
    public void LatinFeatures_StillDecodeACategoryOnItsOwn()
    {
        var parse = MorphologyDecoder.Decode("CON");

        Assert.True(parse.IsDecoded);
        Assert.Equal("conjunction", parse.PartOfSpeech);
    }

    /// <summary>
    /// An unrecognised feature is shown rather than dropped. This corpus is
    /// not exhaustively documented, and a value passed through raw is worth
    /// more than one silently lost.
    /// </summary>
    [Fact]
    public void LatinFeatures_PassThroughAValueTheyDoNotKnow()
    {
        var parse = MorphologyDecoder.Decode("NOMcom|Case=Abessive|Numb=Sing");

        Assert.True(parse.IsDecoded);
        Assert.Contains("abessive", parse.Description);
    }

    /// <summary>
    /// The Greek tags must be untouched by any of this - they are positional,
    /// carry no '=', and were already right.
    /// </summary>
    [Fact]
    public void LatinFeatures_DoNotDisturbTheGreekTags()
    {
        var greek = MorphologyDecoder.Decode("n--s---mn-");

        Assert.True(greek.IsDecoded);
        Assert.Contains("nominative", greek.Description);
        Assert.Contains("singular", greek.Description);
    }
}
