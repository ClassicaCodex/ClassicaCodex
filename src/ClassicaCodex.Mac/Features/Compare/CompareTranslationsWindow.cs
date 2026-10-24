using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.Compare;

/// <summary>
/// Two or more translations of the SAME work side by side - three English
/// renderings of Agamemnon, say - rather than different authors sharing a
/// tag. Each column is one translation's full running text, scrolling on its
/// own; there is no "nothing to show" case, since an ingested edition always
/// has text. The Windows CompareTranslationsForm.
///
/// Most of the Windows form is owner-drawing: measuring and painting wrapped
/// rows, and putting every row back after a resize because a ListBox measures
/// a row once. Avalonia wraps and re-measures a row by itself, so all of that
/// is gone and what is left is the comparison.
/// </summary>
internal sealed class CompareTranslationsWindow : FeatureWindow
{
    /// <summary>
    /// Below this a column is too narrow to read a line of prose in - a dozen
    /// words at most, wrapped to nothing.
    /// </summary>
    private const double MinimumColumnWidth = 240;

    /// <summary>
    /// The face an exported translation is set in. Windows uses Palatino
    /// Linotype; macOS ships the same face as Palatino.
    /// </summary>
    private const string ExportFont = "Palatino";

    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly int? _preferredWorkId;

    private readonly ListBox _workList;
    private readonly TextBlock _workMessage;
    private readonly StackPanel _translationChecks;
    private readonly Button _compareButton;
    private readonly ScrollViewer _columnsScroller;
    private readonly Grid _columnsGrid;
    private readonly TextBlock _placeholder;

    private List<(int WorkId, string AuthorName, string WorkTitle)> _works = new();
    private readonly List<(CheckBox Box, Edition Translation)> _checks = new();
    private readonly List<CompareColumn> _columns = new();
    private int _loadGeneration;

    /// <param name="preferredWorkId">
    /// The work open in the reader. If it is one with two or more
    /// translations it is chosen to begin with, since it is almost always the
    /// one the reader means.
    /// </param>
    public CompareTranslationsWindow(int? preferredWorkId)
    {
        _preferredWorkId = preferredWorkId;

        Title = "Compare Translations";
        Width = 1200;
        Height = 780;
        MinWidth = 760;
        MinHeight = 480;

        _workList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(4),
            ItemTemplate = EchoUi.Rows<WorkChoice>(w => new TextBlock
            {
                Text = w.Label,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 1)
            })
        };
        _workList.SelectionChanged += async (_, _) => await LoadTranslationsAsync();

        _workMessage = new TextBlock
        {
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14),
            IsVisible = false
        };
        var workGrid = new Grid();
        workGrid.Children.Add(_workList);
        workGrid.Children.Add(_workMessage);

        _translationChecks = new StackPanel { Spacing = 4, Margin = new Thickness(10, 8) };

        _compareButton = new Button
        {
            Content = "Compare Selected",
            Classes = { "accent" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        ToolTip.SetTip(_compareButton, $"Set the ticked translations side by side ({(OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+")}Return)");
        _compareButton.Click += async (_, _) => await RenderColumnsAsync();

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,3*,Auto,2*,Auto"), Width = 280 };
        AddRow(left, new TextBlock { Text = "WORKS WITH 2+ TRANSLATIONS", Classes = { "section" }, Margin = new Thickness(2, 0, 0, 6) }, 0);
        AddRow(left, new Border { Classes = { "page" }, Child = workGrid }, 1);
        AddRow(left, new TextBlock { Text = "TRANSLATIONS TO COMPARE", Classes = { "section" }, Margin = new Thickness(2, 16, 0, 6) }, 2);
        AddRow(left, new Border { Classes = { "panel" }, Child = new ScrollViewer { Content = _translationChecks } }, 3);
        AddRow(left, _compareButton, 4);

        _columnsGrid = new Grid();
        _columnsScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // The columns scroll themselves, each on its own; this one only
            // ever scrolls sideways, when there are more of them than fit.
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _columnsGrid
        };
        _columnsScroller.SizeChanged += (_, _) => FitColumns();

        _placeholder = new TextBlock
        {
            Text = "Choose a work, tick the translations to set side by side, and press Compare Selected.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var right = new Grid();
        right.Children.Add(_columnsScroller);
        right.Children.Add(_placeholder);
        var page = new Border { Classes = { "page" }, Child = right, Margin = new Thickness(16, 0, 0, 0) };

        var layout = new DockPanel { Margin = new Thickness(18, 16) };
        DockPanel.SetDock(left, Dock.Left);
        layout.Children.Add(left);
        layout.Children.Add(page);
        Content = layout;

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && EchoUi.IsCommand(e))
            {
                e.Handled = true;
                await RenderColumnsAsync();
            }
        };

        Opened += (_, _) => LoadTask = LoadWorksAsync();
    }

    internal Task? LoadTask { get; private set; }

    internal ListBox WorkList => _workList;

    internal IReadOnlyList<CheckBox> TranslationChecks => _checks.Select(c => c.Box).ToList();

    internal IReadOnlyList<CompareColumn> Columns => _columns;

    internal Task CompareAsync() => RenderColumnsAsync();

    /// <summary>The translations of the chosen work have been read and their boxes drawn.</summary>
    internal Task? TranslationsTask { get; private set; }

    private static void AddRow(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    private async Task LoadWorksAsync()
    {
        try
        {
            _works = await _editionRepo.GetWorksWithMultipleTranslationsAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't read the library", ex);
            return;
        }

        if (_works.Count == 0)
        {
            _workMessage.Text = "(no work has 2+ translations ingested yet)";
            _workMessage.IsVisible = true;
            _workList.ItemsSource = null;
            return;
        }

        var choices = _works.Select(w => new WorkChoice(w.WorkId, w.AuthorName, w.WorkTitle)).ToList();
        _workList.ItemsSource = choices;

        if (_preferredWorkId is { } preferred && choices.FirstOrDefault(c => c.WorkId == preferred) is { } open)
        {
            _workList.SelectedItem = open;
            _workList.ScrollIntoView(open);
        }
    }

    private Task LoadTranslationsAsync() => TranslationsTask = LoadTranslationsCoreAsync();

    private async Task LoadTranslationsCoreAsync()
    {
        var generation = ++_loadGeneration;
        _translationChecks.Children.Clear();
        _checks.Clear();

        if (_workList.SelectedItem is not WorkChoice work) return;

        List<Edition> editions;
        try
        {
            editions = await _editionRepo.GetByWorkAsync(work.WorkId);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't read the translations", ex);
            return;
        }

        // A later choice has overtaken this one while it was reading.
        if (generation != _loadGeneration) return;

        // Ticked by default: with two or three translations of a work the
        // common case is comparing all of them, not picking a subset - unlike
        // Compare Sources, where a tag can match a dozen authors and an
        // explicit choice matters more.
        foreach (var translation in editions.Where(e => e.Kind == EditionKind.Translation))
        {
            var box = new CheckBox { Content = new TextBlock { Text = TranslatorLabel(translation), TextWrapping = TextWrapping.Wrap }, IsChecked = true };
            _checks.Add((box, translation));
            _translationChecks.Children.Add(box);
        }
    }

    /// <summary>
    /// The heading for a translation's column. The translator where the
    /// source metadata names one; otherwise the last segment of its CTS
    /// identifier, as the reader's own edition picker falls back to - not a
    /// proper name, but at least it tells two apart.
    /// </summary>
    internal static string TranslatorLabel(Edition edition)
    {
        var generated = GeneratedAt(edition);

        if (!string.IsNullOrWhiteSpace(edition.Translator))
        {
            // Two AI renderings of one work are both "A.I." and otherwise
            // indistinguishable in a heading, which is no use when the point
            // of the screen is telling renderings apart.
            return generated == null
                ? edition.Translator
                : $"{edition.Translator} - {generated.Value.ToLocalTime():d MMM yyyy, HH:mm}";
        }

        var suffix = edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(suffix) ? "Untitled translation" : suffix;
    }

    /// <summary>
    /// When an AI translation was made, read back out of its CTS identifier.
    /// Create Translation mints those as "...ai-gemini-20260808193042", so the
    /// moment is already recorded and needs no column of its own. Anything
    /// else returns null and is labelled as before.
    /// </summary>
    internal static DateTime? GeneratedAt(Edition edition)
    {
        const string marker = "ai-gemini-";

        var at = edition.CtsUrn.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        var stamp = edition.CtsUrn[(at + marker.Length)..];

        return DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private async Task RenderColumnsAsync()
    {
        var chosen = _checks.Where(c => c.Box.IsChecked == true).Select(c => c.Translation).ToList();

        if (chosen.Count < 2)
        {
            await Alert.Info(this, "Nothing to compare", "Check at least two translations first.");
            return;
        }

        if (!_compareButton.IsEnabled) return;
        _compareButton.IsEnabled = false;

        try
        {
            _columns.Clear();
            _columnsGrid.Children.Clear();
            _columnsGrid.ColumnDefinitions.Clear();
            _placeholder.IsVisible = false;

            var work = _workList.SelectedItem as WorkChoice;

            for (var i = 0; i < chosen.Count; i++)
            {
                var column = new CompareColumn(chosen[i], TranslatorLabel(chosen[i]), work);
                column.List.ContextMenu = BuildColumnMenu(column);
                EchoUi.SelectRowOnContextRequest(column.List);
                column.List.DoubleTapped += async (_, e) =>
                {
                    if (EchoUi.OnARow(e.Source) && column.List.SelectedItem is TextNode node && work != null)
                        await NavigateAsync(work.WorkId, node.TextNodeId);
                };

                _columnsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                column.View.BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0);
                Grid.SetColumn(column.View, i);
                _columnsGrid.Children.Add(column.View);
                _columns.Add(column);
            }

            FitColumns();

            // Each column's text read on its own and at the same time; the
            // headings above are already showing while they load.
            await Task.WhenAll(_columns.Select(async column =>
            {
                var editionId = column.Translation.EditionId;
                var nodes = await Task.Run(() => _textNodeRepo.GetByEditionAsync(editionId));
                column.Show(nodes);
            }));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't load the translations", ex);
        }
        finally
        {
            _compareButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Shares the width equally until there is not enough to share. Thucydides
    /// carries twelve translations in a full library; an equal share of this
    /// window would give each about a hundred pixels, a word and a half of
    /// every line. Past that point the columns keep a readable width and the
    /// page scrolls sideways instead of shrinking them all into uselessness.
    /// Worked out again whenever the window changes size, which on Windows
    /// waited for the drag to end.
    /// </summary>
    private void FitColumns()
    {
        var count = _columnsGrid.ColumnDefinitions.Count;
        if (count == 0) return;

        var available = _columnsScroller.Bounds.Width;
        var scrolls = available > 0 && available / count < MinimumColumnWidth;

        foreach (var definition in _columnsGrid.ColumnDefinitions)
            definition.Width = scrolls ? new GridLength(MinimumColumnWidth) : GridLength.Star;

        _columnsGrid.Width = scrolls ? MinimumColumnWidth * count : double.NaN;

        // Sideways scrolling off whenever the columns fit. A viewer that may
        // scroll sideways measures its content as infinitely wide, and star
        // columns given infinite width stop sharing it and stop wrapping.
        _columnsScroller.HorizontalScrollBarVisibility = scrolls ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private ContextMenu BuildColumnMenu(CompareColumn column)
    {
        var menu = new ContextMenu();
        var copy = EchoUi.MenuItem("Copy to Clipboard", "Copy", async () =>
        {
            if (column.List.SelectedItem is TextNode node)
            {
                await ClipboardText.SetAsync(this,
                    $"{column.Header} [{PassageCitation.Display(node.CitationRef, node.Milestone)}]: {node.Text}");
            }
        }, $"{EchoUi.CommandKey}+C");
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());
        menu.Items.Add(EchoUi.MenuItem("Export This Translation…", "Export", () => ExportTranslationAsync(column)));
        menu.Opening += (_, _) => copy.IsEnabled = column.List.SelectedItem is TextNode;

        column.List.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.C && EchoUi.IsCommand(e) && column.List.SelectedItem is TextNode node)
            {
                e.Handled = true;
                await ClipboardText.SetAsync(this,
                    $"{column.Header} [{PassageCitation.Display(node.CitationRef, node.Milestone)}]: {node.Text}");
            }
        };

        return menu;
    }

    /// <summary>
    /// The whole column written out, not the one passage Copy takes - each
    /// passage labelled with its citation reference, so the export can be read
    /// back against the original.
    /// </summary>
    private async Task ExportTranslationAsync(CompareColumn column)
    {
        var title = column.Work != null ? $"{column.Work.AuthorName}, {column.Work.WorkTitle} - {column.Header}" : column.Header;

        var path = await Files.SaveAsync(this, "Export Translation", SafeFileName(title) + ".txt", Files.Text, Files.Word, Files.Pdf);
        if (path == null) return;

        var chunks = column.Nodes.Select(n => (Label: n.CitationRef, n.Text)).ToList();
        const string sourceUrl = "Perseus Digital Library (via Classica Codex) - see About for full attribution and licensing.";
        var extension = Path.GetExtension(path).ToLowerInvariant();

        try
        {
            switch (extension)
            {
                case ".docx": PassageExportService.ExportDocx(path, title, sourceUrl, chunks, ExportFont); break;
                case ".pdf":
                    Export.PdfFonts.EnsureInstalled();
                    PassageExportService.ExportPdf(path, title, sourceUrl, chunks, Export.PdfFonts.FamilyFor(ExportFont));
                    break;
                default: PassageExportService.ExportText(path, title, sourceUrl, chunks); break;
            }
        }
        catch (Exception ex)
        {
            await Alert.Warn(this, "Export failed", $"Export failed: {ex.Message}");
        }
    }

    private static string SafeFileName(string title)
    {
        var cleaned = new string(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());
        return cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }

    internal sealed record WorkChoice(int WorkId, string AuthorName, string WorkTitle)
    {
        public string Label => $"{AuthorName}, {WorkTitle}";

        public override string ToString() => Label;
    }
}

/// <summary>One translation's column: its heading and its running text.</summary>
internal sealed class CompareColumn
{
    private readonly TextBlock _count;
    private readonly TextBlock _loading;

    public CompareColumn(Edition translation, string header, CompareTranslationsWindow.WorkChoice? work)
    {
        Translation = translation;
        Header = header;
        Work = work;

        var heading = new TextBlock
        {
            Text = header,
            Classes = { "h3" },
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _count = new TextBlock { Classes = { "caption" }, HorizontalAlignment = HorizontalAlignment.Center };

        var headStack = new StackPanel { Spacing = 1 };
        headStack.Children.Add(heading);
        headStack.Children.Add(_count);

        var head = new Border { Padding = new Thickness(10, 8), BorderThickness = new Thickness(0, 0, 0, 1), Child = headStack };
        head.Bind(Border.BackgroundProperty, head.GetResourceObservable("Cc.PageHeader"));
        head.Bind(Border.BorderBrushProperty, head.GetResourceObservable("Cc.Rule"));

        var font = EchoUi.TranslationFont;
        List = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(4, 6),
            ItemTemplate = EchoUi.Rows<TextNode>(node => BuildRow(node, font))
        };
        EchoUi.CompactRows(List);

        _loading = new TextBlock
        {
            Text = "Reading…",
            Classes = { "muted" },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var body = new Grid();
        body.Children.Add(List);
        body.Children.Add(_loading);

        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(body);

        View = new Border { Child = dock };
        View.Bind(Border.BorderBrushProperty, View.GetResourceObservable("Cc.Rule"));
    }

    public Edition Translation { get; }

    public string Header { get; }

    public CompareTranslationsWindow.WorkChoice? Work { get; }

    public ListBox List { get; }

    public Border View { get; }

    public IReadOnlyList<TextNode> Nodes { get; private set; } = Array.Empty<TextNode>();

    public void Show(IReadOnlyList<TextNode> nodes)
    {
        Nodes = nodes;
        List.ItemsSource = nodes;
        _loading.IsVisible = false;
        _count.Text = $"{nodes.Count:N0} passages";
    }

    /// <summary>
    /// A passage, wrapped to the column. The whole of it: the Windows column
    /// cut a row at 2,000 characters, which was about GDI+ and not about
    /// reading, and a comparison loses something real when it is cut.
    /// </summary>
    private static Control BuildRow(TextNode node, FontFamily font)
    {
        var text = EchoUi.PassageText(node.Text, font, 14.5);
        ToolTip.SetTip(text, PassageCitation.Bracketed(node.CitationRef, node.Milestone));
        ToolTip.SetShowDelay(text, 700);
        return new Border { Padding = new Thickness(8, 3), Child = text };
    }
}
