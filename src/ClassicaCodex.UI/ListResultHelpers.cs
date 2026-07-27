namespace ClassicaCodex.UI;

/// <summary>
/// Two behaviors shared by every result list in the app that shows
/// "Author, Work: passage text" rows - Word Study's occurrences, Auto-Tag's
/// matches, the myth network's passage lists, Reception Tracker, Intertextual
/// Echoes, Bookmarks, the Tag Browser, Places Map, Compare Sources, and the
/// main reader's own search results.
///
/// The citation ref used to be printed inline in every row, which is exactly
/// the clutter SyncListView already avoids for the reader panes themselves -
/// hidden by default, available on hover. These helpers give every other
/// list that same tradeoff, plus a right-click "Copy to Clipboard" that
/// copies the full line - author, work, ref, and passage - since removing
/// the ref from view shouldn't make it harder to actually get at when it's
/// wanted.
/// </summary>
public static class ListResultHelpers
{
    /// <summary>
    /// Shows a row's citation ref in a tooltip as the mouse moves over it.
    /// <paramref name="citationRefAt"/> looks the ref up by row index against
    /// whatever backing list the caller already keeps (every one of these
    /// forms keeps one, for double-click navigation) - null for a row with
    /// nothing to show clears the tip rather than displaying an empty one.
    /// </summary>
    public static void AttachCitationTooltip(ListBox listBox, Func<int, string?> citationRefAt)
    {
        var toolTip = new ToolTip();
        ReadingTheme.ApplyToToolTip(toolTip);
        var lastIndex = -2;

        listBox.MouseMove += (_, e) =>
        {
            var index = listBox.IndexFromPoint(e.Location);
            if (index == lastIndex) return;
            lastIndex = index;

            var citationRef = index >= 0 && index < listBox.Items.Count ? citationRefAt(index) : null;
            toolTip.SetToolTip(listBox, string.IsNullOrEmpty(citationRef) ? string.Empty : $"[{citationRef}]");
        };

        // Leaving the control - or the list being cleared and rebuilt under
        // an unmoved mouse - can otherwise leave a stale ref showing for a
        // row that's no longer there. Reset so the next MouseMove always
        // re-evaluates rather than trusting the old index.
        listBox.MouseLeave += (_, _) =>
        {
            lastIndex = -2;
            toolTip.SetToolTip(listBox, string.Empty);
        };
    }

    /// <summary>
    /// Adds a right-click "Copy to Clipboard" item that copies the full text
    /// - author, work, citation ref, and passage - for whichever row is
    /// under the cursor. Right-clicking selects that row first (left-click
    /// selection and any existing CheckOnClick behavior are untouched, since
    /// this only wires up the right button), so it's obvious which row the
    /// menu is about to act on.
    /// </summary>
    public static ContextMenuStrip AttachCopyToClipboardMenu(ListBox listBox, Func<int, string?> fullTextAt)
    {
        var menu = new ContextMenuStrip();
        var copyItem = menu.Items.Add("Copy to Clipboard");
        copyItem.Image = AppIcons.Get("CopyToClipboard", 16);
        ReadingTheme.ApplyToContextMenu(menu);

        listBox.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var index = listBox.IndexFromPoint(e.Location);
            if (index >= 0) listBox.SelectedIndex = index;
        };

        copyItem.Click += (_, _) =>
        {
            var index = listBox.SelectedIndex;
            var text = index >= 0 ? fullTextAt(index) : null;
            if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text);
        };

        listBox.ContextMenuStrip = menu;
        return menu;
    }

    /// <summary>
    /// Adds a "Show related artifacts..." right-click item to a ListBox
    /// whose selected item can be resolved to a searchable name - reuses
    /// the same select-on-right-click convention as
    /// AttachCopyToClipboardMenu, so both can coexist on the same list if
    /// ever needed.
    /// </summary>
    public static void AttachArtifactSearchMenu(ListBox listBox, Func<int, string?> nameAt, Form owner)
    {
        var menu = listBox.ContextMenuStrip ?? new ContextMenuStrip();
        var artifactItem = menu.Items.Add("Show related artifacts...");
        ReadingTheme.ApplyToContextMenu(menu);

        listBox.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var index = listBox.IndexFromPoint(e.Location);
            if (index >= 0) listBox.SelectedIndex = index;
        };

        artifactItem.Click += (_, _) =>
        {
            var index = listBox.SelectedIndex;
            var name = index >= 0 ? nameAt(index) : null;
            if (string.IsNullOrEmpty(name)) return;

            using var artifactForm = new ArtifactBrowserForm(name, name);
            artifactForm.ShowDialog(owner);
        };

        listBox.ContextMenuStrip = menu;
    }
}
