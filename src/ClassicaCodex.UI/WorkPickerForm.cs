using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Chooses which works a search covers.
///
/// The two ends of the range are the obvious ones - this text, or everything
/// - but the useful answers are often in between: the three surviving plays
/// of a trilogy, or everything by one author. A checkbox could only ever
/// offer the ends.
///
/// The list is long enough that it needs filtering to be usable at all: a
/// full corpus is a couple of thousand authors and rather more works, so the
/// filter box is the primary control here and the list is what it acts on.
/// </summary>
public class WorkPickerForm : ScaledForm
{
    private readonly TextBox _filterBox;
    private readonly ComboBox _showCombo;
    private readonly CheckedListBox _list;
    private readonly CheckBox _allWorksCheck;
    private readonly Label _countLabel;

    private readonly List<(int WorkId, string Label)> _allWorks = new();
    private readonly HashSet<int> _selected = new();

    // Set while the list is being filled. Adding an item with a tick raises
    // ItemCheck, so without this the handler runs during construction -
    // before the form has a window handle - and again on every keystroke in
    // the filter box, re-recording choices it already holds.
    private bool _populating;

    /// <summary>
    /// Which works the list shows, independent of the text filter.
    ///
    /// Picking through a couple of thousand works happens across several
    /// filter terms - type an author, tick two plays, type another author,
    /// tick three more - and by the end there is no way to see what has been
    /// accumulated, because every choice made is off screen the moment the
    /// filter moves on. Chosen answers that, and Not chosen is what makes
    /// "select all shown" usable for the remainder.
    /// </summary>
    private enum ShowMode
    {
        All = 0,
        Chosen = 1,
        NotChosen = 2
    }

    private ShowMode CurrentShowMode => (ShowMode)Math.Max(_showCombo.SelectedIndex, 0);

    /// <summary>
    /// The chosen work ids, or empty for "everything" - which the caller
    /// passes on as no filter at all rather than as a list of every id.
    /// </summary>
    public IReadOnlyCollection<int> SelectedWorkIds =>
        _allWorksCheck.Checked ? Array.Empty<int>() : _selected.ToList();

    public WorkPickerForm(
        List<Author> authors,
        Dictionary<int, List<Work>> worksByAuthor,
        IReadOnlyCollection<int> initiallySelected)
    {
        Text = "Choose Texts to Search";
        AppIcons.ApplyWindowIcon(this, "Library");
        ClientSize = new Size(640, 560);
        MinimumSize = new Size(520, 420);
        StartPosition = FormStartPosition.CenterParent;

        foreach (var author in authors)
        {
            if (!worksByAuthor.TryGetValue(author.AuthorId, out var works)) continue;

            foreach (var work in works)
            {
                _allWorks.Add((work.WorkId, $"{author.Name} - {work.Title}"));
            }
        }

        foreach (var id in initiallySelected) _selected.Add(id);

        _allWorksCheck = new CheckBox
        {
            Text = "Search every text in the corpus",
            Left = 12,
            Top = 12,
            Width = 400,
            Checked = initiallySelected.Count == 0
        };

        var filterLabel = new Label { Text = "Filter:", Left = 12, Top = 44, Width = 44 };
        _filterBox = new TextBox
        {
            Left = 58,
            Top = 40,
            Width = 366,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "author or title"
        };

        var showLabel = new Label
        {
            Text = "Show:", Left = 432, Top = 44, Width = 42,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _showCombo = new ComboBox
        {
            Left = 476,
            Top = 40,
            Width = 152,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _showCombo.Items.AddRange(new object[] { "All texts", "Chosen only", "Not chosen" });
        _showCombo.SelectedIndex = 0;
        _showCombo.SelectedIndexChanged += (_, _) => RefreshList();

        _list = new CheckedListBox
        {
            Left = 12,
            Top = 72,
            Width = 616,
            Height = 424,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            CheckOnClick = true,
            IntegralHeight = false
        };

        _countLabel = new Label
        {
            Left = 12,
            Top = 506,
            Width = 380,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            ForeColor = Color.DimGray
        };

        var okButton = new Button
        {
            Text = "Use These", Left = 428, Top = 502, Width = 96, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK
        };
        var cancelButton = new Button
        {
            Text = "Cancel", Left = 532, Top = 502, Width = 96, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        // Acts on what the filter is showing, not on the whole corpus -
        // narrowing to an author and taking the lot is the reason to have
        // this at all. "Clear every choice" is separate because it is the
        // one that reaches beyond what you can see, and shouldn't be
        // something you hit by aiming for the one above it.
        var listMenu = new ContextMenuStrip();

        var selectShown = listMenu.Items.Add("Select all shown");
        selectShown.Click += (_, _) => SetAllShown(true);

        var unselectShown = listMenu.Items.Add("Unselect all shown");
        unselectShown.Click += (_, _) => SetAllShown(false);

        listMenu.Items.Add(new ToolStripSeparator());

        var clearAll = listMenu.Items.Add("Clear every choice");
        clearAll.Click += (_, _) =>
        {
            _selected.Clear();
            RefreshList();
        };

        listMenu.Opening += (_, e) =>
        {
            // Nothing to act on when the filter has matched nothing.
            if (_list.Items.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            selectShown.Text = $"Select all {_list.Items.Count:N0} shown";
            unselectShown.Text = $"Unselect all {_list.Items.Count:N0} shown";
            clearAll.Enabled = _selected.Count > 0;
        };

        _list.ContextMenuStrip = listMenu;
        ReadingTheme.ApplyToContextMenu(listMenu);

        _filterBox.TextChanged += (_, _) => RefreshList();

        // Ticks are recorded as they happen rather than read off the list at
        // the end - the list only ever holds the works matching the current
        // filter, so anything filtered out would otherwise be silently
        // unticked by the act of typing.
        _list.ItemCheck += (_, e) =>
        {
            if (_populating) return;
            if (_list.Items[e.Index] is not WorkEntry entry) return;

            // e.NewValue rather than the item's current state, which hasn't
            // changed yet - and no BeginInvoke needed, since the count is
            // read from this set rather than from the list.
            if (e.NewValue == CheckState.Checked) _selected.Add(entry.WorkId);
            else _selected.Remove(entry.WorkId);

            UpdateCount();
        };

        _allWorksCheck.CheckedChanged += (_, _) =>
        {
            _filterBox.Enabled = !_allWorksCheck.Checked;
            _showCombo.Enabled = !_allWorksCheck.Checked;
            _list.Enabled = !_allWorksCheck.Checked;
            UpdateCount();
        };

        Controls.AddRange(new Control[]
        {
            _allWorksCheck, filterLabel, _filterBox, showLabel, _showCombo, _list, _countLabel,
            okButton, cancelButton
        });

        RefreshList();
        _filterBox.Enabled = !_allWorksCheck.Checked;
        _showCombo.Enabled = !_allWorksCheck.Checked;
        _list.Enabled = !_allWorksCheck.Checked;

        ReadingTheme.AttachTo(this);
    }

    private sealed record WorkEntry(int WorkId, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Rebuilds the visible list for the current filter and show mode,
    /// restoring ticks from what has been chosen rather than from what the
    /// list held before.
    ///
    /// The two narrowings compose: "Chosen only" with an author typed shows
    /// what has been picked from that author, not everything picked.
    /// </summary>
    private void RefreshList()
    {
        var filter = _filterBox.Text.Trim();

        IEnumerable<(int WorkId, string Label)> matching = _allWorks;

        if (filter.Length > 0)
        {
            matching = matching.Where(w => w.Label.Contains(filter, StringComparison.CurrentCultureIgnoreCase));
        }

        matching = CurrentShowMode switch
        {
            ShowMode.Chosen => matching.Where(w => _selected.Contains(w.WorkId)),
            ShowMode.NotChosen => matching.Where(w => !_selected.Contains(w.WorkId)),
            _ => matching
        };

        var matched = matching.ToList();

        _populating = true;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();

            // Capped because a full corpus runs to thousands of works and a
            // CheckedListBox holding all of them is slow to build on every
            // keystroke. Typing narrows it; the count says when that is
            // needed.
            foreach (var (workId, label) in matched.Take(500))
            {
                _list.Items.Add(new WorkEntry(workId, label), _selected.Contains(workId));
            }
        }
        finally
        {
            _list.EndUpdate();
            _populating = false;
        }

        UpdateCount(matched.Count);
    }

    /// <summary>
    /// Ticks or unticks everything the filter is currently showing.
    ///
    /// Updates the record directly and suppresses the per-item handler
    /// rather than letting several hundred ItemCheck events do it one at a
    /// time - the handler would arrive at the same answer, slowly.
    /// </summary>
    private void SetAllShown(bool chosen)
    {
        _populating = true;
        _list.BeginUpdate();
        try
        {
            for (var i = 0; i < _list.Items.Count; i++)
            {
                if (_list.Items[i] is not WorkEntry entry) continue;

                _list.SetItemChecked(i, chosen);

                if (chosen) _selected.Add(entry.WorkId);
                else _selected.Remove(entry.WorkId);
            }
        }
        finally
        {
            _list.EndUpdate();
            _populating = false;
        }

        // A bulk change makes the list wrong under the filtered modes -
        // everything just unticked is still listed under "Chosen only" - so
        // it is rebuilt. Deliberately not done for a single tick: an item
        // vanishing from under the pointer as it is clicked makes the next
        // click land on something else.
        if (CurrentShowMode != ShowMode.All)
        {
            RefreshList();
            return;
        }

        UpdateCount();
    }

    private void UpdateCount() => UpdateCount(null);

    private void UpdateCount(int? matchingCount)
    {
        if (_allWorksCheck.Checked)
        {
            _countLabel.Text = "Every text in the corpus.";
            return;
        }

        var shown = matchingCount == null
            ? string.Empty
            : matchingCount > _list.Items.Count
                ? $"  ({_list.Items.Count:N0} of {matchingCount:N0} shown - keep typing to narrow)"
                : string.Empty;

        _countLabel.Text = _selected.Count == 0
            ? $"No texts chosen - nothing will be searched.{shown}"
            : $"{_selected.Count:N0} text(s) chosen.{shown}";
    }
}
