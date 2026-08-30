using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Two editions of one work, side by side, with what they disagree about.
///
/// This is the one thing in the app that needs more than one collection to
/// exist at all. A single corpus gives one printing of a text and no way to
/// know what it settled; two give the disagreement, which is where the
/// editorial work actually is.
///
/// Deliberately not a verdict. It sorts differences by how much of one they
/// are - typography, spelling, lineation, or the words - and shows the counts,
/// so the reader can see at a glance whether a pairing is worth reading before
/// reading any of it. Which reading is right is not a question this can answer
/// and it does not pretend to.
/// </summary>
public class CollationForm : ScaledForm
{
    private readonly CollationRepository _repo = new();

    private readonly ComboBox _pairBox = new();
    private readonly ComboBox _showBox = new();
    private readonly Button _exportButton = new();
    private readonly Label _summary = new();
    private readonly ListView _rows = new();
    private readonly TextBox _leftDetail = new();
    private readonly TextBox _rightDetail = new();
    private readonly Label _leftHeader = new();
    private readonly Label _rightHeader = new();

    private List<CollationPair> _pairs = new();
    private CollationResult? _result;
    private bool _loading;

    public CollationForm()
    {
        Text = "Classica Codex - Collate Editions";
        AppIcons.ApplyWindowIcon(this, "Collate");
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterParent;

        var explainer = new Label
        {
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(10, 8, 10, 0),
            ForeColor = Color.DimGray,
            Text = "Works this library holds more than one edition of. Differences are graded: " +
                   "only the last kind is a difference in the words."
        };

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(10, 4, 10, 4),
            WrapContents = false
        };

        _pairBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _pairBox.Width = 520;
        _pairBox.SelectedIndexChanged += async (_, _) => await LoadSelectedAsync();

        _showBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _showBox.Width = 220;
        _showBox.Items.AddRange(["Differences in the words", "Every difference", "Every passage"]);
        _showBox.SelectedIndex = 0;
        _showBox.SelectedIndexChanged += (_, _) => RenderRows();

        controls.Controls.Add(new Label { Text = "Work:", Width = 44, TextAlign = ContentAlignment.MiddleLeft });
        controls.Controls.Add(_pairBox);
        controls.Controls.Add(new Label { Text = "Show:", Width = 46, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(16, 0, 0, 0) });
        controls.Controls.Add(_showBox);

        // The same menu the list carries on right-click, reachable from a
        // button as well. A collation is a table someone will want in a
        // spreadsheet, and an export you can only find by right-clicking a
        // list is an export most people never find.
        _exportButton.Text = "Export...";
        _exportButton.Width = 96;
        _exportButton.Margin = new Padding(16, 2, 0, 0);
        _exportButton.Click += (_, _) =>
            _rows.ContextMenuStrip?.Show(_exportButton, new Point(0, _exportButton.Height));
        controls.Controls.Add(_exportButton);

        _summary.Dock = DockStyle.Top;
        _summary.Height = 34;
        _summary.Padding = new Padding(10, 6, 10, 0);

        _rows.Dock = DockStyle.Fill;
        _rows.View = View.Details;
        _rows.FullRowSelect = true;
        _rows.MultiSelect = false;
        _rows.HideSelection = false;
        _rows.Columns.Add("Passage", 90);
        _rows.Columns.Add("Difference", 130);
        _rows.Columns.Add("", 440);
        _rows.Columns.Add("", 440);
        _rows.SelectedIndexChanged += (_, _) => ShowSelectedDetail();

        // The reading font, because these two columns are the text - and no
        // horizontal scrollbar, which is what makes WinForms measure every item
        // with GDI+ and throw on glyphs the font cannot resolve.
        var detail = BuildDetailPanel();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 420
        };
        split.Panel1.Controls.Add(_rows);
        split.Panel2.Controls.Add(detail);

        Controls.Add(split);
        Controls.Add(_summary);
        Controls.Add(controls);
        Controls.Add(explainer);

        ResultExport.AttachTo(_rows, SuggestedFileName, ExportRows, ExportNotes);
        if (_rows.ContextMenuStrip != null) ReadingTheme.ApplyToContextMenu(_rows.ContextMenuStrip);

        WindowShortcuts.CloseOnEscape(this);
        ReadingTheme.AttachTo(this);

        Load += async (_, _) => await LoadPairsAsync();
    }

    private Control BuildDetailPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10, 6, 10, 10)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _leftHeader.Dock = DockStyle.Fill;
        _leftHeader.Font = new Font(Font, FontStyle.Bold);
        _rightHeader.Dock = DockStyle.Fill;
        _rightHeader.Font = new Font(Font, FontStyle.Bold);

        foreach (var box in new[] { _leftDetail, _rightDetail })
        {
            box.Dock = DockStyle.Fill;
            box.Multiline = true;
            box.ReadOnly = true;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.ScrollBars = ScrollBars.Vertical;
            // The same face and size the reader uses for the original-language
            // pane. These two boxes are the text, not a report about it.
            box.Font = new Font("Palatino Linotype", ReadingFontSettings.SourceSize);
        }

        panel.Controls.Add(_leftHeader, 0, 0);
        panel.Controls.Add(_rightHeader, 1, 0);
        panel.Controls.Add(_leftDetail, 0, 1);
        panel.Controls.Add(_rightDetail, 1, 1);

        return panel;
    }

    private sealed record PairOption(CollationPair Pair, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// What the picker shows for one pairing.
    ///
    /// The collection names alone are not enough, and assuming they were made
    /// five entries of Agathemerus read identically. A work can have more than
    /// two editions - one here has six - so several pairings share the same two
    /// collections, and two editions can come from the same collection
    /// entirely, which leaves the collection name saying nothing at all.
    ///
    /// So the version identifier is added wherever the collections do not
    /// separate the pairings, and left off where they do - it is precise but it
    /// is not what anyone is looking for, and putting "perseus-grc2" on every
    /// row would bury the collection names under identifiers that matter for a
    /// handful of works.
    /// </summary>
    private string DescribePair(CollationPair pair)
    {
        var left = SetupDataSourceCatalog.DescribeCollection(pair.LeftCollection);
        var right = SetupDataSourceCatalog.DescribeCollection(pair.RightCollection);

        var sameWork = _pairs.Count(p => p.WorkId == pair.WorkId);
        var sameCollections = _pairs.Count(p =>
            p.WorkId == pair.WorkId
            && p.LeftCollection == pair.LeftCollection
            && p.RightCollection == pair.RightCollection);

        var sides = sameWork > 1 && (sameCollections > 1 || pair.WithinOneCollection)
            ? $"{left} {pair.LeftVersion} / {right} {pair.RightVersion}"
            : $"{left} / {right}";

        return $"{pair.AuthorName} - {pair.WorkTitle}  ({sides})";
    }

    private async Task LoadPairsAsync()
    {
        _pairs = await _repo.FindPairsAsync();

        _pairBox.Items.Clear();
        foreach (var pair in _pairs) _pairBox.Items.Add(new PairOption(pair, DescribePair(pair)));

        if (_pairs.Count == 0)
        {
            // Not an error, and the commonest state for a library with one
            // collection - so it says what would make it possible rather than
            // just showing an empty list.
            _summary.Text = "No work in this library has two original-language editions, so there is " +
                            "nothing to collate yet. Installing a collection that overlaps one you " +
                            "already have creates the pairs.";
            _showBox.Enabled = false;
            return;
        }

        _pairBox.SelectedIndex = 0;
    }

    private async Task LoadSelectedAsync()
    {
        if (_loading || _pairBox.SelectedItem is not PairOption { Pair: var pair }) return;

        _loading = true;
        try
        {
            UseWaitCursor = true;
            _result = await _repo.CollateAsync(pair);

            // The version identifier always, on the columns and panes that hold
            // the text itself. Two editions from one collection would otherwise
            // give both sides the same heading, and even across collections the
            // identifier is what a reader would cite.
            _leftHeader.Text =
                $"{SetupDataSourceCatalog.DescribeCollection(pair.LeftCollection)}  ({pair.LeftVersion})";
            _rightHeader.Text =
                $"{SetupDataSourceCatalog.DescribeCollection(pair.RightCollection)}  ({pair.RightVersion})";

            RenderSummary();
            RenderRows();
        }
        finally
        {
            UseWaitCursor = false;
            _loading = false;
        }
    }

    private void RenderSummary()
    {
        if (_result is not { } result) return;

        if (!result.IsAlignable)
        {
            // Said rather than hidden. These two divide the work so differently
            // that their references do not name the same passages - which is a
            // fact about the editions worth knowing, and much better than a
            // list of a thousand invented variants.
            _summary.Text = result.Shared == 0
                ? "These two editions share no passage references - they divide the work differently, " +
                  "so there is nothing to line up."
                : $"These two editions disagree at almost every one of the {result.Shared:N0} references " +
                  "they appear to share, which means the references are not naming the same passages. " +
                  "Not collatable.";
            return;
        }

        _summary.Text =
            $"{result.Shared:N0} shared passages:  {result.Identical:N0} identical,  " +
            $"{result.PresentationDiffers:N0} punctuation or spacing,  " +
            $"{result.OrthographyDiffers:N0} spelling,  " +
            $"{result.LineationDiffers:N0} line division,  " +
            $"{result.TextDiffers:N0} in the words." +
            (result.OnlyInLeft + result.OnlyInRight > 0
                ? $"   ({result.OnlyInLeft:N0} only on the left, {result.OnlyInRight:N0} only on the right)"
                : string.Empty);

        // A note about what a high rate means, not a warning that something is
        // wrong. Sophocles' Ajax comes out at 89% here, and looking at the rows
        // shows that rate is honest: a dialectal kunagia against kunegia, an
        // anexei against an anexei subjunctive, differing word division. Those
        // are two editors' texts, not two printings of one.
        //
        // Which is the useful thing to say. At this rate the two editions
        // disagree wholesale rather than at points, so the list is a
        // description of the distance between them and not a shortlist of
        // cruxes - and it should not be read as one.
        const double wholesale = 0.6;
        if (result.Shared > 0 && result.TextDiffers > result.Shared * wholesale)
        {
            _summary.Text += "   ⚠ At this rate the two disagree wholesale rather than at points - " +
                             "different editors' texts, or one transcribed less carefully. Read it as " +
                             "the distance between them, not a shortlist of cruxes.";
        }
    }

    private void RenderRows()
    {
        _rows.BeginUpdate();
        try
        {
            _rows.Items.Clear();
            if (_result is not { } result) return;

            _rows.Columns[2].Text = _leftHeader.Text;
            _rows.Columns[3].Text = _rightHeader.Text;

            foreach (var row in result.Rows.Where(Included))
            {
                var item = new ListViewItem(row.PassageRef) { Tag = row };
                item.SubItems.Add(Describe(row.Status));
                item.SubItems.Add(row.Left ?? "—");
                item.SubItems.Add(row.Right ?? "—");
                _rows.Items.Add(item);
            }

            if (_rows.Items.Count > 0) _rows.Items[0].Selected = true;
        }
        finally
        {
            _rows.EndUpdate();
        }
    }

    /// <summary>
    /// The three views, narrowest first, because the narrowest is the one worth
    /// opening with: on the Aeschylus pairings roughly a fifth of the shared
    /// lines differ in the words and the rest is typography, and showing the
    /// typography by default would bury the readings under it.
    /// </summary>
    private bool Included(CollationRow row) => _showBox.SelectedIndex switch
    {
        0 => row.Status is CollationStatus.TextDiffers
            or CollationStatus.OnlyInLeft or CollationStatus.OnlyInRight,
        1 => row.Status != CollationStatus.Identical,
        _ => true
    };

    private static string Describe(CollationStatus status) => status switch
    {
        CollationStatus.Identical => "identical",
        CollationStatus.PresentationDiffers => "punctuation",
        CollationStatus.OrthographyDiffers => "spelling",
        CollationStatus.LineationDiffers => "line division",
        CollationStatus.TextDiffers => "THE WORDS",
        CollationStatus.OnlyInLeft => "only on the left",
        _ => "only on the right"
    };

    /// <summary>
    /// Named for the pairing rather than the work, because a work can pair up
    /// several ways and two files called "collation-Ajax.csv" holding different
    /// comparisons would be worse than one badly named file.
    /// </summary>
    private string SuggestedFileName()
    {
        if (_pairBox.SelectedItem is not PairOption { Pair: var pair }) return "collation";

        return $"collation-{pair.WorkTitle}-{pair.LeftVersion}-{pair.RightVersion}";
    }

    /// <summary>
    /// The rows as shown, filter included.
    ///
    /// What is on screen is what someone means by "export this". Writing the
    /// full comparison instead would hand back thousands of punctuation
    /// differences nobody asked to see - and the note above the table says
    /// which view produced it, so the file cannot be mistaken for the whole
    /// collation.
    ///
    /// Built from the row objects rather than the list cells because the cells
    /// carry a dash where an edition has nothing at a reference. That dash is
    /// right on screen and wrong in a file, where it would read as a passage
    /// whose text is "—" rather than as an absence.
    /// </summary>
    private IReadOnlyList<IReadOnlyList<string>> ExportRows()
    {
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "Passage", "Difference", _leftHeader.Text, _rightHeader.Text }
        };

        if (_result is not { } result) return table;

        foreach (var row in result.Rows.Where(Included))
        {
            table.Add(new[]
            {
                row.PassageRef,
                Describe(row.Status),
                row.Left ?? string.Empty,
                row.Right ?? string.Empty
            });
        }

        return table;
    }

    /// <summary>
    /// What the table means, written above it.
    ///
    /// A collation exported without this is four columns of Greek whose
    /// provenance has to be remembered - which two editions, graded how,
    /// filtered to what. The counts go in as well, because the substantive
    /// figure is what says whether the pairing is worth trusting, and a file
    /// showing 89% without it looks like a discovery rather than a warning.
    /// </summary>
    private IReadOnlyList<string> ExportNotes()
    {
        var notes = new List<string> { $"Classica Codex collation - {DateTime.Now:yyyy-MM-dd HH:mm}" };

        if (_pairBox.SelectedItem is PairOption option)
        {
            var pair = option.Pair;
            notes.Add($"{pair.AuthorName}, {pair.WorkTitle}");
            notes.Add($"Left:  {_leftHeader.Text}   ({pair.LeftEditionUrn})");
            notes.Add($"Right: {_rightHeader.Text}   ({pair.RightEditionUrn})");
        }

        if (_result is { } result)
        {
            notes.Add(result.IsAlignable
                ? $"{result.Shared:N0} shared passages: {result.Identical:N0} identical, " +
                  $"{result.PresentationDiffers:N0} punctuation, {result.OrthographyDiffers:N0} spelling, " +
                  $"{result.LineationDiffers:N0} line division, {result.TextDiffers:N0} in the words. " +
                  $"{result.OnlyInLeft:N0} only on the left, {result.OnlyInRight:N0} only on the right."
                : "These editions are not alignable - their citation references do not name the " +
                  "same passages - so the rows below are not a collation.");

            if (result.Shared > 0 && result.TextDiffers > result.Shared * 0.6)
            {
                notes.Add("NOTE: at this rate the two editions disagree wholesale rather than at " +
                          "points - different editors' texts, or one transcribed less carefully. " +
                          "These rows describe the distance between them; they are not a shortlist " +
                          "of cruxes.");
            }
        }

        notes.Add($"Showing: {_showBox.SelectedItem}. Differences are graded - only \"THE WORDS\" is a " +
                  "difference in what the text says. Nothing here rules on which reading is right.");

        return notes;
    }

    private void ShowSelectedDetail()
    {
        if (_rows.SelectedItems.Count == 0 || _rows.SelectedItems[0].Tag is not CollationRow row)
        {
            _leftDetail.Text = _rightDetail.Text = string.Empty;
            return;
        }

        _leftDetail.Text = row.Left ?? "(this edition has nothing at this reference)";
        _rightDetail.Text = row.Right ?? "(this edition has nothing at this reference)";
    }
}
