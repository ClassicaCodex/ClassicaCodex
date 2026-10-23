using ClassicaCodex.Ingestion.Goetia;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the 1904 Goetia out of Wikisource's wikitext, which keeps the
/// habits of the web transcription it was copied from: footnotes in blocks
/// numbered by page with bare digits for markers, a spirit's entry broken
/// across a page with and without "[paragraph continues]", and a number in
/// the text that looks like a marker and is not.
/// </summary>
public class GoetiaTests
{
    private const string Wiki = """
        {{PD-US}}
        {{header
         | title      = [[The Lesser Key of Solomon]]
        }}
        *[[/Title Page|Title Page]]

        ==The Initiated Interpretation Of Ceremonial Magic==
        It is loftily amusing to the student of Magical literature.

        This being true, all sense-impressions are dependent on changes in the brain, <ref>Thought is a secretion of the brain (Weissmann).</ref> we must include illusions.

        ==References==
        <References/>

        ==Preliminary Definition Of Magic==
        MAGIC is the Highest Knowledge of Natural Philosophy, 1 advanced in its works; so that true Agents 2 being applied.
        Footnotes

        15:1 This Preliminary Definition of Magic is found in very few Codices.

        15:2 Or Actives.

        ==Shemhamphorash==

        1.) BAEL.--The First Principal Spirit is a King ruling in the East, called Bael.

        (2.) AGARES.--The Second Spirit is a Duke called Agreas, or Agares.

        (3.) VASSAGO.--The Third Spirit is called Vassago

        (or Vasago). He is of the Order of Dominations. 1 He hath under his Command 6 Legions of Spirits.

        [paragraph continues] His Seal is this.

        (4.) SAMIGINA.--The Fourth Spirit is Samigina, in the Form of a Lion, 1 riding upon a Horse.

        THESE be the 72 Mighty Kings and Princes which King Solomon Commanded into a Vessel of Brass.

        Footnotes

        24:1 Or Dominions, as they are usually termed.

        26:1 Or "with the Face of a Lion."

        26:2 Should be 30.

        ==Observations==
        FIRST, thou shalt know and observe the Moon's Age for thy working.

        (HERE ENDETH THIS FIRST BOOK OF THE LEMEGETON, WHICH IS CALLED THE GOETIA.)

        *[[/Conjurations|Conjurations]]
        ==Preamble==

        Yse Conjuratiouns of ye Books Goetia in ye Lemegeton.

        ==Ye Fyrste Conjouratioun==
        I invoke and move thee, O thou, Spirit N.

        -------------

        Obey the mighty sounds!
        """;

    /// <summary>The sample stops at spirit 4, where the real list goes on to 72.</summary>
    private static IReadOnlyList<GoetiaWork> Works() =>
        GoetiaLoader.Parse(Wiki.Replace("THESE be", string.Concat(
            Enumerable.Range(5, 68).Select(n => $"({n}.) SPIRIT.--The spirit numbered {n}.\n\n")) + "THESE be"));

    private static Dictionary<string, string> Passages(string slug) =>
        Works().Single(w => w.Slug == slug).Passages.ToDictionary(p => p.Citation, p => p.Text);

    [Fact]
    public void TheBookIsFourWorks()
    {
        Assert.Equal(
            new[] { "spirits", "art", "initiated-interpretation", "conjurations" },
            Works().Select(w => w.Slug));
        Assert.Equal(new[] { false, false, true, true }, Works().Select(w => w.ByCrowley));
    }

    [Fact]
    public void ASpiritIsCitedByItsNumberAndKeepsItsWholeEntry()
    {
        var spirits = Passages("spirits");

        Assert.StartsWith("1.) BAEL.--", spirits["1"]);
        Assert.Equal(
            "(3.) VASSAGO.--The Third Spirit is called Vassago (or Vasago). He is of the Order of Dominations. "
            + "He hath under his Command 6 Legions of Spirits. His Seal is this.",
            spirits["3"]);
        Assert.StartsWith("THESE be the 72", spirits["end"]);
        Assert.Equal("(72.) SPIRIT.--The spirit numbered 72.", spirits["72"]);
    }

    /// <summary>
    /// The markers come out and the notes follow their passage. "6 Legions"
    /// stays: it is a number. 26:2's marker is not in the text, and the note
    /// says so.
    /// </summary>
    [Fact]
    public void FootnotesFollowThePassageTheirMarkerIsIn()
    {
        var spirits = Passages("spirits");

        Assert.Equal("Note: Or Dominions, as they are usually termed.", spirits["3.n1"]);
        Assert.Equal("Note: Or \"with the Face of a Lion.\"", spirits["4.n1"]);
        Assert.Equal("Note, its marker lost in this transcription: Should be 30.", spirits["4.n2"]);
        Assert.Equal("(4.) SAMIGINA.--The Fourth Spirit is Samigina, in the Form of a Lion, riding upon a Horse.", spirits["4"]);

        var art = Passages("art");
        Assert.Equal("MAGIC is the Highest Knowledge of Natural Philosophy, advanced in its works; so that true Agents being applied.", art["1.1"]);
        Assert.Equal("Note: Or Actives.", art["1.1.n2"]);
    }

    [Fact]
    public void TheArtIsCitedBySectionAndParagraph()
    {
        var art = Passages("art");

        Assert.Equal("Preliminary Definition Of Magic", art["1.head"]);
        Assert.Equal("Observations", art["2.head"]);
        Assert.StartsWith("(HERE ENDETH", art["2.2"]);
        Assert.DoesNotContain(art.Values, t => t.Contains("Conjurations|") || t.Contains("[["));
    }

    [Fact]
    public void CrowleysReferencesBecomeNotes()
    {
        var essay = Passages("initiated-interpretation");

        Assert.Equal(
            "This being true, all sense-impressions are dependent on changes in the brain, we must include illusions.",
            essay["2"]);
        Assert.Equal("Note: Thought is a secretion of the brain (Weissmann).", essay["2.n1"]);
    }

    [Fact]
    public void CrowleysConjurationsLoseOnlyTheRules()
    {
        var conjurations = Passages("conjurations");

        Assert.Equal("Preamble", conjurations["1.head"]);
        Assert.Equal("I invoke and move thee, O thou, Spirit N.", conjurations["2.1"]);
        Assert.Equal("Obey the mighty sounds!", conjurations["2.2"]);
    }

    [Fact]
    public void AListThatStopsShortIsRefused() =>
        Assert.Throws<InvalidDataException>(() => GoetiaLoader.Parse(Wiki));
}
