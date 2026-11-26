using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Lexicon;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.DictionaryCitations;

/// <summary>
/// One dictionary entry that cites the line, as the window shows it.
/// </summary>
/// <param name="Headword">The entry's headword, in its own script.</param>
/// <param name="Source">"LSJ" or "Lewis &amp; Short".</param>
/// <param name="Sense">The sense the citation is printed under, "A.II.2", or null.</param>
/// <param name="Before">The entry's words leading up to the citation, within the same sense: what it quotes and glosses.</param>
/// <param name="Label">The citation as printed.</param>
/// <param name="Entry">The whole entry as plain text, for the reader who wants it.</param>
internal sealed record CitingItem(string Headword, string? Source, string? Sense, string Before, string Label, string Entry)
{
    public string Heading => Sense == null ? Source ?? string.Empty : $"{Source}, sense {Sense}";
}

/// <summary>
/// Cited in the Dictionaries: the LSJ and Lewis and Short entries that cite one
/// line, each with the words it prints before the citation - the phrase it
/// quotes and the meaning it gives it - and the whole entry a click away. The
/// same entry under two spellings, and the same entry in the two copies of
/// Lewis and Short the dictionary step loads, are listed once.
/// </summary>
internal sealed class DictionaryCitationsWindow : FeatureWindow
{
    /// <summary>How much of an entry is shown before a citation.</summary>
    internal const int ContextLength = 260;

    private readonly Edition? _edition;
    private readonly IReadOnlyList<long> _citationIds;
    private readonly FontFamily _readingFont;
    private readonly ListBox _list;
    private readonly TextBlock _message;
    private readonly TextBlock _count;
    private List<CitingItem> _items = new();

    public DictionaryCitationsWindow(TextNode node, Edition? edition, IReadOnlyList<long> citationIds, FontFamily readingFont, string? attribution)
    {
        _edition = edition;
        _citationIds = citationIds;
        _readingFont = readingFont;

        Title = "Cited in the Dictionaries";
        Width = 860;
        Height = 680;
        MinWidth = 540;
        MinHeight = 420;

        _list = new ListBox { SelectionMode = SelectionMode.Single, ItemTemplate = EchoUi.Rows<CitingItem>(BuildCard) };
        EchoUi.CompactRows(_list);

        _message = new TextBlock
        {
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 420
        };

        _count = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };

        var top = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(EchoUi.SourceHeader("CITED IN THE DICTIONARIES", node, readingFont, attribution));
        top.Children.Add(new Border
        {
            Classes = { "notice" },
            Child = new TextBlock { Text = DictionaryCitationsFeature.Accuracy, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 }
        });

        var body = new Grid();
        body.Children.Add(_list);
        body.Children.Add(_message);

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_count, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(_count);
        layout.Children.Add(new Border { Classes = { "page" }, Child = body });
        Content = layout;

        Opened += (_, _) => LoadTask = LoadAsync();
    }

    /// <summary>The loading, for the tests to wait on.</summary>
    internal Task? LoadTask { get; private set; }

    internal IReadOnlyList<CitingItem> Items => _items;

    private async Task LoadAsync()
    {
        _message.Text = "Reading the entries…";
        _message.IsVisible = true;

        try
        {
            var entries = await Task.Run(() => new LexiconCitationRepository().GetCitingEntriesAsync(_citationIds));
            _items = Arrange(entries.Select(Describe), _edition?.Language).ToList();
        }
        catch (Exception ex)
        {
            _message.Text = $"Couldn't read the library: {ex.Message}";
            return;
        }

        _list.ItemsSource = _items;
        _message.IsVisible = _items.Count == 0;
        if (_items.Count == 0) _message.Text = "The entries that cited this line are no longer in the library.";

        var bySource = _items.GroupBy(i => i.Source ?? "?").Select(g => $"{g.Key} {g.Count()}");
        var entriesText = _items.Count == 1 ? "1 entry" : $"{_items.Count:N0} entries";
        _count.Text = $"{entriesText} ({string.Join(", ", bySource)}). Look up a word of the line in Word Study to follow the dictionaries' other citations.";
    }

    /// <summary>
    /// The entries in reading order: the dictionary of the line's own language
    /// first, then by headword; one of each entry and sense, since the same
    /// entry can arrive twice.
    /// </summary>
    internal static IEnumerable<CitingItem> Arrange(IEnumerable<CitingItem> items, string? language)
    {
        var first = string.Equals(language, "lat", StringComparison.OrdinalIgnoreCase) ? "Lewis & Short" : "LSJ";
        return items
            .GroupBy(i => (i.Source, Headword: WordNormalizer.Normalize(i.Headword), i.Sense, i.Label))
            .Select(g => g.First())
            .OrderBy(i => i.Source == first ? 0 : 1)
            .ThenBy(i => i.Source, StringComparer.Ordinal)
            .ThenBy(i => i.Headword, StringComparer.CurrentCulture)
            .ThenBy(i => i.Sense, StringComparer.Ordinal);
    }

    /// <summary>
    /// One citing entry: the words it prints before this citation, back to the
    /// start of the sense or about <see cref="ContextLength"/> characters,
    /// whichever comes first.
    /// </summary>
    internal static CitingItem Describe(CitingEntry entry)
    {
        var pieces = LexiconEntryText.PiecesForReading(entry.Entry, entry.Language);
        var at = -1;
        for (var i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].Citation?.Ordinal != entry.Ordinal) continue;
            at = i;
            break;
        }

        var plain = LexiconEntryText.ForReading(entry.Entry, entry.Language);
        if (at < 0) return new CitingItem(entry.Headword, entry.Source, entry.Sense, string.Empty, string.Empty, plain);

        var before = string.Concat(pieces.Take(at).Select(p => p.Text));
        var sense = before.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (sense >= 0) before = before[(sense + 2)..];
        before = before.Trim();

        if (before.Length > ContextLength)
        {
            var cut = before.Length - ContextLength;
            var space = before.IndexOf(' ', cut);
            before = "… " + before[(space >= 0 && space < cut + 30 ? space + 1 : cut)..];
        }

        return new CitingItem(entry.Headword, entry.Source, entry.Sense, before, pieces[at].Text, plain);
    }

    private Control BuildCard(CitingItem item)
    {
        var stack = new StackPanel { Spacing = 5, Margin = new Thickness(4, 6) };

        var heading = new TextBlock { TextWrapping = TextWrapping.Wrap };
        heading.Inlines!.Add(new Run(item.Headword)
        {
            FontFamily = ScriptFonts.Compose(_readingFont, ScriptFonts.FamilyFor(item.Headword)),
            FontSize = 17,
            FontWeight = FontWeight.SemiBold
        });
        var where = new Run("   " + item.Heading) { FontSize = 12 };
        where.Bind(TextElement.ForegroundProperty, heading.GetResourceObservable("Cc.InkMuted"));
        heading.Inlines.Add(where);
        stack.Children.Add(heading);

        var context = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 14 * 1.4 };
        var face = ScriptFonts.Compose((FontFamily)Application.Current!.FindResource("Cc.TranslationFont")!, ScriptFonts.FamilyFor(item.Before));
        if (item.Before.Length > 0) context.Inlines!.Add(new Run(item.Before + " ") { FontFamily = face });
        var label = new Run(item.Label) { FontSize = 12.5, FontWeight = FontWeight.SemiBold };
        label.Bind(TextElement.ForegroundProperty, context.GetResourceObservable("Cc.Accent"));
        context.Inlines!.Add(label);
        stack.Children.Add(context);

        var whole = new SelectableTextBlock
        {
            Text = item.Entry,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = face,
            FontSize = 13,
            LineHeight = 19,
            Margin = new Thickness(0, 4, 0, 0)
        };
        stack.Children.Add(new Expander
        {
            Header = new TextBlock { Text = "The whole entry", Classes = { "caption" } },
            Content = whole,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        return stack;
    }
}
