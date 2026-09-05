using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// A traditional KWIC concordance: every occurrence of a word lined up in
/// three columns (left context, the word, right context) so patterns in
/// usage jump out visually - this is the format published concordances to
/// Homer, Livy, etc. have used for a century, just computed on demand here
/// instead of printed in a volume.
/// </summary>
public class ConcordanceForm : ScaledForm
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

    /// <summary>
    /// Each hit as it would be written out, kept alongside the rows rather than
    /// read back off them.
    ///
    /// The left context on screen is deliberately truncated - that is what
    /// keeps the keyword column aligned down the page, which is the whole point
    /// of a concordance view - so the visible cells are not the data. Exporting
    /// them would hand over a left context cut to fit a column.
    /// </summary>
    private readonly List<(string Left, string Keyword, string Right, string Source, string CitationRef, string FullLine)> _rowExport = new();

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

        // Two lines' worth of height, because this now reports the totals
        // across the library as well as what is laid out, and one line of it
        // was being cut off mid-sentence at the default 23px.
        //
        // 54 rather than the 34 that fits at 100%, because 34 is only just
        // enough: measured with the longest status this form produces, two
        // lines want 30px at the default text size, 40px at 125% and 50px at
        // 150%. A box sized to the first of those loses its second line on
        // any machine not at 100%, which is a large share of them.
        _statusLabel = new Label
        {
            Text = "", Left = 504, Top = 6, Width = 760, Height = 54,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _resultsList = new ListView
        {
            Left = 12,
            Top = 64,
            Width = 1260,
            Height = 644,
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

        // And the same results as a table, added to that menu rather than
        // replacing it. A concordance is read as prose and analysed as rows -
        // sorting a thousand occurrences by what precedes the keyword is a
        // spreadsheet question, and the passage export cannot answer it.
        ResultExport.AddTo(
            _resultsList.ContextMenuStrip!,
            () => $"concordance-{_wordBox.Text.Trim()}",
            KwicRows,
            () => new[]
            {
                $"Classica Codex concordance - {DateTime.Now:yyyy-MM-dd HH:mm}",
                $"Keyword: {_wordBox.Text.Trim()}   ({_rowExport.Count:N0} occurrences)",
                "One row per occurrence, so a line containing the word twice appears twice. " +
                "Left and right context are the full line either side of the keyword, not the " +
                "truncated form the screen shows."
            });

        Controls.Add(wordLabel);
        Controls.Add(_wordBox);
        Controls.Add(_searchButton);
        Controls.Add(_statusLabel);
        Controls.Add(_resultsList);

        // The column-header strip is a native child control that ignores the
        // ListView's colours, so it needs owner-drawing to follow the theme.
        ReadingTheme.EnableThemedHeader(_resultsList);
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private IReadOnlyList<IReadOnlyList<string>> KwicRows()
    {
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "LeftContext", "Keyword", "RightContext", "Source", "Citation", "FullLine" }
        };

        foreach (var (left, keyword, right, source, citation, fullLine) in _rowExport)
        {
            table.Add(new[] { left, keyword, right, source, citation, fullLine });
        }

        return table;
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
        _rowExport.Clear();
        _currentPassages.Clear();

        try
        {
            // Whole words through the index, not a substring scan of the raw
            // text, and this is a correctness fix before it is a speed one.
            //
            // A concordance is a word framed by its context, so it has to find
            // the word. The substring scan compares what was typed against the
            // text as printed, and this corpus is not printed the way anyone
            // types: Greek carries diacritics nobody enters into a search box,
            // 87 editions are set in lunate sigma, and Latin u/v and i/j are
            // the editor's choice rather than the author's. Concordancing
            // Greek "μηνιν" found 8 lines where the word is in 316, and
            // "iustitia" found 1,425 of 4,196 - and a concordance that quietly
            // misses three quarters of its word is worse than no concordance.
            //
            // Being roughly a hundred times faster is the smaller half:
            // measured on a full library, 13ms against 2,564ms for that Greek
            // word, because it seeks an index instead of reading 594MB of
            // text.
            //
            // What it gives up is the inflections a substring happened to
            // catch - "sophia" no longer picks up "sophian" - and that is
            // worth giving up, because it only ever caught the ones that
            // differ by a suffix and never the ones that differ by an accent
            // or a prefix. This concordance is now of a word, consistently.
            // The question about every form of a headword has its own screen,
            // which asks the lemma data rather than guessing from spelling.
            var filters = new SearchFilters { Query = word, MatchMode = SearchMatchMode.WholeWord };
            var (hits, distribution) = await Task.Run(async () => (
                await _textNodeRepo.SearchFilteredAsync(filters),
                await _textNodeRepo.CountMatchesByWorkAsync(filters)));
            var matches = hits.Rows;

            // What the index was actually asked for, which is what may be
            // sitting in these lines - the typed spelling is only one of them.
            var targets = WordOccurrences.TargetsFor(word);

            // The KWIC framing is what a concordance is for, so it travels
            // with the export as each passage's detail - one entry per line,
            // showing every occurrence in that line rather than repeating
            // the line once per occurrence.
            _currentPassages = matches
                .Select(m => new ExportPassage(
                    m.WorkId, m.TextNodeId, m.AuthorName, m.WorkTitle, m.CitationRef, m.Text,
                    BuildKwicDetail(m.Text, targets)))
                .ToList();

            var rowCount = 0;
            foreach (var m in matches)
            {
                // One KWIC row per occurrence, with the keyword column
                // carrying the word as that edition prints it rather than as
                // it was typed - which is the point of a concordance drawn
                // from editions that disagree about spelling.
                var occurrences = WordOccurrences.Find(m.Text, targets);

                foreach (var (start, length) in occurrences)
                {
                    var left = m.Text[..start].TrimStart();
                    var keyword = m.Text.Substring(start, length);
                    var right = m.Text[(start + length)..].TrimEnd();

                    AddRow(left, keyword, right, m.Text, m.AuthorName, m.WorkTitle, m.CitationRef, m.WorkId, m.TextNodeId);
                    rowCount++;
                }

                // A line the search matched but whose word cannot be located
                // in it shows whole rather than being silently dropped. It
                // should no longer happen now that both ends normalize the
                // same way, and it is still the right way to fail.
                if (occurrences.Count == 0)
                {
                    AddRow("", "(matched line)", m.Text, m.Text, m.AuthorName, m.WorkTitle, m.CitationRef, m.WorkId, m.TextNodeId);
                    rowCount++;
                }
            }

            // A concordance that silently stops isn't a concordance - and one
            // that can only say "5000+" cannot answer the question it was
            // opened to answer. The line count is now counted across the
            // library rather than across the rows that fitted, so what is
            // capped is how much of it can be laid out on screen.
            _statusLabel.Text = hits.Truncated
                ? $"{distribution.TotalMatches:N0} lines contain {word}, in {distribution.WorkCount:N0} works " +
                  $"by {distribution.AuthorCount:N0} authors.\r\n" +
                  $"Showing {rowCount:N0} occurrences from the first {matches.Count:N0} - narrow the search for the rest."
                : $"{rowCount:N0} occurrence(s) across {matches.Count:N0} line(s), " +
                  $"in {distribution.WorkCount:N0} work(s) by {distribution.AuthorCount:N0} author(s).";
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
    private static string BuildKwicDetail(string text, IReadOnlyCollection<string> targets)
    {
        var parts = new List<string>();

        foreach (var (start, length) in WordOccurrences.Find(text, targets))
        {
            var left = TrimLeftContext(text[..start].TrimStart());
            var right = text[(start + length)..].TrimEnd();
            if (right.Length > 90) right = right[..90] + "\u2026";

            parts.Add($"{left} \u27e8{text.Substring(start, length)}\u27e9 {right}");
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
        _rowExport.Add((left, keyword, right, $"{authorName}, {workTitle}", citationRef, fullLineText));
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
