using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

public class TagBrowserForm : Form
{
    private readonly ListBox _tagList;
    private readonly ListBox _resultsList;
    private readonly TagRepository _tagRepo = new();

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _currentResults = new();

    /// <summary>
    /// Set by MainForm before showing this dialog. Double-clicking a result
    /// invokes this with the work and text node to jump to, then closes the
    /// browser so the main reader is what's left on screen.
    /// </summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public TagBrowserForm()
    {
        Text = "Browse by Tag - Cross-Reference Across Authors";
        AppIcons.ApplyWindowIcon(this, "AutoTag");
        Width = 900;
        Height = 680;
        StartPosition = FormStartPosition.CenterParent;

        var tagLabel = new Label { Text = "Tags: (right-click: artifacts)", Left = 12, Top = 10, Width = 220 };
        _tagList = new ListBox
        {
            Left = 12,
            Top = 32,
            Width = 220,
            Height = 560,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        _tagList.SelectedIndexChanged += async (_, _) => await LoadResultsAsync();
        ListResultHelpers.AttachArtifactSearchMenu(_tagList,
            i => i < _tagList.Items.Count && _tagList.Items[i] is Tag tag ? tag.Name : null, this);

        var resultsLabel = new Label
        {
            Text = "Every passage tagged with this - across every author (double-click to jump to it):",
            Left = 244,
            Top = 10,
            Width = 620
        };
        _resultsList = new ListBox
        {
            Left = 244,
            Top = 32,
            Width = 620,
            Height = 560,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            HorizontalScrollbar = true
        };
        _resultsList.DoubleClick += async (_, _) => await JumpToSelectedResultAsync();
        ListResultHelpers.AttachCitationTooltip(_resultsList,
            i => i < _currentResults.Count ? _currentResults[i].CitationRef : null);
        ListResultHelpers.AttachCopyToClipboardMenu(_resultsList,
            i => i < _currentResults.Count
                ? $"{_currentResults[i].AuthorName}, {_currentResults[i].WorkTitle} [{_currentResults[i].CitationRef}]: {_currentResults[i].Text}"
                : null);
        ListResultHelpers.AttachExportMenu(_resultsList, () => (
            _tagList.SelectedItem is Tag selected
                ? $"Passages tagged \u201c{selected.Name}\u201d"
                : "Tagged passages",
            _currentResults.Select(r => new ExportPassage(
                r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text)).ToList()), this);

        var compareButton = new Button
        {
            Text = "Compare Sources...",
            Left = 12,
            Top = 602,
            Width = 220,
            Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        compareButton.Click += (_, _) =>
        {
            if (_tagList.SelectedItem is not Tag tag)
            {
                MessageBox.Show(this, "Select a tag first.", "Nothing selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var compareForm = new CompareForm(tag.Name);
            compareForm.ShowDialog(this);
        };

        var deleteTagButton = new Button
        {
            Text = "Delete Tag",
            Left = 244,
            Top = 602,
            Width = 130,
            Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        deleteTagButton.Click += async (_, _) => await DeleteSelectedTagAsync();
        AppIcons.Apply(deleteTagButton, "Delete", 16);

        var clearAllButton = new Button
        {
            Text = "Clear All Tags...",
            Left = 734,
            Top = 602,
            Width = 130,
            Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        clearAllButton.Click += async (_, _) => await ClearAllTagsAsync();
        AppIcons.Apply(clearAllButton, "Delete", 16);

        Controls.Add(tagLabel);
        Controls.Add(_tagList);
        Controls.Add(compareButton);
        Controls.Add(deleteTagButton);
        Controls.Add(clearAllButton);
        Controls.Add(resultsLabel);
        Controls.Add(_resultsList);

        Load += async (_, _) => await LoadTagsAsync();
        ReadingTheme.AttachTo(this);
    }

    private async Task DeleteSelectedTagAsync()
    {
        if (_tagList.SelectedItem is not Tag tag)
        {
            MessageBox.Show(this, "Select a tag first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Delete the tag \"{tag.Name}\"? This removes it from every line it's applied to. This can't be undone.",
            "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        await _tagRepo.DeleteTagAsync(tag.TagId);
        _resultsList.Items.Clear();
        await LoadTagsAsync();
    }

    private async Task ClearAllTagsAsync()
    {
        var firstConfirm = MessageBox.Show(this,
            "Delete every tag you've created, and every line association with them?\r\n\r\n" +
            "This does not touch bookmarks or anything else - only tags. This can't be undone.",
            "Clear All Tags", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (firstConfirm != DialogResult.Yes) return;

        // A second, more deliberate confirmation - this is a full wipe, not
        // a single deletion, and deserves more friction than one click.
        var secondConfirm = MessageBox.Show(this,
            "Really clear ALL tags? There is no undo for this.",
            "Are you sure?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (secondConfirm != DialogResult.Yes) return;

        await _tagRepo.ClearAllTagsAsync();
        _resultsList.Items.Clear();
        await LoadTagsAsync();

        MessageBox.Show(this, "All tags cleared.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task LoadTagsAsync()
    {
        _tagList.Items.Clear();
        var tags = await _tagRepo.GetAllTagsAsync();

        if (tags.Count == 0)
        {
            _tagList.Items.Add("(no tags yet - right-click a line in the reader to add one)");
            return;
        }

        foreach (var tag in tags)
        {
            _tagList.Items.Add(tag);
        }
    }

    private async Task LoadResultsAsync()
    {
        _resultsList.Items.Clear();
        _currentResults = new List<(int, long, string, string, string, string)>();

        if (_tagList.SelectedItem is not Tag tag) return;

        _currentResults = await _tagRepo.GetByTagAsync(tag.Name);
        foreach (var r in _currentResults)
        {
            _resultsList.Items.Add($"{r.AuthorName}, {r.WorkTitle}: {r.Text}");
        }

        if (_currentResults.Count == 0)
        {
            _resultsList.Items.Add("(nothing tagged with this yet)");
        }
    }

    private async Task JumpToSelectedResultAsync()
    {
        var index = _resultsList.SelectedIndex;
        if (index < 0 || index >= _currentResults.Count || OnNavigate == null) return;

        var result = _currentResults[index];
        await OnNavigate(result.WorkId, result.TextNodeId);
        Close();
    }
}
