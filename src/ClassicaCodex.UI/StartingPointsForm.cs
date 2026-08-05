using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// A short list of works that are reasonable to translate first, filtered to
/// what this library actually holds.
///
/// Opened from the same menu as Translate This Myself, because that is the
/// moment the choice is made and the moment it goes wrong. See StartingPoints
/// for why the recommendations exist at all.
/// </summary>
public class StartingPointsForm : Form
{
    private readonly ListView _list;
    private readonly Label _whyLabel;
    private readonly Button _openButton;

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();

    private List<(StartingPoints.Suggestion Suggestion, Work Work)> _available = new();

    /// <summary>The work the reader chose, or null if they closed without choosing.</summary>
    public Work? ChosenWork { get; private set; }

    public StartingPointsForm()
    {
        Text = "Where to start";
        AppIcons.ApplyWindowIcon(this, "GettingStarted");

        // ClientSize, not Width/Height. Height includes the title bar and
        // borders, so laying controls out against it puts the bottom row
        // roughly forty pixels below the area that actually draws - which is
        // exactly where the buttons went.
        ClientSize = new Size(760, 640);
        MinimumSize = new Size(640, 560);
        StartPosition = FormStartPosition.CenterParent;

        var headerLabel = new Label
        {
            Text = "Good places to start translating",
            Left = 16,
            Top = 12,
            Width = 700,
            Height = 26,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold)
        };
        Controls.Add(headerLabel);

        var introLabel = new Label
        {
            Text = "Ancient texts vary enormously in difficulty, and the library tree gives no "
                 + "sign of it. These are ordered easiest first, roughly the order they are "
                 + "taught in. Only works found in your library are shown.",
            Left = 16,
            Top = 42,
            Width = 700,
            Height = 44,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(introLabel);

        _list = new ListView
        {
            Left = 16,
            Top = 94,
            Width = 700,
            Height = 330,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false
        };
        _list.Columns.Add("Work", 300);
        _list.Columns.Add("Language", 90);
        _list.Columns.Add("In your library as", 300);
        _list.SelectedIndexChanged += (_, _) => ShowWhyForSelection();
        _list.DoubleClick += (_, _) => ChooseSelected();
        Controls.Add(_list);

        // The reason sits in its own panel rather than a tooltip or a fourth
        // column: it is a sentence or two of prose and it is the part worth
        // reading, not an annotation on the row.
        _whyLabel = new Label
        {
            Left = 16,
            Top = 434,
            Width = 700,
            Height = 84,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Text = "Select a work to see why it is a good place to begin."
        };
        Controls.Add(_whyLabel);

        var hardLabel = new Label
        {
            Text = StartingPoints.HardWorksNote,
            Left = 16,
            Top = 524,
            Width = 700,
            Height = 60,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray
        };
        Controls.Add(hardLabel);

        _openButton = new Button
        {
            Text = "Translate This",
            Left = 520,
            Top = 594,
            Width = 120,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Enabled = false
        };
        _openButton.Click += (_, _) => ChooseSelected();
        Controls.Add(_openButton);

        var closeButton = new Button
        {
            Text = "Close",
            Left = 648,
            Top = 594,
            Width = 80,
            Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(closeButton);
        CancelButton = closeButton;

        Load += async (_, _) => await LoadSuggestionsAsync();

        ReadingTheme.AttachTo(this, () => hardLabel.ForeColor = ReadingTheme.MutedText);
    }

    private async Task LoadSuggestionsAsync()
    {
        try
        {
            var authors = await _authorRepo.GetAllAsync();
            var authorNames = authors.ToDictionary(a => a.AuthorId, a => a.Name);
            var worksByAuthor = await _workRepo.GetAllGroupedByAuthorAsync();

            var candidates = StartingPoints.AvailableIn(worksByAuthor, authorNames);

            // A work with no original-language edition cannot be translated
            // from, and the workbench would refuse it after the reader had
            // already committed to the choice. Checked here rather than in
            // the matcher because it costs a query per candidate, and there
            // are at most a dozen of those.
            _available = new List<(StartingPoints.Suggestion, Work)>();
            foreach (var candidate in candidates)
            {
                var editions = await _editionRepo.GetByWorkAsync(candidate.Work.WorkId);
                if (editions.Any(e => e.Kind == EditionKind.Original)) _available.Add(candidate);
            }

            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var (suggestion, work) in _available)
                {
                    var language = suggestion.Language == "grc" ? "Greek" : "Latin";
                    var item = new ListViewItem(new[] { suggestion.Display, language, work.Title });
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            if (_available.Count == 0)
            {
                _whyLabel.Text = "None of the recommended works are in this library yet. "
                               + "The Setup Wizard can fetch more of the corpus.";
            }
        }
        catch (Exception ex)
        {
            _whyLabel.Text = $"Couldn't read the library: {ex.Message}";
        }
    }

    private void ShowWhyForSelection()
    {
        if (_list.SelectedIndices.Count == 0)
        {
            _openButton.Enabled = false;
            return;
        }

        var (suggestion, _) = _available[_list.SelectedIndices[0]];
        _whyLabel.Text = suggestion.Why;
        _openButton.Enabled = true;
    }

    private void ChooseSelected()
    {
        if (_list.SelectedIndices.Count == 0) return;

        ChosenWork = _available[_list.SelectedIndices[0]].Work;
        DialogResult = DialogResult.OK;
        Close();
    }
}
