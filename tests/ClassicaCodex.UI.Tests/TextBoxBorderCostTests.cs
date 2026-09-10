using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Why a text box is built with its border already set.
///
/// Reported as "Cross-Language Echo takes a bit just to open". It was not the
/// form: it was the passage the reader had right-clicked. The form puts that
/// passage into a read-only text box in its constructor, and the theme sets
/// every text box's BorderStyle afterwards, at Load. WinForms recreates a text
/// box's window handle when its BorderStyle changes, and recreating the handle
/// re-inserts everything the box holds - so the cost was proportional to the
/// length of whatever had been right-clicked. Measured: 3 ms for a
/// 3,265-character paragraph, 164 ms at 41,475, and 1,650 ms for the
/// 468,865-character passage that is this corpus's longest.
///
/// The assignment is free when the value already matches, because WinForms
/// short-circuits it - so the fix is to build the box with the border the theme
/// is going to want. A guard inside the theme would not have helped: the first
/// apply is precisely the one where the value differs.
///
/// These tests use bare framework controls and do not touch the persisted
/// theme - ReadingTheme.SetMode and Toggle write to the user's theme.txt, so
/// everything here has to hold in whichever mode the machine is in.
/// </summary>
public class TextBoxBorderCostTests
{
    /// <summary>
    /// The load-bearing fact. If the theme ever wants a different border from
    /// the one boxes are built with, every one of them silently goes back to
    /// paying for a handle recreation, and nothing else would catch it.
    /// </summary>
    [Fact]
    public void TheThemeSetsExactlyTheBorderTextBoxesAreBuiltWith()
    {
        using var form = new Form();
        using var box = new TextBox { Multiline = true };
        form.Controls.Add(box);

        ReadingTheme.Apply(form);

        Assert.Equal(ReadingTheme.TextBoxBorder, box.BorderStyle);
    }

    /// <summary>
    /// A box built the way the affected forms now build theirs is left alone by
    /// the theme - which is the whole point, since "left alone" is what costs
    /// nothing.
    /// </summary>
    [Fact]
    public void ABoxBuiltWithThatBorderIsNotChangedByTheming()
    {
        using var form = new Form();
        using var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = ReadingTheme.TextBoxBorder,
            Text = new string('x', 50_000)
        };
        form.Controls.Add(box);

        var before = box.BorderStyle;
        ReadingTheme.Apply(form);

        Assert.Equal(before, box.BorderStyle);
        Assert.Equal(50_000, box.Text.Length);
    }

    /// <summary>
    /// And the default is genuinely different, so the saving is real rather
    /// than a coincidence of WinForms happening to agree with the theme.
    /// </summary>
    [Fact]
    public void TheWinFormsDefaultIsNotWhatTheThemeWants()
    {
        using var box = new TextBox();

        Assert.NotEqual(ReadingTheme.TextBoxBorder, box.BorderStyle);
    }

    /// <summary>
    /// A RichTextBox goes through the same branch of the theme - it is matched
    /// on TextBoxBase, not TextBox - so it carries the same trap.
    /// </summary>
    [Fact]
    public void ARichTextBoxIsThemedTheSameWay()
    {
        using var form = new Form();
        using var box = new RichTextBox();
        form.Controls.Add(box);

        ReadingTheme.Apply(form);

        Assert.Equal(ReadingTheme.TextBoxBorder, box.BorderStyle);
    }

    /// <summary>
    /// Theming a box twice must stay a no-op the second time. The theme is
    /// re-applied whenever the mode changes, and a box holding a long passage
    /// would otherwise pay the recreation again on every change.
    /// </summary>
    [Fact]
    public void ApplyingTheThemeTwiceLeavesTheBorderAlone()
    {
        using var form = new Form();
        using var box = new TextBox { Multiline = true, Text = new string('x', 10_000) };
        form.Controls.Add(box);

        ReadingTheme.Apply(form);
        var afterFirst = box.BorderStyle;
        ReadingTheme.Apply(form);

        Assert.Equal(afterFirst, box.BorderStyle);
        Assert.Equal(ReadingTheme.TextBoxBorder, box.BorderStyle);
    }
}
