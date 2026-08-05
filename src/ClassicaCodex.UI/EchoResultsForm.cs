using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class EchoResultsForm : Form
{
    private readonly Label _sourceLabel;
    private readonly ListBox _resultsList;
    private readonly TextNodeRepository _textNodeRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount)> _currentResults = new();

    /// <summary>Set by MainForm before showing this dialog.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public EchoResultsForm(TextNode sourceNode)
    {
        Text = "Intertextual Echoes";
        AppIcons.ApplyWindowIcon(this, "SimilarWorks");
        Width = 900;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;

        _sourceLabel = new Label
        {
            Text = $"Looking for echoes of: [{sourceNode.CitationRef}] {sourceNode.Text}",
            Left = 12,
            Top = 10,
            Width = 860,
            Height = 40
        };

        var explainer = new Label
        {
            Text = "Ranked by shared rare words - not proof of borrowing, just candidates worth a human look. " +
                   "Only compares against the same kind of text (original-vs-translation) as the source line.",
            Left = 12,
            Top = 50,
            Width = 860,
            Height = 34,
            ForeColor = Color.DimGray
        };

        _resultsList = new ListBox
        {
            Left = 12,
            Top = 90,
            Width = 860,
            Height = 480,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true
        };
        _resultsList.DoubleClick += async (_, _) => await JumpToSelectedAsync();
        ListResultHelpers.AttachCitationTooltip(_resultsList,
            i => i < _currentResults.Count ? _currentResults[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_resultsList,
            i => i < _currentResults.Count
                ? $"{_currentResults[i].AuthorName}, {_currentResults[i].WorkTitle} [{_currentResults[i].CitationRef}]: {_currentResults[i].Text}"
                : null);
        ListResultHelpers.AttachExportMenu(_resultsList, () => (
            "Intertextual echoes",
            _currentResults.Select(r => new ExportPassage(
                r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text)).ToList()), this);

        Controls.Add(_sourceLabel);
        Controls.Add(explainer);
        Controls.Add(_resultsList);

        Load += async (_, _) => await LoadEchoesAsync(sourceNode.TextNodeId);
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task LoadEchoesAsync(long sourceTextNodeId)
    {
        _resultsList.Items.Clear();
        _resultsList.Items.Add("Searching...");

        _currentResults = await _textNodeRepo.FindEchoesAsync(sourceTextNodeId);

        _resultsList.Items.Clear();
        if (_currentResults.Count == 0)
        {
            _resultsList.Items.Add("(no candidate echoes found - the words in this line may be too common, or too rare to share with anything)");
            return;
        }

        foreach (var r in _currentResults)
        {
            _resultsList.Items.Add(
                $"[{r.SharedWordCount} shared] {r.AuthorName}, {r.WorkTitle}: {r.Text}");
        }
    }

    private async Task JumpToSelectedAsync()
    {
        var index = _resultsList.SelectedIndex;
        if (index < 0 || index >= _currentResults.Count || OnNavigate == null) return;

        var result = _currentResults[index];
        await OnNavigate(result.WorkId, result.TextNodeId);
        Close();
    }
}
