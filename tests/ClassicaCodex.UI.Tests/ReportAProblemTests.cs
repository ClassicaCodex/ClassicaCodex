using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// The route out of the application for somebody who has hit a bug.
///
/// It is worth testing for a reason that has nothing to do with the code
/// being hard: a broken report link fails silently and in the one direction
/// nobody is watching. The reader who would have told us it is broken is
/// exactly the reader it just failed, and they have no second way to say so.
/// Every check here is about the address and the instructions agreeing with
/// each other, because that is what actually rots - a repository moves, or a
/// caption is reworded, and the help text keeps confidently naming something
/// that is no longer on screen.
///
/// None of this constructs a window. These are read as source text, which is
/// how the rest of this project checks things whose only fault would be in
/// what they say rather than what they compute.
/// </summary>
public class ReportAProblemTests
{
    /// <summary>
    /// The label on the link, spelled once here and expected in two places:
    /// on the control, and quoted by the help text that tells the reader to
    /// look for it.
    /// </summary>
    private const string LinkCaption = "Found a problem? Report it on GitHub";

    /// <summary>
    /// It points at this project's own issue tracker, over HTTPS.
    ///
    /// Spelled out in full rather than assembled from parts, so that a
    /// mistyped owner or repository is a failure here rather than a link
    /// that opens somebody else's project and looks perfectly fine doing it.
    /// </summary>
    [Fact]
    public void TheAddressIsThisProjectsIssueTracker()
    {
        Assert.Equal("https://github.com/ClassicaCodex/ClassicaCodex/issues", ProjectLinks.Issues);
    }

    /// <summary>
    /// Nothing else writes that address out by hand.
    ///
    /// It used to live as a literal inside the crash dialog and nowhere else.
    /// Now that a second window carries it, a copy in each is a pair that can
    /// disagree - and the half that still pointed at the old repository would
    /// be a working link to the wrong place, which is the failure nobody
    /// reports because it does not look like one.
    /// </summary>
    [Fact]
    public void OnlyOneFileSpellsTheAddressOut()
    {
        var offenders = Directory
            .EnumerateFiles(UiSourceDirectory(), "*.cs", SearchOption.AllDirectories)
            // Build output, not source. obj\ carries generated files that are
            // rewritten on every build and are nobody's to fix.
            .Where(f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase)
                        && !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            .Where(f => !string.Equals(Path.GetFileName(f), "ProjectLinks.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains(ProjectLinks.Issues, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files write the issue tracker address out by hand instead of using "
            + "ProjectLinks.Issues, so they can drift apart from it: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// Help offers it as something to click, not as an address to copy.
    ///
    /// The topics are drawn in a read-only TextBox, which cannot carry a
    /// link at all - so prose in there could only ever print the address for
    /// typing in by hand, which was already the situation in the crash
    /// dialog. The link has to be its own control.
    /// </summary>
    [Fact]
    public void TheHelpWindowCarriesItAsALink()
    {
        var help = File.ReadAllText(Path.Combine(UiSourceDirectory(), "HelpForm.cs"));

        Assert.Contains("new LinkLabel", help, StringComparison.Ordinal);
        Assert.Contains("ProjectLinks.Issues", help, StringComparison.Ordinal);
        Assert.Contains("LinkClicked", help, StringComparison.Ordinal);
        Assert.Contains(LinkCaption, help, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the help text names the link by the caption it actually has.
    ///
    /// The "When something looks wrong" topic tells the reader to look for
    /// that wording at the bottom of the window. Reword the control without
    /// rewording the topic and the instruction sends them hunting for
    /// something that is not there - while every test that only checks the
    /// link exists carries on passing.
    /// </summary>
    [Fact]
    public void TheHelpTextQuotesTheCaptionTheLinkActuallyHas()
    {
        var help = File.ReadAllText(Path.Combine(UiSourceDirectory(), "HelpForm.cs"));

        // Once on the control, once inside the help prose that quotes it.
        var mentions = CountOccurrences(help, LinkCaption);
        Assert.True(mentions >= 2,
            "The help text should quote the link's caption so a reader can find it, but \""
            + LinkCaption + "\" appears " + mentions + " time(s) in HelpForm.cs.");

        // And the topic that a reader reaches after something has gone wrong
        // is the one that has to say where to take it.
        var topic = help[help.IndexOf("(\"When something looks wrong\"", StringComparison.Ordinal)..];
        Assert.Contains(LinkCaption, topic, StringComparison.Ordinal);
        Assert.Contains("errors.log", topic, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var at = text.IndexOf(needle, StringComparison.Ordinal);
        while (at >= 0)
        {
            count++;
            at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string UiSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ClassicaCodex.sln")))
            directory = directory.Parent;

        Assert.True(directory != null, "Could not find ClassicaCodex.sln above " + AppContext.BaseDirectory);
        return Path.Combine(directory!.FullName, "src", "ClassicaCodex.UI");
    }
}
