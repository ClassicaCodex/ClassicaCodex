using System.Text.RegularExpressions;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// No list may be handed a whole passage as a row.
///
/// This is a lint rather than a unit test, and it is written that way on
/// purpose: the bug it guards cannot be reached from this project. It lives in
/// GDI+, needs a WinForms message loop, and only fires on a passage that is
/// both very long and awkwardly encoded. What CAN be checked from here is the
/// one line of code that lets it happen.
///
/// <b>What went wrong.</b> ReadingTheme turns on HorizontalScrollbar for every
/// ListBox, which makes WinForms measure each item with Graphics.MeasureString
/// as it is added. GDI+ has a simple text path with no practical length limit
/// and a complex shaping path - entered by a format character, a combining
/// mark, a control character, a right-to-left script or an unmapped codepoint
/// - and the complex path fails above roughly 32,000 characters in one run,
/// reporting only "A generic error occurred in GDI+".
///
/// Both halves are needed, and that is why it survived so long. A long plain
/// passage measures fine at any length; a short one full of soft hyphens
/// measures fine too. It was investigated once before, blamed on private-use
/// codepoints, could not be reproduced, and was written off - because each
/// condition was tested alone.
///
/// This library has 22 passages that meet both: Migne and CSEL works ingested
/// as whole sections, carrying soft hyphens left at the line breaks of the
/// printed page by OCR. Facundus of Hermiane is 32,132 characters with 195 of
/// them, and it was row 871 of an Auto-Tag search for "Athena". Measured
/// directly: of those 22, eighteen crash the list untruncated and all 22 are
/// accepted once the row is cut.
///
/// So every list that shows passages puts its rows through
/// ListResultHelpers.RowText, and this fails if a new one forgets. The reader
/// panes are exempt and must stay that way, being the one list that cannot
/// shorten what it shows.
///
/// What keeps them exempt is one line in ReadingTheme, and it is worth being
/// exact about which: the switch there has "case SyncListView" ahead of "case
/// ListBox", so the reader never receives the HorizontalScrollbar = true that
/// every other list gets, and the scroll-extent measurement that crashes is
/// never asked for. SyncListView itself never sets the property either way -
/// so deleting or reordering that one case would hand the reader the
/// scrollbar without a line of SyncListView changing. That is what the second
/// test below guards; it used to guard the absence of a line that was never
/// there.
///
/// That exemption used to be described as what let the reader show a
/// 77,659-character passage whole. It never did: a list row cannot exceed 255
/// pixels, so what the reader showed was as much of such a passage as fitted.
/// It shows them whole now because it divides them across rows - see
/// ReaderRowSplitter - which is a different mechanism, and does not change
/// what this test is guarding.
/// </summary>
public class PassageRowLengthTests
{
    /// <summary>
    /// Matches an Items.Add whose interpolated row ends in a bare .Text -
    /// "{r.Text}", "{p.Text}", "{node.Text}" - which is a whole passage going
    /// into a row unbounded.
    /// </summary>
    private static readonly Regex RawPassageRow =
        new(@"Items\.Add\((\s|@?\$"")[^;]*\{\s*[A-Za-z_][A-Za-z0-9_\.\[\]]*\.Text\s*\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Items.Add(x.Text) with no interpolation at all - the same thing.</summary>
    private static readonly Regex BarePassageRow =
        new(@"Items\.Add\(\s*[A-Za-z_][A-Za-z0-9_\.\[\]]*\.Text\s*\)", RegexOptions.Compiled);

    [Fact]
    public void NoListIsHandedAWholePassageAsARow()
    {
        var ui = UiSourceDirectory();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ui, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (Match match in RawPassageRow.Matches(source).Concat(BarePassageRow.Matches(source)))
            {
                // The point is the cut, not where it is made - a caller that
                // trimmed the text earlier is fine.
                if (match.Value.Contains("RowText", StringComparison.Ordinal)) continue;

                var line = source.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{name}:{line}  {Collapse(match.Value)}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These pass a whole passage to a list row. Wrap the text in "
            + "ListResultHelpers.RowText - see this test's summary for what GDI+ does with "
            + "an unbounded one:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The reader must keep its exemption. If it ever gains a horizontal
    /// scrollbar it starts being measured, and it is the one list that
    /// genuinely cannot truncate what it shows.
    ///
    /// Guarded where the exemption actually lives - ReadingTheme's switch,
    /// where SyncListView must be matched before ListBox - rather than in
    /// SyncListView, which never mentions the property at all.
    /// </summary>
    [Fact]
    public void TheReaderIsNeverMeasured()
    {
        var theme = File.ReadAllText(Path.Combine(UiSourceDirectory(), "ReadingTheme.cs"));

        var reader = theme.IndexOf("case SyncListView", StringComparison.Ordinal);
        var anyList = theme.IndexOf("case ListBox", StringComparison.Ordinal);

        Assert.True(reader >= 0, "ReadingTheme no longer has a case for SyncListView, so the reader panes "
            + "now fall through to the ListBox case and are given a horizontal scrollbar. See this test's summary.");

        Assert.True(anyList < 0 || reader < anyList,
            "ReadingTheme matches ListBox before SyncListView, so the reader panes are now given "
            + "HorizontalScrollbar = true and every row they are handed gets measured by GDI+. "
            + "See this test's summary for what that does to a long passage.");

        // Being reached first is only half of it. The case has to also not do
        // the thing, or ordering it correctly buys nothing - so read what sits
        // between the case label and its break. Starting at the label rather
        // than above it keeps the explanatory comment, which names the
        // property in order to explain it, out of the scan.
        var readerCase = theme[reader..theme.IndexOf("break;", reader, StringComparison.Ordinal)];

        Assert.DoesNotContain("HorizontalScrollbar", readerCase, StringComparison.Ordinal);

        // Matched however it is spelled. Every site in this codebase but one
        // assigns the ListHorizontalScrollbar constant rather than the
        // literal, so looking for "= true" would miss the idiom the code
        // actually uses.
        Assert.DoesNotContain("HorizontalScrollbar =",
            File.ReadAllText(Path.Combine(UiSourceDirectory(), "SyncListView.cs")), StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from the test binary to the repository, which is wherever the
    /// solution file is. Beats a relative path from the output folder, which
    /// changes with configuration and target framework.
    /// </summary>
    private static string UiSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ClassicaCodex.sln")))
            directory = directory.Parent;

        Assert.True(directory != null, "Could not find ClassicaCodex.sln above " + AppContext.BaseDirectory);
        var ui = Path.Combine(directory!.FullName, "src", "ClassicaCodex.UI");
        Assert.True(Directory.Exists(ui), "Expected the UI sources at " + ui);
        return ui;
    }

    private static string Collapse(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim() is { Length: > 96 } long_ ? long_[..96] + "…" : Regex.Replace(text, @"\s+", " ").Trim();
}
