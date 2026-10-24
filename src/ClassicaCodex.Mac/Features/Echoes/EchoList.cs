using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Features.Export;

namespace ClassicaCodex.Mac.Features.Echoes;

/// <summary>
/// One candidate echo: a passage elsewhere in the library sharing rare words
/// with the source, in the shape TextNodeRepository.FindEchoesAsync returns.
/// </summary>
internal sealed record EchoHit(
    int WorkId,
    long TextNodeId,
    string AuthorName,
    string WorkTitle,
    string CitationRef,
    string Text,
    int SharedWordCount,
    string? Milestone)
{
    public static List<EchoHit> From(
        IEnumerable<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, int SharedWordCount, string? Milestone)> rows) =>
        rows.Select(r => new EchoHit(r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text, r.SharedWordCount, r.Milestone)).ToList();

    /// <summary>The whole line - author, work, reference and passage - for the pasteboard.</summary>
    public string ClipboardText =>
        $"{AuthorName}, {WorkTitle} [{PassageCitation.Display(CitationRef, Milestone)}]: {Text}";

    public ExportPassage ToExport() =>
        new(WorkId, TextNodeId, AuthorName, WorkTitle, CitationRef, Text, Milestone: Milestone);
}

/// <summary>
/// Pieces the echo and comparison windows share: the faces they read in, the
/// block naming the source passage, the cut a list row makes, and how their
/// lists build, space and select rows.
/// </summary>
internal static class EchoUi
{
    /// <summary>
    /// What a two- or three-line header can usefully hold. Windows cuts here
    /// because a Win32 caption of 65,536 characters or more draws nothing at
    /// all; Avalonia has no such limit, but the header is there to say which
    /// passage this is, and three lines say it.
    /// </summary>
    public const int HeaderTextLimit = 300;

    /// <summary>
    /// As much of a passage as one result row shows - the Windows list's cut.
    /// The full text stays in the hit, which is what the copy, the export and
    /// the jump all read.
    /// </summary>
    public const int RowTextLimit = 400;

    public static string Cut(string? text, int limit)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= limit) return text;

        // Not through a surrogate pair, which would leave half a character.
        var cut = limit;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    public static FontFamily SourceFont => (FontFamily)Application.Current!.FindResource("Cc.SourceFont")!;

    public static FontFamily TranslationFont => (FontFamily)Application.Current!.FindResource("Cc.TranslationFont")!;

    /// <summary>
    /// The face a passage from this pane reads in: the original's or the
    /// translation's. Find Echoes only ever compares like with like, so the
    /// candidates read in the same face as the line they were found for.
    /// </summary>
    public static FontFamily ReadingFontFor(Edition? edition, bool fromOriginal)
    {
        var original = edition is { } e ? e.Kind == EditionKind.Original : fromOriginal;
        return original ? SourceFont : TranslationFont;
    }

    /// <summary>A text block for passage text, in the script's own font and direction.</summary>
    public static TextBlock PassageText(string text, FontFamily readingFont, double size = 15, int maxLines = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = ScriptFonts.Compose(readingFont, ScriptFonts.FamilyFor(text)),
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = size * 1.34,
            FlowDirection = ScriptFonts.IsRightToLeft(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };

        if (maxLines > 0)
        {
            block.MaxLines = maxLines;
            block.TextTrimming = TextTrimming.WordEllipsis;
        }

        return block;
    }

    /// <summary>
    /// The block at the top of an echo window that names the passage being
    /// looked for: a small heading, its reference, and the opening of its text.
    /// </summary>
    public static Control SourceHeader(string heading, TextNode node, FontFamily readingFont, string? attribution = null)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = heading, Classes = { "section" } });

        var reference = PassageCitation.Bracketed(node.CitationRef, node.Milestone);
        var line = new TextBlock { Classes = { "h3" }, TextWrapping = TextWrapping.Wrap };
        line.Text = attribution == null ? reference : $"{attribution} {reference}".Trim();
        if (line.Text.Length > 0) stack.Children.Add(line);

        stack.Children.Add(PassageText(Cut(node.Text, HeaderTextLimit), readingFont, 15.5, maxLines: 3));

        return new Border { Classes = { "panel" }, Padding = new Thickness(14, 10), Child = stack };
    }

    /// <summary>
    /// An item template that draws nothing for a null item. Avalonia builds a
    /// list's template once more, with null, when it clears a container for
    /// reuse - after the items are replaced, or as rows scroll out of a long
    /// list - and a template that reached into the item there would throw and
    /// take the window with it.
    /// </summary>
    public static FuncDataTemplate<T> Rows<T>(Func<T, Control> build) where T : class =>
        new((item, _) => item is null ? new Panel() : build(item), supportsRecycling: false);

    /// <summary>
    /// Closer rows than Fluent's own item padding, which is sized for a
    /// one-line list of labels rather than a list of passages that already
    /// carry their own spacing.
    /// </summary>
    public static void CompactRows(ListBox list) =>
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 5)) }
        });

    /// <summary>A muted line saying what a result is and is not.</summary>
    public static TextBlock Explainer(string text) =>
        new() { Text = text, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };

    /// <summary>
    /// Selects the row under the pointer before a context menu opens, as a
    /// right-click on a Mac list does - otherwise the menu would act on
    /// whatever was last clicked rather than on what was right-clicked.
    /// </summary>
    public static void SelectRowOnContextRequest(ListBox list)
    {
        list.ContextRequested += (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item
                && list.IndexFromContainer(item) is var index and >= 0)
            {
                list.SelectedIndex = index;
            }
        };
    }

    /// <summary>True when a double-click landed on a row rather than on the scroll bar or the margin.</summary>
    public static bool OnARow(object? eventSource) =>
        (eventSource as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null;

    public static MenuItem MenuItem(string header, string glyph, Func<Task> action, string? gesture = null)
    {
        var item = new MenuItem { Header = header, Icon = Glyphs.Icon(glyph, 14) };
        if (gesture != null) item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += async (_, _) => await action();
        return item;
    }

    /// <summary>⌘ on a Mac, Ctrl elsewhere - the modifier a shortcut is shown and matched with.</summary>
    public static string CommandKey => OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";

    public static bool IsCommand(KeyEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
}

/// <summary>
/// A list of candidate echoes, as Find Echoes and the three columns of the
/// Reception Tracker show them: how many rare words each shares, whose it is,
/// and the opening of the passage.
///
/// The reference is not printed on the row. The Windows lists used to print
/// it inline and dropped it as clutter - the reader panes already keep it out
/// of the way - so it shows on hover instead, and goes with every copy and
/// export, since hiding it should not make it harder to get at.
///
/// A message ("Searching…", "none found") is drawn over the list rather than
/// put in it, so a message is structurally incapable of being copied,
/// exported or jumped to as if it were a passage.
/// </summary>
internal sealed class EchoList : UserControl
{
    private readonly ListBox _list;
    private readonly TextBlock _message;
    private readonly FontFamily _readingFont;
    private readonly Func<string> _exportTitle;
    private readonly MenuItem _copyItem;
    private IReadOnlyList<EchoHit> _hits = Array.Empty<EchoHit>();

    public EchoList(FontFamily readingFont, Func<string> exportTitle)
    {
        _readingFont = readingFont;
        _exportTitle = exportTitle;

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(6),
            ItemTemplate = EchoUi.Rows<EchoHit>(BuildRow)
        };
        EchoUi.CompactRows(_list);

        _message = new TextBlock
        {
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            MaxWidth = 380,
            IsVisible = false
        };

        _copyItem = EchoUi.MenuItem("Copy to Clipboard", "Copy", CopySelectedAsync, $"{EchoUi.CommandKey}+C");
        var menu = new ContextMenu();
        menu.Items.Add(_copyItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(EchoUi.MenuItem("Export All Passages…", "Export", ExportAllAsync));
        menu.Opening += (_, _) => _copyItem.IsEnabled = Selected != null;
        _list.ContextMenu = menu;
        EchoUi.SelectRowOnContextRequest(_list);

        _list.DoubleTapped += async (_, e) =>
        {
            if (EchoUi.OnARow(e.Source)) await ActivateSelectedAsync();
        };
        _list.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ActivateSelectedAsync();
            }
            else if (e.Key == Key.C && EchoUi.IsCommand(e))
            {
                e.Handled = true;
                await CopySelectedAsync();
            }
        };

        var grid = new Grid();
        grid.Children.Add(_list);
        grid.Children.Add(_message);
        Content = grid;
    }

    /// <summary>Called when a row is double-clicked or Return is pressed on it.</summary>
    public Func<EchoHit, Task>? Activated { get; set; }

    /// <summary>The candidates the list holds - never a message row.</summary>
    public IReadOnlyList<EchoHit> Hits => _hits;

    public EchoHit? Selected => _list.SelectedItem as EchoHit;

    public ListBox List => _list;

    /// <summary>The message the list is showing in place of rows, or null when it shows rows.</summary>
    public string? Message => _message.IsVisible ? _message.Text : null;

    public void ShowMessage(string text)
    {
        _hits = Array.Empty<EchoHit>();
        _list.ItemsSource = null;
        _message.Text = text;
        _message.IsVisible = true;
    }

    public void Show(IReadOnlyList<EchoHit> hits, string emptyMessage)
    {
        if (hits.Count == 0)
        {
            ShowMessage(emptyMessage);
            return;
        }

        _hits = hits;
        _message.IsVisible = false;
        _list.ItemsSource = hits;
    }

    private async Task ActivateSelectedAsync()
    {
        if (Selected is { } hit && Activated != null) await Activated(hit);
    }

    private async Task CopySelectedAsync()
    {
        if (Selected is { } hit) await ClipboardText.SetAsync(this, hit.ClipboardText);
    }

    private Task ExportAllAsync()
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
            ExportFeature.ExportPassagesFrom(owner, _exportTitle(), _hits.Select(h => h.ToExport()).ToList());
        return Task.CompletedTask;
    }

    private Control BuildRow(EchoHit hit)
    {
        var count = new TextBlock
        {
            Text = hit.SharedWordCount.ToString("N0"),
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        count.Bind(TextBlock.ForegroundProperty, count.GetResourceObservable("Cc.Accent"));

        var badgeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        badgeStack.Children.Add(count);
        badgeStack.Children.Add(new TextBlock { Text = "shared", Classes = { "caption" }, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center });

        var badge = new Border
        {
            Classes = { "notice" },
            Padding = new Thickness(4, 5),
            Width = 56,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 12, 0),
            Child = badgeStack
        };
        ToolTip.SetTip(badge, hit.SharedWordCount == 1
            ? "One of this passage's rarest words is in it"
            : $"{hit.SharedWordCount} of this passage's rarest words are in it");

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = $"{hit.AuthorName}, {hit.WorkTitle}",
            FontWeight = FontWeight.SemiBold,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(EchoUi.PassageText(EchoUi.Cut(hit.Text, EchoUi.RowTextLimit), _readingFont, 14.5, maxLines: 3));

        var row = new DockPanel { Margin = new Thickness(2, 4) };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(text);

        // The reference on hover, as the Windows lists give it.
        ToolTip.SetTip(text, PassageCitation.Bracketed(hit.CitationRef, hit.Milestone));
        ToolTip.SetShowDelay(text, 500);
        return row;
    }
}
