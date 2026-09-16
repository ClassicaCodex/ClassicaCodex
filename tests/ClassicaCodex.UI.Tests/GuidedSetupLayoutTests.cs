using System.Reflection;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// The Guided Setup wizard at a display scaling other than 100%.
///
/// <b>What went wrong.</b> RenderStep assigns eleven coordinates every time a
/// step is shown, and all eleven were written as design pixels. That method
/// runs after WinForms has scaled the form, so at 150% it stamped the 100%
/// layout back over the scaled one - and it moved the path box without moving
/// the Browse button beside it, because the button is positioned once in the
/// constructor and was never reassigned here. The box landed inside the
/// description paragraph and the button was stranded below it. Reported from a
/// laptop at 150%; invisible at 100%, which is every machine it was written
/// and reviewed on.
///
/// <b>How these tests reach it.</b> The test process is 96 DPI and cannot be
/// made otherwise, so they scale the form by hand with Control.Scale - the
/// same pass WinForms runs on a high-DPI display - and then ask RenderStep to
/// lay out a step. The assertions are relationships rather than numbers, so
/// they hold at any scaling: the path row sits below the description, and the
/// button stays level with its box.
/// </summary>
public class GuidedSetupLayoutTests : IClassFixture<EmptyLibraryFixture>
{
    private const int DataFolderStep = 2;
    private const int DatabaseStep = 1;

    private static void AtScale(float factor, int step, Action<GuidedSetupForm, Control, Control, Control> check)
    {
        StaHarness.Run(_ =>
        {
            using var form = new GuidedSetupForm();
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();

            if (Math.Abs(factor - 1f) > 0.001f)
            {
                form.Scale(new SizeF(factor, factor));
                Application.DoEvents();
            }

            var type = typeof(GuidedSetupForm);
            type.GetField("_currentStep", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(form, step);
            type.GetMethod("RenderStep", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(form, null);

            Application.DoEvents();

            Control Field(string name) => (Control)type
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(form)!;

            check(form, Field("_descriptionScroll"), Field("_pathBox"), Field("_browseButton"));

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The exact reported symptom: at 150% the path box was moved up into the
    /// description and the Browse button was left behind.
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void ThePathRowStaysBelowTheDescription(float scaling)
    {
        AtScale(scaling, DataFolderStep, (_, description, path, browse) =>
        {
            Assert.True(path.Top >= description.Bottom,
                $"at {scaling:P0} the path box starts at {path.Top}, which is "
                + $"{description.Bottom - path.Top}px above the bottom of the description panel "
                + $"({description.Bottom}) - it is being drawn inside the paragraph.");

            Assert.True(browse.Visible, "Browse should be visible on the Download Folder step");

            Assert.True(Math.Abs(browse.Top - path.Top) <= Math.Max(6, (int)(6 * scaling)),
                $"at {scaling:P0} the Browse button is at {browse.Top} and the path box it belongs "
                + $"to is at {path.Top} - {Math.Abs(browse.Top - path.Top)}px apart. They have come "
                + "apart; see this test's summary.");
        });
    }

    /// <summary>
    /// The Database step shares the same row and the same code path, so it
    /// broke the same way and is worth its own case rather than being assumed.
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    public void TheDatabaseStepsPathRowStaysBelowItsDescriptionToo(float scaling)
    {
        AtScale(scaling, DatabaseStep, (_, description, path, browse) =>
        {
            Assert.True(path.Top >= description.Bottom,
                $"at {scaling:P0} the path box overlaps the description by "
                + $"{description.Bottom - path.Top}px");

            Assert.True(Math.Abs(browse.Top - path.Top) <= Math.Max(6, (int)(6 * scaling)),
                $"at {scaling:P0} Browse and the path box are "
                + $"{Math.Abs(browse.Top - path.Top)}px apart");
        });
    }

    /// <summary>
    /// A lint over the method that broke, because the mistake is one line
    /// away from returning and reads as completely ordinary: every coordinate
    /// assigned in RenderStep has to go through Scale, since that method runs
    /// after the form has been scaled.
    /// </summary>
    [Fact]
    public void RenderStepScalesEveryCoordinateItAssigns()
    {
        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "GuidedSetupForm.cs"));

        var start = source.IndexOf("private void RenderStep()", StringComparison.Ordinal);
        Assert.True(start > 0, "RenderStep has been renamed; this lint no longer guards anything");

        var body = source[start..];
        body = body[..body.IndexOf("\n    private ", start == 0 ? 0 : 1, StringComparison.Ordinal)];

        var raw = System.Text.RegularExpressions.Regex.Matches(
                body,
                @"\.(?:Top|Left|Width|Height)\s*=\s*(?<value>[^;]+);")
            .Where(m =>
            {
                var value = m.Groups["value"].Value;

                // A bare number, or a ternary between two bare numbers, with
                // no Scale() anywhere in it.
                return System.Text.RegularExpressions.Regex.IsMatch(value, @"\b\d+\b")
                       && !value.Contains("Scale(", StringComparison.Ordinal);
            })
            .Select(m => m.Value.Trim())
            .ToList();

        Assert.True(raw.Count == 0,
            "these coordinates in RenderStep are raw device pixels and will stamp the 100% layout "
            + "over the scaled one on any display above 100%:\n  " + string.Join("\n  ", raw));
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
