using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class MythNetworkForm : ScaledForm
{
    private readonly GraphCanvas _canvas;
    private readonly TrackBar _thresholdTrackBar;
    private readonly Label _thresholdValueLabel;
    private readonly ComboBox _modeComboBox;
    private readonly NumericUpDown _windowUpDown;
    private readonly ListBox _passageList;
    private readonly Label _selectedTagLabel;
    private readonly TagRepository _tagRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _currentPassages = new();

    /// <summary>
    /// Set by MainForm before showing this dialog. Double-clicking a passage
    /// invokes this with the work and text node to jump to.
    /// </summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public MythNetworkForm()
    {
        Text = "Myth Network - drag nodes to untangle, click one to see its passages";
        AppIcons.ApplyWindowIcon(this, "MythNetwork");
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterParent;

        var relayoutButton = new Button { Text = "Re-layout", Left = 12, Top = 8, Width = 100, Height = 26 };
        relayoutButton.Click += (_, _) => _canvas.Relayout();

        var autoTagButton = new Button { Text = "Auto-Tag...", Left = 120, Top = 8, Width = 110, Height = 26 };
        autoTagButton.Click += (_, _) =>
        {
            using var autoTagForm = new AutoTagForm { OnNavigate = OnNavigate };
            autoTagForm.TagsChanged += () => _ = LoadGraphAsync();
            autoTagForm.ShowDialog(this);
        };

        var shapesButton = new Button { Text = "Shapes...", Left = 238, Top = 8, Width = 90, Height = 26 };
        shapesButton.Click += (_, _) =>
        {
            using var shapesForm = new CategoryShapesForm();
            shapesForm.ShapesChanged += () => _canvas.Invalidate();
            shapesForm.ShowDialog(this);
            _canvas.Invalidate();
        };

        var modeLabel = new Label { Text = "Co-occurrence:", Left = 338, Top = 12, Width = 100 };
        _modeComboBox = new ComboBox
        {
            Left = 442,
            Top = 8,
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _modeComboBox.Items.AddRange(new object[]
        {
            "Same work (weaker, more edges)",
            "Same passage (stronger, fewer edges)"
        });
        _modeComboBox.SelectedIndex = 1; // proximity is the more useful default now that it exists
        _modeComboBox.SelectedIndexChanged += async (_, _) => await LoadGraphAsync();

        var windowLabel = new Label { Text = "Window (lines):", Left = 642, Top = 12, Width = 100 };
        _windowUpDown = new NumericUpDown
        {
            Left = 746,
            Top = 8,
            Width = 60,
            Minimum = 1,
            Maximum = 500,
            Value = 25
        };
        _windowUpDown.ValueChanged += async (_, _) => await LoadGraphAsync();

        var thresholdLabel = new Label { Text = "Min shared:", Left = 12, Top = 44, Width = 90 };
        _thresholdTrackBar = new TrackBar
        {
            Left = 104,
            Top = 36,
            Width = 150,
            // A TrackBar ignores Height until AutoSize is off, and defaults
            // to ~45px - tall enough to overhang the canvas below it.
            AutoSize = false,
            Height = 26,
            Minimum = 1,
            Maximum = 10,
            Value = 1,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 1
        };
        _thresholdValueLabel = new Label { Text = "1", Left = 258, Top = 44, Width = 30 };
        _thresholdTrackBar.Scroll += (_, _) =>
        {
            _thresholdValueLabel.Text = _thresholdTrackBar.Value.ToString();
            _canvas.SetMinSharedWorks(_thresholdTrackBar.Value);
        };

        var legend = new Label
        {
            Text = "Circle size = how often you've used the tag. Line thickness = how strongly two tags co-occur. " +
                   "Drag nodes; click one to browse its passages; right-click one to search for related artifacts.",
            Left = 296,
            Top = 40,
            Width = 700
        };

        _canvas = new GraphCanvas
        {
            Left = 12,
            Top = 68,
            Width = 860,
            Height = 678,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };
        _canvas.NodeClicked += async name => await LoadPassagesAsync(name);
        _canvas.EdgeClicked += async (nameA, nameB) => await LoadEdgePassagesAsync(nameA, nameB);
        _canvas.NodeRightClicked += name =>
        {
            using var artifactForm = new ArtifactBrowserForm(name, name);
            artifactForm.ShowDialog(this);
        };

        _selectedTagLabel = new Label
        {
            Text = "Click a tag to see its passages here.",
            Left = 884,
            Top = 68,
            Width = 300,
            Font = new Font(Font, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        _passageList = new ListBox
        {
            Left = 884,
            Top = 94,
            Width = 300,
            Height = 652,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right
        };
        _passageList.DoubleClick += async (_, _) => await JumpToSelectedPassageAsync();
        ListResultHelpers.AttachCitationTooltip(_passageList,
            i => i < _currentPassages.Count ? _currentPassages[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_passageList,
            i => i < _currentPassages.Count
                ? $"{_currentPassages[i].AuthorName}, {_currentPassages[i].WorkTitle} [{_currentPassages[i].CitationRef}]: {_currentPassages[i].Text}"
                : null);

        Controls.Add(relayoutButton);
        Controls.Add(autoTagButton);
        Controls.Add(shapesButton);
        Controls.Add(modeLabel);
        Controls.Add(_modeComboBox);
        Controls.Add(windowLabel);
        Controls.Add(_windowUpDown);
        Controls.Add(thresholdLabel);
        Controls.Add(_thresholdTrackBar);
        Controls.Add(_thresholdValueLabel);
        Controls.Add(legend);
        Controls.Add(_canvas);
        Controls.Add(_selectedTagLabel);
        Controls.Add(_passageList);

        Load += async (_, _) =>
        {
            ReadingTheme.Apply(this);
            await LoadGraphAsync();
        };

        // Kept in sync if the mode is toggled while this is open. The
        // handler is removed on close so it doesn't outlive the form and
        // keep a disposed window alive.
        void OnThemeChanged()
        {
            ReadingTheme.Apply(this);
            _canvas.Invalidate();
            Invalidate(true);
        }

        WindowShortcuts.CloseOnEscape(this);

        ReadingTheme.Changed += OnThemeChanged;
        FormClosed += (_, _) => ReadingTheme.Changed -= OnThemeChanged;
    }

    private async Task LoadGraphAsync()
    {
        var useProximity = _modeComboBox.SelectedIndex != 0; // default (index 1) is proximity
        _windowUpDown.Enabled = useProximity;

        var (nodes, edges) = useProximity
            ? await _tagRepo.GetProximityCoOccurrenceGraphAsync((int)_windowUpDown.Value)
            : await _tagRepo.GetCoOccurrenceGraphAsync();

        _canvas.SetData(nodes, edges);

        // The slider was hardcoded to top out at 10, which turned out to be
        // far below real edge weights once encyclopedic sources (Apollodorus,
        // Ovid) are in the mix - basically every pair of gods shares dozens
        // of works with something that mentions the whole pantheon. Sizing
        // the range to the actual data means the slider can reach wherever
        // this dataset's real signal-vs-noise line actually falls, instead
        // of maxing out well short of it.
        var maxWeight = _canvas.MaxEdgeWeight;
        _thresholdTrackBar.Maximum = Math.Max(maxWeight, 1);
        _thresholdTrackBar.TickFrequency = Math.Max(maxWeight / 20, 1);
    }

    private async Task LoadPassagesAsync(string tagName)
    {
        _selectedTagLabel.Text = $"\"{tagName}\" (double-click a passage to jump to it):";
        _passageList.Items.Clear();

        _currentPassages = await _tagRepo.GetByTagAsync(tagName);
        foreach (var p in _currentPassages)
        {
            _passageList.Items.Add($"{p.AuthorName}, {p.WorkTitle}: {p.Text}");
        }

        if (_currentPassages.Count == 0)
        {
            _passageList.Items.Add("(no passages found)");
        }
    }

    /// <summary>
    /// Loads the specific passages that justify a clicked edge - whichever
    /// ones actually satisfy the current co-occurrence mode (same work, or
    /// nearby in the same passage), not just anything tagged with either
    /// name. This is what makes clicking an edge meaningfully different from
    /// clicking either of its two endpoint nodes.
    /// </summary>
    private async Task LoadEdgePassagesAsync(string tagNameA, string tagNameB)
    {
        _selectedTagLabel.Text = $"\"{tagNameA}\" \u2194 \"{tagNameB}\" (double-click a passage to jump to it):";
        _passageList.Items.Clear();

        var useProximity = _modeComboBox.SelectedIndex != 0;
        var window = (int)_windowUpDown.Value;

        var edgePassages = await _tagRepo.GetEdgePassagesAsync(tagNameA, tagNameB, useProximity, window);

        _currentPassages = edgePassages
            .Select(p => (p.WorkId, p.TextNodeId, p.AuthorName, p.WorkTitle, p.CitationRef, p.Text))
            .ToList();

        foreach (var p in edgePassages)
        {
            _passageList.Items.Add($"[{p.TagName}] {p.AuthorName}, {p.WorkTitle}: {p.Text}");
        }

        if (edgePassages.Count == 0)
        {
            _passageList.Items.Add("(nothing qualified this edge - try widening the proximity window)");
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
