using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Help;

namespace ClassicaCodex.Mac.Features.Apparatus;

/// <summary>
/// The editor's notes on a line, and on the edition around it.
///
/// Called "Editor's Notes" in the interface rather than "Textual Apparatus",
/// because that is what the content usually turns out to be. A strict
/// apparatus criticus records manuscript variants - Plutarch's Greek gives
/// "εἶτʼ οὐ R: εἶτα", where R is a manuscript. But most of what the corpus
/// actually carries is commentary: Fowler on Ad principem ineruditum
/// identifies a Nauck fragment, cites Diogenes Laertius, notes where Cicero
/// translates a line. Both are useful and both belong here; only one of them
/// is an apparatus, so the broader name is the honest one.
///
/// A printed edition sets this in small type at the foot of the page: which
/// manuscripts read what, who conjectured what, which lines are doubted and by
/// whom. It is the record of how the text in front of you came to look the way
/// it does, and it is normally the first thing lost when a text is digitised
/// for reading.
///
/// Two views because two questions get asked. "Why does this line look like
/// this?" is about one line. "What did this editor actually do?" is about the
/// whole edition - and reading an editor's notes straight through is how you
/// find out whether they are conservative or interventionist.
/// </summary>
public sealed class EditorNotesWindow : FeatureWindow
{
    private readonly ApparatusRepository _apparatusRepo = new();

    private readonly int _editionId;
    private readonly string _citationRef;
    private readonly string _editionLabel;

    private readonly ToggleButton _thisLine;
    private readonly ToggleButton _wholeEdition;
    private readonly ListBox _entryList;
    private readonly SelectableTextBlock _detail;
    private readonly TextBlock _status;

    private List<ApparatusEntry> _entries = new();
    private int _loadGeneration;

    public EditorNotesWindow(int editionId, TextNode node, string editionLabel, string lineFont)
    {
        _editionId = editionId;
        _citationRef = node.CitationRef;
        _editionLabel = editionLabel;

        Title = "Editor's Notes";
        Width = 900;
        Height = 640;
        MinWidth = 560;
        MinHeight = 420;

        // The line itself sits at the top, because every entry below is a
        // comment on it and reading them without it in view means holding the
        // Greek in your head while you read about it. In the pane's own face,
        // so a Hebrew or a cuneiform line arrives as itself.
        var citation = new TextBlock
        {
            Text = PassageCitation.Display(node.CitationRef, node.Milestone),
            Classes = { "section" },
            Margin = new Thickness(0, 0, 0, 4)
        };
        var line = new SelectableTextBlock
        {
            Text = node.Text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            LineHeight = 23,
            MaxLines = 4,
            TextTrimming = TextTrimming.WordEllipsis
        };
        if (!string.IsNullOrWhiteSpace(lineFont)) line.FontFamily = new FontFamily(lineFont);
        else Prose.UseFont(line, "Cc.SourceFont");
        ToolTip.SetTip(line, node.Text);

        var lineStack = new StackPanel();
        lineStack.Children.Add(citation);
        lineStack.Children.Add(line);
        lineStack.Children.Add(new TextBlock { Text = editionLabel, Classes = { "caption" }, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });

        var lineCard = new Border { Classes = { "card" }, Padding = new Thickness(16, 12, 16, 12), Child = lineStack };

        // The two views as a segmented control, the Mac's way of offering one
        // of two.
        _thisLine = Segment("This line");
        _wholeEdition = Segment("Whole edition");
        _thisLine.IsChecked = true;
        _thisLine.Click += async (_, _) => await ChooseAsync(wholeEdition: false);
        _wholeEdition.Click += async (_, _) => await ChooseAsync(wholeEdition: true);

        var segment = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 10),
            Child = new UniformGrid { Columns = 2, Children = { _thisLine, _wholeEdition } }
        };
        segment.Bind(Border.BackgroundProperty, segment.GetResourceObservable("Cc.Hover"));

        _entryList = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock
            {
                Text = text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 1)
            }, supportsRecycling: true)
        };
        _entryList.SelectionChanged += (_, _) => ShowDetail();

        // One entry to a row at a Mac list's height, not Fluent's touch-sized one.
        Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(PaddingProperty, new Thickness(10, 5)), new Setter(MinHeightProperty, 0d) }
        });
        var listPage = new Border { Classes = { "page" }, Child = _entryList, Padding = new Thickness(4) };

        // Entries are often long and full of sigla; the list truncates and
        // this shows the whole thing, selectable so it can be copied into a
        // note.
        _detail = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14.5, LineHeight = 21 };
        Prose.UseFont(_detail, "Cc.SourceFont");
        var detailPage = new Border
        {
            Classes = { "page" },
            Child = new ScrollViewer { Content = _detail, Padding = new Thickness(16, 12) }
        };

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Height = 10, Background = Brushes.Transparent };
        var lists = new Grid { RowDefinitions = new RowDefinitions("3*,10,2*") };
        Grid.SetRow(splitter, 1);
        Grid.SetRow(detailPage, 2);
        lists.Children.Add(listPage);
        lists.Children.Add(splitter);
        lists.Children.Add(detailPage);

        _status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 0, 0) };

        var root = new DockPanel { Margin = new Thickness(18, 16, 18, 14) };
        DockPanel.SetDock(lineCard, Dock.Top);
        DockPanel.SetDock(segment, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(lineCard);
        root.Children.Add(segment);
        root.Children.Add(_status);
        root.Children.Add(lists);
        Content = root;

        Opened += async (_, _) => await LoadAsync();
    }

    private bool WholeEdition => _wholeEdition.IsChecked == true;

    internal IReadOnlyList<ApparatusEntry> Entries => _entries;

    internal string StatusText => _status.Text ?? string.Empty;

    internal string DetailText => _detail.Text ?? string.Empty;

    internal Task ShowWholeEditionAsync() => ChooseAsync(wholeEdition: true);

    private static ToggleButton Segment(string text) => new()
    {
        Classes = { "segment" },
        Content = text,
        MinWidth = 120,
        Padding = new Thickness(12, 4),
        CornerRadius = new CornerRadius(6),
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private async Task ChooseAsync(bool wholeEdition)
    {
        // A segmented control always has one half chosen; clicking the chosen
        // half again leaves it chosen rather than turning it off.
        _wholeEdition.IsChecked = wholeEdition;
        _thisLine.IsChecked = !wholeEdition;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var generation = ++_loadGeneration;
        var whole = WholeEdition;

        List<ApparatusEntry> entries;
        try
        {
            entries = whole
                ? await _apparatusRepo.GetForEditionAsync(_editionId)
                : await _apparatusRepo.GetForLineAsync(_editionId, _citationRef);
        }
        catch (Exception ex)
        {
            if (generation == _loadGeneration) _status.Text = ex.Message;
            return;
        }

        // A later choice of view has already been made; its own load fills the list.
        if (generation != _loadGeneration) return;

        _entries = entries;
        _entryList.ItemsSource = entries.Select(e => Describe(e, whole)).ToList();

        if (entries.Count == 0)
        {
            _detail.Text = string.Empty;

            // Absence has two quite different causes and the distinction
            // matters: an edition can carry no apparatus at all, or carry one
            // that simply says nothing about this line. Reporting the
            // edition-wide count separates them, so nobody concludes the
            // feature is broken when the truth is that Browning's translation
            // has no apparatus to show.
            int editionTotal;
            try { editionTotal = await _apparatusRepo.CountForEditionAsync(_editionId); }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
                return;
            }

            if (generation != _loadGeneration) return;

            _status.Text = editionTotal == 0
                ? $"{_editionLabel} carries no editor's notes. Translations often don't, and " +
                  "not every edition records them."
                : $"Nothing recorded for this line. {editionTotal:N0} entries elsewhere in this edition.";
            return;
        }

        _entryList.SelectedIndex = 0;
        ShowDetail();
        _status.Text = whole
            ? $"{entries.Count:N0} entries across {_editionLabel}."
            : $"{entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} for this line.";
    }

    /// <summary>
    /// One line for the list. Structured entries get lemma and siglum spelled
    /// out; prose notes are shown as the editor wrote them, since imposing a
    /// shape on them would mean inventing one.
    /// </summary>
    internal static string Describe(ApparatusEntry e, bool wholeEdition)
    {
        var prefix = wholeEdition ? $"[{PassageCitation.Display(e.CitationRef, e.Milestone)}] " : string.Empty;

        if (e.Kind == "variant")
        {
            // A Menota note is written as the two readings either side of a
            // colon, so its Content already opens with the lemma and prefixing
            // it again gives "Uphaf ] Uphaf : Vphaf Sogo". Printed only where
            // the lemma came from somewhere the reader cannot see - a @wit or
            // @resp attribute, as in the Perseus editions.
            var repeated = !string.IsNullOrWhiteSpace(e.Lemma)
                           && e.Content.StartsWith(e.Lemma, StringComparison.Ordinal);

            var lemma = string.IsNullOrWhiteSpace(e.Lemma) || repeated ? "" : $"{e.Lemma}  ]  ";
            var witness = string.IsNullOrWhiteSpace(e.Witness) ? "" : $"  ({e.Witness})";
            return $"{prefix}{lemma}{e.Content}{witness}";
        }

        var resp = string.IsNullOrWhiteSpace(e.Witness) ? "" : $"  - {e.Witness}";
        return $"{prefix}{e.Content}{resp}";
    }

    private void ShowDetail()
    {
        var index = _entryList.SelectedIndex;
        if (index < 0 || index >= _entries.Count)
        {
            _detail.Text = string.Empty;
            return;
        }

        var e = _entries[index];
        var lines = new List<string>();

        if (WholeEdition) lines.Add($"Line {PassageCitation.Display(e.CitationRef, e.Milestone)}");

        lines.Add(e.Kind == "variant" ? "Manuscript variant" : "Editor's note");
        if (!string.IsNullOrWhiteSpace(e.Lemma)) lines.Add($"Adopted reading: {e.Lemma}");
        if (!string.IsNullOrWhiteSpace(e.Witness)) lines.Add($"Witness / editor: {e.Witness}");

        lines.Add(string.Empty);
        lines.Add(e.Content);

        _detail.Text = string.Join(Environment.NewLine, lines);
    }
}
