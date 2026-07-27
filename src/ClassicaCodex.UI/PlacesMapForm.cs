using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class PlacesMapForm : Form
{
    private readonly MapCanvas _canvas;
    private readonly ListBox _passageList;
    private readonly Label _selectedPlaceLabel;
    private readonly CheckBox _showAllPlacesCheckbox;
    private readonly TagRepository _tagRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

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

        _showAllPlacesCheckbox = new CheckBox
        {
            Text = "Show all known places",
            Left = 12,
            Top = 46,
            Width = 170,
            Height = 22
        };
        _showAllPlacesCheckbox.CheckedChanged += (_, _) => _canvas.ShowAllKnownPlaces = _showAllPlacesCheckbox.Checked;

        // Small swatches referencing MapCanvas's own color constants directly,
        // not a guessed-at copy - so this key can never quietly go out of
        // sync with what color the pins actually are.
        var yourTagsSwatch = new Panel { Left = 210, Top = 50, Width = 12, Height = 12, BackColor = MapCanvas.PinFillColor };
        var yourTagsLabel = new Label { Text = "Your tagged places", Left = 226, Top = 46, Width = 130 };
        var knownPlacesSwatch = new Panel { Left = 366, Top = 50, Width = 12, Height = 12, BackColor = MapCanvas.KnownPlaceFillColor };
        var knownPlacesLabel = new Label
        {
            Text = "All known places (click to search the text for it)",
            Left = 382,
            Top = 46,
            Width = 320
        };

        _canvas = new MapCanvas
        {
            Left = 12,
            Top = 76,
            Width = 860,
            Height = 668,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };
        _canvas.PlaceClicked += async (name, isYourTag) =>
        {
            if (isYourTag) await LoadTaggedPassagesAsync(name);
            else await LoadSearchResultsAsync(name);
        };

        _selectedPlaceLabel = new Label
        {
            Text = "Click a place to see its passages here.",
            Left = 884,
            Top = 76,
            Width = 300,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        _passageList = new ListBox
        {
            Left = 884,
            Top = 102,
            Width = 300,
            Height = 642,
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
        Controls.Add(_showAllPlacesCheckbox);
        Controls.Add(yourTagsSwatch);
        Controls.Add(yourTagsLabel);
        Controls.Add(knownPlacesSwatch);
        Controls.Add(knownPlacesLabel);
        Controls.Add(_canvas);
        Controls.Add(_selectedPlaceLabel);
        Controls.Add(_passageList);

        Load += async (_, _) => await LoadPlacesAsync();
    }

    private async Task LoadPlacesAsync()
    {
        var (nodes, _) = await _tagRepo.GetCoOccurrenceGraphAsync();

        var yourTagMarkers = new List<MapCanvas.PlaceMarker>();
        var taggedCoordinates = new HashSet<(double, double)>();
        foreach (var node in nodes)
        {
            var coords = PlaceData.Lookup(node.Name);
            if (coords == null) continue;

            yourTagMarkers.Add(new MapCanvas.PlaceMarker
            {
                Name = node.Name,
                Lat = coords.Value.Lat,
                Lon = coords.Value.Lon,
                UsageCount = node.UsageCount,
                IsYourTag = true
            });
            taggedCoordinates.Add((coords.Value.Lat, coords.Value.Lon));
        }

        // Everything in the full catalog EXCEPT places already covered
        // above - otherwise a place you've tagged would show two stacked
        // pins, one from each list, right on top of each other.
        var knownPlaceMarkers = PlaceData.All()
            .Where(p => !taggedCoordinates.Contains((p.Lat, p.Lon)))
            .Select(p => new MapCanvas.PlaceMarker
            {
                Name = p.Name,
                Lat = p.Lat,
                Lon = p.Lon,
                IsYourTag = false
            })
            .ToList();

        _canvas.SetData(yourTagMarkers);
        _canvas.SetAllPlacesData(knownPlaceMarkers);
    }

    private async Task LoadTaggedPassagesAsync(string placeName)
    {
        _selectedPlaceLabel.Text = $"Passages tagged \"{placeName}\" (double-click to jump):";
        _passageList.Items.Clear();

        _currentPassages = await _tagRepo.GetByTagAsync(placeName);
        RenderPassageList();
    }

    /// <summary>
    /// For a place you haven't tagged - there's no tag data to show, so
    /// this runs the same word-form-aware text search the main search box
    /// uses instead, over the place's literal name.
    /// </summary>
    private async Task LoadSearchResultsAsync(string placeName)
    {
        _selectedPlaceLabel.Text = $"Search results for \"{placeName}\" (double-click to jump):";
        _passageList.Items.Clear();

        _currentPassages = await _textNodeRepo.SearchAsync(placeName);
        RenderPassageList();
    }

    private void RenderPassageList()
    {
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
