using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class BookmarksForm : ScaledForm
{
    private readonly ListBox _bookmarkList;

    // Created once, not per paint. DrawItem runs for every visible row on
    // every repaint - scrolling a list of bookmarks was allocating two GDI
    // font handles per row per frame and disposing none of them.
    private readonly Font _italicFont;
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
            DrawMode = DrawMode.OwnerDrawVariable
        };
        _italicFont = new Font(_bookmarkList.Font, FontStyle.Italic);

        _bookmarkList.DoubleClick += async (_, _) => await JumpToSelectedAsync();
        _bookmarkList.DrawItem += BookmarkList_DrawItem;
        _bookmarkList.MeasureItem += BookmarkList_MeasureItem;
        ListResultHelpers.AttachCitationTooltip(_bookmarkList,
            i => i < _currentBookmarks.Count ? _currentBookmarks[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_bookmarkList, i =>
        {
            if (i >= _currentBookmarks.Count) return null;
            var b = _currentBookmarks[i];
            var full = $"{b.AuthorName}, {b.WorkTitle} [{PassageCitation.Display(b.CitationRef)}]: {b.Text}";
            return string.IsNullOrEmpty(b.Note) ? full : $"{full}\nNote: {b.Note}";
        });

        ListResultHelpers.AttachExportMenu(_bookmarkList, () => (
            "Bookmarks",
            _currentBookmarks.Select(b => new ExportPassage(
                b.WorkId, b.TextNodeId, b.AuthorName, b.WorkTitle, b.CitationRef, b.Text)).ToList()), this);

        // And as rows, alongside the document export rather than instead of it.
        // The passage export writes prose to quote from; this writes the note
        // and the date too, which is what someone sorting or filtering their own
        // reading actually wants and which no prose format carries usefully.
        ResultExport.AddTo(
            _bookmarkList.ContextMenuStrip!,
            () => "bookmarks",
            () =>
            {
                var table = new List<IReadOnlyList<string>>
                {
                    new[] { "Author", "Work", "Citation", "Text", "Note", "CreatedUtc" }
                };

                foreach (var b in _currentBookmarks)
                {
                    table.Add(new[]
                    {
                        b.AuthorName, b.WorkTitle, b.CitationRef, b.Text,
                        b.Note ?? string.Empty,
                        b.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                    });
                }

                return table;
            },
            () => new[]
            {
                $"Classica Codex bookmarks - {DateTime.Now:yyyy-MM-dd HH:mm}",
                $"{_currentBookmarks.Count:N0} bookmarks.",
                "The citation is the durable identity - bookmarks are stored against it rather " +
                "than an internal id, so they survive a corpus being re-ingested."
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
        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task LoadBookmarksAsync()
    {
        _bookmarkList.Items.Clear();
        _currentBookmarks = await _bookmarkRepo.GetAllAsync();

        // Bookmarks whose passage isn't currently ingested don't appear in the
        // list above - they're dormant, not deleted. Saying so matters:
        // otherwise a corpus re-ingest that changed citation refs looks
        // exactly like the app having quietly lost someone's notes.
        var dormant = await _bookmarkRepo.CountDormantAsync();

        if (_currentBookmarks.Count == 0 && dormant == 0)
        {
            _bookmarkList.Items.Add("(no bookmarks yet - right-click a line in the reader to add one)");
            RefreshExtent();
            return;
        }

        foreach (var b in _currentBookmarks)
        {
            _bookmarkList.Items.Add(b);
        }

        if (dormant > 0)
        {
            _bookmarkList.Items.Add(dormant == 1
                ? "(1 more bookmark is waiting on a text that isn't ingested right now)"
                : $"({dormant} more bookmarks are waiting on texts that aren't ingested right now)");
        }

        RefreshExtent();
    }

    /// <summary>
    /// Unlike the other result lists, the rows here are bookmark objects
    /// rather than the strings they draw as, so the drawing has to be
    /// described rather than read back off the list.
    /// </summary>
    private void RefreshExtent() =>
        ListResultHelpers.RefreshHorizontalExtent(_bookmarkList, i =>
        {
            // The trailing rows are the list's own messages - the empty state
            // and the dormant-bookmark notice - which are plain strings.
            if (i >= _currentBookmarks.Count) return _bookmarkList.Items[i]?.ToString();

            var b = _currentBookmarks[i];
            var line = $"{b.AuthorName}, {b.WorkTitle}: {b.Text}";
            if (string.IsNullOrEmpty(b.Note)) return line;

            // A row with a note is two lines and either can be the wider, so
            // the longer one is what the row needs to be measured by. The two
            // spaces stand in for the note's 16px indent; approximating it
            // low only stops the scrollbar a few pixels early.
            var noteLine = $"  Note: {b.Note}";
            return noteLine.Length > line.Length ? noteLine : line;
        });

    protected override void Dispose(bool disposing)
    {
        if (disposing) _italicFont.Dispose();

        base.Dispose(disposing);
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

        if (e.Index < 0) return;

        var foreColor = selected ? ReadingTheme.SelectionText : ReadingTheme.Text;

        // Rows past the bookmarks themselves are the list's own messages - the
        // empty-state line, or the dormant-bookmark notice. This used to
        // return here having painted only the background, so those messages
        // were added to the list and then never drawn: the empty state showed
        // as a blank box rather than the sentence explaining how to add one.
        if (e.Index >= _currentBookmarks.Count)
        {
            TextRenderer.DrawText(e.Graphics, _bookmarkList.Items[e.Index]?.ToString() ?? string.Empty,
                _italicFont, new Point(e.Bounds.Left, e.Bounds.Top), ReadingTheme.MutedText);
            return;
        }

        var b = _currentBookmarks[e.Index];

        var line1 = $"{b.AuthorName}, {b.WorkTitle}: {b.Text}";
        TextRenderer.DrawText(e.Graphics, line1, _bookmarkList.Font,
            new Point(e.Bounds.Left, e.Bounds.Top), foreColor);

        if (!string.IsNullOrEmpty(b.Note))
        {
            // The note is secondary text, so it stays muted - but muted
            // relative to whichever surface it's actually sitting on.
            var noteColor = selected ? ReadingTheme.SelectionText : ReadingTheme.MutedText;
            TextRenderer.DrawText(e.Graphics, $"Note: {b.Note}", _italicFont,
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
