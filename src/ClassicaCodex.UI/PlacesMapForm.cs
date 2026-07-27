using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class PlacesMapForm : Form
{
    private readonly MapCanvas _canvas;
    private readonly ListBox _passageList;
    private readonly Label _selectedPlaceLabel;
    private readonly TagRepository _tagRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _currentPassages = new();

    /// <summary>Set by MainForm before showing this dialog.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public PlacesMapForm()
    {
        Text = "Places Map - click a place to see passages that mention it";
        AppIcons.ApplyWindowIcon(this, "PlaceMap");
        Width = 1200;
        Height = 780;
        StartPosition = FormStartPosition.CenterParent;

        var legend = new Label
        {
            Text = "Places are matched from your tags against a curated ancient-places reference list - tag a line " +
                   "with a place name (\"Athens\", \"Troy\", \"Rome\"...) to see it show up here. Scroll to zoom, " +
                   "drag to pan, double-click open sea to reset the view.",
            Left = 12,
            Top = 10,
            Width = 860
        };

        _canvas = new MapCanvas
        {
            Left = 12,
            Top = 44,
            Width = 860,
            Height = 700,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };
        _canvas.PlaceClicked += async name => await LoadPassagesAsync(name);

        _selectedPlaceLabel = new Label
        {
            Text = "Click a place to see its passages here.",
            Left = 884,
            Top = 44,
            Width = 300,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        _passageList = new ListBox
        {
            Left = 884,
            Top = 70,
            Width = 300,
            Height = 674,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
            HorizontalScrollbar = true
        };
        _passageList.DoubleClick += async (_, _) => await JumpToSelectedPassageAsync();
        ListResultHelpers.AttachCitationTooltip(_passageList,
            i => i < _currentPassages.Count ? _currentPassages[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_passageList,
            i => i < _currentPassages.Count
                ? $"{_currentPassages[i].AuthorName}, {_currentPassages[i].WorkTitle} [{_currentPassages[i].CitationRef}]: {_currentPassages[i].Text}"
                : null);

        Controls.Add(legend);
        Controls.Add(_canvas);
        Controls.Add(_selectedPlaceLabel);
        Controls.Add(_passageList);

        Load += async (_, _) => await LoadPlacesAsync();
    }

    private async Task LoadPlacesAsync()
    {
        var (nodes, _) = await _tagRepo.GetCoOccurrenceGraphAsync();

        var markers = new List<MapCanvas.PlaceMarker>();
        foreach (var node in nodes)
        {
            var coords = PlaceData.Lookup(node.Name);
            if (coords == null) continue;

            markers.Add(new MapCanvas.PlaceMarker
            {
                Name = node.Name,
                Lat = coords.Value.Lat,
                Lon = coords.Value.Lon,
                UsageCount = node.UsageCount
            });
        }

        _canvas.SetData(markers);
    }

    private async Task LoadPassagesAsync(string placeName)
    {
        _selectedPlaceLabel.Text = $"\"{placeName}\" (double-click a passage to jump to it):";
        _passageList.Items.Clear();

        _currentPassages = await _tagRepo.GetByTagAsync(placeName);
        foreach (var p in _currentPassages)
        {
            _passageList.Items.Add($"{p.AuthorName}, {p.WorkTitle}: {p.Text}");
        }

        if (_currentPassages.Count == 0)
        {
            _passageList.Items.Add("(no passages found)");
        }
    }

    private async Task JumpToSelectedPassageAsync()
    {
        var index = _passageList.SelectedIndex;
        if (index < 0 || index >= _currentPassages.Count || OnNavigate == null) return;

        var passage = _currentPassages[index];
        await OnNavigate(passage.WorkId, passage.TextNodeId);
        Close();
    }
}
