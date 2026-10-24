using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Research;

/// <summary>
/// One entry in a pop-up menu: the value it stands for and the words it shows.
/// The research windows offer enums, question ids and records in the same
/// kind of menu, and this keeps the value and its wording together rather
/// than matching them up by index.
/// </summary>
internal sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// How an enum value reads in a menu or a table: "Primary text" rather than
/// "PrimaryText". The stored values are untouched - the dossier and the
/// research log still write the names the Windows app writes.
/// </summary>
internal static class Words
{
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
    {
        ["ClassicaCodexAnalysis"] = "App analysis",
        ["AiCandidate"] = "AI candidate",
        ["AiProposed"] = "AI proposed",
        ["AiCrossLanguage"] = "AI cross-language",
        ["AiCorpusInvestigation"] = "AI corpus investigation",
        ["RareWordOverlap"] = "Rare-word overlap",
        ["OnHold"] = "On hold",
        ["FollowUp"] = "Follow-up",
        ["InProgress"] = "In progress",
        ["SourceToTarget"] = "Source → target",
        ["TargetToSource"] = "Target → source",
        ["CorpusInvestigator"] = "Corpus Investigator",
        ["ParallelStudio"] = "Parallel Studio",
        ["ReadingQueue"] = "Reading queue",
        ["ScholarlyClaim"] = "Scholarly claim",
        ["EchoResult"] = "Echo result",
        ["DoesNotDiscriminate"] = "Does not discriminate",
        ["ProjectWork"] = "This work only",
        ["SameAuthor"] = "All works by this author",
        ["EntireCorpus"] = "Entire installed corpus"
    };

    public static string Of(Enum value)
    {
        var name = value.ToString();
        if (Overrides.TryGetValue(name, out var fixedWording)) return fixedWording;

        // PascalCase into a sentence: a capital after a lower-case letter
        // starts a new, lower-cased word.
        var text = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]))
            {
                text.Append(' ');
                text.Append(char.ToLowerInvariant(c));
            }
            else
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }

    public static List<Choice<T>> Choices<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new Choice<T>(v, Of(v))).ToList();
}

/// <summary>
/// The building blocks the research windows share, so that twenty windows
/// put a label over a field, a status line under a table and a notice above
/// a list the same way. Everything takes its colour from the palette.
/// </summary>
internal static class Ui
{
    /// <summary>A small label above a field.</summary>
    public static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        Classes = { "caption" },
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4)
    };

    /// <summary>A label over a control, with the space a form row needs beneath it.</summary>
    public static StackPanel Field(string label, Control input, double bottom = 12)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, bottom) };
        stack.Children.Add(FieldLabel(label));
        stack.Children.Add(input);
        return stack;
    }

    /// <summary>Two fields side by side, sharing the row equally.</summary>
    public static Grid Pair(Control left, Control right, double gap = 14)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,{gap},*") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    public static TextBox Line(string? placeholder = null) => new()
    {
        PlaceholderText = placeholder,
        TextWrapping = TextWrapping.NoWrap
    };

    public static TextBox Area(double height, string? placeholder = null, bool readOnly = false) => new()
    {
        PlaceholderText = placeholder,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = height,
        IsReadOnly = readOnly,
        VerticalContentAlignment = VerticalAlignment.Top
    };

    /// <summary>A text box set in the reading face, for a passage of Greek or Latin.</summary>
    public static TextBox Passage(string text, double fontSize = 14)
    {
        var box = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        box.Bind(TextBox.FontFamilyProperty, box.GetResourceObservable("Cc.SourceFont"));
        return box;
    }

    public static Button Button(string text, string? glyph = null, bool accent = false, string? tip = null)
    {
        object content = text;
        if (glyph != null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(Glyphs.Icon(glyph, 14));
            row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = row;
        }

        var button = new Button { Content = content, VerticalAlignment = VerticalAlignment.Center };
        if (accent) button.Classes.Add("accent");
        if (tip != null) ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>A flat glyph button for a list's own small toolbar.</summary>
    public static Button Glyph(string glyph, string tip)
    {
        var button = new Button { Classes = { "toolbar" }, Content = Glyphs.Icon(glyph, 14) };
        ToolTip.SetTip(button, tip);
        return button;
    }

    /// <summary>A button that opens a menu, the Mac's pull-down button.</summary>
    public static Button MenuButton(string text, string glyph, MenuFlyout menu)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(Glyphs.Icon(glyph, 14));
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(Glyphs.Icon("ChevronDown", 10));
        return new Button { Content = row, Flyout = menu, VerticalAlignment = VerticalAlignment.Center };
    }

    public static MenuItem Item(string header, string? glyph, Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        if (glyph != null) item.Icon = Glyphs.Icon(glyph, 14);
        item.Click += async (_, _) => await action();
        return item;
    }

    public static MenuItem Item(string header, string? glyph, Action action) =>
        Item(header, glyph, () =>
        {
            action();
            return Task.CompletedTask;
        });

    public static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    /// <summary>A muted, wrapping explanation.</summary>
    public static TextBlock Muted(string text) => new()
    {
        Text = text,
        Classes = { "muted" },
        TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock Caption(string text) => new()
    {
        Text = text,
        Classes = { "caption" },
        TextWrapping = TextWrapping.Wrap
    };

    /// <summary>
    /// The explanation a research window puts above its work - what the
    /// records are and are not. Tinted, so it reads as the window speaking
    /// rather than as data.
    /// </summary>
    public static Border Notice(string text, out TextBlock block)
    {
        block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        return new Border { Classes = { "notice" }, Child = block };
    }

    public static Border Notice(string text) => Notice(text, out _);

    /// <summary>The section caption of a sidebar or a form: small capitals in faint ink.</summary>
    public static TextBlock Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Classes = { "section" },
        Margin = new Thickness(0, 0, 0, 6)
    };

    public static TextBlock Status() => new()
    {
        Classes = { "caption" },
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>The strip along the bottom of a window that says what just happened.</summary>
    public static Border StatusBar(TextBlock status)
    {
        var bar = new Border
        {
            Padding = new Thickness(14, 6),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = status
        };
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("Cc.Rule"));
        bar.Bind(Border.BackgroundProperty, bar.GetResourceObservable("Cc.PageHeader"));
        return bar;
    }

    /// <summary>A row of buttons along the bottom of a window, the default one last.</summary>
    public static Border ButtonBar(Control? left, params Control[] right)
    {
        var dock = new DockPanel { LastChildFill = true };
        var buttons = Row(right);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);
        if (left != null) dock.Children.Add(left);

        var bar = new Border
        {
            Padding = new Thickness(16, 10),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = dock
        };
        bar.Bind(Border.BorderBrushProperty, bar.GetResourceObservable("Cc.Rule"));
        return bar;
    }

    /// <summary>The window's header band: a title and what the window is for.</summary>
    public static Border Header(string title, string? explanation, out TextBlock explanationBlock)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = title, Classes = { "h2" }, TextTrimming = TextTrimming.CharacterEllipsis });
        explanationBlock = Caption(explanation ?? string.Empty);
        explanationBlock.IsVisible = explanation != null;
        stack.Children.Add(explanationBlock);

        var header = new Border
        {
            Padding = new Thickness(18, 14, 18, 12),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = stack
        };
        header.Bind(Border.BackgroundProperty, header.GetResourceObservable("Cc.PageHeader"));
        header.Bind(Border.BorderBrushProperty, header.GetResourceObservable("Cc.Rule"));
        return header;
    }

    public static Border Header(string title, string? explanation = null) => Header(title, explanation, out _);

    /// <summary>A surface in the sidebar's colour, ruled on its right.</summary>
    public static Border Sidebar(Control child, double width)
    {
        var border = new Border
        {
            Width = width,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = child
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("Cc.Sidebar"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Cc.Rule"));
        return border;
    }

    /// <summary>
    /// Puts a list and its pane side by side with a draggable divider. With a
    /// <paramref name="rightWidth"/> the two share the width in that proportion as
    /// the window is resized, rather than the left keeping its width and the right
    /// being pushed past the window's edge when it narrows.
    /// </summary>
    public static Grid Split(Control left, Control right, double leftWidth, double leftMin, double rightMin, double? rightWidth = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                rightWidth == null
                    ? new ColumnDefinition(leftWidth, GridUnitType.Pixel) { MinWidth = leftMin }
                    : new ColumnDefinition(leftWidth, GridUnitType.Star) { MinWidth = leftMin },
                new ColumnDefinition(1, GridUnitType.Pixel),
                new ColumnDefinition(rightWidth ?? 1, GridUnitType.Star) { MinWidth = rightMin }
            }
        };

        var rule = new Border { Classes = { "vrule" } };
        var splitter = new GridSplitter { Width = 6, Margin = new Thickness(-3, 0), ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(rule, 1);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(rule);
        grid.Children.Add(right);
        grid.Children.Add(splitter);
        return grid;
    }

    /// <summary>The same, one above the other, sharing the height in proportion.</summary>
    public static Grid Stack(Control top, Control bottom, double topHeight, double topMin, double bottomMin, double bottomHeight)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(topHeight, GridUnitType.Star) { MinHeight = topMin },
                new RowDefinition(1, GridUnitType.Pixel),
                new RowDefinition(bottomHeight, GridUnitType.Star) { MinHeight = bottomMin }
            }
        };

        var rule = new Border { Classes = { "rule" } };
        var splitter = new GridSplitter { Height = 6, Margin = new Thickness(0, -3), ResizeDirection = GridResizeDirection.Rows, Background = Brushes.Transparent };
        Grid.SetRow(top, 0);
        Grid.SetRow(rule, 1);
        Grid.SetRow(splitter, 1);
        Grid.SetRow(bottom, 2);
        grid.Children.Add(top);
        grid.Children.Add(rule);
        grid.Children.Add(bottom);
        grid.Children.Add(splitter);
        return grid;
    }

    /// <summary>
    /// A read-only table with one row selected at a time - the shape of every
    /// list the research windows edit beside.
    /// </summary>
    public static DataGrid Table()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserSortColumns = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 28
        };
        return grid;
    }

    /// <summary>
    /// A text column bound to a property of the row. A width of zero shares out
    /// what the fixed columns leave, but never less than a readable minimum - the
    /// passage or the claim is usually the column the table is for, and a share
    /// of nothing would hide it behind the fixed ones.
    /// </summary>
    public static DataGridTextColumn Column(string header, string property, double width = 0, double minWidth = 180) => new()
    {
        Header = header,
        Binding = new ReflectionBinding(property) { Mode = BindingMode.OneWay },
        Width = width <= 0 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width),
        MinWidth = width <= 0 ? minWidth : 40
    };

    /// <summary>A column of Greek or Latin, set in the reading face.</summary>
    public static DataGridTextColumn SourceColumn(string header, string property, double width = 0)
    {
        var column = Column(header, property, width);
        if (Application.Current?.TryGetResource("Cc.SourceFont", null, out var font) == true && font is FontFamily family)
            column.FontFamily = family;
        column.FontSize = 13.5;
        return column;
    }

    /// <summary>
    /// Tightens a list's rows to the padding its column header uses, so a header
    /// and the rows under it line up, and a long list of check boxes is not a
    /// screenful of padding.
    /// </summary>
    public static T Compact<T>(T list, double vertical = 3) where T : ListBox
    {
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.PaddingProperty, new Thickness(10, vertical)),
                new Setter(ListBoxItem.MinHeightProperty, 0d)
            }
        });
        return list;
    }

    /// <summary>
    /// A double-click on a row, never on the header: double-clicking a column
    /// divider to size it used to open whatever row happened to be current.
    /// </summary>
    public static void OnRowDoubleTapped(DataGrid grid, Func<Task> action)
    {
        grid.DoubleTapped += async (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) == null) return;
            if ((e.Source as Visual)?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) != null) return;
            await action();
        };
    }

    /// <summary>Selects <paramref name="item"/> in a table and brings it into view.</summary>
    public static void Reveal(DataGrid grid, object? item)
    {
        grid.SelectedItem = item;
        if (item != null) grid.ScrollIntoView(item, null);
    }

    public static ComboBox Combo<T>(IEnumerable<Choice<T>> choices, double minWidth = 0)
    {
        var box = new ComboBox
        {
            ItemsSource = choices.ToList(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = minWidth
        };
        if (box.ItemCount > 0) box.SelectedIndex = 0;
        return box;
    }

    public static ComboBox EnumCombo<T>() where T : struct, Enum => Combo(Words.Choices<T>());

    /// <summary>Replaces a menu's choices, keeping or making a selection.</summary>
    public static void SetChoices<T>(ComboBox box, IEnumerable<Choice<T>> choices, T? select = default)
    {
        var list = choices.ToList();
        box.ItemsSource = list;
        var match = list.FindIndex(c => EqualityComparer<T>.Default.Equals(c.Value, select!));
        box.SelectedIndex = match >= 0 ? match : list.Count > 0 ? 0 : -1;
    }

    /// <summary>Selects the choice holding <paramref name="value"/>, or the first.</summary>
    public static void Select<T>(ComboBox box, T value)
    {
        if (box.ItemsSource is not IList<Choice<T>> list) return;
        var match = list.ToList().FindIndex(c => EqualityComparer<T>.Default.Equals(c.Value, value));
        box.SelectedIndex = match >= 0 ? match : list.Count > 0 ? 0 : -1;
    }

    public static T? Value<T>(ComboBox box) => box.SelectedItem is Choice<T> choice ? choice.Value : default;

    /// <summary>The question menus every research window shares, with its catch-all first.</summary>
    public static List<Choice<long?>> QuestionChoices(string general, IEnumerable<ClassicaCodex.Core.Models.ResearchQuestion> questions)
    {
        var choices = new List<Choice<long?>> { new(null, general) };
        choices.AddRange(questions.Select(q => new Choice<long?>(q.ResearchQuestionId, q.Text)));
        return choices;
    }

    /// <summary>A list whose rows are a line of wrapped text each.</summary>
    public static ListBox TextList<T>(Func<T, string> text, Func<T, string?>? detail = null) where T : class
    {
        return new ListBox
        {
            ItemTemplate = new FuncDataTemplate<T>((item, _) =>
            {
                if (item == null) return new TextBlock();
                var stack = new StackPanel { Spacing = 1, Margin = new Thickness(2, 3) };
                stack.Children.Add(new TextBlock { Text = text(item), TextWrapping = TextWrapping.Wrap });
                if (detail?.Invoke(item) is { Length: > 0 } extra)
                    stack.Children.Add(new TextBlock { Text = extra, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });
                return stack;
            }, supportsRecycling: false)
        };
    }

    /// <summary>A form that scrolls inside its pane.</summary>
    public static ScrollViewer Scroll(Control content, Thickness? padding = null) => new()
    {
        Content = content,
        Padding = padding ?? new Thickness(18, 14, 18, 14),
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
    };

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Clip(string text, int length) => text.Length <= length ? text : text[..length] + "…";

    /// <summary>True for ⌘ on a Mac, and Ctrl elsewhere, where the tests and Linux builds run.</summary>
    public static bool Command(KeyEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

    /// <summary>How a key chord is written in a tooltip: ⇧⌘N, in the order a Mac menu writes it.</summary>
    public static string Chord(string key, bool shift = false) => OperatingSystem.IsMacOS()
        ? (shift ? "⇧⌘" : "⌘") + key
        : (shift ? "Ctrl+Shift+" : "Ctrl+") + key;

    /// <summary>A file name made from a project name, as the Windows exports make it.</summary>
    public static string SafeName(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }
}
