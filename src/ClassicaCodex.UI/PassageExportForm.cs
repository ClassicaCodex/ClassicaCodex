using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Opened from a reader pane's right-click menu. Starts from the clicked
/// line and lets you grow the export to however many consecutive lines make
/// up the passage you actually want, previews exactly what will be written,
/// then saves it as .txt, .docx, or .pdf.
/// </summary>
public class PassageExportForm : Form
{
    private readonly TextNode _startNode;
    private readonly int _editionId;
    private readonly string _authorName;
    private readonly string _workTitle;
    private readonly string _fontName;
    private readonly int? _counterpartEditionId;
    private readonly bool _counterpartIsOriginal;

    /// <summary>
    /// How the counterpart edition is named in the reader - "trans. Gemini
    /// (AI-generated)", "trans. Samuel Butler", "Greek (original)". Null only
    /// when the caller could not resolve the edition, in which case export
    /// falls back to the old bare label rather than inventing attribution.
    /// </summary>
    private readonly string? _counterpartDescriptor;
    private readonly string _originalFontName;

    private readonly RadioButton _lineCountModeRadio;
    private readonly RadioButton _toEndModeRadio;
    private readonly RadioButton _entireWorkModeRadio;
    private readonly NumericUpDown _lineCountUpDown;
    private readonly CheckBox _showCitationsCheckbox;
    private readonly CheckBox _combineCheckbox;
    private readonly CheckBox _bilingualCheckbox;
    private readonly TextBox _previewBox;
    private readonly RadioButton _txtRadio;
    private readonly RadioButton _docxRadio;
    private readonly RadioButton _pdfRadio;
    private readonly Label _statusLabel;

    private readonly TextNodeRepository _textNodeRepo = new();
    private List<(string CitationRef, string Text)> _currentLines = new();

    /// <summary>
    /// Aligns the counterpart edition's passages against the primary by
    /// citation ref - see PassageAligner's own remarks for why an exact-only
    /// match isn't enough. Null when bilingual mode is off or there's no
    /// counterpart edition to align against.
    /// </summary>
    private PassageAligner? _aligner;

    public PassageExportForm(
        TextNode startNode, int editionId, string authorName, string workTitle, string fontName,
        int? counterpartEditionId = null, bool counterpartIsOriginal = false, string? originalFontName = null,
        string? counterpartDescriptor = null)
    {
        _startNode = startNode;
        _editionId = editionId;
        _authorName = authorName;
        _workTitle = workTitle;
        _fontName = fontName;
        _counterpartEditionId = counterpartEditionId;
        _counterpartIsOriginal = counterpartIsOriginal;
        _counterpartDescriptor = counterpartDescriptor;
        _originalFontName = originalFontName ?? fontName;

        Text = "Export Passage";
        AppIcons.ApplyWindowIcon(this, "Export");
        Width = 620;
        Height = 660;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var headerLabel = new Label
        {
            Text = $"{authorName}, {workTitle} - starting at [{startNode.CitationRef}]",
            Left = 16,
            Top = 14,
            Width = 580,
            Font = new Font(Font, FontStyle.Bold)
        };

        // Each radio set lives in its own Panel on purpose: WinForms scopes
        // radio-button mutual exclusion to the parent container, so leaving
        // these and the format radios all parented to the Form made every
        // radio on the dialog one single group - picking "PDF" would clear
        // the scope selection. Separate containers keep them independent.
        var scopePanel = new Panel { Left = 12, Top = 38, Width = 584, Height = 58, BorderStyle = BorderStyle.FixedSingle };

        _lineCountModeRadio = new RadioButton { Text = "Number of lines:", Left = 8, Top = 6, Width = 130, Checked = true };
        _lineCountUpDown = new NumericUpDown { Left = 142, Top = 4, Width = 60, Minimum = 1, Maximum = 5000, Value = 1 };
        _lineCountUpDown.ValueChanged += async (_, _) => await RefreshPreviewAsync();

        _toEndModeRadio = new RadioButton { Text = "From here to end of work", Left = 220, Top = 6, Width = 180 };
        _entireWorkModeRadio = new RadioButton { Text = "Entire work (from the beginning)", Left = 8, Top = 30, Width = 230 };

        _lineCountModeRadio.CheckedChanged += async (_, _) =>
        {
            _lineCountUpDown.Enabled = _lineCountModeRadio.Checked;
            if (_lineCountModeRadio.Checked) await RefreshPreviewAsync();
        };
        _toEndModeRadio.CheckedChanged += async (_, _) =>
        {
            if (_toEndModeRadio.Checked) await RefreshPreviewAsync();
        };
        _entireWorkModeRadio.CheckedChanged += async (_, _) =>
        {
            if (_entireWorkModeRadio.Checked) await RefreshPreviewAsync();
        };

        scopePanel.Controls.Add(_lineCountModeRadio);
        scopePanel.Controls.Add(_lineCountUpDown);
        scopePanel.Controls.Add(_toEndModeRadio);
        scopePanel.Controls.Add(_entireWorkModeRadio);

        _showCitationsCheckbox = new CheckBox { Text = "Show citation refs", Left = 16, Top = 104, Width = 170, Checked = true };
        _showCitationsCheckbox.CheckedChanged += (_, _) => RefreshPreview();

        _combineCheckbox = new CheckBox { Text = "Combine into one continuous passage", Left = 190, Top = 104, Width = 240 };
        _combineCheckbox.CheckedChanged += (_, _) => RefreshPreview();

        _bilingualCheckbox = new CheckBox
        {
            Text = "Include both original and translation",
            Left = 16,
            Top = 128,
            Width = 300,
            Enabled = counterpartEditionId != null
        };
        _bilingualCheckbox.CheckedChanged += async (_, _) => await RefreshPreviewAsync();

        if (counterpartEditionId == null)
        {
            var tip = new ToolTip();
            ReadingTheme.ApplyToToolTip(tip);
            tip.SetToolTip(_bilingualCheckbox,
                "This work doesn't have both an original and a translation loaded in the reader.");
        }

        var previewLabel = new Label { Text = "Preview:", Left = 16, Top = 156, Width = 200 };
        _previewBox = new TextBox
        {
            Left = 16,
            Top = 178,
            Width = 580,
            Height = 268,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(fontName, 10F)
        };

        // Was only a 10px gap to the preview box above (446 -> 456) -
        // tight enough to read as crowding once the format row got busier
        // with icons. Everything from here down is pushed down and given
        // more room to breathe.
        var formatLabel = new Label { Text = "Export as:", Left = 16, Top = 470, Width = 100 };

        var formatPanel = new Panel { Left = 12, Top = 494, Width = 584, Height = 44, BorderStyle = BorderStyle.FixedSingle };

        // Widened again to seat the bigger 22px icon (was 16px) plus a
        // little padding, same 12px/10px gaps between the three preserved.
        _txtRadio = new RadioButton { Text = "Plain text (.txt)", Left = 8, Top = 9, Width = 176, Checked = true };
        _docxRadio = new RadioButton { Text = "Word document (.docx)", Left = 196, Top = 9, Width = 206 };
        _pdfRadio = new RadioButton { Text = "PDF (.pdf)", Left = 412, Top = 9, Width = 146 };
        AppIcons.Apply(_txtRadio, "ExportTxt", 22);
        AppIcons.Apply(_docxRadio, "ExportDocx", 22);
        AppIcons.Apply(_pdfRadio, "ExportPdf", 22);
        formatPanel.Controls.Add(_txtRadio);
        formatPanel.Controls.Add(_docxRadio);
        formatPanel.Controls.Add(_pdfRadio);

        _statusLabel = new Label { Left = 16, Top = 548, Width = 580, Height = 20, ForeColor = Color.DimGray };

        var exportButton = new Button { Text = "Export...", Left = 424, Top = 576, Width = 90, Height = 30 };
        exportButton.Click += async (_, _) => await ExportAsync();
        AppIcons.Apply(exportButton, "Export", 16);

        var closeButton = new Button { Text = "Close", Left = 520, Top = 576, Width = 76, Height = 30, DialogResult = DialogResult.Cancel };

        Controls.Add(headerLabel);
        Controls.Add(scopePanel);
        Controls.Add(_showCitationsCheckbox);
        Controls.Add(_combineCheckbox);
        Controls.Add(_bilingualCheckbox);
        Controls.Add(previewLabel);
        Controls.Add(_previewBox);
        Controls.Add(formatLabel);
        Controls.Add(formatPanel);
        Controls.Add(_statusLabel);
        Controls.Add(exportButton);
        Controls.Add(closeButton);

        Load += async (_, _) => await RefreshPreviewAsync();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    /// <summary>
    /// Positions in the counterpart edition aligned with this citation ref -
    /// delegates to the shared PassageAligner; see its remarks for why exact
    /// matching alone isn't enough.
    /// </summary>
    private List<int>? ResolveCounterpartIndices(string citationRef) => _aligner?.ResolveIndices(citationRef);

    private async Task RefreshPreviewAsync()
    {
        List<TextNode> nodes;
        var requestedCount = (int)_lineCountUpDown.Value;

        if (_entireWorkModeRadio.Checked)
        {
            // Whole edition from its first line, not from wherever the
            // right-click happened to be.
            nodes = await _textNodeRepo.GetByEditionAsync(_editionId);
        }
        else
        {
            var lineCount = _toEndModeRadio.Checked ? int.MaxValue : requestedCount;
            nodes = await _textNodeRepo.GetRangeAsync(_editionId, _startNode.TextNodeId, lineCount);
        }

        _currentLines = nodes.Select(n => (n.CitationRef, n.Text)).ToList();

        // Citation ref is the only shared key between an original and its
        // translation - they don't share line numbering, IDs, or ordering.
        // Loaded lazily, and for the whole counterpart edition at once,
        // since a lookup by citation needs the full map anyway and an
        // edition is at most a few thousand rows.
        if (_bilingualCheckbox.Checked && _counterpartEditionId != null)
        {
            var counterpartNodes = await _textNodeRepo.GetByEditionAsync(_counterpartEditionId.Value);
            _aligner = new PassageAligner(counterpartNodes);
        }
        else
        {
            _aligner = null;
        }

        RefreshPreview();

        if (_bilingualCheckbox.Checked && _aligner != null)
        {
            var matched = _currentLines.Count(l => ResolveCounterpartIndices(l.CitationRef) != null);
            _statusLabel.Text = matched == _currentLines.Count
                ? $"{nodes.Count} line(s), all paired."
                : $"{nodes.Count} line(s); {matched} paired by citation ref. Unpaired translation passages (introductions, cast lists) are still included.";
        }
        else
        {
            _statusLabel.Text = _lineCountModeRadio.Checked && nodes.Count < requestedCount
                ? $"Only {nodes.Count} line(s) available from here to the end of the edition."
                : $"{nodes.Count} line(s).";
        }
    }

    /// <summary>
    /// Rebuilds just the preview text from already-fetched lines - used
    /// when a toggle changes, so flipping "Show citation refs" or "Combine"
    /// doesn't need a database round-trip.
    /// </summary>
    private void RefreshPreview()
    {
        var chunks = BuildRenderChunks();
        _previewBox.Text = string.Join(Environment.NewLine,
            chunks.Select(c => string.IsNullOrEmpty(c.Label) ? c.Text : $"{c.Label} {c.Text}"));
    }

    /// <summary>
    /// Turns the fetched lines into whatever the toggles say the output
    /// should actually look like - shared by the preview and every export
    /// format, so they can never drift out of sync with each other.
    ///
    /// Bilingual mode pairs each line with its counterpart by citation ref,
    /// which is the only key the two editions genuinely share (they don't
    /// share IDs, line numbering, or line counts). That pairing is
    /// deliberately partial: a verse original is lineated per line while a
    /// prose translation often carries one citation per paragraph, so many
    /// original lines simply have no counterpart at that exact ref. Those
    /// are emitted alone rather than dropped or guessed at - showing the
    /// original with no translation beside it is honest; inventing an
    /// alignment that isn't in the source data wouldn't be.
    /// </summary>
    private List<(string Label, string Text)> BuildRenderChunks()
    {
        if (_currentLines.Count == 0) return new();

        var bilingual = _bilingualCheckbox.Checked && _aligner != null;
        // The counterpart is labelled with its actual edition - translator
        // included - rather than a bare "trans.". Exported text outlives the
        // application that produced it, so whether a rendering is Butler's,
        // the reader's own, or a machine's has to travel with it.
        var counterpartLabel = _counterpartDescriptor
            ?? (_counterpartIsOriginal ? "original" : "trans.");

        if (_combineCheckbox.Checked)
        {
            var rangeLabel = !_showCitationsCheckbox.Checked ? string.Empty
                : _currentLines.Count == 1 ? $"[{_currentLines[0].CitationRef}]"
                : $"[{_currentLines.First().CitationRef}\u2013{_currentLines.Last().CitationRef}]";

            var primaryText = string.Join(" ", _currentLines.Select(l => l.Text));

            if (!bilingual)
            {
                return new List<(string, string)> { (rangeLabel, primaryText) };
            }

            // Combined + bilingual reads best as two continuous blocks - the
            // whole passage in one language, then the whole passage in the
            // other. Every counterpart passage is included exactly once, in
            // its own reading order, so introductions and cast lists survive
            // even though nothing in the original pairs with them.
            var counterpartText = string.Join(" ", _aligner!.Ordered.Select(c => c.Text));

            var result = new List<(string, string)> { (rangeLabel, primaryText) };
            if (counterpartText.Length > 0)
            {
                result.Add(($"({counterpartLabel})", counterpartText));
            }
            return result;
        }

        var chunks = new List<(string Label, string Text)>();

        if (!bilingual)
        {
            foreach (var line in _currentLines)
            {
                var soloLabel = _showCitationsCheckbox.Checked ? $"[{line.CitationRef}]" : string.Empty;
                chunks.Add((soloLabel, line.Text));
            }
            return chunks;
        }

        // Merge both sequences rather than walking the primary and hanging
        // counterparts off it. Iterating the primary alone silently drops
        // every counterpart passage that has no primary equivalent - which
        // is exactly what introductions, prefatory arguments, and cast lists
        // are, since they usually appear only in the translation. Tracking
        // what's been emitted lets those appear in their proper position.
        var counterpartOrdered = _aligner!.Ordered;
        var emitted = new bool[counterpartOrdered.Count];
        var cursor = 0;

        void EmitCounterpart(int index)
        {
            if (index < 0 || index >= counterpartOrdered.Count || emitted[index]) return;
            emitted[index] = true;
            chunks.Add(($"({counterpartLabel})", counterpartOrdered[index].Text));
        }

        foreach (var line in _currentLines)
        {
            var indices = ResolveCounterpartIndices(line.CitationRef);

            if (indices is { Count: > 0 })
            {
                // Anything in the counterpart edition standing before this
                // match and still unemitted belongs here - ahead of the line
                // it precedes, not appended as an afterthought at the end.
                var firstIndex = indices.Min();
                for (; cursor < firstIndex; cursor++) EmitCounterpart(cursor);
            }

            var label = _showCitationsCheckbox.Checked ? $"[{line.CitationRef}]" : string.Empty;
            chunks.Add((label, line.Text));

            if (indices == null) continue;

            // Emitted once each, so a coarse counterpart spanning many
            // primary lines - an English chapter over a dozen Latin sections -
            // appears alongside the first line it covers, not under each.
            foreach (var index in indices) EmitCounterpart(index);
        }

        // Whatever's left: counterpart passages that follow the last matched
        // line, or never matched anything at all.
        for (var i = 0; i < counterpartOrdered.Count; i++) EmitCounterpart(i);

        return chunks;
    }

    private async Task ExportAsync()
    {
        if (_currentLines.Count == 0)
        {
            MessageBox.Show(this, "Nothing to export.", "Empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var extension = _txtRadio.Checked ? "txt" : _docxRadio.Checked ? "docx" : "pdf";
        var filter = _txtRadio.Checked ? "Text file (*.txt)|*.txt"
            : _docxRadio.Checked ? "Word document (*.docx)|*.docx"
            : "PDF file (*.pdf)|*.pdf";

        var suggestedName = $"{_authorName} - {_workTitle} {_startNode.CitationRef}".Replace(":", "_");
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            suggestedName = suggestedName.Replace(invalid, '_');
        }

        using var saveDialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = $"{suggestedName}.{extension}",
            Title = "Export Passage"
        };
        if (saveDialog.ShowDialog(this) != DialogResult.OK) return;

        var title = _showCitationsCheckbox.Checked
            ? $"{_authorName}, {_workTitle} [{_currentLines.First().CitationRef}\u2013{_currentLines.Last().CitationRef}]"
            : $"{_authorName}, {_workTitle}";
        var sourceUrl = "Perseus Digital Library (via Classica Codex) - see About for full attribution and licensing.";
        var chunks = BuildRenderChunks();

        // A bilingual document has to render polytonic Greek regardless of
        // which pane was right-clicked, and the translation pane's font
        // (Georgia) has no Greek coverage - it'd come out as fallback
        // glyphs or boxes. The original-language font (Palatino Linotype)
        // handles both scripts, so it's the safe choice for mixed output.
        var exportFont = _bilingualCheckbox.Checked ? _originalFontName : _fontName;

        try
        {
            if (_txtRadio.Checked)
            {
                PassageExportService.ExportText(saveDialog.FileName, title, sourceUrl, chunks);
            }
            else if (_docxRadio.Checked)
            {
                PassageExportService.ExportDocx(saveDialog.FileName, title, sourceUrl, chunks, exportFont);
            }
            else
            {
                PassageExportService.ExportPdf(saveDialog.FileName, title, sourceUrl, chunks, exportFont);
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
