using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Mac.Features.Translation;

/// <summary>
/// Picks which edition of a work to translate from, when there is more than
/// one to choose between.
///
/// This is asked once and never again for a given translation, because the
/// answer is not really a preference - it decides the citation references
/// every passage gets filed under. Switching source part-way would leave
/// work already done keyed to a scheme the new source doesn't use, which is
/// the misalignment this whole approach exists to avoid.
///
/// Modal, as on Windows: nothing can happen next until it is answered.
/// </summary>
public sealed class EditionChoiceDialog : Window
{
    private readonly ListBox _list;
    private readonly List<Edition> _editions;
    private Edition? _chosen;

    private EditionChoiceDialog(string title, string prompt, List<Edition> editions, IReadOnlyDictionary<int, int> lineCounts)
    {
        _editions = editions;

        Title = title;
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _list = new ListBox
        {
            Height = 200,
            ItemsSource = editions.Select(e => Describe(e, lineCounts)).ToList(),
            ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock
            {
                Text = text,
                Margin = new Thickness(4, 3),
                TextTrimming = TextTrimming.CharacterEllipsis
            }),
            SelectedIndex = editions.Count > 0 ? 0 : -1
        };
        _list.DoubleTapped += (_, _) => Choose();

        var border = new Border { Classes = { "panel" }, Padding = new Thickness(4), Child = _list };

        var use = new Button { Content = "Use This", Classes = { "accent" }, IsDefault = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        use.Click += (_, _) => Choose();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(use);

        var layout = new StackPanel { Margin = new Thickness(22, 18), Spacing = 12 };
        layout.Children.Add(new TextBlock { Text = title, Classes = { "h2" } });
        layout.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Classes = { "muted" } });
        layout.Children.Add(border);
        layout.Children.Add(buttons);
        Content = layout;

        Opened += (_, _) => _list.Focus();
    }

    /// <summary>
    /// Line count included because it is the one thing that distinguishes
    /// two editions of the same text at a glance - often the only visible
    /// difference between them.
    /// </summary>
    internal static string Describe(Edition edition, IReadOnlyDictionary<int, int> lineCounts)
    {
        var lines = lineCounts.TryGetValue(edition.EditionId, out var n) ? $"{n:N0} lines" : "unknown length";
        var translator = string.IsNullOrWhiteSpace(edition.Translator) ? null : edition.Translator;
        var suffix = edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

        return translator == null ? $"{suffix}  ·  {lines}" : $"{translator}  ({suffix})  ·  {lines}";
    }

    private void Choose()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _editions.Count) return;

        _chosen = _editions[_list.SelectedIndex];
        Close();
    }

    /// <returns>The edition chosen, or null if the reader cancelled.</returns>
    public static async Task<Edition?> AskAsync(
        Window owner, string prompt, List<Edition> editions, IReadOnlyDictionary<int, int> lineCounts)
    {
        var dialog = new EditionChoiceDialog("Choose an Edition", prompt, editions, lineCounts);
        await dialog.ShowDialog(owner);
        return dialog._chosen;
    }
}
