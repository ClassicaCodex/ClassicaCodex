using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// What a result list is allowed to put in a row, and where in the passage
/// that row is taken from.
///
/// Both rules exist because of the same corpus fact: some of this library is
/// ingested as whole sections rather than lines, so a single "passage" can
/// run to tens of thousands of characters. Handing one of those to a list
/// crashes GDI+ (see PassageRowLengthTests), and taking its first 400
/// characters can miss the reason the row is in the list at all.
/// </summary>
public class ResultRowTests
{
    [Fact]
    public void ShortTextIsLeftAlone()
    {
        Assert.Equal("Sing, goddess", ListResultHelpers.RowText("Sing, goddess"));
    }

    [Fact]
    public void NullAndEmptyBecomeEmpty()
    {
        Assert.Equal(string.Empty, ListResultHelpers.RowText(null));
        Assert.Equal(string.Empty, ListResultHelpers.RowText(string.Empty));
    }

    [Fact]
    public void LongTextIsCutToTheLimit()
    {
        var row = ListResultHelpers.RowText(new string('a', 5000), 400);

        Assert.Equal(401, row.Length); // 400 plus the ellipsis
        Assert.EndsWith("…", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// Cutting mid-pair would leave half a character, which is what a lone
    /// high surrogate is - and the corpus has real astral-plane codepoints
    /// in it, so this is reachable rather than theoretical.
    /// </summary>
    [Fact]
    public void TheCutNeverSplitsASurrogatePair()
    {
        // One astral character (two UTF-16 units) repeated, so a cut at any
        // odd index would land inside a pair.
        var text = string.Concat(Enumerable.Repeat("\U00010300", 500));

        for (var limit = 10; limit < 40; limit++)
        {
            var row = ListResultHelpers.RowText(text, limit);
            var body = row.TrimEnd('…');

            Assert.False(char.IsHighSurrogate(body[^1]),
                $"limit {limit} left a dangling high surrogate");
        }
    }

    // ---- RowTextAround ----------------------------------------------------

    [Fact]
    public void AWindowIsOnlyNeededWhenTheTextIsTooLong()
    {
        const string text = "Sing, goddess, the wrath of Achilles";

        Assert.Equal(text, ListResultHelpers.RowTextAround(text, 28, 8));
    }

    [Fact]
    public void WithNoMatchTheWindowIsTheOpening()
    {
        var text = new string('a', 5000);

        Assert.Equal(ListResultHelpers.RowText(text, 400),
                     ListResultHelpers.RowTextAround(text, -1, 0, 400));
    }

    /// <summary>
    /// The case that matters: a match thousands of characters into a section
    /// has to appear in the row, or the list is asking you to confirm a hit
    /// you cannot see.
    /// </summary>
    [Fact]
    public void AMatchPastTheCutIsStillVisible()
    {
        var text = new string('a', 4000) + "Athena" + new string('b', 4000);

        var row = ListResultHelpers.RowTextAround(text, 4000, "Athena".Length, 400);

        Assert.Contains("Athena", row, StringComparison.Ordinal);
        Assert.StartsWith("…", row, StringComparison.Ordinal);
        Assert.EndsWith("…", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWindowKeepsSomeTextBeforeTheMatch()
    {
        var text = new string('a', 2000) + "Athena" + new string('b', 2000);

        var row = ListResultHelpers.RowTextAround(text, 2000, "Athena".Length, 400);

        // Something precedes the match, so it does not read as if the passage
        // begins there.
        Assert.True(row.IndexOf("Athena", StringComparison.Ordinal) > 1,
            "the match should not sit at the very start of the window");
    }

    [Fact]
    public void AMatchNearTheStartDoesNotGainALeadingEllipsis()
    {
        var text = "Athena " + new string('b', 5000);

        var row = ListResultHelpers.RowTextAround(text, 0, "Athena".Length, 400);

        Assert.StartsWith("Athena", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWindowStaysWithinTheLimit()
    {
        var text = new string('a', 4000) + "Athena" + new string('b', 4000);

        var row = ListResultHelpers.RowTextAround(text, 4000, "Athena".Length, 400);

        Assert.True(row.Trim('…').Length <= 400, $"window was {row.Trim('…').Length} characters");
    }

    [Fact]
    public void AMatchLongerThanTheWindowStillShowsItsOpening()
    {
        var text = new string('a', 1000) + new string('x', 2000) + new string('b', 1000);

        var row = ListResultHelpers.RowTextAround(text, 1000, 2000, 400);

        Assert.Contains("x", row, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutOfRangeMatchFallsBackRatherThanThrowing()
    {
        var text = new string('a', 5000);

        Assert.Equal(ListResultHelpers.RowText(text, 400),
                     ListResultHelpers.RowTextAround(text, 99999, 6, 400));
    }
}
