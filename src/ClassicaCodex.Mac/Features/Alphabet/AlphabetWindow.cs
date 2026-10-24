using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;

namespace ClassicaCodex.Mac.Features.Alphabet;

/// <summary>
/// The alphabet of the language being translated from, plus the marks that
/// sit on it.
///
/// Not a translation aid - it doesn't help with meaning at all. It's for the
/// step before that: someone who can't yet read the script can't look a word
/// up, can't tell which letters they're seeing, and can't type a headword
/// into a search box. Every other tool in the workbench assumes you're past
/// that point, and this is the one thing that gets you there.
///
/// The diacritics matter more than the letters for actually reading Greek,
/// which is why they get as much room here. Breathings change how a word is
/// pronounced and are easy to miss entirely at reading size; the iota
/// subscript is invisible to anyone who doesn't know to look for it and
/// changes the grammatical case when it's there.
///
/// The Windows form sets this out as one block of text aligned with spaces.
/// Here it is tables, with the letters at a size where a breathing can be
/// told from an accent - and every letter can be selected and copied, which
/// is the other way of getting a headword into a search box. Opened from the
/// Tools menu, with no Greek or Latin text in the reader to say which, the
/// switch at the top chooses.
/// </summary>
public sealed class AlphabetWindow : FeatureWindow
{
    private readonly ContentControl _body = new();
    private readonly TextBlock _title = new() { Classes = { "h1" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _greekTab;
    private readonly ToggleButton _latinTab;
    private readonly ScrollViewer _scroller;
    private readonly FontFamily _letterFont = ToolPane.Font("Cc.SourceFont");

    public AlphabetWindow(string? languageCode)
    {
        Width = 640;
        Height = 680;
        MinWidth = 520;
        MinHeight = 420;

        _greekTab = Segment("Greek");
        _latinTab = Segment("Latin");
        _greekTab.Click += (_, _) => ShowLanguage("grc");
        _latinTab.Click += (_, _) => ShowLanguage("lat");

        var segment = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new UniformGrid { Columns = 2, Children = { _greekTab, _latinTab } }
        };
        segment.Bind(Border.BackgroundProperty, segment.GetResourceObservable("Cc.Hover"));

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(segment, Dock.Right);
        header.Children.Add(segment);
        header.Children.Add(_title);

        var column = new StackPanel { Margin = new Thickness(28, 22, 28, 28) };
        column.Children.Add(header);
        column.Children.Add(_body);

        _scroller = new ScrollViewer { Content = column };
        Content = _scroller;

        ShowLanguage(languageCode);
    }

    /// <summary>"grc", "lat" or anything else - the language whose reference is showing.</summary>
    internal string Language { get; private set; } = string.Empty;

    internal void ShowLanguage(string? languageCode)
    {
        Language = (languageCode ?? string.Empty).ToLowerInvariant() switch
        {
            "grc" or "greek" => "grc",
            "lat" or "latin" => "lat",
            var other => other
        };

        Title = Language switch
        {
            "grc" => "The Greek Alphabet",
            "lat" => "The Latin Alphabet",
            _ => "Alphabet"
        };

        _title.Text = Title;
        _greekTab.IsChecked = Language == "grc";
        _latinTab.IsChecked = Language == "lat";

        _body.Content = Language switch
        {
            "grc" => GreekReference(),
            "lat" => LatinReference(),
            _ => NoReference()
        };

        _scroller.Offset = default;
    }

    private static ToggleButton Segment(string text) => new()
    {
        Classes = { "segment" },
        Content = text,
        MinWidth = 72,
        Padding = new Thickness(12, 4),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        CornerRadius = new CornerRadius(6)
    };

    private static Control NoReference()
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
        stack.Children.Add(new TextBlock { Text = "No alphabet reference for this language.", Classes = { "h2" } });
        stack.Children.Add(new TextBlock
        {
            Text = "This is here for scripts you may not read yet - Greek especially. " +
                   "A text already in the Latin alphabet needs no chart.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap
        });
        return stack;
    }

    private Control GreekReference()
    {
        var stack = new StackPanel();

        stack.Children.Add(Section("THE LETTERS"));

        var letters = new (string Upper, string Lower, string Name, string Sound)[]
        {
            ("Α", "α", "alpha", "a, as in father"),
            ("Β", "β", "beta", "b"),
            ("Γ", "γ", "gamma", "g, hard as in go"),
            ("Δ", "δ", "delta", "d"),
            ("Ε", "ε", "epsilon", "e, short as in pet"),
            ("Ζ", "ζ", "zeta", "zd, or z"),
            ("Η", "η", "eta", "e, long as in they"),
            ("Θ", "θ", "theta", "th"),
            ("Ι", "ι", "iota", "i"),
            ("Κ", "κ", "kappa", "k"),
            ("Λ", "λ", "lambda", "l"),
            ("Μ", "μ", "mu", "m"),
            ("Ν", "ν", "nu", "n"),
            ("Ξ", "ξ", "xi", "x, as in axe"),
            ("Ο", "ο", "omicron", "o, short as in pot"),
            ("Π", "π", "pi", "p"),
            ("Ρ", "ρ", "rho", "r"),
            ("Σ", "σ / ς", "sigma", "s   (ς only at a word's end)"),
            ("Τ", "τ", "tau", "t"),
            ("Υ", "υ", "upsilon", "u, French tu"),
            ("Φ", "φ", "phi", "ph, as in phone"),
            ("Χ", "χ", "chi", "ch, as in Scottish loch"),
            ("Ψ", "ψ", "psi", "ps, as in lapse"),
            ("Ω", "ω", "omega", "o, long as in bone")
        };

        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("64,84,104,*") };
        AddHeaderRow(table, "UPPER", "LOWER", "NAME", "ROUGHLY");
        foreach (var (upper, lower, name, sound) in letters)
        {
            var row = AddRow(table);
            Place(table, Letter(upper), row, 0);
            Place(table, Letter(lower), row, 1);
            Place(table, new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }, row, 2);
            Place(table, Note(sound), row, 3);
        }

        stack.Children.Add(Card(table));

        stack.Children.Add(Section("THE MARKS ABOVE AND BELOW"));
        stack.Children.Add(Paragraph(
            "These are not decoration. They are part of the word, and the app's search ignores them so you " +
            "can type without hunting for the right key."));

        stack.Children.Add(MarkGroup("Breathings", "every word starting with a vowel has one", new[]
        {
            ("ἀ", "smooth", "no extra sound"),
            ("ἁ", "rough", "an h before the vowel: ἁ = ha"),
            ("ῥ", string.Empty, "rho at the start of a word always takes the rough breathing")
        }));

        stack.Children.Add(MarkGroup("Accents", "which syllable is raised", new[]
        {
            ("ά", "acute", string.Empty),
            ("ὰ", "grave", "an acute pushed down by the word that follows"),
            ("ᾶ", "circumflex", string.Empty)
        }));

        stack.Children.Add(MarkGroup("Iota subscript", "the tiny hook underneath", new[]
        {
            ("ᾳ ῃ ῳ", string.Empty,
             "Easy to miss, and it usually marks the dative case - so it often carries the difference between " +
             "\"the goddess\" and \"to the goddess\".")
        }));

        stack.Children.Add(MarkGroup("Diaeresis", "two dots", new[]
        {
            ("ϊ ϋ", string.Empty, "the vowel is its own syllable, not part of a pair")
        }));

        stack.Children.Add(Section("PAIRS THAT ARE ONE SOUND"));
        stack.Children.Add(Card(Pairs(new[]
        {
            ("αι", "as in aisle"), ("ει", "as in eight"),
            ("οι", "as in boil"), ("ου", "as in soup"),
            ("αυ", "as in how"), ("ευ", "eh-oo, run together")
        }, footnote: ("γγ γκ γχ", "the first gamma is pronounced n: ἄγγελος = angelos"))));

        return stack;
    }

    private Control LatinReference()
    {
        var stack = new StackPanel();

        stack.Children.Add(Section("THE LETTERS"));
        stack.Children.Add(Paragraph(
            "The same 26 letters you already read, with a few differences worth knowing - they are why a word " +
            "can look unfamiliar even when it isn't."));

        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("72,*") };
        foreach (var (letter, note) in new[]
                 {
                     ("C", "always hard, as in cat - never as in city"),
                     ("G", "always hard, as in go"),
                     ("V", "written for both u and v: VENIT is venit"),
                     ("I", "written for both i and j: IAM is iam"),
                     ("Y Z", "only in words borrowed from Greek"),
                     ("W", "does not exist")
                 })
        {
            var row = AddRow(table);
            Place(table, Letter(letter), row, 0);
            Place(table, Note(note, muted: false), row, 1);
        }

        stack.Children.Add(Card(table));

        stack.Children.Add(Section("VOWEL LENGTH"));
        var macrons = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
        var macronRow = AddRow(macrons);
        Place(macrons, Letter("ā ē ī ō ū"), macronRow, 0);
        Place(macrons, Note("A macron marks a long vowel.", muted: false), macronRow, 1);
        stack.Children.Add(Card(macrons));
        stack.Children.Add(Paragraph(
            "Printed texts often leave it out entirely, and length can be the only difference between two " +
            "forms - rosa \"rose\" against rosā \"by a rose\". If a form looks wrong, this is often why."));

        stack.Children.Add(Section("PAIRS THAT ARE ONE SOUND"));
        stack.Children.Add(Card(Pairs(new[]
        {
            ("ae", "as in aisle"), ("oe", "as in oil"),
            ("au", "as in how"), ("ei", "as in eight")
        })));

        return stack;
    }

    /// <summary>
    /// A family of marks: its name and what it is for, then each mark large
    /// enough to see, its name and what it does.
    /// </summary>
    private Control MarkGroup(string name, string purpose, (string Glyph, string Name, string Note)[] marks)
    {
        var title = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        title.Inlines!.Add(new Avalonia.Controls.Documents.Run(name) { FontWeight = FontWeight.SemiBold });
        title.Inlines.Add(PassageText.Ink(title, "  -  " + purpose, "Cc.InkMuted"));

        var table = new Grid { ColumnDefinitions = new ColumnDefinitions("110,96,*") };
        foreach (var (glyph, markName, note) in marks)
        {
            var row = AddRow(table);
            Place(table, Letter(glyph), row, 0);
            Place(table, new TextBlock { Text = markName, VerticalAlignment = VerticalAlignment.Center }, row, 1);
            Place(table, Note(note), row, 2);

            // A mark with no name of its own lends its note the name's room.
            if (markName.Length == 0)
            {
                var noteCell = table.Children[^1];
                Grid.SetColumn(noteCell, 1);
                Grid.SetColumnSpan(noteCell, 2);
            }
        }

        var stack = new StackPanel { Children = { title, table } };
        return Card(stack);
    }

    private Control Pairs((string Letters, string Sound)[] pairs, (string Letters, string Note)? footnote = null)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*,64,*") };
        for (var i = 0; i < pairs.Length; i += 2)
        {
            var row = AddRow(grid);
            Place(grid, Letter(pairs[i].Letters), row, 0);
            Place(grid, Note(pairs[i].Sound, muted: false), row, 1);
            if (i + 1 < pairs.Length)
            {
                Place(grid, Letter(pairs[i + 1].Letters), row, 2);
                Place(grid, Note(pairs[i + 1].Sound, muted: false), row, 3);
            }
        }

        if (footnote is not { } extra) return grid;

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), Margin = new Thickness(0, 10, 0, 0) };
        var lineRow = AddRow(line);
        Place(line, Letter(extra.Letters), lineRow, 0);
        Place(line, Note(extra.Note, muted: false), lineRow, 1);

        var rule = new Border { Classes = { "rule" }, Margin = new Thickness(0, 10, 0, 0) };
        return new StackPanel { Children = { grid, rule, line } };
    }

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        Classes = { "section" },
        Margin = new Thickness(2, 22, 0, 8)
    };

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Classes = { "muted" },
        LineHeight = 19,
        Margin = new Thickness(2, 0, 2, 10)
    };

    private static Border Card(Control child) => new()
    {
        Classes = { "card" },
        Padding = new Thickness(18, 12, 18, 12),
        Margin = new Thickness(0, 0, 0, 10),
        Child = child
    };

    /// <summary>A letter or a mark, at a size where its breathing and accent can be told apart, and selectable.</summary>
    private SelectableTextBlock Letter(string text) => new()
    {
        Text = text,
        FontFamily = _letterFont,
        FontSize = 24,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock Note(string text, bool muted = true)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (muted) block.Classes.Add("muted");
        return block;
    }

    private static void AddHeaderRow(Grid table, params string[] headers)
    {
        var row = AddRow(table, GridLength.Auto);
        for (var i = 0; i < headers.Length; i++)
            Place(table, new TextBlock { Text = headers[i], Classes = { "section" }, Margin = new Thickness(0, 0, 0, 4) }, row, i);
    }

    private static int AddRow(Grid table, GridLength? height = null)
    {
        table.RowDefinitions.Add(new RowDefinition(height ?? new GridLength(38)));
        return table.RowDefinitions.Count - 1;
    }

    private static void Place(Grid table, Control cell, int row, int column)
    {
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        table.Children.Add(cell);
    }
}
