using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Help;

/// <summary>
/// Help: the topics down the left, the chosen one set as a page on the right,
/// and the way out to the issue tracker at the foot of the list.
///
/// The text is the Windows help, every topic of it, adjusted to say how the
/// Mac app does things (see <see cref="HelpTopics"/>). The search field is
/// the Mac's addition: Help on a Mac is something you type into, and with
/// twenty-seven topics the one that mentions a word is rarely the one whose
/// title does.
/// </summary>
public sealed class HelpWindow : FeatureWindow
{
    private static readonly Regex Emphasis = new(@"\*([^*\s][^*]*?)\*", RegexOptions.Compiled);

    private readonly ListBox _topicList;
    private readonly TextBox _search;
    private readonly StackPanel _page;
    private readonly ScrollViewer _scroller;
    private readonly TextBlock _noMatch;

    public HelpWindow()
    {
        Title = "Classica Codex Help";
        Width = 960;
        Height = 680;
        MinWidth = 700;
        MinHeight = 460;

        _search = new TextBox
        {
            Classes = { "search" },
            PlaceholderText = "Search Help",
            InnerLeftContent = new PathIcon { Data = Glyphs.Get("Search"), Width = 13, Height = 13, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.6 },
            Margin = new Thickness(12, 14, 12, 10)
        };
        _search.TextChanged += (_, _) => Filter();

        _topicList = new ListBox
        {
            Margin = new Thickness(6, 0),
            ItemTemplate = new FuncDataTemplate<string>((title, _) =>
            {
                var row = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 2) };
                ToolTip.SetTip(row, title);
                return row;
            }, supportsRecycling: true)
        };
        _topicList.SelectionChanged += (_, _) => ShowSelectedTopic();

        // Sidebar rows at the height a Mac sidebar sets them, not Fluent's
        // touch-sized ones.
        Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(PaddingProperty, new Thickness(10, 6)), new Setter(MinHeightProperty, 0d) }
        });

        _noMatch = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 4, 16, 0), IsVisible = false };

        // Outside the topics rather than inside one of them, because a reader
        // who has hit a bug does not know which topic would have mentioned it.
        // This is the one control in the window that is the same on every
        // topic, which is the point: it is the way out of the help window when
        // help is not what was needed.
        var report = new Button
        {
            Classes = { "link" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(16, 10, 10, 14),
            Content = ReportLinkContent()
        };
        ToolTip.SetTip(report, ProjectLinks.Issues);
        report.Click += async (_, _) => await OpenIssueTrackerAsync();

        var listColumn = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(_noMatch, Dock.Top);
        DockPanel.SetDock(report, Dock.Bottom);
        listColumn.Children.Add(_search);
        listColumn.Children.Add(_noMatch);
        listColumn.Children.Add(report);
        listColumn.Children.Add(_topicList);

        var sidebar = new Border { Child = listColumn, BorderThickness = new Thickness(0, 0, 1, 0) };
        sidebar.Bind(Border.BackgroundProperty, sidebar.GetResourceObservable("Cc.Sidebar"));
        sidebar.Bind(Border.BorderBrushProperty, sidebar.GetResourceObservable("Cc.Rule"));

        _page = new StackPanel { Margin = new Thickness(40, 30, 40, 40), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        _scroller = new ScrollViewer { Content = _page };
        var pageHost = new Border { Child = _scroller };
        pageHost.Bind(Border.BackgroundProperty, pageHost.GetResourceObservable("Cc.Page"));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("272,*") };
        Grid.SetColumn(pageHost, 1);
        grid.Children.Add(sidebar);
        grid.Children.Add(pageHost);
        Content = grid;

        Filter();
        Opened += (_, _) =>
        {
            if (_topicList.SelectedIndex < 0 && _topicList.ItemCount > 0) _topicList.SelectedIndex = 0;
        };
    }

    /// <summary>The title of the topic on the page.</summary>
    internal string? ShownTopic { get; private set; }

    internal int TopicCount => _topicList.ItemCount;

    internal void ShowTopic(string title) => _topicList.SelectedItem = title;

    internal void SetSearch(string text) => _search.Text = text;

    private static Control ReportLinkContent()
    {
        var row = new DockPanel();
        var icon = new PathIcon { Data = Glyphs.Get("Bug"), Width = 13, Height = 13, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(new TextBlock { Text = "Found a problem? Report it on GitHub", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return row;
    }

    /// <summary>
    /// Narrows the list to the topics that mention what was typed, in the
    /// title or anywhere in the text, keeping the page on its topic when that
    /// topic still qualifies.
    /// </summary>
    private void Filter()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        var current = _topicList.SelectedItem as string;

        var titles = HelpTopics.All
            .Where(t => query.Length == 0
                        || t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || HelpOutline.Expand(t.Body).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(t => t.Title)
            .ToList();

        _topicList.ItemsSource = titles;
        _noMatch.Text = $"No topic mentions “{query}”.";
        _noMatch.IsVisible = titles.Count == 0;

        if (titles.Count == 0) return;
        _topicList.SelectedItem = current != null && titles.Contains(current) ? current : titles[0];
    }

    private void ShowSelectedTopic()
    {
        if (_topicList.SelectedItem is not string title) return;
        var topic = HelpTopics.All.FirstOrDefault(t => t.Title == title);
        if (topic.Title == null) return;

        ShownTopic = title;
        _page.Children.Clear();

        var heading = Prose.Heading(topic.Title, 28, top: 0);
        heading.Margin = new Thickness(0, 0, 0, 4);
        _page.Children.Add(heading);
        _page.Children.Add(Prose.Rule(10, 18));

        var blocks = HelpOutline.Parse(HelpOutline.Expand(topic.Body.Trim()));
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var control = Render(block, first: i == 0);

            // The last item of a list leaves the room a paragraph would, so
            // the prose after it does not read as one more item.
            var next = i + 1 < blocks.Count ? blocks[i + 1] : null;
            if (block.Kind == HelpBlockKind.Item && (next == null || next.Kind != HelpBlockKind.Item || next.Level != block.Level))
                control.Margin = new Thickness(control.Margin.Left, control.Margin.Top, control.Margin.Right, 14);

            _page.Children.Add(control);
        }

        // Every topic opens at its top, as the Windows text box was put back
        // to its first character on each change of topic.
        _scroller.Offset = default;
    }

    private Control Render(HelpBlock block, bool first)
    {
        var indent = block.Level * 20;

        switch (block.Kind)
        {
            case HelpBlockKind.Heading:
                return new SelectableTextBlock
                {
                    Text = block.Text,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = block.Level == 0 ? 16 : 14.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(indent, first ? 0 : block.Level == 0 ? 20 : 12, 0, 5)
                };

            case HelpBlockKind.Item:
                return Item(block, indent);

            default:
                if (LooksLikePath(block.Text) || HelpOutline.IsFolder(block.Text)) return PathBlock(block.Text, indent);

                var paragraph = Reading(block.Text);
                paragraph.Margin = new Thickness(indent, 0, 0, block.Text.EndsWith(':') ? 7 : 12);
                return paragraph;
        }
    }

    /// <summary>
    /// A list item, with its term in bold where it has one - "Whole words
    /// only - the default…", "#  it carries at least one tag" - so a list of
    /// options reads as one.
    /// </summary>
    private Control Item(HelpBlock block, int indent)
    {
        var dot = new TextBlock { Text = "•", FontWeight = FontWeight.Bold, Width = 16, Margin = new Thickness(0, 1, 0, 0) };
        dot.Bind(TextBlock.ForegroundProperty, dot.GetResourceObservable("Cc.Accent"));

        var (term, rest) = SplitTerm(block.Text);
        var text = Reading(rest, term);

        var row = new DockPanel { Margin = new Thickness(indent + 2, 0, 0, 7) };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(text);
        return row;
    }

    internal static (string? Term, string Remainder) SplitTerm(string text)
    {
        // "?  an inquiry has been started": a mark and its meaning.
        var gap = text.IndexOf("  ", StringComparison.Ordinal);
        if (gap is > 0 and <= 3) return (text[..gap], text[gap..].TrimStart());

        // "Copy Passage - the line's text on its own".
        var dash = text.IndexOf(" - ", StringComparison.Ordinal);
        if (dash is > 0 and <= 60) return (text[..dash], text[dash..]);

        return (null, text);
    }

    /// <summary>A paragraph in the reading face, with *emphasis* set in italic.</summary>
    private static SelectableTextBlock Reading(string text, string? boldLead = null)
    {
        var paragraph = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14.5, LineHeight = 22 };
        Prose.UseFont(paragraph, "Cc.TranslationFont");

        if (boldLead != null)
        {
            paragraph.Inlines!.Add(new Run(boldLead) { FontWeight = FontWeight.SemiBold });
            if (!text.StartsWith(' ') && !text.StartsWith('-')) paragraph.Inlines.Add(new Run("  "));
        }

        var at = 0;
        foreach (Match match in Emphasis.Matches(text))
        {
            if (match.Index > at) paragraph.Inlines!.Add(new Run(text[at..match.Index]));
            paragraph.Inlines!.Add(new Run(match.Groups[1].Value) { FontStyle = FontStyle.Italic });
            at = match.Index + match.Length;
        }

        if (at < text.Length) paragraph.Inlines!.Add(new Run(text[at..]));
        return paragraph;
    }

    private static bool LooksLikePath(string text) =>
        !text.Contains(' ') && (text.StartsWith('/') || text.StartsWith("~/", StringComparison.Ordinal));

    /// <summary>A folder or a file, in the fixed-width face on a panel, to be copied exactly.</summary>
    private static Control PathBlock(string path, int indent) => new Border
    {
        Classes = { "panel" },
        Padding = new Thickness(10, 6),
        Margin = new Thickness(indent, 0, 0, 12),
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new SelectableTextBlock { Text = path, Classes = { "mono" }, TextWrapping = TextWrapping.Wrap }
    };

    /// <summary>
    /// Hands the issue tracker to whatever browser the reader uses.
    ///
    /// The failure here is shown rather than swallowed, which is not what the
    /// citation links do: a reader who cannot open a citation has lost a
    /// reference and can look it up, but this is the route for telling
    /// somebody the application is broken, and a click that does nothing at
    /// all leaves them with no way to report the very thing they came here
    /// about. The message carries the address so it can be typed in by hand.
    /// </summary>
    private async Task OpenIssueTrackerAsync()
    {
        if (ExternalLinks.Opener(ProjectLinks.Issues)) return;

        await Alert.Warn(this, "Report a problem",
            "Couldn't open a browser.\n\nThe address is:\n" + ProjectLinks.Issues);
    }
}
