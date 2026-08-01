using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// One passage in a gathered set, in the shape every view that gathers them
/// already produces - the Tag Browser, Concordance, Echo results, Reception
/// Tracker, Places Map, and Word Study all return exactly these fields, so a
/// caller can hand its results straight over without reshaping them.
/// </summary>
public sealed record ExportPassage(
    int WorkId,
    long TextNodeId,
    string AuthorName,
    string WorkTitle,
    string CitationRef,
    string Text,
    string? Detail = null);

/// <summary>
/// Exports a set of passages gathered from across the library, as opposed to
/// a continuous run of lines from one work - which is what PassageExportForm
/// already handles and why this is a separate dialog rather than another
/// mode bolted onto it.
///
/// The difference is not cosmetic. A passage set has no start line to count
/// forward from, no single work, no single language, and no inherent reading
/// order - so the scope controls that dialog is built around (how many lines,
/// to the end, entire work) have nothing to act on here. What matters
/// instead is where each passage came from, since a set spanning twenty
/// authors is unreadable without attribution on every line.
///
/// This is the missing half of the app's research loop. Tagging, searching,
/// and echo-hunting all gather passages from everywhere; until now the
/// gathered result could only be read on screen, and getting it into a
/// document meant re-finding every passage by hand and exporting it one at a
/// time.
/// </summary>
public class PassageSetExportForm : Form
{
    private readonly string _collectionTitle;
    private readonly IReadOnlyList<ExportPassage> _passages;

    /// <summary>
    /// What the calling view calls the extra line it attached to each
    /// passage - "why each was suggested" for Cross-Language Echo, "the
    /// keyword in context" for the Concordance. Supplied by the caller
    /// because only the caller knows what its detail actually is; a generic
    /// "Include details" would tell the reader nothing.
    /// </summary>
    private readonly string _detailLabel;

    private readonly CheckBox _showCitationsCheckbox;
    private readonly CheckBox _showSourceCheckbox;
    private readonly CheckBox _groupByWorkCheckbox;
    private readonly CheckBox _includeTranslationsCheckbox;
    private readonly CheckBox _includeDetailCheckbox;
    private readonly TextBox _previewBox;
    private readonly RadioButton _txtRadio;
    private readonly RadioButton _docxRadio;
    private readonly RadioButton _pdfRadio;
    private readonly Label _statusLabel;
    private readonly Button _exportButton;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionRepository _editionRepo = new();

    /// <summary>
    /// Counterpart text per passage, keyed by TextNodeId. Populated only when
    /// translations are switched on, and deliberately partial: a passage
    /// whose work has no second edition, or whose citation ref has no
    /// counterpart in it, simply isn't in here. Those export alone rather
    /// than being dropped or guessed at - the same rule PassageExportForm's
    /// bilingual mode follows.
    /// </summary>
    private readonly Dictionary<long, string> _counterpartByTextNodeId = new();

    /// <summary>
    /// Mixed scripts are the norm here, not the exception - one tag can pull
    /// Greek, Latin, and English into a single document. Georgia, which the
    /// translation pane uses, has no polytonic Greek coverage and would
    /// render it as fallback boxes. Palatino Linotype covers both scripts,
    /// so a cross-work export always uses it regardless of where the
    /// passages came from.
    /// </summary>
    private const string MixedScriptFont = "Palatino Linotype";

    public PassageSetExportForm(
        string collectionTitle, IReadOnlyList<ExportPassage> passages, string? detailLabel = null)
    {
        _collectionTitle = collectionTitle;
        _passages = passages;
        _detailLabel = detailLabel ?? "extra detail";

        Text = "Export Passages";
        AppIcons.ApplyWindowIcon(this, "Export");
        Width = 620;
        Height = 680;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var distinctWorks = passages.Select(p => (p.AuthorName, p.WorkTitle)).Distinct().Count();

        var headerLabel = new Label
        {
            Text = $"{collectionTitle} \u2014 {passages.Count:N0} passage(s) from {distinctWorks:N0} work(s)",
            Left = 16,
            Top = 14,
            Width = 580,
            Font = new Font(Font, FontStyle.Bold)
        };

        _showCitationsCheckbox = new CheckBox
        {
            Text = "Show citation refs", Left = 16, Top = 44, Width = 170, Checked = true
        };
        _showCitationsCheckbox.CheckedChanged += (_, _) => RefreshPreview();

        // On by default, unlike the single-work dialog where it would be
        // repeated noise. Here it's the difference between a usable document
        // and a wall of unattributed quotations.
        _showSourceCheckbox = new CheckBox
        {
            Text = "Show author and work", Left = 190, Top = 44, Width = 190, Checked = true
        };
        _showSourceCheckbox.CheckedChanged += (_, _) => RefreshPreview();

        _groupByWorkCheckbox = new CheckBox
        {
            Text = "Group by work, with headings", Left = 16, Top = 68, Width = 230, Checked = true
        };
        _groupByWorkCheckbox.CheckedChanged += (_, _) => RefreshPreview();

        _includeTranslationsCheckbox = new CheckBox
        {
            Text = "Include translations where available", Left = 250, Top = 68, Width = 260
        };
        _includeTranslationsCheckbox.CheckedChanged += async (_, _) => await RefreshCounterpartsAsync();

        // Only offered when the passages actually carry a detail. Most
        // views don't attach one, and a permanently dead checkbox is worse
        // than no checkbox.
        var hasDetail = passages.Any(p => !string.IsNullOrWhiteSpace(p.Detail));
        _includeDetailCheckbox = new CheckBox
        {
            Text = $"Include {_detailLabel}",
            Left = 16,
            Top = 92,
            Width = 480,
            Checked = hasDetail,
            Visible = hasDetail
        };
        _includeDetailCheckbox.CheckedChanged += (_, _) => RefreshPreview();

        var previewLabel = new Label
        {
            Text = "Preview:", Left = 16, Top = hasDetail ? 122 : 98, Width = 200
        };
        _previewBox = new TextBox
        {
            Left = 16,
            Top = hasDetail ? 144 : 120,
            Width = 580,
            Height = hasDetail ? 306 : 330,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(MixedScriptFont, 10F)
        };

        var formatLabel = new Label { Text = "Export as:", Left = 16, Top = 466, Width = 100 };

        // Own Panel, same reason PassageExportForm gives: WinForms scopes
        // radio mutual exclusion to the parent container.
        var formatPanel = new Panel
        {
            Left = 12, Top = 490, Width = 584, Height = 44, BorderStyle = BorderStyle.FixedSingle
        };
        _txtRadio = new RadioButton { Text = "Plain text (.txt)", Left = 8, Top = 9, Width = 176, Checked = true };
        _docxRadio = new RadioButton { Text = "Word document (.docx)", Left = 196, Top = 9, Width = 206 };
        _pdfRadio = new RadioButton { Text = "PDF (.pdf)", Left = 412, Top = 9, Width = 146 };
        AppIcons.Apply(_txtRadio, "ExportTxt", 22);
        AppIcons.Apply(_docxRadio, "ExportDocx", 22);
        AppIcons.Apply(_pdfRadio, "ExportPdf", 22);
        formatPanel.Controls.Add(_txtRadio);
        formatPanel.Controls.Add(_docxRadio);
        formatPanel.Controls.Add(_pdfRadio);

        _statusLabel = new Label { Left = 16, Top = 544, Width = 580, Height = 34, ForeColor = Color.DimGray };

        _exportButton = new Button { Text = "Export...", Left = 424, Top = 596, Width = 90, Height = 30 };
        _exportButton.Click += async (_, _) => await ExportAsync();
        AppIcons.Apply(_exportButton, "Export", 16);

        var closeButton = new Button
        {
            Text = "Close", Left = 520, Top = 596, Width = 76, Height = 30, DialogResult = DialogResult.Cancel
        };
        CancelButton = closeButton;

        Controls.Add(headerLabel);
        Controls.Add(_showCitationsCheckbox);
        Controls.Add(_showSourceCheckbox);
        Controls.Add(_groupByWorkCheckbox);
        Controls.Add(_includeTranslationsCheckbox);
        Controls.Add(_includeDetailCheckbox);
        Controls.Add(previewLabel);
        Controls.Add(_previewBox);
        Controls.Add(formatLabel);
        Controls.Add(formatPanel);
        Controls.Add(_statusLabel);
        Controls.Add(_exportButton);
        Controls.Add(closeButton);

        Load += (_, _) =>
        {
            RefreshPreview();
            _statusLabel.Text = passages.Count == 0
                ? "Nothing to export."
                : $"{passages.Count:N0} passage(s) ready.";
            _exportButton.Enabled = passages.Count > 0;
        };

        ReadingTheme.AttachTo(this);
    }

    /// <summary>
    /// Looks up the counterpart edition for every passage, once, when the
    /// translations toggle is switched on.
    ///
    /// Passages are grouped by the edition they came from rather than
    /// resolved one at a time: every passage from the same edition shares
    /// one counterpart and one aligner, so a tag covering three hundred
    /// lines of the Iliad loads that translation once instead of three
    /// hundred times.
    /// </summary>
    private async Task RefreshCounterpartsAsync()
    {
        _counterpartByTextNodeId.Clear();

        if (!_includeTranslationsCheckbox.Checked || _passages.Count == 0)
        {
            RefreshPreview();
            _statusLabel.Text = $"{_passages.Count:N0} passage(s) ready.";
            return;
        }

        _statusLabel.Text = "Looking up translations...";
        UseWaitCursor = true;

        try
        {
            var editionIdByNode = await _textNodeRepo.GetEditionIdsAsync(
                _passages.Select(p => p.TextNodeId).ToList());

            var groups = _passages
                .Where(p => editionIdByNode.ContainsKey(p.TextNodeId))
                .GroupBy(p => editionIdByNode[p.TextNodeId]);

            foreach (var group in groups)
            {
                var sourceEditionId = group.Key;
                var siblings = await _editionRepo.GetByWorkAsync(group.First().WorkId);

                // Prefer a sibling of the opposite kind - the translation of
                // an original, or the original behind a translation. Falling
                // back to any other edition covers works whose editions were
                // ingested without a usable Kind, where "the other one" is
                // still more useful than nothing.
                var counterpart =
                    siblings.FirstOrDefault(e => e.EditionId != sourceEditionId && e.Kind == EditionKind.Translation)
                    ?? siblings.FirstOrDefault(e => e.EditionId != sourceEditionId && e.Kind == EditionKind.Original)
                    ?? siblings.FirstOrDefault(e => e.EditionId != sourceEditionId);

                if (counterpart == null) continue;

                var aligner = new PassageAligner(await _textNodeRepo.GetByEditionAsync(counterpart.EditionId));

                foreach (var passage in group)
                {
                    var text = aligner.ResolveText(passage.CitationRef);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _counterpartByTextNodeId[passage.TextNodeId] = text;
                    }
                }
            }

            var paired = _counterpartByTextNodeId.Count;
            _statusLabel.Text = paired == _passages.Count
                ? $"{_passages.Count:N0} passage(s); all paired with a translation."
                : $"{_passages.Count:N0} passage(s); {paired:N0} paired. The rest have no counterpart edition " +
                  "loaded, or no passage at that citation - those export on their own.";
        }
        catch (Exception ex)
        {
            _includeTranslationsCheckbox.Checked = false;
            _statusLabel.Text = $"Couldn't load translations: {ex.Message}";
        }
        finally
        {
            UseWaitCursor = false;
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        var chunks = BuildRenderChunks();
        _previewBox.Text = string.Join(Environment.NewLine,
            chunks.Select(c => string.IsNullOrEmpty(c.Label) ? c.Text : $"{c.Label} {c.Text}"));
    }

    /// <summary>
    /// The single definition of what the output looks like, shared by the
    /// preview and all three formats so they can't drift apart - the same
    /// arrangement PassageExportForm uses.
    ///
    /// A chunk's Label renders bold and grey in .docx and .pdf, so a work
    /// heading is expressed as a label with no text rather than needing the
    /// export service to learn about headings.
    /// </summary>
    private List<(string Label, string Text)> BuildRenderChunks()
    {
        var chunks = new List<(string Label, string Text)>();
        if (_passages.Count == 0) return chunks;

        var grouped = _groupByWorkCheckbox.Checked;
        var showSource = _showSourceCheckbox.Checked;
        var showCitations = _showCitationsCheckbox.Checked;

        // Grouping sorts by author then work so a document reads in a stable
        // order; without it the caller's own order is preserved, which for a
        // concordance or echo result is itself meaningful (relevance, or
        // position in the text).
        var ordered = grouped
            ? _passages
                .OrderBy(p => p.AuthorName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.WorkTitle, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : _passages.ToList();

        string? currentGroup = null;

        foreach (var passage in ordered)
        {
            if (grouped)
            {
                var groupKey = $"{passage.AuthorName}, {passage.WorkTitle}";
                if (groupKey != currentGroup)
                {
                    currentGroup = groupKey;
                    chunks.Add((groupKey, string.Empty));
                }
            }

            // Inside a group the author and work are already overhead, so
            // repeating them on every line would be noise rather than
            // attribution.
            var parts = new List<string>();
            if (showSource && !grouped) parts.Add($"{passage.AuthorName}, {passage.WorkTitle}");
            if (showCitations) parts.Add($"[{passage.CitationRef}]");

            chunks.Add((string.Join(" ", parts), passage.Text));

            if (_counterpartByTextNodeId.TryGetValue(passage.TextNodeId, out var counterpart))
            {
                chunks.Add(("(trans.)", counterpart));
            }

            if (_includeDetailCheckbox.Checked && !string.IsNullOrWhiteSpace(passage.Detail))
            {
                chunks.Add(($"({_detailLabel})", passage.Detail));
            }
        }

        return chunks;
    }

    private async Task ExportAsync()
    {
        if (_passages.Count == 0)
        {
            MessageBox.Show(this, "Nothing to export.", "Empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var extension = _txtRadio.Checked ? "txt" : _docxRadio.Checked ? "docx" : "pdf";
        var filter = _txtRadio.Checked ? "Text file (*.txt)|*.txt"
            : _docxRadio.Checked ? "Word document (*.docx)|*.docx"
            : "PDF file (*.pdf)|*.pdf";

        var suggestedName = _collectionTitle;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            suggestedName = suggestedName.Replace(invalid, '_');
        }

        using var saveDialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = $"{suggestedName}.{extension}",
            Title = "Export Passages"
        };
        if (saveDialog.ShowDialog(this) != DialogResult.OK) return;

        var sourceUrl = "Perseus Digital Library (via Classica Codex) - see About for full attribution and licensing.";
        var chunks = BuildRenderChunks();

        try
        {
            if (_txtRadio.Checked)
            {
                PassageExportService.ExportText(saveDialog.FileName, _collectionTitle, sourceUrl, chunks);
            }
            else if (_docxRadio.Checked)
            {
                PassageExportService.ExportDocx(saveDialog.FileName, _collectionTitle, sourceUrl, chunks, MixedScriptFont);
            }
            else
            {
                PassageExportService.ExportPdf(saveDialog.FileName, _collectionTitle, sourceUrl, chunks, MixedScriptFont);
            }

            var openFolder = MessageBox.Show(this,
                $"Exported to {saveDialog.FileName}.\r\n\r\nOpen the containing folder?",
                "Done", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (openFolder == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{saveDialog.FileName}\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
