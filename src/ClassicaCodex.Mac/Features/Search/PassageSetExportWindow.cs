using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Search;

/// <summary>
/// One passage in a gathered set, in the shape every view that gathers them
/// already produces - the Windows record of the same name, which lives in a
/// WinForms file the Mac cannot compile.
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
/// Exports a set of passages gathered from across the library - a search, a
/// concordance - as opposed to a continuous run of lines from one work.
///
/// The Mac half of the Windows PassageSetExportForm, option for option. A
/// passage set has no start line to count forward from, no single work, no
/// single language and no inherent reading order, so what matters instead
/// is where each passage came from: a set spanning twenty authors is
/// unreadable without attribution on every line.
///
/// The writing is the shared PassageExportService's, so a document exported
/// here is the document the Windows app would have written.
/// </summary>
internal sealed class PassageSetExportWindow : Window
{
    private readonly string _collectionTitle;
    private readonly IReadOnlyList<ExportPassage> _passages;

    /// <summary>
    /// What the calling view calls the extra line it attached to each
    /// passage - "the keyword in context" for the Concordance. Supplied by
    /// the caller because only the caller knows what its detail is; a generic
    /// "Include details" would tell the reader nothing.
    /// </summary>
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
    private readonly Button _exportButton;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionRepository _editionRepo = new();

    /// <summary>
    /// Counterpart text per passage, keyed by TextNodeId, with the label of
    /// the edition it came from. Populated only when translations are
    /// switched on, and deliberately partial: a passage whose work has no
    /// second edition, or whose citation has no counterpart in it, exports
    /// alone rather than being dropped or guessed at.
    ///
    /// The label is carried per passage because a gathered set spans many
    /// works and each resolves its own counterpart - and the one chosen can
    /// be an AI translation, stored as an edition like any other, that the
    /// reader never explicitly asked to export.
    /// </summary>
    private readonly Dictionary<long, (string Text, string Label)> _counterparts = new();

    /// <summary>
    /// The face the document is set in. Mixed scripts are the norm here - one
    /// search can pull Greek, Latin and English into a single file - and
    /// Palatino Linotype covers both alphabets, so a cross-work export always
    /// uses it. Named as Windows names it, since the file is as likely to be
    /// opened there; Word on a Mac substitutes the system's own Palatino.
    /// </summary>
    private const string MixedScriptFont = "Palatino Linotype";

    public PassageSetExportWindow(string collectionTitle, IReadOnlyList<ExportPassage> passages, string? detailLabel = null)
    {
        _collectionTitle = collectionTitle;
        _passages = passages;
        _detailLabel = detailLabel ?? "extra detail";

        Title = "Export Passages";
        Width = 640;
        Height = 700;
        MinWidth = 520;
        MinHeight = 520;
        CanMinimize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var distinctWorks = passages.Select(p => (p.AuthorName, p.WorkTitle)).Distinct().Count();

        var header = new TextBlock
        {
            Text = $"{collectionTitle} — {passages.Count:N0} passage(s) from {distinctWorks:N0} work(s)",
            Classes = { "h2" },
            TextWrapping = TextWrapping.Wrap
        };

        _showCitations = Option("Show citation refs", true);

        // On by default, unlike the single-work export where it would be
        // repeated noise. Here it is the difference between a usable document
        // and a wall of unattributed quotations.
        _showSource = Option("Show author and work", true);
        _groupByWork = Option("Group by work, with headings", true);

        _includeTranslations = new CheckBox { Content = "Include translations where available" };
        _includeTranslations.IsCheckedChanged += async (_, _) => await RefreshCounterpartsAsync();

        // Only offered when the passages carry a detail. Most views don't
        // attach one, and a permanently dead checkbox is worse than none.
        var hasDetail = passages.Any(p => !string.IsNullOrWhiteSpace(p.Detail));
        _includeDetail = Option($"Include {_detailLabel}", hasDetail);
        _includeDetail.IsVisible = hasDetail;

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
            FontSize = 13
        };
        _preview.Bind(TextBox.FontFamilyProperty, _preview.GetResourceObservable("Cc.SourceFont"));
        ScrollViewer.SetVerticalScrollBarVisibility(_preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        _txt = Format("Plain text (.txt)", "FileText", true);
        _docx = Format("Word document (.docx)", "FileDoc", false);
        _pdf = Format("PDF (.pdf)", "FilePdf", false);
        var formats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        formats.Children.Add(_txt);
        formats.Children.Add(_docx);
        formats.Children.Add(_pdf);

        _status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

        _exportButton = new Button { Content = "Export…", Classes = { "accent" }, IsDefault = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        _exportButton.Click += async (_, _) => await ExportAsync();
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(close);
        buttons.Children.Add(_exportButton);

        var bottom = new StackPanel { Spacing = 10 };
        bottom.Children.Add(new TextBlock { Text = "EXPORT AS", Classes = { "section" } });
        bottom.Children.Add(formats);
        bottom.Children.Add(_status);
        bottom.Children.Add(buttons);

        var top = new StackPanel { Spacing = 12 };
        top.Children.Add(header);
        top.Children.Add(options);
        top.Children.Add(new TextBlock { Text = "PREVIEW", Classes = { "section" }, Margin = new Thickness(0, 4, 0, 0) });

        var layout = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        bottom.Margin = new Thickness(0, 14, 0, 0);
        top.Margin = new Thickness(0, 0, 0, 8);
        layout.Children.Add(top);
        layout.Children.Add(bottom);
        layout.Children.Add(_preview);
        Content = layout;

        KeyDown += (_, e) =>
        {
            var closeChord = e.Key == Key.W && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
            if (e.Key == Key.Escape || closeChord)
            {
                Close();
                e.Handled = true;
            }
        };

        RefreshPreview();
        _status.Text = passages.Count == 0 ? "Nothing to export." : $"{passages.Count:N0} passage(s) ready.";
        _exportButton.IsEnabled = passages.Count > 0;
    }

    /// <summary>
    /// Shows the dialog for a set, or says there is nothing to show it for
    /// rather than opening an empty one.
    /// </summary>
    public static async Task ShowAsync(Window owner, string title, IReadOnlyList<ExportPassage> passages, string? detailLabel = null)
    {
        if (passages.Count == 0)
        {
            await Alert.Info(owner, "Nothing to export", "There are no passages here to export yet.");
            return;
        }

        await new PassageSetExportWindow(title, passages, detailLabel).ShowDialog(owner);
    }

    private CheckBox Option(string text, bool isChecked)
    {
        var box = new CheckBox { Content = text, IsChecked = isChecked };
        box.IsCheckedChanged += (_, _) => RefreshPreview();
        return box;
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static RadioButton Format(string text, string glyph, bool isChecked)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(Glyphs.Icon(glyph, 15));
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return new RadioButton { Content = content, GroupName = "export-format", IsChecked = isChecked };
    }

    /// <summary>
    /// Looks up the counterpart edition for every passage, once, when the
    /// translations switch is turned on.
    ///
    /// Grouped by the edition each passage came from rather than resolved one
    /// at a time: every passage from the same edition shares one counterpart
    /// and one aligner, so three hundred lines of the Iliad load that
    /// translation once instead of three hundred times.
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

        _status.Text = "Looking up translations...";
        Cursor = new Cursor(StandardCursorType.Wait);

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
                // back to any other edition covers works ingested without a
                // usable Kind, where "the other one" still beats nothing.
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
            Cursor = null;
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        _preview.Text = string.Join(Environment.NewLine,
            BuildRenderChunks().Select(c => string.IsNullOrEmpty(c.Label) ? c.Text : $"{c.Label} {c.Text}"));
    }

    /// <summary>
    /// The single definition of what the output looks like, shared by the
    /// preview and all three formats so they cannot drift apart.
    ///
    /// A chunk's label renders bold and grey in .docx and .pdf, so a work
    /// heading is a label with no text rather than something the export
    /// service has to learn about.
    /// </summary>
    internal List<(string Label, string Text)> BuildRenderChunks()
    {
        var chunks = new List<(string Label, string Text)>();
        if (_passages.Count == 0) return chunks;

        var grouped = _groupByWork.IsChecked == true;
        var showSource = _showSource.IsChecked == true;
        var showCitations = _showCitations.IsChecked == true;
        var includeDetail = _includeDetail.IsChecked == true;

        // Grouping sorts by author then work so a document reads in a stable
        // order; without it the caller's own order is kept, which for a
        // concordance is itself meaningful - position in the text.
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

            if (includeDetail && !string.IsNullOrWhiteSpace(passage.Detail))
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

        var extension = _txt.IsChecked == true ? "txt" : _docx.IsChecked == true ? "docx" : "pdf";
        var type = extension switch { "txt" => Files.Text, "docx" => Files.Word, _ => Files.Pdf };

        var path = await Files.SaveAsync(this, "Export Passages", $"{_collectionTitle}.{extension}", type);
        if (path == null) return;

        try
        {
            Write(path, extension);

            if (await Alert.Confirm(this, "Done", $"Exported to {path}.\n\nOpen the containing folder?",
                    confirm: "Show in Finder", cancel: "Not Now", kind: AlertKind.Info))
            {
                MacShell.Reveal(path);
            }
        }
        catch (Exception ex) when (extension == "pdf" && !OperatingSystem.IsWindows())
        {
            // The shared PDF writer finds the font to embed through the
            // Windows registry, and off Windows it finds nothing and fails
            // without a useful message. Said plainly, with the way round it,
            // rather than as a null reference.
            await Alert.Error(this, "Error",
                "Export failed: the PDF writer could not find a font to embed. It looks fonts up the way " +
                $"Windows does, which does not work on this Mac yet. Word and plain text export work.\n\n({ex.Message})");
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Error", $"Export failed: {ex.Message}");
        }
    }

    /// <summary>Writes the set to <paramref name="path"/> in the named format, as the dialog stands.</summary>
    internal void Write(string path, string extension)
    {
        var sourceUrl = "Perseus Digital Library (via Classica Codex) - see About for full attribution and licensing.";
        var chunks = BuildRenderChunks();

        switch (extension)
        {
            case "txt": PassageExportService.ExportText(path, _collectionTitle, sourceUrl, chunks); break;
            case "docx": PassageExportService.ExportDocx(path, _collectionTitle, sourceUrl, chunks, MixedScriptFont); break;
            default: PassageExportService.ExportPdf(path, _collectionTitle, sourceUrl, chunks, MixedScriptFont); break;
        }
    }
}
