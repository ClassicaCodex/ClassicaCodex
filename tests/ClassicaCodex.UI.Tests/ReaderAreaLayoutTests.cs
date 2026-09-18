using System.Text.RegularExpressions;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// The main window's reader area at a display scaling other than 100%.
///
/// <b>What went wrong.</b> RelayoutReaderArea runs on Resize and on Shown -
/// both after WinForms has scaled the window - and put the reader's left edge
/// at a literal 320. That is a design pixel, and the library column beside it
/// is not: the column is scaled with everything else, so above 100% it grew
/// past 320 while the reader stayed put. The reader's left edge carries the
/// dropdown naming the edition being read, the column carries the author
/// filter box, and the filter box is added to Controls first - index 0 being
/// the FRONT of the WinForms z-order - so the box was drawn over the dropdown.
/// Reported from a laptop as the filter box "sometimes" covering the work
/// name, which is exactly what 125% looks like: six pixels of overlap, enough
/// to clip the dropdown's edge and not enough to look deliberate.
///
/// Measured with the main window's own design coordinates, before the fix:
///
/// <code>
///         library column ends   reader started at   overlap
/// 100%    310                   320                 none
/// 125%    387                   320                 67px
/// 150%    465                   320                 145px
/// 200%    620                   320                 300px
/// </code>
///
/// <b>How these tests reach it.</b> MainForm cannot be constructed here - it
/// loads a library and saves a reading position - so the library column is
/// rebuilt from bare framework controls at MainForm's own design coordinates
/// and scaled by hand with Control.Scale, the same pass a high-DPI display
/// runs. DpiScaling.DpiForTests supplies the other half, so the code under
/// test sees the same factor the controls were moved by; without it the
/// measurement would be a mixture that cannot occur on a real machine.
/// </summary>
[Collection(SharedProcessStateCollection.Name)]
public class ReaderAreaLayoutTests : IDisposable
{
    /// <summary>Never leave a simulated DPI behind for the next test.</summary>
    public void Dispose() => DpiScaling.DpiForTests = null;

    /// <summary>
    /// MainForm's library column, in design pixels: the tree, and the row of
    /// controls above it. Kept here as the same literals MainForm uses, so
    /// that a change to the column's width which is not reflected here shows
    /// up as these tests measuring something the window no longer has.
    /// </summary>
    private static (Rectangle Tree, Rectangle FilterBox, Rectangle FavouritesStar, Rectangle Toggle)
        DesignColumn() =>
        (new Rectangle(10, 82, 300, 658), new Rectangle(72, 54, 190, 23),
         new Rectangle(268, 54, 42, 24),
         // The show/hide button. It is the one control in this column that
         // stays when the library is hidden, and it is on the reader's own
         // top row, which is why it needs to be here.
         new Rectangle(10, 54, 36, 24));

    /// <summary>
    /// Runs the check against a library column scaled the way a high-DPI
    /// display would scale it. The controls are bare framework ones - no
    /// ClassicaCodex form is constructed.
    /// </summary>
    private static void AtScale(
        float factor, Action<Rectangle, Rectangle, Rectangle, Func<int, int>> check)
    {
        StaHarness.Run(harness =>
        {
            // Its own window rather than the harness's, because the column
            // has to be laid out at MainForm's coordinates and then scaled
            // as a whole - which is what Control.Scale does to a form.
            var (treeBounds, filterBounds, starBounds, toggleBounds) = DesignColumn();

            using var host = new Form { ClientSize = new Size(1840, 800), ShowInTaskbar = false };
            var tree = new TreeView { Bounds = treeBounds };
            var filter = new TextBox { Bounds = filterBounds };
            var star = new CheckBox { Bounds = starBounds };
            var toggle = new Button { Bounds = toggleBounds };

            host.Controls.Add(tree);
            host.Controls.Add(filter);
            host.Controls.Add(star);
            host.Controls.Add(toggle);
            // A control with no handle measures nothing - see StaHarness.
            host.CreateControl();
            _ = host.Handle;

            DpiScaling.DpiForTests = 96f * factor;

            if (Math.Abs(factor - 1f) > 0.001f)
            {
                host.Scale(new SizeF(factor, factor));
                Application.DoEvents();
            }

            // The column is the union of everything in it, which is what
            // MainForm now measures rather than assuming the tree is widest.
            var column = Rectangle.Union(Rectangle.Union(tree.Bounds, filter.Bounds), star.Bounds);

            check(host.ClientRectangle, column, toggle.Bounds, n => DpiScaling.Scale(host, n));

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The reported symptom, as a rule: nothing in the library column may
    /// overhang the reader, at any scaling.
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void TheReaderStartsAfterTheLibraryColumn(float scaling)
    {
        AtScale(scaling, (client, column, toggle, scale) =>
        {
            var reader = ReaderAreaLayout.For(
                client.Size, scale(54), column.Right, toggle.Right, libraryCollapsed: false, scale);

            Assert.True(reader.Left >= column.Right,
                $"at {scaling:P0} the library column ends at {column.Right} and the reader starts at "
                + $"{reader.Left} - it is {column.Right - reader.Left}px underneath the column. The "
                + "author filter box is in front of the reader in the z-order, so this is the filter "
                + "box drawn over the dropdown naming the edition being read.");

            Assert.Equal(scale(ReaderAreaLayout.LibraryGap), reader.Left - column.Right);
        });
    }

    /// <summary>
    /// At 100% the answer has to be exactly what it was before any of this -
    /// a literal 320 - or the fix has quietly moved the window every existing
    /// reader already has.
    /// </summary>
    [Fact]
    public void AtOneHundredPercentNothingMoves()
    {
        AtScale(1.0f, (client, column, toggle, scale) =>
        {
            var reader = ReaderAreaLayout.For(
                client.Size, 54, column.Right, toggle.Right, libraryCollapsed: false, scale);

            // 320 left, 20 off the right, 20 off the bottom - the three
            // numbers that used to be written into RelayoutReaderArea.
            Assert.Equal(320, reader.Left);
            Assert.Equal(client.Width - 340, reader.Width);
            Assert.Equal(client.Height - 74, reader.Height);
        });
    }

    /// <summary>
    /// With the library hidden the reader takes the column's width back - but
    /// not the strip the show/hide button still occupies.
    ///
    /// This is the second time this bug has been fixed. The first was the
    /// author filter box over the edition dropdown, above 100% only. This is
    /// the same collision with the library HIDDEN: the toggle is the one
    /// control that has to stay, it sits at the window margin on the reader's
    /// own top row, and the reader started at that same margin - so the
    /// button covered the first thirty-odd pixels of the dropdown, at every
    /// scaling including 100%. Reported as an author's name reading
    /// "ymous (menota)".
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void TheCollapsedReaderStartsClearOfTheButtonThatBringsTheLibraryBack(float scaling)
    {
        AtScale(scaling, (client, column, toggle, scale) =>
        {
            var reader = ReaderAreaLayout.For(
                client.Size, scale(54), column.Right, toggle.Right, libraryCollapsed: true, scale);

            Assert.True(reader.Left >= toggle.Right,
                $"at {scaling:P0} the show/hide button ends at {toggle.Right} and the reader starts "
                + $"at {reader.Left} - it is {toggle.Right - reader.Left}px underneath the button. "
                + "The button is in front of the reader in the z-order, so this is the button drawn "
                + "over the start of the dropdown naming the edition being read.");

            Assert.Equal(scale(ReaderAreaLayout.LibraryGap), reader.Left - toggle.Right);

            // And it must still be a real gain: the point of hiding the
            // library is the width, and nearly all of it comes back.
            Assert.True(reader.Left < column.Right,
                "with the library hidden the reader is supposed to reclaim its width");
        });
    }

    /// <summary>
    /// The right and bottom margins are margins at every scaling, not a
    /// 20-pixel strip that looks generous at 100% and cramped at 200%.
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void TheWindowMarginsScale(float scaling)
    {
        AtScale(scaling, (client, column, toggle, scale) =>
        {
            var top = scale(54);
            var reader = ReaderAreaLayout.For(
                client.Size, top, column.Right, toggle.Right, libraryCollapsed: false, scale);

            Assert.Equal(scale(ReaderAreaLayout.Margin), client.Width - reader.Right);
            Assert.Equal(scale(ReaderAreaLayout.Margin), client.Height - reader.Bottom);
        });
    }

    /// <summary>
    /// Dragged narrower than the reader can usefully be, it stops shrinking -
    /// and the floor is a scaled floor, since 400 device pixels at 200% is
    /// half the reading column it is at 100%.
    /// </summary>
    [Theory]
    [InlineData(1.0f)]
    [InlineData(2.0f)]
    public void TheMinimumSizeScalesToo(float scaling)
    {
        AtScale(scaling, (_, column, toggle, scale) =>
        {
            var reader = ReaderAreaLayout.For(
                new Size(120, 90), scale(54), column.Right, toggle.Right, libraryCollapsed: false, scale);

            Assert.Equal(scale(ReaderAreaLayout.MinimumWidth), reader.Width);
            Assert.Equal(scale(ReaderAreaLayout.MinimumHeight), reader.Height);
        });
    }

    /// <summary>
    /// A lint, for the same reason the canvas has one: this method's distances
    /// are applied after the window is scaled, so a bare number added here is
    /// a raw device pixel that undoes the scaling for whatever it touches -
    /// and the symptom only appears on a machine set to something other than
    /// 100%, which is not the machine it will be written on.
    /// </summary>
    [Fact]
    public void TheReaderAreaRelayoutScalesEveryDistance()
    {
        var body = RelayoutReaderAreaBody();

        var raw = Regex.Matches(body, @"(?<!Scale\()\b\d+\b")
            .Select(m => m.Value)
            .Where(value => value != "0")   // Math.Max(..., 0) clamps, it does not measure
            .Distinct()
            .ToList();

        Assert.True(raw.Count == 0,
            "these numbers are used raw inside RelayoutReaderArea rather than through Scale(): "
            + string.Join(", ", raw)
            + ". That method runs after WinForms has scaled the window, so a raw number stamps the "
            + "100% layout back over the scaled one - see ReaderAreaLayout for what that did to the "
            + "reader at 125% and above.");
    }

    /// <summary>
    /// And that the left edge is still measured from the column rather than
    /// from a number that happens to sit right of it today. The lint above
    /// would pass a hand-written <c>Scale(320)</c>, which is the fix someone
    /// reaches for first and which breaks again the moment the column changes
    /// width.
    /// </summary>
    [Fact]
    public void TheReaderEdgeIsMeasuredFromTheLibraryColumn()
    {
        var body = RelayoutReaderAreaBody();

        Assert.Contains("_libraryTree.Right", body);
        Assert.Contains("_favoritesOnlyCheck.Right", body);
        Assert.Contains("ReaderAreaLayout.For", body);
    }

    /// <summary>
    /// RelayoutReaderArea's code, with its comments stripped - they are full
    /// of the percentages and pixel counts the lint above is looking for.
    /// </summary>
    private static string RelayoutReaderAreaBody()
    {
        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "MainForm.cs"));

        var start = source.IndexOf("void RelayoutReaderArea()", StringComparison.Ordinal);
        Assert.True(start >= 0, "RelayoutReaderArea is no longer in MainForm.cs under that name");

        var body = source[start..];
        var end = body.IndexOf("\n        }", StringComparison.Ordinal);
        Assert.True(end > 0, "could not find the end of RelayoutReaderArea");

        return Regex.Replace(body[..end], @"//[^\r\n]*", string.Empty);
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
