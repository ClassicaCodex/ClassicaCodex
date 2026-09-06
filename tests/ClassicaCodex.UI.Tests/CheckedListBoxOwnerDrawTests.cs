using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Why Auto-Tag does not highlight the matched forms in its rows.
///
/// It used to try. The form set <c>DrawMode = DrawMode.OwnerDrawFixed</c> on
/// its CheckedListBox and handled DrawItem, painting each row in parts with
/// the matched forms on a highlight - about sixty lines, including drawing
/// the checkbox by hand because owner-draw was believed to suppress it.
///
/// None of it ever ran. CheckedListBox overrides DrawMode with a setter that
/// discards the value and a getter that always answers Normal, so owner-draw
/// cannot be switched on and DrawItem is never raised. There is no compiler
/// warning and no exception; the assignment simply evaporates, which is why
/// the code sat there looking correct.
///
/// These tests record that behaviour rather than our own, deliberately. It is
/// the evidence for deleting a feature, and if a future .NET ever honours the
/// property these will fail - at which point the highlighting could come back
/// rather than staying deleted for a reason nobody can reconstruct.
/// </summary>
public class CheckedListBoxOwnerDrawTests
{
    [Fact]
    public void CheckedListBoxDiscardsDrawMode()
    {
        using var list = new CheckedListBox { DrawMode = DrawMode.OwnerDrawFixed };

        Assert.Equal(DrawMode.Normal, list.DrawMode);
    }

    [Fact]
    public void CheckedListBoxNeverRaisesDrawItem()
    {
        using var form = new Form();
        using var list = new CheckedListBox { DrawMode = DrawMode.OwnerDrawFixed, Dock = DockStyle.Fill };

        var draws = 0;
        list.DrawItem += (_, _) => draws++;

        form.Controls.Add(list);
        list.Items.Add("alpha");
        list.Items.Add("beta");

        // Force the handle and a paint cycle.
        form.Show();
        form.Refresh();
        Application.DoEvents();
        form.Close();

        Assert.Equal(0, draws);
    }

    /// <summary>
    /// The control test. A plain ListBox in the same conditions does raise
    /// DrawItem - so the zero above is CheckedListBox's behaviour, not this
    /// test failing to trigger a paint.
    /// </summary>
    [Fact]
    public void APlainListBoxDoesRaiseDrawItem()
    {
        using var form = new Form();
        using var list = new ListBox { DrawMode = DrawMode.OwnerDrawFixed, Dock = DockStyle.Fill };

        var draws = 0;
        list.DrawItem += (_, e) => { draws++; e.DrawBackground(); };

        form.Controls.Add(list);
        list.Items.Add("alpha");
        list.Items.Add("beta");

        form.Show();
        form.Refresh();
        Application.DoEvents();
        form.Close();

        Assert.True(draws > 0, "a plain ListBox should have painted its items");
        Assert.Equal(DrawMode.OwnerDrawFixed, list.DrawMode);
    }
}
