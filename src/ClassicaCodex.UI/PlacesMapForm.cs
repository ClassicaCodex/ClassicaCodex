using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class PlacesMapForm : ScaledForm
{
    private readonly MapCanvas _canvas;
    private readonly FlowLayoutPanel _kindFilters;

    /// <summary>
    /// The legend swatches, so they can be recoloured after the theme pass has
    /// repainted them.
    /// </summary>
    private readonly List<(PlaceKind Kind, Panel Swatch)> _kindSwatches = new();
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
                   "results. Scroll to zoom, drag to pan, double-click open sea to reset the view. " +
                   "Untick a kind below to thin the map out.",
            Left = 12,
            Top = 10,
            Width = 860
        };

        // One checkbox per kind, drawn in its own pin colour so the legend is
        // also the key. Sits between the instructions and the map rather than
        // beside it: the map is the widest thing on the form and taking width
        // from it to save 26 vertical pixels is a poor trade.
        _kindFilters = new FlowLayoutPanel
        {
            Left = 12,
            Top = 44,
            Width = 860,
            Height = 26,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _canvas = new MapCanvas
        {
            Left = 12,
            Top = 74,
            Width = 860,
            Height = 670,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };

        var showIcon = new PictureBox
        {
            Image = AppIcons.Get("Show", 18),
            Width = 18,
            Height = 18,
            SizeMode = PictureBoxSizeMode.AutoSize,
            Margin = new Padding(0, 4, 8, 0)
        };

        // Null when the Icons folder is absent, which AppIcons treats as normal
        // - so the row degrades to plain checkboxes rather than a blank gap.
        if (showIcon.Image != null) _kindFilters.Controls.Add(showIcon);

        foreach (var kind in Enum.GetValues<PlaceKind>())
        {
            var count = PlaceData.All().Count(p => p.Kind == kind);

            // A swatch in the pin colour, then the label in the theme's normal
            // text colour.
            //
            // The label WAS drawn in the pin colour, which broke in dark mode:
            // those colours are chosen to read against the map's parchment and
            // sea, not against a form background, and ReadingTheme quite
            // reasonably re-themed the control afterwards and put them back to
            // black. Colour belongs on a swatch, where it only has to be itself;
            // text belongs in whatever colour text is meant to be.
            var swatch = new Panel
            {
                Width = 11,
                Height = 11,
                Margin = new Padding(0, 7, 4, 0),
                BackColor = _canvas.LegendColorFor(kind)
            };

            var box = new CheckBox
            {
                // The count is here because "Sanctuaries" covers seven places
                // and "Cities" covers a hundred and nineteen, and knowing that
                // before clicking saves finding out by clicking.
                Text = $"{KindLabel(kind)} ({count})",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 3, 16, 0)
            };

            // The swatch keeps its colour whether the group is shown or not.
            // Dimming it was the first attempt and it read as a second, weaker
            // checkbox: the tick already says whether the group is on, and a
            // key that changes colour is no longer a key.
            box.CheckedChanged += (_, _) => _canvas.SetKindVisible(kind, box.Checked);

            _kindSwatches.Add((kind, swatch));
            _kindFilters.Controls.Add(swatch);
            _kindFilters.Controls.Add(box);
        }
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

            // No horizontal scrollbar, deliberately. Setting it makes WinForms measure
            // every item it is given with GDI+ to work out the scroll extent, and that
            // measurement throws on characters the list's font cannot resolve - which
            // the Menota transcriptions are full of, since medieval Nordic glyphs are
            // encoded in the Unicode private use area. Clicking a place that matched one
            // of those passages took the window down with "a generic error occurred in
            // GDI+" and no indication of which passage or why.
            //
            // The measurement is the only thing that fails, so the fix is to stop asking
            // for it. Nothing is lost that this list was providing: at 300px wide,
            // scrolling a passage sideways was never how it was read - the entry is
            // there to be recognised and double-clicked, which opens it in the reader.
            // The text is trimmed below so the part that identifies it stays visible.
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
        Controls.Add(_kindFilters);
        Controls.Add(_canvas);
        Controls.Add(_artifactBrowser);
        Controls.Add(_selectedPlaceLabel);
        Controls.Add(_passageList);

        Load += (_, _) => LoadPlaces();
        // The swatch colours go in AttachTo's "extra" callback, not before or
        // after the call.
        //
        // AttachTo does not theme anything immediately - it registers
        // form.Load, and Apply walks the control tree then, repainting every
        // Panel in the surface colour. So colouring these in the constructor
        // set them and Load promptly wiped them; colouring them on the line
        // after AttachTo did exactly the same thing, because that line still
        // runs before Load. The first version only ever showed a colour once a
        // CheckedChanged handler happened to fire later than Load, which is why
        // the swatches appeared on the first click and not before.
        //
        // "extra" runs after every apply, which also keeps them right across a
        // theme switch rather than only at startup.
        ReadingTheme.AttachTo(this, () =>
        {
            foreach (var (kind, swatch) in _kindSwatches)
                swatch.BackColor = _canvas.LegendColorFor(kind);
        });

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
    /// <summary>
    /// Plural, readable label for a kind. Region covers islands and countries
    /// as well, so it is worded for what it holds rather than named after the
    /// enum.
    /// </summary>
    private static string KindLabel(PlaceKind kind) => kind switch
    {
        PlaceKind.City => "Cities",
        PlaceKind.Sanctuary => "Sanctuaries",
        PlaceKind.Battlefield => "Battlefields",
        PlaceKind.Water => "Rivers and seas",
        _ => "Regions and islands"
    };

    private void LoadPlaces()
    {
        var markers = PlaceData.All()
            .Select(p => new MapCanvas.PlaceMarker
            {
                Name = p.Name,
                Lat = p.Lat,
                Lon = p.Lon,
                IsYourTag = true,
                Kind = p.Kind
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
            // Plain ASCII rather than a star or a bullet - originally because the
            // scroll-extent measurement choked on anything else, and still because a
            // marker that renders as a box says less than four brackets do.
            var mark = _tagsByNode.TryGetValue(p.TextNodeId, out var tags)
                ? $"[tagged: {string.Join(", ", tags)}]  "
                : "";

            // Trimmed rather than scrolled. The whole passage is one double-click away
            // in the reader, where it is set in a font chosen for it.
            var text = p.Text.Length <= 160 ? p.Text : p.Text[..160] + "…";

            _passageList.Items.Add($"{mark}{p.AuthorName}, {p.WorkTitle}: {text}");
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
