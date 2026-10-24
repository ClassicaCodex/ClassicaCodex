using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Tags;

namespace ClassicaCodex.Mac.Features.Bookmarks;

/// <summary>
/// Every bookmarked line, newest first, with its note - the Windows
/// BookmarksForm. Double-click one to read it in place; delete what is no
/// longer wanted.
///
/// Bookmarks whose passage is not in the library at the moment are not
/// shown and not lost either, and the window says how many there are.
/// </summary>
internal sealed class BookmarksWindow : FeatureWindow
{
    private readonly BookmarkRepository _bookmarkRepo = new();
    private readonly ListBox _bookmarkList;
    private readonly TextBlock _message;
    private readonly TextBlock _dormantNotice;
    private readonly TextBlock _count;

    private List<Bookmark> _currentBookmarks = new();

    public BookmarksWindow()
    {
        Title = "Bookmarks";
        Width = 820;
        Height = 600;
        MinWidth = 520;
        MinHeight = 360;
        PassageResults.AddListStyles(this);

        var (page, list, message) = PassageResults.Page(item => item is Bookmark b ? BookmarkRow(b) : new TextBlock());
        _bookmarkList = list;
        _message = message;

        _bookmarkList.DoubleTapped += async (_, _) => await JumpToSelectedAsync();
        _bookmarkList.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await JumpToSelectedAsync();
            }
            else if (e.Key is Key.Back or Key.Delete)
            {
                e.Handled = true;
                await DeleteSelectedAsync();
            }
        };

        PassageResults.Menu(_bookmarkList,
            PassageResults.CopyItem(_bookmarkList, item =>
            {
                if (item is not Bookmark b) return null;
                var full = PassageResults.CopyLine(b.AuthorName, b.WorkTitle, b.CitationRef, b.Milestone, b.Text);
                return string.IsNullOrEmpty(b.Note) ? full : $"{full}\nNote: {b.Note}";
            }),
            PassageResults.ExportAllItem(this, () => _currentBookmarks.Count));

        // And as rows, alongside the document export rather than instead of
        // it. The passage export writes prose to quote from; this writes the
        // note and the date too, which is what someone sorting or filtering
        // their own reading actually wants and which no prose format carries.
        TableExport.Attach(
            _bookmarkList,
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
            notes: () => new[]
            {
                $"Classica Codex bookmarks - {DateTime.Now:yyyy-MM-dd HH:mm}",
                $"{_currentBookmarks.Count:N0} bookmarks.",
                "The citation is the durable identity - bookmarks are stored against it rather " +
                "than an internal id, so they survive a corpus being re-ingested."
            });

        var deleteButton = new Button { Content = TagBrowserWindow.Labelled("Delete Selected", "Trash") };
        deleteButton.Click += async (_, _) => await DeleteSelectedAsync();

        _count = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Bottom };
        _dormantNotice = new TextBlock { Classes = { "caption" }, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(2, 8, 2, 0) };

        var heading = new DockPanel { Margin = new Thickness(2, 0, 2, 8) };
        DockPanel.SetDock(_count, Dock.Right);
        heading.Children.Add(_count);
        heading.Children.Add(new TextBlock { Text = "Your bookmarked lines (double-click to jump to one):", Classes = { "muted" } });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), Children = { deleteButton } };

        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 18) };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_dormantNotice, Dock.Bottom);
        root.Children.Add(heading);
        root.Children.Add(buttons);
        root.Children.Add(_dormantNotice);
        root.Children.Add(page);
        Content = root;

        Opened += async (_, _) => await LoadBookmarksAsync();
    }

    /// <summary>The bookmarks listed; for tests.</summary>
    internal IReadOnlyList<Bookmark> Listed => _currentBookmarks;

    internal string DormantNotice => _dormantNotice.IsVisible ? _dormantNotice.Text ?? string.Empty
        : _message.IsVisible ? _message.Text ?? string.Empty
        : string.Empty;

    internal async Task LoadBookmarksAsync()
    {
        int dormant;
        try
        {
            _currentBookmarks = (await _bookmarkRepo.GetAllAsync())
                .Select(b => new Bookmark(b.BookmarkId, b.WorkId, b.TextNodeId, b.AuthorName, b.WorkTitle, b.CitationRef, b.Text, b.Note, b.CreatedAt, b.Milestone))
                .ToList();

            // Bookmarks whose passage isn't currently ingested don't appear in
            // the list - they're dormant, not deleted. Saying so matters:
            // otherwise a corpus re-ingest that changed citation refs looks
            // exactly like the app having quietly lost someone's notes.
            dormant = await _bookmarkRepo.CountDormantAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't load the bookmarks", ex);
            return;
        }

        _bookmarkList.ItemsSource = _currentBookmarks;
        _count.Text = _currentBookmarks.Count switch
        {
            0 => string.Empty,
            1 => "1 bookmark",
            var n => $"{n:N0} bookmarks"
        };

        var dormantText = dormant == 1
            ? "1 more bookmark is waiting on a text that isn't ingested right now."
            : $"{dormant} more bookmarks are waiting on texts that aren't ingested right now.";

        // Under the list when there are rows above it; in the list's place
        // when every bookmark is waiting.
        PassageResults.SetMessage(_message, _currentBookmarks.Count > 0
            ? null
            : dormant > 0
                ? dormantText
                : "No bookmarks yet - right-click a line in the reader to add one.");

        _dormantNotice.IsVisible = dormant > 0 && _currentBookmarks.Count > 0;
        _dormantNotice.Text = dormantText;
    }

    /// <summary>
    /// Two lines where there is a note: the passage, then the note - secondary
    /// text, muted and indented under it.
    /// </summary>
    private static Control BookmarkRow(Bookmark b)
    {
        var row = PassageResults.Row(b.Source, PassageResults.RowText(b.Text), b.CitationRef, b.Milestone);
        if (string.IsNullOrEmpty(b.Note)) return row;

        var note = new TextBlock
        {
            Text = $"Note: {b.Note}",
            FontStyle = FontStyle.Italic,
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(16, 3, 0, 0)
        };

        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(note);
        return stack;
    }

    /// <summary>
    /// Puts the reader on the bookmark, then closes this window so the reader
    /// is what's left on screen.
    /// </summary>
    private async Task JumpToSelectedAsync()
    {
        if (_bookmarkList.SelectedItem is not Bookmark bookmark) return;

        await NavigateAsync(bookmark.WorkId, bookmark.TextNodeId);
        Close();
    }

    private async Task DeleteSelectedAsync()
    {
        if (_bookmarkList.SelectedItem is not Bookmark bookmark) return;

        if (!await Alert.Confirm(this, "Confirm", "Delete this bookmark?", "Delete")) return;

        try
        {
            await _bookmarkRepo.DeleteAsync(bookmark.BookmarkId);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't delete the bookmark", ex);
            return;
        }

        await LoadBookmarksAsync();
    }

    /// <summary>One bookmark as the list holds it; a class, so two alike are still two rows.</summary>
    internal sealed class Bookmark
    {
        public Bookmark(int bookmarkId, int workId, long textNodeId, string authorName, string workTitle,
            string citationRef, string text, string? note, DateTime createdAt, string? milestone)
        {
            BookmarkId = bookmarkId;
            WorkId = workId;
            TextNodeId = textNodeId;
            AuthorName = authorName;
            WorkTitle = workTitle;
            CitationRef = citationRef;
            Text = text;
            Note = note;
            CreatedAt = createdAt;
            Milestone = milestone;
        }

        public int BookmarkId { get; }
        public int WorkId { get; }
        public long TextNodeId { get; }
        public string AuthorName { get; }
        public string WorkTitle { get; }
        public string CitationRef { get; }
        public string Text { get; }
        public string? Note { get; }
        public DateTime CreatedAt { get; }
        public string? Milestone { get; }

        public string Source => $"{AuthorName}, {WorkTitle}";
    }
}
