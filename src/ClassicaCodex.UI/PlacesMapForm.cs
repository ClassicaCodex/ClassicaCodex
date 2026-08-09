using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class PlacesMapForm : Form
{
    private readonly MapCanvas _canvas;
    private readonly ListBox _passageList;
    private readonly Label _selectedPlaceLabel;
    private readonly TagRepository _tagRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly ArtifactRepository _artifactRepo = new();

    private readonly ArtifactBrowserControl _artifactBrowser;

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _currentPassages = new();

    /// <summary>Tags on the currently listed passages, keyed by node.</summary>
    private Dictionary<long, List<string>> _tagsByNode = new();

    /// <summary>
    /// The place whose passages are listed, kept separately from the label
    /// above the list. That label is a full sentence - "Passages tagged
    /// "Delphi" (double-click to jump):" - which is right for the screen and
    /// wrong for anything that needs the bare name, like the title of an
    /// export.
    /// </summary>
    private string _selectedPlaceName = string.Empty;

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
            Text = "Click any place to search the library for it. Passages you have tagged are marked in the " +
                   "results. Scroll to zoom, drag to pan, double-click open sea to reset the view.",
            Left = 12,
            Top = 10,
            Width = 860
        };

        _canvas = new MapCanvas
        {
            Left = 12,
            Top = 48,
            Width = 860,
            Height = 696,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };
        _canvas.PlaceClicked += async (name, _) =>
        {
            await LoadArtifactsAsync(name);
            await LoadSearchResultsAsync(name);
        };

        _artifactBrowser = new ArtifactBrowserControl
        {
            Left = 884,
            Top = 76,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        _selectedPlaceLabel = new Label
        {
            Text = "Click a place to see its passages here.",
            Left = 884,
            Top = 390,
            Width = 300,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        _passageList = new ListBox
        {
            Left = 884,
            Top = 416,
            Width = 300,
            Height = 328,
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
        ListResultHelpers.AttachExportMenu(_passageList, () => (
            $"Passages mentioning {_selectedPlaceName}",
            _currentPassages.Select(r => new ExportPassage(
                r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text)).ToList()), this);

        Controls.Add(legend);
        Controls.Add(_canvas);
        Controls.Add(_artifactBrowser);
        Controls.Add(_selectedPlaceLabel);
        Controls.Add(_passageList);

        Load += (_, _) => LoadPlaces();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    /// <summary>
    /// Every place in the reference list, on one footing.
    ///
    /// The map used to pin tagged places in one colour and the rest in
    /// another, behind a toggle. That split was worth nothing here: the
    /// reference list is loaded for everyone during setup, so on a fresh
    /// library every pin was the same colour anyway, and a place you happened
    /// to have tagged behaved differently from an identical one beside it for
    /// reasons the map couldn't show. Tags still surface, but where they mean
    /// something - against the passages themselves, in the list.
    /// </summary>
    private void LoadPlaces()
    {
        var markers = PlaceData.All()
            .Select(p => new MapCanvas.PlaceMarker
            {
                Name = p.Name,
                Lat = p.Lat,
                Lon = p.Lon,
                IsYourTag = true
            })
            .ToList();

        _canvas.SetData(markers);
        _canvas.SetAllPlacesData(new List<MapCanvas.PlaceMarker>());
    }

    private async Task LoadArtifactsAsync(string placeName)
    {
        var artifacts = await _artifactRepo.GetByPlaceNameAsync(placeName);
        _artifactBrowser.LoadArtifacts(artifacts);
    }

    /// <summary>
    /// Runs the same word-form-aware search the main search box uses, over the
    /// place's literal name.
    /// </summary>
    private async Task LoadSearchResultsAsync(string placeName)
    {
        _selectedPlaceName = placeName;
        _selectedPlaceLabel.Text = $"Search results for \"{placeName}\" (double-click to jump):";
        _passageList.Items.Clear();

        var hits = await _textNodeRepo.SearchAsync(placeName);
        _currentPassages = hits.Rows;

        _tagsByNode = await _tagRepo.GetTagNamesForNodesAsync(
            _currentPassages.Select(p => p.TextNodeId).ToList());

        RenderPassageList(hits.Truncated);
    }

    private void RenderPassageList(bool truncated = false)
    {
        foreach (var p in _currentPassages)
        {
            // A passage you have already marked, met again from the map side.
            // The tags didn't produce this list and aren't a filter on it -
            // they are worth noticing where they happen to coincide with it,
            // which is the only thing tags were ever doing on this screen.
            //
            // Plain ASCII rather than a star or a bullet. This ListBox has
            // HorizontalScrollbar set, so every item added is measured for the
            // scroll extent, and a character the list's font has no glyph for
            // took that measurement down with a GDI+ error rather than falling
            // back to a box. The same lesson as the elided Greek in the PDF
            // export: one font, no fallback, so stay inside what it carries.
            var mark = _tagsByNode.TryGetValue(p.TextNodeId, out var tags)
                ? $"[tagged: {string.Join(", ", tags)}]  "
                : "";

            _passageList.Items.Add($"{mark}{p.AuthorName}, {p.WorkTitle}: {p.Text}");
        }

        if (_currentPassages.Count == 0)
        {
            _passageList.Items.Add("(no passages found)");
        }
        else if (truncated)
        {
            _passageList.Items.Add($"--- stopped at {_currentPassages.Count}; there are more ---");
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
