using ClassicaCodex.Data.Repositories;

using ClassicaCodex.Core;

namespace ClassicaCodex.UI;

public class TimelineForm : ScaledForm
{
    private readonly Panel _scrollHost;
    private readonly TimelineCanvas _canvas;
    private readonly Label _statusLabel;
    private readonly ListBox _workList;
    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();

    private List<Core.Models.Work> _currentWorks = new();

    /// <summary>Set by MainForm before showing this dialog. Double-clicking a
    /// work invokes this with the work to open, then closes the timeline.</summary>
    public Func<int, Task>? OnOpenWork { get; set; }

    public TimelineForm()
    {
        Text = "Author Timeline";
        AppIcons.ApplyWindowIcon(this, "Timeline");
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterParent;

        _statusLabel = new Label
        {
            Text = "Loading...",
            Left = 12,
            Top = 10,
            Width = 860
        };

        _scrollHost = new Panel
        {
            Left = 12,
            Top = 34,
            Width = 860,
            Height = 712,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true,
            BorderStyle = BorderStyle.FixedSingle
        };

        _canvas = new TimelineCanvas { Width = _scrollHost.Width - 24, Left = 0, Top = 0 };
        _canvas.AuthorClicked += async id => await LoadWorksAsync(id);
        _scrollHost.Controls.Add(_canvas);
        _scrollHost.Resize += (_, _) => _canvas.Width = Math.Max(_scrollHost.ClientSize.Width - 4, 400);

        var worksLabel = new Label
        {
            Text = "Click an author to see their works (double-click one to open it):",
            Left = 884,
            Top = 10,
            Width = 300
        };

        _workList = new ListBox
        {
            Left = 884,
            Top = 34,
            Width = 300,
            Height = 712,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right
        };
        _workList.DoubleClick += async (_, _) => await OpenSelectedWorkAsync();

        Controls.Add(_statusLabel);
        Controls.Add(_scrollHost);
        Controls.Add(worksLabel);
        Controls.Add(_workList);

        void ApplyThemeHere()
        {
            ReadingTheme.Apply(this);

            // The scroll host sits directly behind the canvas, so it takes
            // the reading surface color rather than the window background -
            // otherwise the uncovered strip beside the timeline reads as a
            // seam rather than part of the same surface.
            _scrollHost.BackColor = ReadingTheme.Surface;

            _canvas.Invalidate();
            Invalidate(true);
        }

        Load += async (_, _) =>
        {
            ApplyThemeHere();
            await LoadTimelineAsync();
        };

        WindowShortcuts.CloseOnEscape(this);

        // Kept in sync if the mode is toggled while this is open; the handler
        // is removed on close so it doesn't outlive the form.
        void OnThemeChanged() => ApplyThemeHere();

        ReadingTheme.Changed += OnThemeChanged;
        FormClosed += (_, _) => ReadingTheme.Changed -= OnThemeChanged;
    }

    private async Task LoadTimelineAsync()
    {
        var authors = await _authorRepo.GetAllAsync();

        var entries = new List<TimelineCanvas.TimelineEntry>();
        var unmatchedCount = 0;

        foreach (var author in authors)
        {
            var era = AuthorEraData.Lookup(author.Name);
            if (era == null)
            {
                unmatchedCount++;
                continue;
            }

            entries.Add(new TimelineCanvas.TimelineEntry
            {
                AuthorId = author.AuthorId,
                Name = author.Name,
                StartYear = era.Value.StartYear,
                EndYear = era.Value.EndYear
            });
        }

        _canvas.SetData(entries);
        _statusLabel.Text = $"{entries.Count} authors dated on the timeline below " +
                             $"({unmatchedCount} others in your library have no date on record and aren't shown). " +
                             "Dates are rough consensus estimates, not settled fact.";
    }

    private async Task LoadWorksAsync(int authorId)
    {
        _workList.Items.Clear();
        _currentWorks = await _workRepo.GetByAuthorAsync(authorId);

        foreach (var work in _currentWorks)
        {
            _workList.Items.Add(work.Title);
        }

        if (_currentWorks.Count == 0)
        {
            _workList.Items.Add("(no works found)");
        }
    }

    private async Task OpenSelectedWorkAsync()
    {
        var index = _workList.SelectedIndex;
        if (index < 0 || index >= _currentWorks.Count || OnOpenWork == null) return;

        await OnOpenWork(_currentWorks[index].WorkId);
        Close();
    }
}
