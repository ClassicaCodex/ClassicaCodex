using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Features.WordStudy;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// The pieces the stylometry windows are built from: a titled card for a
/// group of settings, a label beside its control, a whole-number field, a
/// window heading with the sentence that says what the window is for, and
/// the ⌘ chords.
///
/// The Windows forms set every one of these by pixel. Here they are the same
/// few shapes used six times over, so the four benches read as one tool.
/// </summary>
internal static class Bench
{
    /// <summary>
    /// A card with a small capitalised title over its settings - the Mac's
    /// version of a GroupBox, which on Windows is what tells "how text is
    /// counted" apart from "which works are counted".
    /// </summary>
    public static Border Group(string title, Control content)
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), Classes = { "section" } });
        stack.Children.Add(content);
        return new Border { Classes = { "card" }, Padding = new Thickness(14, 10, 14, 12), Child = stack };
    }

    /// <summary>A label and its control on one line, the label in a column of its own so the controls line up.</summary>
    public static Control Field(string label, Control control, double labelWidth = 150)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*") };
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>
    /// A whole-number field with the limits and step the Windows control had.
    /// The limits matter: they are where the Windows form drew the line
    /// between a setting worth trying and one that measures nothing.
    /// </summary>
    public static NumericUpDown Number(int minimum, int maximum, int increment, int value, double width = 120) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Value = value,
        FormatString = "0",
        ClipValueToMinMax = true,
        Width = width,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    /// <summary>
    /// The field's value as an int, inside its limits. A field the reader has
    /// cleared reads as its minimum rather than as nothing, so a run is never
    /// started on a value nobody chose.
    /// </summary>
    public static int Read(NumericUpDown field) =>
        (int)Math.Clamp(field.Value ?? field.Minimum, field.Minimum, field.Maximum);

    public static void Set(NumericUpDown field, int value) =>
        field.Value = Math.Clamp(value, field.Minimum, field.Maximum);

    /// <summary>Puts a button in a cell of an action grid, stretched to the cell.</summary>
    public static void Place(Grid grid, Control control, int row, int column, int span = 1)
    {
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, span);
        grid.Children.Add(control);
    }

    /// <summary>The window's title line and the sentence under it saying what the window is for.</summary>
    public static StackPanel Heading(string title, string intro)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        stack.Children.Add(new TextBlock { Text = title, Classes = { "h2" } });
        stack.Children.Add(new TextBlock
        {
            Text = intro,
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        return stack;
    }

    /// <summary>
    /// A block of report text - the summaries the benches write under a run.
    /// Selectable, because these are the sentences people paste into notes.
    /// </summary>
    public static SelectableTextBlock Report(string text = "") => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 19
    };

    /// <summary>A status line, quieter than the report above it.</summary>
    public static TextBlock Status(string text = "") => new()
    {
        Text = text,
        Classes = { "caption" },
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>A push button with a glyph and, where there is one, its ⌘ chord in the tooltip.</summary>
    public static Button Button(string text, string glyph, string? tip = null)
    {
        var button = ToolPane.GlyphButton(text, glyph);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        if (tip != null) ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>
    /// The progress bar a long run shows under its status. Indeterminate until
    /// the run knows how many steps it has.
    /// </summary>
    public static ProgressBar Progress() => new()
    {
        Height = 4,
        MinHeight = 4,
        IsVisible = false,
        Margin = new Thickness(0, 6, 0, 0)
    };

    /// <summary>
    /// Runs <paramref name="action"/> on ⌘ and <paramref name="key"/> - the
    /// chords a Mac user reaches for first: ⌘R to run, ⌘. to stop, ⌘S to save.
    /// A disabled button's chord does nothing, as its menu item would.
    /// </summary>
    public static void Chord(Window window, Key key, Button button, Action action)
    {
        window.KeyDown += (_, e) =>
        {
            if (e.Key != key || !e.KeyModifiers.HasFlag(WordStudyWindow.CommandKey)) return;
            if (!button.IsEffectivelyEnabled) return;
            e.Handled = true;
            action();
        };
    }

    /// <summary>
    /// The settings column of a bench: the settings scroll if the window is
    /// short, and the buttons that act on them stay in sight beneath, so Run
    /// is never the thing scrolled out of view.
    /// </summary>
    public static Control SidePanel(Control settings, Control actions, double width)
    {
        settings.Margin = new Thickness(0, 0, 14, 0);
        var scroller = new ScrollViewer
        {
            Content = settings,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        actions.Margin = new Thickness(0, 12, 14, 0);
        var dock = new DockPanel { Width = width };
        DockPanel.SetDock(actions, Dock.Bottom);
        dock.Children.Add(actions);
        dock.Children.Add(scroller);
        return dock;
    }

    /// <summary>
    /// A neighbour's label without its author in front. The engine labels a
    /// sample "Author, Title [1/2]", which a table with an author column of
    /// its own would print as "Sophocles  Sophocles, Trachiniae [1/2]". Only
    /// the display drops it; what is saved and exported keeps the label whole.
    /// </summary>
    public static string WithoutAuthor(string author, string label) =>
        !string.IsNullOrEmpty(author) && label.StartsWith(author + ", ", StringComparison.Ordinal)
            ? label[(author.Length + 2)..]
            : label;

    /// <summary>"Title (⌘R)" for a tooltip, with the chord the platform uses.</summary>
    public static string WithChord(string text, string key) =>
        $"{text} ({(OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+")}{key})";
}

/// <summary>
/// A list of ticks, for the pool and donor authors and the grid's settings -
/// what the Windows benches use a CheckedListBox for.
///
/// <see cref="Changed"/> fires only for a tick the reader made, never for one
/// set from code, which is the distinction the validation bench needs: a
/// preset setting the ticks must not turn itself into "Custom" by doing so.
/// </summary>
internal sealed class CheckList
{
    private readonly StackPanel _stack = new() { Spacing = 2, Margin = new Thickness(8, 6) };
    private readonly List<(CheckBox Box, object Tag)> _items = new();
    private bool _setting;

    /// <param name="maxHeight">The tallest the list grows before it scrolls; a short list is only as tall as its rows.</param>
    public CheckList(double maxHeight)
    {
        View = new Border
        {
            Classes = { "panel" },
            MaxHeight = maxHeight,
            Child = new ScrollViewer { Content = _stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
        };
    }

    public Control View { get; }

    /// <summary>A tick the reader made, with the tag of the row they made it on.</summary>
    public event Action<object>? Changed;

    public IReadOnlyList<object> Tags => _items.Select(i => i.Tag).ToList();

    public IReadOnlyList<object> CheckedTags => _items.Where(i => i.Box.IsChecked == true).Select(i => i.Tag).ToList();

    public void Clear()
    {
        _items.Clear();
        _stack.Children.Clear();
    }

    public void Add(string label, object tag, bool isChecked)
    {
        var box = new CheckBox
        {
            Content = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis },
            IsChecked = isChecked,
            MinHeight = 26,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        box.IsCheckedChanged += (_, _) =>
        {
            if (!_setting) Changed?.Invoke(tag);
        };

        _items.Add((box, tag));
        _stack.Children.Add(box);
    }

    public bool IsChecked(object tag) => _items.Any(i => Equals(i.Tag, tag) && i.Box.IsChecked == true);

    /// <summary>Flips one row as a click on it would, so <see cref="Changed"/> fires. For the tests.</summary>
    internal void Click(object tag)
    {
        foreach (var (box, t) in _items)
        {
            if (Equals(t, tag)) box.IsChecked = box.IsChecked != true;
        }
    }

    /// <summary>Ticks or unticks one row from code; <see cref="Changed"/> does not fire.</summary>
    public void SetChecked(object tag, bool isChecked) => Quietly(() =>
    {
        foreach (var (box, t) in _items)
        {
            if (Equals(t, tag)) box.IsChecked = isChecked;
        }
    });

    /// <summary>Ticks or unticks every row from code; <see cref="Changed"/> does not fire.</summary>
    public void SetAll(bool isChecked) => Quietly(() =>
    {
        foreach (var (box, _) in _items) box.IsChecked = isChecked;
    });

    /// <summary>Ticks exactly the rows <paramref name="wanted"/> accepts.</summary>
    public void SetWhere(Func<object, bool> wanted) => Quietly(() =>
    {
        foreach (var (box, tag) in _items) box.IsChecked = wanted(tag);
    });

    private void Quietly(Action action)
    {
        _setting = true;
        try { action(); }
        finally { _setting = false; }
    }
}

/// <summary>
/// The title test both the single run and the validation sweep use to keep
/// compilations out of a pool.
///
/// One list here where Windows keeps two "in step" - StylometryForm's and
/// ValidationForm's - because two copies of a list are two lists the first
/// time somebody adds a marker to one of them. What differs between the two
/// windows is what they exempt, and that stays with each window.
/// </summary>
internal static class Compilations
{
    /// <summary>
    /// Title fragments marking works that are not single compositions by a
    /// single author, and which therefore have no business in a pool whose
    /// z-scores define what "normal" looks like.
    ///
    /// Found by searching the ingested Greek corpus for editorial Latin and
    /// reading what came back. The hits were not tragedies carrying an
    /// apparatus - they were works whose entire content is scholarly:
    /// Aristonicus' notes on the Odyssey, Themistius' index verborum, the
    /// Fragmenta of Chrysippus and Democritus. A fragment collection is an
    /// anthology assembled by a modern editor out of quotations spanning
    /// centuries, and an index is a word list. Neither has an authorial style
    /// to measure, and both were sitting in every normalisation.
    ///
    /// Euripides' own Fragmenta is the clearest case: 17,375 tokens, the
    /// largest "work" in the Euripides set, and the reason Spearman's rho
    /// between length and depth reads 0.43 instead of 0.61.
    ///
    /// Matched case-insensitively on substrings of the title. Deliberately
    /// conservative - commentaries are hard to catch by title without also
    /// catching real works, so no attempt is made to match "In ..." or
    /// "Commentaria". The filter is a checkbox, and what it removed is
    /// reported, so an over-broad match is visible rather than silent.
    /// </summary>
    private static readonly string[] NonCompositionTitleMarkers =
    {
        "fragmenta", "fragment", "fragments",
        "index",          // Themistius, index verborum
        "scholia", "scholion",
        "testimonia",
        "excerpta",
        "lexicon",
        "anthology", "anthologia",
        "gnomologium",    // compiled sayings, not a composition
        "paraphrasis",    // Themistius' Aristotle paraphrases
        "commentar"       // Commentaria / Commentarii / Commentarius
    };

    // WHAT THIS FILTER DOES NOT CATCH.
    //
    // A title-based filter can only remove works whose titles announce what
    // they are. Aristonicus' "De signis Odysseae" - ancient critical notes on
    // Homer, entirely editorial in content - does not. A content-based test
    // would: in a Greek work, the share of nodes containing a run of three or
    // more Latin letters is near zero for a composition and high for a
    // commentary. Worth doing as an ingest-time classification rather than a
    // filter applied on every run. Until then this filter is partial and
    // should be understood as such.

    public static bool IsNonComposition(string workTitle) =>
        NonCompositionTitleMarkers.Any(m => workTitle.Contains(m, StringComparison.OrdinalIgnoreCase));
}
