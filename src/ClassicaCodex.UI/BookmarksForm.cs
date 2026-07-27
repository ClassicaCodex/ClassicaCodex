using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class BookmarksForm : Form
{
    private readonly ListBox _bookmarkList;
    private readonly Button _deleteButton;
    private readonly BookmarkRepository _bookmarkRepo = new();

    private List<(int BookmarkId, int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Note, DateTime CreatedAt)> _currentBookmarks = new();

    /// <summary>
    /// Set by MainForm before showing this dialog. Double-clicking a
    /// bookmark invokes this with the work and text node to jump to, then
    /// closes the browser so the main reader is what's left on screen.
    /// </summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public BookmarksForm()
    {
        Text = "Bookmarks";
        AppIcons.ApplyWindowIcon(this, "Bookmarks");
        // ClientSize, not Width/Height - see AboutForm for why; same fix,
        // same reason the Delete button's bottom edge was getting clipped.
        ClientSize = new Size(780, 560);
        StartPosition = FormStartPosition.CenterParent;

        var label = new Label
        {
            Text = "Your bookmarked lines (double-click to jump to one):",
            Left = 12,
            Top = 10,
            Width = 500
        };

        _bookmarkList = new ListBox
        {
            Left = 12,
            Top = 32,
            Width = 740,
            Height = 460,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true,
            DrawMode = DrawMode.OwnerDrawVariable
        };
        _bookmarkList.DoubleClick += async (_, _) => await JumpToSelectedAsync();
        _bookmarkList.DrawItem += BookmarkList_DrawItem;
        _bookmarkList.MeasureItem += BookmarkList_MeasureItem;
        ListResultHelpers.AttachCitationTooltip(_bookmarkList,
            i => i < _currentBookmarks.Count ? _currentBookmarks[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_bookmarkList, i =>
        {
            if (i >= _currentBookmarks.Count) return null;
            var b = _currentBookmarks[i];
            var full = $"{b.AuthorName}, {b.WorkTitle} [{b.CitationRef}]: {b.Text}";
            return string.IsNullOrEmpty(b.Note) ? full : $"{full}\nNote: {b.Note}";
        });

        _deleteButton = new Button
        {
            Text = "Delete Selected",
            Left = 12,
            Top = 500,
            Width = 140,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _deleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        AppIcons.Apply(_deleteButton, "Delete", 16);

        Controls.Add(label);
        Controls.Add(_bookmarkList);
        Controls.Add(_deleteButton);

        Load += async (_, _) => await LoadBookmarksAsync();
        ReadingTheme.AttachTo(this);
    }

    private async Task LoadBookmarksAsync()
    {
        _bookmarkList.Items.Clear();
        _currentBookmarks = await _bookmarkRepo.GetAllAsync();

        if (_currentBookmarks.Count == 0)
        {
            _bookmarkList.Items.Add("(no bookmarks yet - right-click a line in the reader to add one)");
            return;
        }

        foreach (var b in _currentBookmarks)
        {
            _bookmarkList.Items.Add(b);
        }
    }

    private void BookmarkList_MeasureItem(object? sender, MeasureItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _currentBookmarks.Count)
        {
            e.ItemHeight = 20;
            return;
        }

        // Two lines: passage reference + text, and the note (if any)
        e.ItemHeight = string.IsNullOrEmpty(_currentBookmarks[e.Index].Note) ? 20 : 38;
    }

    private void BookmarkList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        // Explicit fill rather than e.DrawBackground(), which paints the
        // system selection colour and so ignores the app's own theme.
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var backBrush = new SolidBrush(selected ? ReadingTheme.SelectionBackground : ReadingTheme.Surface))
        {
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        }

        if (e.Index < 0 || e.Index >= _currentBookmarks.Count) return;

        var b = _currentBookmarks[e.Index];
        var boldFont = new Font(_bookmarkList.Font, FontStyle.Regular);
        var noteFont = new Font(_bookmarkList.Font, FontStyle.Italic);
        var foreColor = selected ? ReadingTheme.SelectionText : ReadingTheme.Text;

        var line1 = $"{b.AuthorName}, {b.WorkTitle}: {b.Text}";
        TextRenderer.DrawText(e.Graphics, line1, boldFont, new Point(e.Bounds.Left, e.Bounds.Top), foreColor);

        if (!string.IsNullOrEmpty(b.Note))
        {
            // The note is secondary text, so it stays muted - but muted
            // relative to whichever surface it's actually sitting on.
            var noteColor = selected ? ReadingTheme.SelectionText : ReadingTheme.MutedText;
            TextRenderer.DrawText(e.Graphics, $"Note: {b.Note}", noteFont,
                new Point(e.Bounds.Left + 16, e.Bounds.Top + 18), noteColor);
        }

        e.DrawFocusRectangle();
    }

    private async Task JumpToSelectedAsync()
    {
        var index = _bookmarkList.SelectedIndex;
        if (index < 0 || index >= _currentBookmarks.Count || OnNavigate == null) return;

        var bookmark = _currentBookmarks[index];
        await OnNavigate(bookmark.WorkId, bookmark.TextNodeId);
        Close();
    }

    private async Task DeleteSelectedAsync()
    {
        var index = _bookmarkList.SelectedIndex;
        if (index < 0 || index >= _currentBookmarks.Count) return;

        var bookmark = _currentBookmarks[index];
        var confirm = MessageBox.Show(this, "Delete this bookmark?", "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        await _bookmarkRepo.DeleteAsync(bookmark.BookmarkId);
        await LoadBookmarksAsync();
    }
}
