namespace ClassicaCodex.UI;

/// <summary>
/// Where the reader sits in the main window: to the right of the library
/// column, inside the window's own margins, and never smaller than it is
/// usable at.
///
/// <b>Why this is a class rather than four lines in MainForm.</b> It was four
/// lines in MainForm, and one of them read <c>splitContainer.Left = 320</c>.
/// That number is a design pixel, and the method holding it runs on every
/// Resize - which is to say, after WinForms has already scaled the window. A
/// coordinate assigned then lands as a raw device pixel and stamps the 100%
/// layout back over the scaled one; see <see cref="DpiScaling.Scale"/>, where
/// the same mistake is recorded against the setup wizard.
///
/// What it looked like, measured with the main window's own design
/// coordinates:
///
/// <code>
///         library column ends   reader started at   overlap
/// 100%    310                   320                 none
/// 125%    387                   320                 67px
/// 150%    465                   320                 145px
/// 200%    620                   320                 300px
/// </code>
///
/// The reader's left edge carries the dropdown naming the edition being read,
/// and the library column carries the author filter box. The filter box is
/// added to Controls before the reader, and index 0 is the FRONT of the
/// WinForms z-order - so the box won. Measured against that dropdown: at 125%
/// six pixels of its leading edge under the filter box and a 52-pixel band
/// under the favourites star just inside that; at 150%, 134 pixels covered
/// across a 145-pixel overlap. Reported from a laptop; invisible at 100%,
/// which is every machine this was written on.
///
/// So the left edge is no longer a number that happens to sit right of the
/// library today. It is derived from where the library actually ends, which is
/// true at any scaling and stays true if the column is ever made wider.
///
/// <b>And then it happened again, with the library hidden.</b> Collapsing the
/// library was treated as "there is nothing to the reader's left now", so the
/// reader started at the window margin. But one thing does stay: the button
/// that brings the library back, which has to, and which sits at the window
/// margin on the reader's own top row. The reader slid underneath it, and the
/// button won the z-order for the same reason the filter box did - so the
/// first thirty-odd pixels of the edition dropdown went under it at every
/// scaling, 100% included. Reported as an author's name reading "ymous
/// (menota)".
///
/// The rule now has no special case. The reader starts one gap to the right of
/// whatever is still on screen beside it, and which control that is depends
/// only on whether the library is showing.
/// </summary>
internal static class ReaderAreaLayout
{
    /// <summary>
    /// The window's own margin, in design pixels - the gap kept clear at the
    /// right edge and the bottom. Shared with the top-right toolbar chain,
    /// which is pinned to the same edge and has to agree with this.
    /// </summary>
    internal const int Margin = 20;

    /// <summary>Design-pixel gap between the library column and the reader.</summary>
    internal const int LibraryGap = 10;

    /// <summary>
    /// Floors, in design pixels. A window can be dragged narrower than the
    /// reader can usefully be; below these the reader stops shrinking and is
    /// clipped by the window instead, which at least keeps a column of text
    /// readable rather than collapsing it to nothing.
    /// </summary>
    internal const int MinimumWidth = 400;

    /// <inheritdoc cref="MinimumWidth"/>
    internal const int MinimumHeight = 100;

    /// <param name="client">The window's client area, in device pixels.</param>
    /// <param name="top">
    /// Where the reader starts vertically. Already in device pixels: it is set
    /// once at construction and so went through the scaling pass, unlike
    /// everything else here.
    /// </param>
    /// <param name="libraryRight">
    /// The right edge of the library column in device pixels - the furthest
    /// right of the tree and the row of controls above it, measured rather
    /// than assumed.
    /// </param>
    /// <param name="collapsedRight">
    /// The right edge, in device pixels, of what remains beside the reader
    /// once the library is hidden - the button that brings it back. It cannot
    /// be hidden with the rest, it sits on the reader's own top row, and it is
    /// in front of the reader in the z-order, so the reader has to start clear
    /// of it rather than at the window margin.
    /// </param>
    /// <param name="libraryCollapsed">
    /// When the library is hidden the reader takes back all of the column's
    /// width except the strip the toggle button still occupies.
    /// </param>
    /// <param name="scale">
    /// Turns a design pixel into a device pixel - <see cref="DpiScaling.Scale"/>
    /// bound to the window. Passed in rather than reached for so this can be
    /// measured at a scaling the test process cannot actually be set to.
    /// </param>
    internal static Rectangle For(
        Size client, int top, int libraryRight, int collapsedRight,
        bool libraryCollapsed, Func<int, int> scale)
    {
        // Whatever is still on screen to the reader's left, in either state.
        // One rule, no special case: start a gap to the right of it.
        var beside = libraryCollapsed ? collapsedRight : libraryRight;
        var left = beside + scale(LibraryGap);

        return new Rectangle(
            left,
            top,
            Math.Max(client.Width - left - scale(Margin), scale(MinimumWidth)),
            Math.Max(client.Height - top - scale(Margin), scale(MinimumHeight)));
    }
}
