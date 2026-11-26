using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Core.Quotations;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.QuotedBy;

/// <summary>
/// One later passage that quotes the line, as the window shows it.
/// </summary>
/// <param name="Quoting">The quoting passage, in this library.</param>
/// <param name="Links">Every link from it to the line - one, or one from each search when both found it.</param>
/// <param name="Era">The quoting author's dates, where the timeline's table knows them.</param>
/// <param name="StartYear">The first of those years, to put the quoters in order.</param>
/// <param name="Wording">The quoter's words against the edition's, or null when the quoted words could not be placed.</param>
/// <param name="Before">The quoting passage's words just before the quotation.</param>
/// <param name="After">Its words just after.</param>
/// <param name="Note">Said under the comparison when the texts are not the ones the link was found in.</param>
internal sealed record QuoterItem(
    QuotationPassage Quoting, IReadOnlyList<QuotationLink> Links, string? Era, int? StartYear,
    WordingComparison? Wording, string Before, string After, string? Note)
{
    public string Heading =>
        $"{Quoting.Edition.AuthorName}, {Quoting.Edition.WorkTitle} {PassageCitation.Display(Quoting.CitationRef, Quoting.Milestone)}".Trim();
}

/// <summary>
/// Quoted By: the later passages that quote one line, earliest author first,
/// each with the quoter's wording set against the edition's and the words
/// that differ marked, and a jump to the quoting passage that Back returns
/// from.
///
/// Every quoter says which search found it and how strong the match was,
/// and the window opens on how often those searches were right. These are
/// leads for a reader to check, in the way a critical edition's testimonia
/// are not.
/// </summary>
internal sealed class QuotedByWindow : FeatureWindow
{
    private readonly TextNode _node;
    private readonly Edition _edition;
    private readonly IReadOnlyList<QuotationLink> _links;
    private readonly FontFamily _readingFont;
    private readonly ListBox _list;
    private readonly TextBlock _message;
    private readonly TextBlock _count;
    private List<QuoterItem> _items = new();

    public QuotedByWindow(TextNode node, Edition edition, IReadOnlyList<QuotationLink> links, FontFamily readingFont, string? attribution)
    {
        _node = node;
        _edition = edition;
        _links = links;
        _readingFont = readingFont;

        Title = "Quoted By";
        Width = 900;
        Height = 700;
        MinWidth = 560;
        MinHeight = 440;

        _list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = EchoUi.Rows<QuoterItem>(BuildCard)
        };
        EchoUi.CompactRows(_list);
        _list.DoubleTapped += async (_, e) =>
        {
            if (EchoUi.OnARow(e.Source) && _list.SelectedItem is QuoterItem item) await GoToAsync(item);
        };

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
        top.Children.Add(EchoUi.SourceHeader("QUOTED BY", node, readingFont, attribution));
        top.Children.Add(new Border
        {
            Classes = { "notice" },
            Child = new TextBlock { Text = QuotedByFeature.Accuracy, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 }
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

    internal IReadOnlyList<QuoterItem> Items => _items;

    /// <summary>Puts the reader on the quoting passage; the window stays open for the next one.</summary>
    internal Task GoToAsync(QuoterItem item) => NavigateAsync(item.Quoting.Edition.WorkId, item.Quoting.TextNodeId);

    private async Task LoadAsync()
    {
        _message.Text = "Finding the quoting passages…";
        _message.IsVisible = true;

        int missing;
        try
        {
            (_items, missing) = await BuildAsync();
        }
        catch (Exception ex)
        {
            _message.Text = $"Couldn't read the library: {ex.Message}";
            return;
        }

        _list.ItemsSource = _items;
        _message.IsVisible = _items.Count == 0;
        if (_items.Count == 0) _message.Text = "None of the passages that quote this line are in this library.";

        var passages = _items.Count == 1 ? "1 quoting passage" : $"{_items.Count:N0} quoting passages";
        var absent = missing == 0 ? string.Empty
            : missing == 1 ? " One more is in an edition this library does not hold."
            : $" {missing:N0} more are in editions this library does not hold.";
        _count.Text = $"{passages}, earliest author first; undated authors last.{absent} Double-click one, or use Go to Passage; Back returns here.";
    }

    /// <summary>
    /// Finds each quoting passage in the library and lines its words up with
    /// the edition's. Links from one passage to this line - one from each
    /// search, when both found it - make one quoter.
    /// </summary>
    private async Task<(List<QuoterItem> Items, int Missing)> BuildAsync()
    {
        var repository = new QuotationRepository();
        var groups = new Dictionary<long, (QuotationPassage Passage, List<QuotationLink> Links)>();
        var missing = new HashSet<PassageKey>();

        // Testimonia's first: its positions come from exact four-word runs,
        // so where both searches found a passage its span is the tighter one.
        foreach (var link in _links.OrderBy(l => l.Finder))
        {
            var passage = await repository.GetPassageAsync(link.Quoting, link.QuotingFingerprint);
            if (passage == null)
            {
                missing.Add(link.Quoting);
                continue;
            }

            if (groups.TryGetValue(passage.TextNodeId, out var group)) group.Links.Add(link);
            else groups[passage.TextNodeId] = (passage, new List<QuotationLink> { link });
        }

        var sourceText = _node.Text;
        var sourceMatches = TextFingerprint.Of(sourceText);
        var language = _edition.Language;

        var items = await Task.Run(() => groups.Values
            .Select(g => Describe(g.Passage, g.Links, sourceText, sourceMatches, language))
            .OrderBy(i => i.StartYear ?? int.MaxValue)
            .ThenBy(i => i.Quoting.Edition.AuthorName, StringComparer.CurrentCulture)
            .ThenBy(i => i.Quoting.Edition.WorkTitle, StringComparer.CurrentCulture)
            .ThenBy(i => i.Quoting.TextNodeId)
            .ToList());

        return (items, missing.Count);
    }

    /// <summary>
    /// One quoter: where the quotation is in each text, and the two wordings
    /// compared. Positions are trusted only for the text they were found in;
    /// where a reader's copy differs, the words are found again from the
    /// other side's, and where that fails, the window says so rather than
    /// marking words it cannot vouch for.
    /// </summary>
    internal static QuoterItem Describe(
        QuotationPassage quoting, IReadOnlyList<QuotationLink> links, string sourceText, string sourceFingerprint, string? language)
    {
        var link = links[0];
        var quotingText = quoting.Text;

        (int Start, int End)? q = quoting.FingerprintMatches && Fits(link.QuotingStart, link.QuotingEnd, quotingText)
            ? (link.QuotingStart, link.QuotingEnd) : null;
        (int Start, int End)? s = link.SourceFingerprint == sourceFingerprint && Fits(link.SourceStart, link.SourceEnd, sourceText)
            ? (link.SourceStart, link.SourceEnd) : null;
        var placedAgain = q == null || s == null;

        if (q == null && s != null) q = QuotationWording.Locate(quotingText, sourceText[s.Value.Start..s.Value.End], language);
        if (s == null && q != null)
            s = QuotationWording.Locate(sourceText, quotingText[q.Value.Start..q.Value.End], language) ?? (0, sourceText.Length);
        if (q == null && s == null)
        {
            q = QuotationWording.Locate(quotingText, sourceText, language);
            s = (0, sourceText.Length);
        }

        var era = MainWindow.FormatAuthorEra(quoting.Edition.AuthorName);
        var startYear = AuthorEraData.Lookup(quoting.Edition.AuthorName)?.StartYear;

        if (q is not { } quoted)
        {
            return new QuoterItem(quoting, links, era, startYear, null, EchoUi.Cut(quotingText, EchoUi.RowTextLimit), string.Empty,
                "This passage reads differently in this library from the copy the link was found in, and the quoted words could not be found in it.");
        }

        var (sourceStart, sourceEnd) = s!.Value;
        var wording = QuotationWording.Compare(
            quotingText[quoted.Start..quoted.End], sourceText[sourceStart..sourceEnd], language);
        var (before, after) = QuotationWording.Context(quotingText, quoted.Start, quoted.End);

        return new QuoterItem(quoting, links, era, startYear, wording, before, after,
            placedAgain ? "One of these passages reads differently in this library from the copy the link was found in, so the quoted words were found again by matching them." : null);
    }

    private static bool Fits(int start, int end, string text) => start >= 0 && end <= text.Length && start < end;

    /// <summary>What found a link, said plainly, with its strength.</summary>
    internal static string Provenance(QuotationLink link)
    {
        if (link.Finder == QuotationFinder.Testimonia)
        {
            var runs = link.Score == 1 ? "one four-word run" : $"{link.Score.ToString("0", CultureInfo.CurrentCulture)} four-word runs";
            var where = link.Kind == "fragment" ? " This line is in a modern collection of fragments." : string.Empty;
            return $"Testimonia: the author is named beside a citing word, and the passage shares {runs} with this line.{where}";
        }

        var path = link.Kind switch
        {
            "both" => "both its searches",
            "main" => "its shared-rare-words search",
            "short" => "its word-run search",
            _ => "its search"
        };
        var flag = link.Flag switch
        {
            "cento" => " A cento: a poem made out of Virgil's lines, which is a genre rather than citation.",
            "early-modern" => " Early-modern Latin that Migne prints under an ancient name.",
            _ => string.Empty
        };
        return $"Afterlives: rated high, score {link.Score.ToString("0.#", CultureInfo.CurrentCulture)}, found by {path}.{flag}";
    }

    private Control BuildCard(QuoterItem item)
    {
        var stack = new StackPanel { Spacing = 5, Margin = new Thickness(4, 6) };

        var go = new Button { Content = "Go to Passage", Classes = { "link" }, VerticalAlignment = VerticalAlignment.Top };
        go.Click += async (_, _) => await GoToAsync(item);

        var heading = new TextBlock { Text = item.Heading, Classes = { "h3" }, TextWrapping = TextWrapping.Wrap };
        var head = new DockPanel();
        DockPanel.SetDock(go, Dock.Right);
        head.Children.Add(go);
        head.Children.Add(heading);
        stack.Children.Add(head);

        if (item.Era != null) stack.Children.Add(new TextBlock { Text = item.Era, Classes = { "caption" } });
        foreach (var link in item.Links)
            stack.Children.Add(new TextBlock { Text = Provenance(link), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });

        if (item.Wording is { } wording)
        {
            stack.Children.Add(Label("THE QUOTER'S WORDS"));
            stack.Children.Add(Wording(item.Before, wording.Quoting, item.After));
            stack.Children.Add(Label("THE EDITION'S WORDS"));
            stack.Children.Add(Wording(string.Empty, wording.Source, string.Empty));
            stack.Children.Add(new TextBlock { Text = Summary(wording), Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            stack.Children.Add(Wording(item.Before, Array.Empty<WordingPiece>(), string.Empty));
        }

        if (item.Note != null) stack.Children.Add(new TextBlock { Text = item.Note, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });

        return stack;
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, Classes = { "section" }, FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>How far the quoter's words are from the edition's, in words, for the line under the two.</summary>
    internal static string Summary(WordingComparison wording)
    {
        if (wording.SameWords) return "The same words as the edition's, once accents, case, punctuation and spelling conventions are set aside.";

        static string Words(int n) => n == 1 ? "1 word" : $"{n:N0} words";
        return $"Marked: {Words(wording.DifferingQuotingWords)} of the quoter's {wording.QuotingWords:N0} and " +
               $"{Words(wording.DifferingSourceWords)} of the edition's {wording.SourceWords:N0} have no counterpart in the other.";
    }

    /// <summary>
    /// A passage's words with the differing ones marked, in the reading face,
    /// between muted context.
    /// </summary>
    private TextBlock Wording(string before, IReadOnlyList<WordingPiece> pieces, string after)
    {
        var all = before + string.Concat(pieces.Select(p => p.Text)) + after;
        var face = ScriptFonts.Compose(_readingFont, ScriptFonts.FamilyFor(all));
        var block = new TextBlock
        {
            FontSize = 15,
            LineHeight = 15 * 1.38,
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = ScriptFonts.IsRightToLeft(all) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };

        // The context is the passage's own text either side of the quotation,
        // spaces and punctuation as printed, so nothing is put between them.
        if (before.Length > 0)
        {
            var run = new Run(before) { FontFamily = face };
            run.Bind(TextElement.ForegroundProperty, block.GetResourceObservable("Cc.InkMuted"));
            block.Inlines!.Add(run);
        }

        foreach (var piece in pieces)
        {
            var run = new Run(piece.Text) { FontFamily = face };
            if (piece.Differs)
            {
                run.Bind(TextElement.BackgroundProperty, block.GetResourceObservable("Cc.AccentSoft"));
                run.Bind(TextElement.ForegroundProperty, block.GetResourceObservable("Cc.AccentStrong"));
            }

            block.Inlines!.Add(run);
        }

        if (after.Length > 0)
        {
            var run = new Run(after) { FontFamily = face };
            run.Bind(TextElement.ForegroundProperty, block.GetResourceObservable("Cc.InkMuted"));
            block.Inlines!.Add(run);
        }

        SteadyLines.Steady(block, face);
        return block;
    }
}
