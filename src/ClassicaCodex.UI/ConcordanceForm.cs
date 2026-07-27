using System.Text.RegularExpressions;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// A traditional KWIC concordance: every occurrence of a word lined up in
/// three columns (left context, the word, right context) so patterns in
/// usage jump out visually - this is the format published concordances to
/// Homer, Livy, etc. have used for a century, just computed on demand here
/// instead of printed in a volume.
/// </summary>
public class ConcordanceForm : Form
{
    private readonly TextBox _wordBox;
    private readonly Button _searchButton;
    private readonly Label _statusLabel;
    private readonly ListView _resultsList;
    private readonly TextNodeRepository _textNodeRepo = new();

    private List<(int WorkId, long TextNodeId)> _rowTargets = new();
    private List<string> _rowFullText = new();

    /// <summary>Set by MainForm before showing this dialog.</summary>
    public Func<int, long, Task>? OnNavigate { get; set; }

    public ConcordanceForm()
    {
        Text = "Concordance (KWIC)";
        AppIcons.ApplyWindowIcon(this, "Concordance");
        Width = 1300;
        Height = 750;
        StartPosition = FormStartPosition.CenterParent;

        var wordLabel = new Label { Text = "Word:", Left = 12, Top = 14, Width = 50 };
        _wordBox = new TextBox { Left = 64, Top = 11, Width = 260 };
        _wordBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await RunConcordanceAsync();
        };

        _searchButton = new Button { Text = "Build Concordance", Left = 332, Top = 9, Width = 160, Height = 28 };
        _searchButton.Click += async (_, _) => await RunConcordanceAsync();

        _statusLabel = new Label { Text = "", Left = 504, Top = 14, Width = 760, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

        _resultsList = new ListView
        {
            Left = 12,
            Top = 48,
            Width = 1260,
            Height = 660,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            ShowItemToolTips = true
        };
        _resultsList.Columns.Add("Left Context", 420, HorizontalAlignment.Right);
        _resultsList.Columns.Add("Word", 100, HorizontalAlignment.Center);
        _resultsList.Columns.Add("Right Context", 420, HorizontalAlignment.Left);
        _resultsList.Columns.Add("Source", 300, HorizontalAlignment.Left);
        _resultsList.DoubleClick += async (_, _) => await JumpToSelectedAsync();

        // ListView needs its own handling rather than ListResultHelpers -
        // different API (GetItemAt, SelectedIndices) than a plain ListBox.
        var copyMenu = new ContextMenuStrip();
        var copyItem = copyMenu.Items.Add("Copy to Clipboard");
        copyItem.Image = AppIcons.Get("CopyToClipboard", 16);
        ReadingTheme.ApplyToContextMenu(copyMenu);
        _resultsList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var item = _resultsList.GetItemAt(e.X, e.Y);
            if (item != null) item.Selected = true;
        };
        copyItem.Click += (_, _) =>
        {
            if (_resultsList.SelectedIndices.Count == 0) return;
            var index = _resultsList.SelectedIndices[0];
            if (index >= 0 && index < _rowFullText.Count) Clipboard.SetText(_rowFullText[index]);
        };
        _resultsList.ContextMenuStrip = copyMenu;

        Controls.Add(wordLabel);
        Controls.Add(_wordBox);
        Controls.Add(_searchButton);
        Controls.Add(_statusLabel);
        Controls.Add(_resultsList);

        // The column-header strip is a native child control that ignores the
        // ListView's colours, so it needs owner-drawing to follow the theme.
        ReadingTheme.EnableThemedHeader(_resultsList);
        ReadingTheme.AttachTo(this);
    }

    private async Task RunConcordanceAsync()
    {
        var word = _wordBox.Text.Trim();
        if (word.Length == 0) return;

        _searchButton.Enabled = false;
        _statusLabel.Text = "Searching...";
        _resultsList.Items.Clear();
        _rowTargets.Clear();
        _rowFullText.Clear();

        try
        {
            var matches = await _textNodeRepo.SearchAsync(word);
            var wordPattern = new Regex(Regex.Escape(word), RegexOptions.IgnoreCase);

            var rowCount = 0;
            foreach (var m in matches)
            {
                // One KWIC row per literal occurrence of the typed word in
                // the line - a stemmed match (search found "running" for a
                // "run" query) that doesn't contain the literal substring
                // still shows up as a whole-line row further below, rather
                // than being silently dropped.
                var found = false;
                foreach (Match occurrence in wordPattern.Matches(m.Text))
                {
                    found = true;
                    var left = m.Text[..occurrence.Index].TrimStart();
                    var keyword = occurrence.Value;
                    var right = m.Text[(occurrence.Index + occurrence.Length)..].TrimEnd();

                    AddRow(left, keyword, right, m.Text, m.AuthorName, m.WorkTitle, m.CitationRef, m.WorkId, m.TextNodeId);
                    rowCount++;
                }

                if (!found)
                {
                    AddRow("", "(stemmed match)", m.Text, m.Text, m.AuthorName, m.WorkTitle, m.CitationRef, m.WorkId, m.TextNodeId);
                    rowCount++;
                }
            }

            _statusLabel.Text = $"{rowCount} occurrence(s) across {matches.Count} line(s).";
        }
        finally
        {
            _searchButton.Enabled = true;
        }
    }

    private void AddRow(
        string left, string keyword, string right, string fullLineText,
        string authorName, string workTitle, string citationRef, int workId, long textNodeId)
    {
        var item = new ListViewItem(left);
        item.SubItems.Add(keyword);
        item.SubItems.Add(right);
        item.SubItems.Add($"{authorName}, {workTitle}");

        // The ref used to be baked into the visible Source text, which is
        // exactly what was making that column so cramped it needed
        // truncating. ListView supports a real per-item tooltip natively -
        // no MouseMove tracking needed, unlike the plain ListBox views.
        item.ToolTipText = $"[{citationRef}]";

        _resultsList.Items.Add(item);
        _rowTargets.Add((workId, textNodeId));
        _rowFullText.Add($"{authorName}, {workTitle} [{citationRef}]: {fullLineText}");
    }

    private async Task JumpToSelectedAsync()
    {
        if (_resultsList.SelectedIndices.Count == 0 || OnNavigate == null) return;

        var index = _resultsList.SelectedIndices[0];
        if (index < 0 || index >= _rowTargets.Count) return;

        var (workId, textNodeId) = _rowTargets[index];
        await OnNavigate(workId, textNodeId);
        Close();
    }
}
