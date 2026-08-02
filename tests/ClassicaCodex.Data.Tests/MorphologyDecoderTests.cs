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
}
