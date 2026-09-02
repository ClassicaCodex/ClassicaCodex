using ClassicaCodex.Core;

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

    /// <summary>
    /// Adds an "Export All Passages..." right-click item to a result list.
    ///
    /// The whole gathered set is exported, not just the row under the
    /// cursor - which is why the item says "All". A set of passages pulled
    /// from across the library is the thing worth writing to a document;
    /// a single passage already has its own export on the reader's own
    /// right-click menu, reached from the line itself.
    ///
    /// Exporting the backing list rather than the visible rows also means
    /// non-passage rows a list may carry - "(nothing tagged with this yet)",
    /// a truncation notice, the dormant-bookmark line - are structurally
    /// incapable of ending up in the output.
    ///
    /// <paramref name="collect"/> runs at click time rather than at attach
    /// time, so it always sees whatever the list currently holds; these
    /// forms rebuild their results as the user changes a selection.
    /// Returning an empty set is fine and says so rather than opening an
    /// empty dialog.
    /// </summary>
    public static void AttachExportMenu(
        ListBox listBox,
        Func<(string Title, IReadOnlyList<ExportPassage> Passages)> collect,
        Form owner,
        string? detailLabel = null)
    {
        // Same merge-and-reuse as AttachArtifactSearchMenu: several of these
        // lists already carry a Copy to Clipboard item, and replacing the
        // strip outright would silently drop it.
        var menu = listBox.ContextMenuStrip ?? new ContextMenuStrip();
        var exportItem = menu.Items.Add("Export All Passages...");
        exportItem.Image = AppIcons.Get("Export", 16);
        ReadingTheme.ApplyToContextMenu(menu);

        listBox.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var index = listBox.IndexFromPoint(e.Location);
            if (index >= 0) listBox.SelectedIndex = index;
        };

        exportItem.Click += (_, _) => ShowExportDialog(collect, owner, detailLabel);

        listBox.ContextMenuStrip = menu;
    }

    /// <summary>
    /// Same as the ListBox overload, for a ListView - the Concordance's
    /// results are columnar (left context, keyword, right context), so it
    /// uses a ListView where the other result lists use a ListBox.
    /// </summary>
    public static void AttachExportMenu(
        ListView listView,
        Func<(string Title, IReadOnlyList<ExportPassage> Passages)> collect,
        Form owner,
        string? detailLabel = null)
    {
        var menu = listView.ContextMenuStrip ?? new ContextMenuStrip();
        var exportItem = menu.Items.Add("Export All Passages...");
        exportItem.Image = AppIcons.Get("Export", 16);
        ReadingTheme.ApplyToContextMenu(menu);

        exportItem.Click += (_, _) => ShowExportDialog(collect, owner, detailLabel);

        listView.ContextMenuStrip = menu;
    }

    private static void ShowExportDialog(
        Func<(string Title, IReadOnlyList<ExportPassage> Passages)> collect, Form owner, string? detailLabel)
    {
        var (title, passages) = collect();

        if (passages.Count == 0)
        {
            MessageBox.Show(owner, "There are no passages here to export yet.", "Nothing to export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var exportForm = new PassageSetExportForm(title, passages, detailLabel);
        exportForm.ShowDialog(owner);
    }

    /// <summary>
    /// Sizes an owner-drawn list's horizontal scrollbar to its widest row.
    ///
    /// Plain lists get this from the theme, which sets HorizontalScrollbar on
    /// every ListBox in the app and lets WinForms measure the items itself.
    /// WinForms will not do that for an owner-drawn list - it has no idea what
    /// the DrawItem handler is going to put on the row - so the extent stays
    /// at zero and no bar appears however wide the content is. The lists that
    /// show passages are exactly the ones drawn by hand, for the reading font
    /// and the search highlighting, so without this the scrollbar would arrive
    /// everywhere except the screens that most need it.
    ///
    /// <paramref name="rowTextAt"/> returns what the row actually draws, which
    /// the caller knows and this does not. Call it after repopulating: an
    /// extent set for a previous result set is stale, and a stale one that is
    /// too wide leaves a scrollbar sliding over empty space.
    /// </summary>
    public static void RefreshHorizontalExtent(ListBox listBox, Func<int, string?> rowTextAt)
    {
        // Guarded, because this measurement is the one that took the places
        // map down with a GDI+ error - see the note in ReadingTheme, where the
        // corpus-wide re-test is recorded. It does not reproduce, and the
        // measuring here is TextRenderer rather than the GDI+ path that was
        // blamed. But a scrollbar is not worth a window, and a list that
        // cannot measure itself should lose its scrollbar rather than take the
        // form with it.
        try
        {
            var rows = new List<string?>(listBox.Items.Count);
            for (var i = 0; i < listBox.Items.Count; i++) rows.Add(rowTextAt(i));

            var widest = 0;
            foreach (var row in WidestRows.Candidates(rows))
            {
                widest = Math.Max(widest, TextRenderer.MeasureText(row, listBox.Font).Width);
            }

            // The inset the rows are drawn at, plus the checkbox where there
            // is one, so the last character clears the edge instead of sitting
            // against it.
            if (widest > 0) widest += listBox is CheckedListBox ? 32 : 12;

            listBox.HorizontalExtent = widest;
        }
        catch (Exception)
        {
            listBox.HorizontalExtent = 0;
        }
    }
}
