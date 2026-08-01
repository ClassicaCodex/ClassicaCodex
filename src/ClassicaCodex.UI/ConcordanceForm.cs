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

    /// <summary>
    /// The distinct lines that matched, for export - deliberately not one
    /// entry per visible row. A KWIC list shows a row per occurrence, so a
    /// word appearing three times in a line produces three rows; exporting
    /// that shape would repeat the same line three times in the document.
    /// What belongs in an export is the passages, once each.
    /// </summary>
    private List<ExportPassage> _currentPassages = new();
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
        // A zero-width spacer, and the reason for it: Win32's list-view
        // control always draws column 0 left-aligned and ignores whatever
        // alignment is set on it. "Left Context" was already asking to be
        // right-aligned and silently wasn't, which is exactly what made the
        // words nearest the keyword the ones you couldn't see. Pushing it to
        // column 1 makes the alignment take effect.
        _resultsList.Columns.Add(string.Empty, 0, HorizontalAlignment.Left);
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

        // After the assignment above, not before: AttachExportMenu merges
        // into whatever strip the control already has, and assigning
        // copyMenu afterwards would have thrown the merged menu away.
        ListResultHelpers.AttachExportMenu(_resultsList, () => (
            $"Concordance: {_wordBox.Text.Trim()}", _currentPassages), this,
            "the keyword in context");

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
        _currentPassages.Clear();

        try
        {
            var hits = await _textNodeRepo.SearchAsync(word);
            var matches = hits.Rows;

            var wordPattern = new Regex(Regex.Escape(word), RegexOptions.IgnoreCase);

            // The KWIC framing is what a concordance is for, so it travels
            // with the export as each passage's detail - one entry per line,
            // showing every occurrence in that line rather than repeating
            // the line once per occurrence.
            _currentPassages = matches
                .Select(m => new ExportPassage(
                    m.WorkId, m.TextNodeId, m.AuthorName, m.WorkTitle, m.CitationRef, m.Text,
                    BuildKwicDetail(m.Text, wordPattern)))
                .ToList();

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

            // DisplayCount renders "5000+" when the search hit its cap - a
            // concordance that silently stops isn't a concordance.
            _statusLabel.Text = hits.Truncated
                ? $"{rowCount}+ occurrence(s) across {hits.DisplayCount} line(s) - stopped at the result limit, narrow the search for the rest."
                : $"{rowCount} occurrence(s) across {matches.Count} line(s).";
        }
        finally
        {
            _searchButton.Enabled = true;
        }
    }

    /// <summary>
    /// The keyword shown in context, once per occurrence in the line, in
    /// the same left / word / right shape the on-screen columns use.
    /// </summary>
    private static string BuildKwicDetail(string text, Regex wordPattern)
    {
        var parts = new List<string>();

        foreach (Match occurrence in wordPattern.Matches(text))
        {
            var left = TrimLeftContext(text[..occurrence.Index].TrimStart());
            var right = text[(occurrence.Index + occurrence.Length)..].TrimEnd();
            if (right.Length > 90) right = right[..90] + "\u2026";

            parts.Add($"{left} \u27e8{occurrence.Value}\u27e9 {right}");
        }

        return parts.Count == 0 ? string.Empty : string.Join("  |  ", parts);
    }

    private void AddRow(
        string left, string keyword, string right, string fullLineText,
        string authorName, string workTitle, string citationRef, int workId, long textNodeId)
    {
        // First cell is the zero-width spacer column - see the column
        // setup for why it exists.
        var item = new ListViewItem(string.Empty);
        item.SubItems.Add(TrimLeftContext(left));
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

    /// <summary>
    /// Keeps the tail of the left context rather than the head.
    ///
    /// Right-aligning the column is only half the fix. A list view still
    /// truncates an overlong cell at its end, so a long line would lose the
    /// words immediately before the keyword - the ones a concordance exists
    /// to show - and keep the start of the sentence, which is the part you
    /// can already read anywhere. Trimming from the left ourselves means
    /// what survives is always the run-up to the keyword.
    /// </summary>
    private static string TrimLeftContext(string left)
    {
        const int maxChars = 90;
        if (left.Length <= maxChars) return left;

        // Cut at a word boundary where there is one nearby, so the fragment
        // starts on a whole word rather than mid-syllable.
        var tail = left[^maxChars..];
        var space = tail.IndexOf(' ');
        if (space > 0 && space < 20) tail = tail[(space + 1)..];

        return "\u2026 " + tail;
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
