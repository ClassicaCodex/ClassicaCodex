using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Takes the same rare-word-overlap technique as the echo finder, but
/// frames it chronologically: split candidate echoes into ones by authors
/// who wrote LATER than the source (candidates for quoting/imitating it)
/// and ones by authors who wrote EARLIER (candidates for the source having
/// drawn on them). This is what classicists call reception history in one
/// direction and Quellenforschung (source criticism) in the other - both
/// real, named subfields, both usually done by hand.
///
/// Uses the same curated era table as the Timeline view, so it inherits the
/// same coverage gaps: authors not in that table land in "unknown era"
/// rather than being guessed at.
/// </summary>
public class ReceptionTrackerForm : Form
{
    private readonly TextNode _sourceNode;
    private readonly Label _sourceLabel;
    private readonly ListBox _laterList;
    private readonly ListBox _earlierList;
    private readonly ListBox _unknownList;
    private readonly TextNodeRepository _textNodeRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount)> _later = new();
    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount)> _earlier = new();
    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount)> _unknown = new();

    /// <summary>Set by MainForm before showing this dialog.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public ReceptionTrackerForm(TextNode sourceNode)
    {
        _sourceNode = sourceNode;
        Text = "Reception Tracker";
        AppIcons.ApplyWindowIcon(this, "ReceptionTracker");
        Width = 1300;
        Height = 750;
        StartPosition = FormStartPosition.CenterParent;

        _sourceLabel = new Label
        {
            Text = $"Source: [{sourceNode.CitationRef}] {sourceNode.Text}",
            Left = 12,
            Top = 10,
            Width = 1260,
            Height = 40,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var laterLabel = new Label { Text = "Later authors (may be echoing this):", Left = 12, Top = 56, Width = 410 };
        _laterList = new ListBox
        {
            Left = 12, Top = 78, Width = 410, Height = 610,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            HorizontalScrollbar = true
        };
        _laterList.DoubleClick += async (_, _) => await JumpAsync(_laterList, _later);
        ListResultHelpers.AttachCitationTooltip(_laterList,
            i => i < _later.Count ? _later[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_laterList,
            i => i < _later.Count
                ? $"{_later[i].AuthorName}, {_later[i].WorkTitle} [{_later[i].CitationRef}]: {_later[i].Text}"
                : null);

        var earlierLabel = new Label { Text = "Earlier authors (this may echo them):", Left = 434, Top = 56, Width = 410 };
        _earlierList = new ListBox
        {
            Left = 434, Top = 78, Width = 410, Height = 610,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            HorizontalScrollbar = true
        };
        _earlierList.DoubleClick += async (_, _) => await JumpAsync(_earlierList, _earlier);
        ListResultHelpers.AttachCitationTooltip(_earlierList,
            i => i < _earlier.Count ? _earlier[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_earlierList,
            i => i < _earlier.Count
                ? $"{_earlier[i].AuthorName}, {_earlier[i].WorkTitle} [{_earlier[i].CitationRef}]: {_earlier[i].Text}"
                : null);

        var unknownLabel = new Label { Text = "Unknown era (can't place chronologically):", Left = 856, Top = 56, Width = 410 };
        _unknownList = new ListBox
        {
            Left = 856, Top = 78, Width = 410, Height = 610,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true
        };
        _unknownList.DoubleClick += async (_, _) => await JumpAsync(_unknownList, _unknown);
        ListResultHelpers.AttachCitationTooltip(_unknownList,
            i => i < _unknown.Count ? _unknown[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_unknownList,
            i => i < _unknown.Count
                ? $"{_unknown[i].AuthorName}, {_unknown[i].WorkTitle} [{_unknown[i].CitationRef}]: {_unknown[i].Text}"
                : null);

        Controls.Add(_sourceLabel);
        Controls.Add(laterLabel);
        Controls.Add(_laterList);
        Controls.Add(earlierLabel);
        Controls.Add(_earlierList);
        Controls.Add(unknownLabel);
        Controls.Add(_unknownList);

        Load += async (_, _) => await LoadAsync();
        ReadingTheme.AttachTo(this);
    }

    private async Task LoadAsync()
    {
        var source = await _textNodeRepo.GetTextNodeSourceInfoAsync(_sourceNode.TextNodeId);
        var sourceEra = source != null ? AuthorEraData.Lookup(source.Value.AuthorName) : null;

        var echoes = await _textNodeRepo.FindEchoesAsync(_sourceNode.TextNodeId);

        if (sourceEra == null)
        {
            // Can't place the source itself in time, so a relative
            // "earlier/later" split is meaningless - show everything
            // ungrouped rather than pretending to a chronology we don't have.
            _unknown = echoes;
            _later = new();
            _earlier = new();

            _laterList.Items.Add("(source author's date is unknown - can't split by chronology)");
            PopulateList(_unknownList, _unknown);
            return;
        }

        foreach (var echo in echoes)
        {
            var era = AuthorEraData.Lookup(echo.AuthorName);
            if (era == null)
            {
                _unknown.Add(echo);
            }
            else if (era.Value.StartYear > sourceEra.Value.StartYear)
            {
                _later.Add(echo);
            }
            else if (era.Value.StartYear < sourceEra.Value.StartYear)
            {
                _earlier.Add(echo);
            }
            else
            {
                _unknown.Add(echo); // same rough era / possibly the same author - not a clean fit either bucket
            }
        }

        PopulateList(_laterList, _later);
        PopulateList(_earlierList, _earlier);
        PopulateList(_unknownList, _unknown);
    }

    private static void PopulateList(
        ListBox list,
        List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount)> items)
    {
        if (items.Count == 0)
        {
            list.Items.Add("(none found)");
            return;
        }

        foreach (var r in items)
        {
            list.Items.Add($"[{r.SharedWordCount} shared] {r.AuthorName}, {r.WorkTitle}: {r.Text}");
        }
    }

    private async Task JumpAsync(
        ListBox list,
        List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount)> items)
    {
        var index = list.SelectedIndex;
        if (index < 0 || index >= items.Count || OnNavigate == null) return;

        var result = items[index];
        await OnNavigate(result.WorkId, result.TextNodeId);
        Close();
    }
}
