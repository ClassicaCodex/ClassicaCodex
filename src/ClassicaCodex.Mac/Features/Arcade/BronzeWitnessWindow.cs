using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// Scholia: the ancient passages behind a creature in the bestiary, read from
/// the player's own library. Optional reading, separate from the adventure's
/// passage gates - nothing here advances a story.
///
/// A witness may have several editions in the library - the Greek and an
/// English translation, say - and each is offered. Where the witness is a
/// prose section that the library splits into numbered paragraphs, this
/// shows its beginning and says so, and the reader is where to follow the
/// rest. The stored citation is shown beside the readable one, because it is
/// what a player would search for.
/// </summary>
internal sealed class BronzeWitnessWindow : FeatureWindow
{
    private readonly ComboBox _witness = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _edition = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly SelectableTextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Classes = { "muted" } };
    private readonly SelectableTextBlock _passage = new() { TextWrapping = TextWrapping.Wrap, FontSize = 18, LineHeight = 27 };
    private readonly SelectableTextBlock _citation = new() { TextWrapping = TextWrapping.Wrap, Classes = { "caption" } };
    private readonly IReadOnlyList<BronzeWitness> _sources;

    public ArcadePassage? SelectedPassage { get; private set; }

    public BronzeWitnessWindow(IReadOnlyList<BronzeWitness> sources)
    {
        _sources = sources;
        Title = "ΣΧΟΛΙΑ — Ancient witnesses";
        Width = 800;
        Height = 660;
        MinWidth = 620;
        MinHeight = 500;
        if (BronzeIcons.Bestiary() is { } icon) Icon = new WindowIcon(icon);

        _passage.Bind(TextBlock.FontFamilyProperty, _passage.GetResourceObservable("Cc.SourceFont"));

        var open = new Button
        {
            Content = "Open this passage in the reader",
            Classes = { "accent" },
            HorizontalAlignment = HorizontalAlignment.Right,
            MinHeight = 32
        };
        open.Click += (_, _) => { if (SelectedPassage != null) Close(true); };

        var pickers = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        AddPicker(pickers, "Witness", _witness, 0);
        AddPicker(pickers, "Edition", _edition, 1);

        var top = new StackPanel { Spacing = 10 };
        top.Children.Add(pickers);
        top.Children.Add(_note);

        var page = new Border
        {
            Classes = { "page" },
            Margin = new Thickness(0, 12, 0, 10),
            Child = new ScrollViewer
            {
                Content = _passage,
                Padding = new Thickness(22, 18),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(open, Dock.Right);
        footer.Children.Add(open);
        footer.Children.Add(new TextBlock
        {
            Text = "From your installed corpus · Select text to copy · Esc returns to the bestiary",
            Classes = { "caption" },
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        var bottom = new StackPanel();
        bottom.Children.Add(_citation);
        bottom.Children.Add(footer);

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(bottom);
        layout.Children.Add(page);
        Content = layout;

        _witness.ItemsSource = sources.Select(s => s.Witness.Title).ToList();
        _witness.SelectionChanged += (_, _) =>
        {
            if (_witness.SelectedIndex < 0) return;
            var source = _sources[_witness.SelectedIndex];
            _note.Text = source.Witness.Note
                + (source.Witness.Section ? "\n\nThis is the beginning of section " + source.Witness.Citation + ". Continue in the reader to follow its remaining paragraphs." : "");
            var editions = new List<string>();
            for (var i = 0; i < source.Editions.Count; i++)
            {
                var language = source.Editions[i].Language switch { "grc" => "Greek", "eng" or "en" => "English", "lat" => "Latin", var other => other };
                editions.Add($"{language} text · edition {i + 1}");
            }
            _edition.ItemsSource = editions;
            if (editions.Count > 0) _edition.SelectedIndex = 0;
        };
        _edition.SelectionChanged += (_, _) =>
        {
            if (_witness.SelectedIndex < 0 || _edition.SelectedIndex < 0) return;
            SelectedPassage = _sources[_witness.SelectedIndex].Editions[_edition.SelectedIndex];
            _passage.Text = SelectedPassage.Text;
            _citation.Text = $"{SelectedPassage.Author} — {SelectedPassage.Title} {PassageCitation.Display(SelectedPassage.Citation)}\nStored citation: {SelectedPassage.Citation}";
        };
        if (sources.Count > 0) _witness.SelectedIndex = 0;
    }

    internal string NoteText => _note.Text ?? string.Empty;
    internal string PassageText => _passage.Text ?? string.Empty;
    internal string CitationText => _citation.Text ?? string.Empty;

    private static void AddPicker(Grid grid, string label, ComboBox box, int row)
    {
        var caption = new TextBlock
        {
            Text = label,
            Classes = { "muted" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, row == 0 ? 8 : 0)
        };
        Grid.SetRow(caption, row);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        box.Margin = new Thickness(0, 0, 0, row == 0 ? 8 : 0);
        grid.Children.Add(caption);
        grid.Children.Add(box);
    }
}
