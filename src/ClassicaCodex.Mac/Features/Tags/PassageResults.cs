using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.Mac.Features.Export;

namespace ClassicaCodex.Mac.Features.Tags;

/// <summary>
/// What every list of gathered passages in the tag, bookmark and network
/// windows shares - the Mac half of the Windows ListResultHelpers.
///
/// The bargain is the Windows one: a row says who and what - "Homer, Iliad"
/// and the line - and the citation waits in a tooltip, because printing it
/// in every row is the clutter the reader panes already avoid. A right-click
/// "Copy to Clipboard" then copies the whole thing, author, work, reference
/// and passage, so hiding the reference never makes it harder to get at.
/// </summary>
internal static class PassageResults
{
    /// <summary>How much of a passage a summary row shows.</summary>
    public const int RowLimit = 400;

    /// <summary>
    /// The cap for a view whose rows ARE the text being read rather than a
    /// summary of it - Compare Sources, where one source's lines are set
    /// against another's. Higher, because cutting a comparison is a real loss.
    /// </summary>
    public const int ComparisonRowLimit = 2000;

    /// <summary>
    /// As much of a passage as one row shows.
    ///
    /// On Windows this cut is a crash fix: GDI+ gives out measuring a single
    /// run of 32,000 characters with a soft hyphen in it, and this corpus has
    /// such passages - Migne and CSEL works ingested as whole sections. Skia
    /// has no such limit, so here it is only economy: a row shows two lines,
    /// and shaping thirty thousand characters to show two lines of them is
    /// work the list does for every row it realises. Nothing is lost - the
    /// untruncated text stays in the list the window holds, which is what the
    /// copy and the export read from.
    /// </summary>
    public static string RowText(string? text, int limit = RowLimit)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= limit) return text;

        // Not through a surrogate pair, which would leave half a character.
        var cut = limit;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    /// <summary>
    /// The same cut, positioned so a particular stretch of the passage stays
    /// in view. Taking the opening of a long passage is wrong wherever the
    /// row exists to show a hit: the match can sit past the cut, leaving a
    /// row with no sign of why it is in the list - and Auto-Tag asks the
    /// reader to confirm each match before it writes a tag.
    ///
    /// A little of what precedes the match is kept, so the phrase reads as a
    /// phrase, and each cut end is marked with an ellipsis.
    /// </summary>
    public static string RowTextAround(string? text, int matchStart, int matchLength, int limit = RowLimit)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= limit) return text;
        if (matchStart < 0 || matchStart >= text.Length) return RowText(text, limit);

        const int lead = 60;
        var start = Math.Max(0, matchStart - lead);

        // A match longer than the whole window would leave nothing to show;
        // the window still starts at the match so its opening is visible.
        var end = Math.Min(text.Length, start + limit);
        if (end <= matchStart) end = Math.Min(text.Length, matchStart + limit);

        if (start > 0 && char.IsLowSurrogate(text[start])) start++;
        if (end < text.Length && char.IsLowSurrogate(text[end])) end--;

        var window = text[start..end];
        return (start > 0 ? "…" : string.Empty) + window + (end < text.Length ? "…" : string.Empty);
    }

    /// <summary>The line "Copy to Clipboard" puts on the pasteboard: who, what, where, and the passage.</summary>
    public static string CopyLine(string authorName, string workTitle, string citationRef, string? milestone, string text) =>
        $"{authorName}, {workTitle} [{PassageCitation.Display(citationRef, milestone)}]: {text}";

    /// <summary>The reading face, with whatever script font the passage itself needs in front of it.</summary>
    public static FontFamily FontFor(string text)
    {
        var reading = Application.Current?.FindResource("Cc.SourceFont") as FontFamily ?? FontFamily.Default;
        return ScriptFonts.Compose(reading, ScriptFonts.FamilyFor(text));
    }

    /// <summary>
    /// One passage as a list row: the source above in the interface face, the
    /// passage below in the reading face, the citation in the tooltip. A list
    /// whose rows all come from one source passes no source, and the row is
    /// the passage alone.
    /// </summary>
    /// <param name="prefix">A short label before the source - the tag a network edge row belongs to.</param>
    /// <param name="highlight">Stretches of <paramref name="text"/> to set off: the forms a search matched.</param>
    public static Control Row(
        string? source,
        string text,
        string citationRef,
        string? milestone,
        int maxLines = 2,
        string? prefix = null,
        IReadOnlyList<(int Start, int Length)>? highlight = null)
    {
        var row = new StackPanel { Spacing = 1 };

        if (!string.IsNullOrEmpty(source) || prefix != null)
        {
            var heading = new TextBlock { Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis };
            if (prefix != null)
            {
                var tag = new Run(prefix + "  ") { FontWeight = FontWeight.SemiBold };
                tag.Bind(TextElement.ForegroundProperty, heading.GetResourceObservable("Cc.Accent"));
                heading.Inlines!.Add(tag);
            }

            heading.Inlines!.Add(new Run(source ?? string.Empty));
            row.Children.Add(heading);
        }

        var body = new TextBlock
        {
            FontFamily = FontFor(text),
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = maxLines > 0 ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            MaxLines = Math.Max(maxLines, 0),
            FlowDirection = ScriptFonts.IsRightToLeft(text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        Fill(body, text, highlight);

        row.Children.Add(body);

        var citation = PassageCitation.Bracketed(citationRef, milestone);
        if (citation.Length > 0)
        {
            ToolTip.SetTip(row, citation);
            ToolTip.SetShowDelay(row, 700);
        }

        return row;
    }

    /// <summary>Sets the text, with the highlighted stretches in the accent and a heavier weight.</summary>
    private static void Fill(TextBlock block, string text, IReadOnlyList<(int Start, int Length)>? highlight)
    {
        if (highlight == null || highlight.Count == 0)
        {
            block.Text = text;
            return;
        }

        var position = 0;
        foreach (var (start, length) in highlight)
        {
            // Spans can overlap where one searched form contains another; the
            // first to claim a stretch keeps it.
            if (start < position || start + length > text.Length) continue;

            if (start > position) block.Inlines!.Add(new Run(text[position..start]));

            var match = new Run(text.Substring(start, length)) { FontWeight = FontWeight.SemiBold };
            match.Bind(TextElement.ForegroundProperty, block.GetResourceObservable("Cc.AccentStrong"));
            match.Bind(TextElement.BackgroundProperty, block.GetResourceObservable("Cc.AccentSoft"));
            block.Inlines!.Add(match);
            position = start + length;
        }

        if (position < text.Length) block.Inlines!.Add(new Run(text[position..]));
    }

    /// <summary>A line in a list that is a message rather than a result: an empty state, a notice.</summary>
    public static TextBlock Placeholder() => new()
    {
        Classes = { "muted" },
        FontStyle = FontStyle.Italic,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(20),
        MaxWidth = 360,
        IsHitTestVisible = false,
        IsVisible = false
    };

    /// <summary>
    /// A list of results on a page, with its empty-state message laid over
    /// it. The Windows lists put "(nothing tagged with this yet)" in as a row
    /// of its own, which could then be selected, double-clicked and copied;
    /// here the message sits where the rows would be and is not one of them.
    /// </summary>
    public static (Border Page, ListBox List, TextBlock Message) Page(Func<object, Control> rowFor)
    {
        var list = new ListBox
        {
            Classes = { "results" },
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(4),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((item, _) => rowFor(item), supportsRecycling: false)
        };

        var message = Placeholder();
        var grid = new Grid();
        grid.Children.Add(list);
        grid.Children.Add(message);

        return (new Border { Classes = { "page" }, Child = grid }, list, message);
    }

    /// <summary>Shows or clears the message laid over a result list.</summary>
    public static void SetMessage(TextBlock message, string? text)
    {
        message.Text = text ?? string.Empty;
        message.IsVisible = !string.IsNullOrEmpty(text);
    }

    /// <summary>
    /// Rows a little tighter than Fluent's, and the window's own, so a page of
    /// passages reads as a list rather than a column of buttons.
    /// </summary>
    public static void AddListStyles(Window window)
    {
        window.Styles.Add(new Style(x => x.OfType<ListBox>().Class("results").Descendant().OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 6)),
                new Setter(ListBoxItem.MarginProperty, new Thickness(0, 0, 0, 1))
            }
        });
    }

    /// <summary>
    /// Gives a result list its right-click menu. The row under the pointer is
    /// selected first, as a right-click in a Mac list does, so it is plain
    /// which row the menu is about to act on.
    /// </summary>
    public static ContextMenu Menu(ListBox list, params Control[] items)
    {
        var menu = list.ContextMenu ?? new ContextMenu();
        foreach (var item in items) menu.Items.Add(item);
        list.ContextMenu = menu;

        list.ContextRequested += (_, e) =>
        {
            var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
            if (row?.DataContext is { } item && !Equals(list.SelectedItem, item)) list.SelectedItem = item;
        };

        return menu;
    }

    public static MenuItem Item(string header, string glyph, Func<Task> action)
    {
        var item = new MenuItem { Header = header, Icon = Glyphs.Icon(glyph, 14) };
        item.Click += async (_, _) => await action();
        return item;
    }

    /// <summary>"Copy to Clipboard": the full line for the selected row, reference and all.</summary>
    public static MenuItem CopyItem(ListBox list, Func<object, string?> fullTextFor) =>
        Item("Copy to Clipboard", "Copy", async () =>
        {
            if (list.SelectedItem is { } selected && fullTextFor(selected) is { Length: > 0 } text)
                await ClipboardText.SetAsync(list, text);
        });

    /// <summary>
    /// "Export All Passages…" - the whole gathered set, written as a document
    /// to quote from, which is a different thing from the table exports that
    /// sit below it. The whole set rather than the row under the pointer,
    /// which is why the item says "All"; a single passage has its own export
    /// on the reader's line menu.
    ///
    /// The Mac's passage-set export belongs to the Export feature, which does
    /// not have one yet, so this says so rather than doing nothing. It is the
    /// one place these windows reach for it.
    /// </summary>
    public static MenuItem ExportAllItem(Window owner, Func<string> title, Func<IReadOnlyList<ExportPassage>> passages) =>
        Item("Export All Passages…", "Export", async () =>
        {
            var set = passages();
            if (set.Count == 0)
            {
                await Alert.Info(owner, "Nothing to export", "There are no passages here to export yet.");
                return;
            }

            ExportFeature.ExportPassagesFrom(owner, title(), set);
        });

    /// <summary>True when a key event is the command key (Control elsewhere) with <paramref name="key"/>.</summary>
    public static bool IsCommand(KeyEventArgs e, Key key) =>
        e.Key == key && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
}
