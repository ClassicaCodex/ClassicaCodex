using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;

namespace ClassicaCodex.Mac.Features.WorkInfo;

/// <summary>What the reader decided in the attribution sheet.</summary>
/// <param name="Chosen">The judgement to record, or null when handing the work back to the catalog.</param>
/// <param name="ClearOverride">True when the reader asked for the catalog's answer instead.</param>
internal sealed record AttributionDecision((AttributionStatus Status, string? Note)? Chosen, bool ClearOverride);

/// <summary>
/// Records what the reader thinks about a work's attribution - the Windows
/// AttributionForm, as a sheet.
///
/// The library ships a hand-curated catalog of the well-known cases, and this
/// is how somebody disagrees with it. That matters more than it might look:
/// attribution is exactly the sort of question where a reader working through
/// the text reaches their own view, and a library that cannot record it is
/// telling them their opinion does not count.
///
/// Setting anything here marks the work as decided by hand, after which no
/// catalog update and no re-ingest will revise it. "Use the library's answer"
/// hands it back.
/// </summary>
internal sealed class AttributionWindow : Window
{
    private readonly RadioButton _accepted;
    private readonly RadioButton _disputed;
    private readonly RadioButton _spurious;
    private readonly TextBox _note;
    private AttributionDecision? _decision;

    public AttributionWindow(string authorName, string workTitle, AttributionStatus current, string? currentNote, bool setByUser)
    {
        Title = "Attribution";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Worded as what each means rather than by its enum name. "Spurious"
        // is a term of art; "transmitted under the name, but not by this
        // author" is what it says.
        _accepted = Choice("Securely attributed - no serious doubt");
        _disputed = Choice("Disputed - defended and rejected by serious editors");
        _spurious = Choice("Not by this author - transmitted under the name and generally rejected");

        (current switch
        {
            AttributionStatus.Disputed => _disputed,
            AttributionStatus.Spurious => _spurious,
            _ => _accepted
        }).IsChecked = true;

        // Return saves, as the Windows form's accept button does; the note is
        // a line or two of why, not an essay.
        _note = new TextBox
        {
            Text = currentNote ?? string.Empty,
            Height = 64,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false
        };

        // Says where the current value came from, because "disputed" means
        // something different when the reader set it than when a table did.
        var catalogEntry = DisputedWorkData.Lookup(authorName, workTitle);
        var catalogSays = new TextBlock
        {
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap,
            Text = setByUser
                ? "You set this. The library's own catalog will not change it." +
                  (catalogEntry == null
                      ? " The catalog has no entry for this work."
                      : $" The catalog would say: {Describe(catalogEntry.Status)}.")
                : catalogEntry == null
                    ? "From the default - the library's catalog has no entry for this work."
                    : $"From the library's catalog: {catalogEntry.Note}"
        };

        var useCatalog = new Button { Content = "Use the library's answer", IsEnabled = setByUser };
        useCatalog.Click += (_, _) =>
        {
            _decision = new AttributionDecision(null, ClearOverride: true);
            Close();
        };

        var save = new Button { Content = "Save", Classes = { "accent" }, IsDefault = true, MinWidth = 84 };
        save.Click += (_, _) => Save();

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        cancel.Click += (_, _) => Close();

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(cancel);
        right.Children.Add(save);

        var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { useCatalog } });

        var choices = new StackPanel { Spacing = 2, Margin = new Thickness(6, 4, 0, 4) };
        choices.Children.Add(_accepted);
        choices.Children.Add(_disputed);
        choices.Children.Add(_spurious);

        var layout = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 8 };
        layout.Children.Add(new TextBlock { Text = $"{authorName}, {workTitle}", Classes = { "h2" }, TextWrapping = TextWrapping.Wrap });
        layout.Children.Add(new TextBlock { Text = "How securely is this work attributed to the author it is filed under?", TextWrapping = TextWrapping.Wrap });
        layout.Children.Add(choices);
        layout.Children.Add(new TextBlock { Text = "Why (shown in Work Details):" });
        layout.Children.Add(_note);
        layout.Children.Add(catalogSays);
        layout.Children.Add(buttons);
        Content = layout;
    }

    private static RadioButton Choice(string text) => new()
    {
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        GroupName = "attribution"
    };

    private void Save()
    {
        var status = _disputed.IsChecked == true ? AttributionStatus.Disputed
                   : _spurious.IsChecked == true ? AttributionStatus.Spurious
                   : AttributionStatus.Accepted;

        var note = (_note.Text ?? string.Empty).Trim();
        _decision = new AttributionDecision((status, string.IsNullOrEmpty(note) ? null : note), ClearOverride: false);
        Close();
    }

    /// <returns>The decision, or null if the reader cancelled.</returns>
    public async Task<AttributionDecision?> AskAsync(Window owner)
    {
        await ShowDialog(owner);
        return _decision;
    }

    /// <summary>For tests: chooses as a reader would, then saves.</summary>
    internal void Choose(AttributionStatus status, string? note)
    {
        _accepted.IsChecked = status == AttributionStatus.Accepted;
        _disputed.IsChecked = status == AttributionStatus.Disputed;
        _spurious.IsChecked = status == AttributionStatus.Spurious;
        _note.Text = note;
        Save();
    }

    internal static string Describe(AttributionStatus status) => status switch
    {
        AttributionStatus.Disputed => "disputed",
        AttributionStatus.Spurious => "not by this author",
        _ => "securely attributed"
    };
}
