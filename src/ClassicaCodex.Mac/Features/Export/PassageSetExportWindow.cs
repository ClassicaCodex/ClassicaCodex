using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Export;

/// <summary>
/// One passage in a gathered set, in the shape every view that gathers them
/// already produces - the Tag Browser, Concordance, Echo results, Reception
/// Tracker, Places Map, and Word Study all return exactly these fields, so a
/// caller can hand its results straight over without reshaping them. The
/// same record as the Windows app's, field for field.
/// </summary>
public sealed record ExportPassage(
    int WorkId,
    long TextNodeId,
    string AuthorName,
    string WorkTitle,
    string CitationRef,
    string Text,
    string? Detail = null,
    string? Milestone = null);

/// <summary>
/// Exports a set of passages gathered from across the library, as opposed to
/// a continuous run of lines from one work - which is what
/// PassageExportWindow handles, and why this is a separate window rather than
/// another mode bolted onto it.
///
/// The difference is not cosmetic. A passage set has no start line to count
/// forward from, no single work, no single language, and no inherent reading
/// order - so the scope controls that window is built around have nothing to
/// act on here. What matters instead is where each passage came from, since
/// a set spanning twenty authors is unreadable without attribution on every
/// line.
///
/// This is the missing half of the app's research loop: tagging, searching
/// and echo-hunting all gather passages from everywhere, and without this
/// the result could only be read on screen.
/// </summary>
public sealed class PassageSetExportWindow : FeatureWindow
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

    private readonly CheckBox _showCitations;
    private readonly CheckBox _showSource;
    private readonly CheckBox _groupByWork;
    private readonly CheckBox _includeTranslations;
    private readonly CheckBox _includeDetail;
    private readonly TextBox _preview;
    private readonly FormatPicker _formats;
    private readonly TextBlock _status;
    private readonly Button _export;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionRepository _editionRepo = new();

    /// <summary>
    /// Counterpart text per passage, keyed by TextNodeId, with the label of
    /// the edition it came from. Populated only when translations are
    /// switched on, and deliberately partial: a passage whose work has no
    /// second edition, or whose citation ref has no counterpart in it, simply
    /// isn't in here. Those export alone rather than being dropped or guessed
    /// at.
    ///
    /// The label is carried per passage because a gathered set spans many
    /// works, each resolving its own counterpart edition. It matters here
    /// more than anywhere: the counterpart is chosen automatically, and an
    /// AI-generated translation is stored as an edition like any other - so
    /// the edition picked can be a machine rendering that the reader never
    /// explicitly asked to export.
    /// </summary>
    private readonly Dictionary<long, (string Text, string Label)> _counterpartByTextNodeId = new();

    /// <summary>
    /// Mixed scripts are the norm here, not the exception - one tag can pull
    /// Greek, Latin and English into a single document. Georgia, which the
    /// translation pane uses, has no polytonic Greek coverage. The original
    /// pane's reading font (Palatino on a Mac) covers both scripts, so a
    /// cross-work export always uses it regardless of where the passages came
    /// from.
    /// </summary>
    private readonly string _mixedScriptFont;

    public PassageSetExportWindow(string collectionTitle, IReadOnlyList<ExportPassage> passages, string? detailLabel = null)
    {
        _collectionTitle = collectionTitle;
        _passages = passages;
        _detailLabel = detailLabel ?? "extra detail";
        _mixedScriptFont = Application.Current?.FindResource("Cc.SourceFont") is FontFamily source
            ? string.Join(", ", source.FamilyNames)
            : "Palatino";

        Title = "Export Passages";
        Width = 680;
        Height = 720;
        MinWidth = 560;
        MinHeight = 560;

        var distinctWorks = passages.Select(p => (p.AuthorName, p.WorkTitle)).Distinct().Count();

        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock { Text = collectionTitle, Classes = { "h2" }, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock
        {
            Text = $"{passages.Count:N0} passage(s) from {distinctWorks:N0} work(s)",
            Classes = { "caption" }
        });

        _showCitations = new CheckBox { Content = "Show citation refs", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
        _showCitations.IsCheckedChanged += (_, _) => RefreshPreview();

        // On by default, unlike the single-work window where it would be
        // repeated noise. Here it's the difference between a usable document
        // and a wall of unattributed quotations.
        _showSource = new CheckBox { Content = "Show author and work", IsChecked = true };
        _showSource.IsCheckedChanged += (_, _) => RefreshPreview();

        _groupByWork = new CheckBox { Content = "Group by work, with headings", IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
        _groupByWork.IsCheckedChanged += (_, _) => RefreshPreview();

        _includeTranslations = new CheckBox { Content = "Include translations where available" };
        _includeTranslations.IsCheckedChanged += async (_, _) => await RefreshCounterpartsAsync();

        // Only offered when the passages actually carry a detail. Most views
        // don't attach one, and a permanently dead checkbox is worse than no
        // checkbox.
        var hasDetail = passages.Any(p => !string.IsNullOrWhiteSpace(p.Detail));
        _includeDetail = new CheckBox { Content = $"Include {_detailLabel}", IsChecked = hasDetail, IsVisible = hasDetail };
        _includeDetail.IsCheckedChanged += (_, _) => RefreshPreview();

        var firstRow = new WrapPanel();
        firstRow.Children.Add(_showCitations);
        firstRow.Children.Add(_showSource);

        var secondRow = new WrapPanel();
        secondRow.Children.Add(_groupByWork);
        secondRow.Children.Add(_includeTranslations);

        var options = new StackPanel { Spacing = 4 };
        options.Children.Add(firstRow);
        options.Children.Add(secondRow);
        options.Children.Add(_includeDetail);

        _preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(_mixedScriptFont),
            FontSize = 13.5,
            Padding = new Thickness(10, 8)
        };

        _formats = new FormatPicker(includeReferences: false);
        _status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

        var exportContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        exportContent.Children.Add(Glyphs.Icon("Export", 14));
        exportContent.Children.Add(new TextBlock { Text = "Export…", VerticalAlignment = VerticalAlignment.Center });
        _export = new Button { Content = exportContent, Classes = { "accent" }, IsDefault = true, MinWidth = 96 };
        _export.Click += async (_, _) => await ExportAsync();

        var close = new Button { Content = "Close", MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(close);
        buttons.Children.Add(_export);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(_status);

        var top = new StackPanel { Spacing = 8 };
        top.Children.Add(header);
        top.Children.Add(new TextBlock { Text = "LAYOUT", Classes = { "section" }, Margin = new Thickness(0, 10, 0, 0) });
        top.Children.Add(options);
        top.Children.Add(new TextBlock { Text = "PREVIEW", Classes = { "section" }, Margin = new Thickness(0, 8, 0, 0) });

        var formatSection = new StackPanel { Spacing = 6, Margin = new Thickness(0, 14, 0, 0) };
        formatSection.Children.Add(new TextBlock { Text = "EXPORT AS", Classes = { "section" } });
        formatSection.Children.Add(_formats);

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(formatSection, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(formatSection);
        root.Children.Add(_preview);
        Content = root;

        Opened += (_, _) =>
        {
            RefreshPreview();
            SetStatus(passages.Count == 0 ? "Nothing to export." : $"{passages.Count:N0} passage(s) ready.");
            _export.IsEnabled = passages.Count > 0;
        };
    }

    internal ExportFormat Format
    {
        get => _formats.Selected;
        set => _formats.Selected = value;
    }

    internal CheckBox IncludeTranslations => _includeTranslations;

    internal string PreviewText => _preview.Text ?? string.Empty;

    internal string StatusText => _status.Text ?? string.Empty;

    private void SetStatus(string text, bool warning = false)
    {
        _status.Text = text;
        _status.Classes.Set("warning", warning);
        _status.Classes.Set("caption", !warning);
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
    internal async Task RefreshCounterpartsAsync()
    {
        _counterpartByTextNodeId.Clear();

        if (_includeTranslations.IsChecked != true || _passages.Count == 0)
        {
            RefreshPreview();
            SetStatus($"{_passages.Count:N0} passage(s) ready.");
            return;
        }

        SetStatus("Looking up translations...");
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Wait);

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
                var label = EditionLabels.Descriptor(counterpart);

                foreach (var passage in group)
                {
                    var text = aligner.ResolveText(passage.CitationRef);
                    if (!string.IsNullOrWhiteSpace(text)) _counterpartByTextNodeId[passage.TextNodeId] = (text, label);
                }
            }

            var paired = _counterpartByTextNodeId.Count;
            SetStatus(paired == _passages.Count
                ? $"{_passages.Count:N0} passage(s); all paired with a translation."
                : $"{_passages.Count:N0} passage(s); {paired:N0} paired. The rest have no counterpart edition " +
                  "loaded, or no passage at that citation - those export on their own.");
        }
        catch (Exception ex)
        {
            _includeTranslations.IsChecked = false;
            SetStatus($"Couldn't load translations: {ex.Message}", warning: true);
        }
        finally
        {
            Cursor = null;
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
    /// The single definition of what the output looks like, shared by the
    /// preview and all three formats so they can't drift apart.
    ///
    /// A chunk's Label renders bold and grey in .docx and .pdf, so a work
    /// heading is expressed as a label with no text rather than needing the
    /// export service to learn about headings.
    /// </summary>
    private List<(string Label, string Text)> BuildRenderChunks()
    {
        var chunks = new List<(string Label, string Text)>();
        if (_passages.Count == 0) return chunks;

        var grouped = _groupByWork.IsChecked == true;
        var showSource = _showSource.IsChecked == true;
        var showCitations = _showCitations.IsChecked == true;

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
            if (showCitations) parts.Add($"[{PassageCitation.Display(passage.CitationRef, passage.Milestone)}]");

            chunks.Add((string.Join(" ", parts), passage.Text));

            if (_counterpartByTextNodeId.TryGetValue(passage.TextNodeId, out var counterpart))
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

        var format = Format;
        var path = await Files.SaveAsync(this, "Export Passages",
            $"{ExportFiles.SafeName(_collectionTitle)}.{ExportFiles.Extension(format)}", ExportFiles.FileType(format));
        if (path == null) return;

        try
        {
            await WriteFileAsync(path, format);
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Error", $"Export failed: {ex.Message}");
            return;
        }

        await ExportFiles.OfferToRevealAsync(this, path);
    }

    /// <summary>Writes the set as the preview shows it. Throws if the file cannot be written.</summary>
    internal Task WriteFileAsync(string path, ExportFormat format)
    {
        const string source = "Perseus Digital Library (via Classica Codex) - see About for full attribution and licensing.";
        return ExportFiles.WriteAsync(format, path, _collectionTitle, source, BuildRenderChunks(), _mixedScriptFont);
    }
}
