using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Echoes;

/// <summary>
/// One passage in a gathered set, in the shape every view that gathers them
/// already produces - echo results, the Reception Tracker, Cross-Language
/// Echo - so a caller can hand its results straight over without reshaping
/// them. The Windows record of the same name, field for field.
/// </summary>
internal sealed record ExportPassage(
    int WorkId,
    long TextNodeId,
    string AuthorName,
    string WorkTitle,
    string CitationRef,
    string Text,
    string? Detail = null,
    string? Milestone = null);

/// <summary>
/// "Export All Passages…": a set of passages gathered from across the library
/// written to a document - the Windows PassageSetExportForm.
///
/// A different thing from exporting a run of lines from one work. A set has
/// no start line to count from, no single work, no single language and no
/// inherent order, so what matters instead is where each passage came from:
/// a set spanning twenty authors is unreadable without attribution on every
/// line. The whole gathered set is exported, not the row under the pointer;
/// a single passage already has its own export on the reader's line menu.
///
/// Kept here, beside the windows that gather passages, until the shared
/// Export feature offers a set export of its own for every list to call.
/// </summary>
internal static class PassageSetExport
{
    /// <summary>
    /// Opens the dialog, or says there is nothing to export rather than
    /// opening an empty one.
    /// </summary>
    /// <param name="detailLabel">
    /// What the calling view calls the extra line it attached to each
    /// passage - "why each was suggested" for Cross-Language Echo. Only the
    /// caller knows what its detail is; a generic "include details" would
    /// tell the reader nothing.
    /// </param>
    public static async Task OpenAsync(Window owner, string title, IReadOnlyList<ExportPassage> passages, string? detailLabel = null)
    {
        if (passages.Count == 0)
        {
            await Alert.Info(owner, "Nothing to export", "There are no passages here to export yet.");
            return;
        }

        var window = new PassageSetExportWindow(title, passages, detailLabel);
        await window.ShowDialog(owner);
    }
}

internal sealed class PassageSetExportWindow : FeatureWindow
{
    /// <summary>
    /// The face the document is set in. Mixed scripts are the norm here - one
    /// set can pull Greek, Latin and English into a single document - so it
    /// has to carry polytonic Greek. Windows uses Palatino Linotype for that;
    /// macOS ships the same face as plain Palatino, which is what a document
    /// made on a Mac will find when it is opened on one.
    /// </summary>
    private const string MixedScriptFont = "Palatino";

    private const string SourceNote =
        "Perseus Digital Library (via Classica Codex) - see About for full attribution and licensing.";

    private readonly string _collectionTitle;
    private readonly IReadOnlyList<ExportPassage> _passages;
    private readonly string _detailLabel;

    private readonly CheckBox _showCitations;
    private readonly CheckBox _showSource;
    private readonly CheckBox _groupByWork;
    private readonly CheckBox _includeTranslations;
    private readonly CheckBox _includeDetail;
    private readonly TextBox _preview;
    private readonly RadioButton _txt;
    private readonly RadioButton _docx;
    private readonly RadioButton _pdf;
    private readonly TextBlock _status;
    private readonly Button _export;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionRepository _editionRepo = new();

    /// <summary>
    /// Counterpart text per passage, keyed by TextNodeId, with the label of the
    /// edition it came from. Filled only when translations are switched on,
    /// and deliberately partial: a passage whose work has no second edition,
    /// or whose reference has no counterpart in it, exports alone rather than
    /// being dropped or guessed at.
    ///
    /// The label travels per passage because a set spans many works and each
    /// resolves its own counterpart - and the edition chosen can be an
    /// AI-generated translation the reader never explicitly asked to export,
    /// which is exactly why it has to say so on the page.
    /// </summary>
    private readonly Dictionary<long, (string Text, string Label)> _counterparts = new();

    public PassageSetExportWindow(string collectionTitle, IReadOnlyList<ExportPassage> passages, string? detailLabel)
    {
        _collectionTitle = collectionTitle;
        _passages = passages;
        _detailLabel = detailLabel ?? "extra detail";

        Title = "Export Passages";
        Width = 620;
        Height = 660;
        MinWidth = 520;
        MinHeight = 520;
        CanMinimize = false;
        ShowInTaskbar = false;

        var distinctWorks = passages.Select(p => (p.AuthorName, p.WorkTitle)).Distinct().Count();

        _showCitations = new CheckBox { Content = "Show citation refs", IsChecked = true };
        _showCitations.IsCheckedChanged += (_, _) => RefreshPreview();

        // On by default, unlike the single-work export where it would be
        // repeated noise. Here it is the difference between a usable document
        // and a wall of unattributed quotations.
        _showSource = new CheckBox { Content = "Show author and work", IsChecked = true };
        _showSource.IsCheckedChanged += (_, _) => RefreshPreview();

        _groupByWork = new CheckBox { Content = "Group by work, with headings", IsChecked = true };
        _groupByWork.IsCheckedChanged += (_, _) => RefreshPreview();

        _includeTranslations = new CheckBox { Content = "Include translations where available" };
        _includeTranslations.IsCheckedChanged += async (_, _) => await RefreshCounterpartsAsync();

        // Offered only when the passages carry a detail. Most views attach
        // none, and a permanently dead checkbox is worse than no checkbox.
        var hasDetail = passages.Any(p => !string.IsNullOrWhiteSpace(p.Detail));
        _includeDetail = new CheckBox { Content = $"Include {_detailLabel}", IsChecked = hasDetail, IsVisible = hasDetail };
        _includeDetail.IsCheckedChanged += (_, _) => RefreshPreview();

        var options = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
        Place(options, _showCitations, 0, 0);
        Place(options, _showSource, 0, 1);
        Place(options, _groupByWork, 1, 0);
        Place(options, _includeTranslations, 1, 1);
        Place(options, _includeDetail, 2, 0);
        Grid.SetColumnSpan(_includeDetail, 2);

        _preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = EchoUi.SourceFont,
            FontSize = 13.5
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        _txt = new RadioButton { Content = "Plain text (.txt)", GroupName = "format", IsChecked = true };
        _docx = new RadioButton { Content = "Word document (.docx)", GroupName = "format" };
        _pdf = new RadioButton { Content = "PDF (.pdf)", GroupName = "format" };
        var formats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        formats.Children.Add(_txt);
        formats.Children.Add(_docx);
        formats.Children.Add(_pdf);

        _status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

        _export = new Button { Content = "Export…", Classes = { "accent" }, IsDefault = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        _export.Click += async (_, _) => await ExportAsync();
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(close);
        buttons.Children.Add(_export);

        var top = new StackPanel { Spacing = 12 };
        top.Children.Add(new TextBlock
        {
            Text = $"{collectionTitle} — {passages.Count:N0} passage(s) from {distinctWorks:N0} work(s)",
            Classes = { "h2" },
            TextWrapping = TextWrapping.Wrap
        });
        top.Children.Add(options);
        top.Children.Add(new TextBlock { Text = "PREVIEW", Classes = { "section" }, Margin = new Thickness(0, 4, 0, -6) });

        var bottom = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(new TextBlock { Text = "EXPORT AS", Classes = { "section" } });
        bottom.Children.Add(formats);
        bottom.Children.Add(_status);
        bottom.Children.Add(buttons);

        var layout = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(bottom);
        _preview.Margin = new Thickness(0, 12, 0, 0);
        layout.Children.Add(_preview);
        Content = layout;

        RefreshPreview();
        _status.Text = passages.Count == 0 ? "Nothing to export." : $"{passages.Count:N0} passage(s) ready.";
        _export.IsEnabled = passages.Count > 0;
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        control.Margin = new Thickness(0, 0, 12, 2);
        grid.Children.Add(control);
    }

    /// <summary>The preview's text, for the tests to read.</summary>
    internal string PreviewText => _preview.Text ?? string.Empty;

    /// <summary>
    /// Looks up the counterpart edition for every passage, once, when the
    /// translations switch is turned on.
    ///
    /// Grouped by the edition each passage came from rather than resolved one
    /// at a time: every passage from one edition shares one counterpart and
    /// one aligner, so three hundred lines of the Iliad load that translation
    /// once instead of three hundred times.
    /// </summary>
    private async Task RefreshCounterpartsAsync()
    {
        _counterparts.Clear();

        if (_includeTranslations.IsChecked != true || _passages.Count == 0)
        {
            RefreshPreview();
            _status.Text = $"{_passages.Count:N0} passage(s) ready.";
            return;
        }

        _status.Text = "Looking up translations…";
        _includeTranslations.IsEnabled = false;

        try
        {
            var editionIdByNode = await _textNodeRepo.GetEditionIdsAsync(_passages.Select(p => p.TextNodeId).ToList());

            var groups = _passages
                .Where(p => editionIdByNode.ContainsKey(p.TextNodeId))
                .GroupBy(p => editionIdByNode[p.TextNodeId]);

            foreach (var group in groups)
            {
                var sourceEditionId = group.Key;
                var siblings = await _editionRepo.GetByWorkAsync(group.First().WorkId);

                // The opposite kind first - the translation of an original, or
                // the original behind a translation. Any other edition after
                // that covers works ingested without a usable Kind, where "the
                // other one" is still more use than nothing.
                var counterpart =
                    siblings.FirstOrDefault(e => e.EditionId != sourceEditionId && e.Kind == EditionKind.Translation)
                    ?? siblings.FirstOrDefault(e => e.EditionId != sourceEditionId && e.Kind == EditionKind.Original)
                    ?? siblings.FirstOrDefault(e => e.EditionId != sourceEditionId);

                if (counterpart == null) continue;

                var aligner = new PassageAligner(await _textNodeRepo.GetByEditionAsync(counterpart.EditionId));
                var label = EditionLabels.Descriptor(counterpart);

                foreach (var passage in group)
                {
                    var text = aligner.ResolveText(passage.CitationRef);
                    if (!string.IsNullOrWhiteSpace(text)) _counterparts[passage.TextNodeId] = (text, label);
                }
            }

            var paired = _counterparts.Count;
            _status.Text = paired == _passages.Count
                ? $"{_passages.Count:N0} passage(s); all paired with a translation."
                : $"{_passages.Count:N0} passage(s); {paired:N0} paired. The rest have no counterpart edition " +
                  "loaded, or no passage at that citation - those export on their own.";
        }
        catch (Exception ex)
        {
            _includeTranslations.IsChecked = false;
            _status.Text = $"Couldn't load translations: {ex.Message}";
        }
        finally
        {
            _includeTranslations.IsEnabled = true;
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        var chunks = BuildRenderChunks();
        _preview.Text = string.Join(Environment.NewLine,
            chunks.Select(c => string.IsNullOrEmpty(c.Label) ? c.Text : $"{c.Label} {c.Text}"));
    }

    /// <summary>
    /// The one definition of what the output looks like, shared by the preview
    /// and all three formats so they cannot drift apart.
    ///
    /// A chunk's label renders bold and grey in Word and PDF, so a work heading
    /// is a label with no text rather than something the export service has
    /// to learn about.
    /// </summary>
    private List<(string Label, string Text)> BuildRenderChunks()
    {
        var chunks = new List<(string Label, string Text)>();
        if (_passages.Count == 0) return chunks;

        var grouped = _groupByWork.IsChecked == true;
        var showSource = _showSource.IsChecked == true;
        var showCitations = _showCitations.IsChecked == true;

        // Grouping sorts by author then work so a document reads in a stable
        // order; without it the caller's order is kept, which for an echo
        // result is itself meaningful - it is the ranking.
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
            // repeating them on every line would be noise, not attribution.
            var parts = new List<string>();
            if (showSource && !grouped) parts.Add($"{passage.AuthorName}, {passage.WorkTitle}");
            if (showCitations) parts.Add($"[{PassageCitation.Display(passage.CitationRef, passage.Milestone)}]");

            chunks.Add((string.Join(" ", parts), passage.Text));

            if (_counterparts.TryGetValue(passage.TextNodeId, out var counterpart))
                chunks.Add(($"({counterpart.Label})", counterpart.Text));

            if (_includeDetail.IsChecked == true && !string.IsNullOrWhiteSpace(passage.Detail))
                chunks.Add(($"({_detailLabel})", passage.Detail));
        }

        return chunks;
    }

    private async Task ExportAsync()
    {
        if (_passages.Count == 0)
        {
            await Alert.Info(this, "Empty", "Nothing to export.");
            return;
        }

        var (extension, type) = _txt.IsChecked == true ? ("txt", Files.Text)
            : _docx.IsChecked == true ? ("docx", Files.Word)
            : ("pdf", Files.Pdf);

        var path = await Files.SaveAsync(this, "Export Passages", $"{_collectionTitle}.{extension}", type);
        if (path == null) return;

        try
        {
            Write(path, extension, _collectionTitle, BuildRenderChunks());
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Export failed", ExportFailure(ex, extension));
            return;
        }

        if (await Alert.Confirm(this, "Exported", $"Exported to {path}.", "Show in Finder", "Done", AlertKind.Info))
            MacShell.Reveal(path);
    }

    internal static void Write(string path, string extension, string title, IReadOnlyList<(string Label, string Text)> chunks)
    {
        switch (extension)
        {
            case "txt": PassageExportService.ExportText(path, title, SourceNote, chunks); break;
            case "docx": PassageExportService.ExportDocx(path, title, SourceNote, chunks, MixedScriptFont); break;
            default: PassageExportService.ExportPdf(path, title, SourceNote, chunks, MixedScriptFont); break;
        }
    }

    /// <summary>
    /// The message for a failed export. A PDF that fails off Windows gets the
    /// reason too: the PDF writer the two apps share finds its fonts through
    /// the Windows registry, and on a Mac it finds none. Word and plain text
    /// do not depend on it.
    /// </summary>
    internal static string ExportFailure(Exception ex, string extension) =>
        extension == "pdf" && !OperatingSystem.IsWindows()
            ? $"Export failed: {ex.Message}\n\nThe PDF writer shared with the Windows app looks its fonts up in the Windows " +
              "registry and finds none on a Mac yet. Export as a Word document or plain text instead."
            : $"Export failed: {ex.Message}";
}
