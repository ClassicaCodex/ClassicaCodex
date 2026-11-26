using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Features.Echoes;
using ClassicaCodex.Mac.Features.WordStudy;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.GoToCitation;

/// <summary>
/// One line for a citation, and beneath it what the citation opens - the
/// author, work and reference, and the passage itself - found as it is
/// typed or pasted, so the reader sees where Return will take them before
/// pressing it. Closes with what to open: a <see cref="CitationLookupResult"/>
/// with a passage, a <see cref="CitedArtifact"/>, or null.
/// </summary>
public sealed class GoToCitationWindow : Window
{
    public const string Example = "tlg0020.tlg001.perseus-grc2 457";

    // The reports cite a manuscript line of Medieval Hands as
    // "CatmusLines – Paris, BnF, lat. 9768 #120078": no edition, no reference.
    private static readonly Regex ManuscriptLine = new(@"^\s*CatmusLines\s*[–—-]\s*(?<shelfmark>[^#]+)#", RegexOptions.CultureInvariant);

    private readonly CopiedCitationResolver _resolver;
    private readonly TextBox _box;
    private readonly TextBlock _heading = new() { Classes = { "h3" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _edition = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _passage = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
    private readonly TextBlock _note = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _open = new() { Content = "Open", Classes = { "accent" }, IsDefault = true, MinWidth = 84, IsEnabled = false };

    private CancellationTokenSource? _typing;

    /// <summary>A museum object named that the library does not hold.</summary>
    private sealed record MissingArtifact(string Name);

    public GoToCitationWindow(CopiedCitationResolver resolver, string initial = "")
    {
        _resolver = resolver;

        Title = "Go to Citation";
        SizeToContent = SizeToContent.Height;
        Width = 560;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _box = new TextBox { Text = initial, PlaceholderText = Example };
        _box.TextChanged += (_, _) => Look(delay: true);

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        cancel.Click += (_, _) => Close(null);
        _open.Click += async (_, _) => await OpenAsync();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_open);

        var found = new StackPanel { Spacing = 4, MinHeight = 64 };
        found.Children.Add(_heading);
        found.Children.Add(_edition);
        found.Children.Add(_passage);
        found.Children.Add(_note);

        var layout = new StackPanel { Margin = new Thickness(22, 18), Spacing = 12 };
        layout.Children.Add(new TextBlock
        {
            Text = "An edition's identifier and a reference, as the Atlas of Ancient Myth and the reports copy a citation " +
                   $"when you click it: {Example}. A work's identifier will do in place of an edition's.",
            TextWrapping = TextWrapping.Wrap
        });
        layout.Children.Add(_box);
        layout.Children.Add(found);
        layout.Children.Add(buttons);
        Content = layout;

        Opened += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
            Look(delay: false);
        };
    }

    /// <summary>What the window shows now: a lookup result, an object, or null. For the tests.</summary>
    internal object? Shown { get; private set; }

    internal string HeadingText => _heading.Text ?? string.Empty;

    internal string NoteText => _note.Text ?? string.Empty;

    internal TextBox Box => _box;

    internal Button OpenButton => _open;

    /// <summary>What the box holds, looked up: shown when it is found, if the box has not changed since.</summary>
    private void Look(bool delay)
    {
        _typing?.Cancel();
        var typing = _typing = new CancellationTokenSource();
        var text = _box.Text;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delay) await Task.Delay(180, typing.Token);
                var result = await FindAsync(text, typing.Token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!typing.IsCancellationRequested) Show(text, result);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => ShowProblem(ex.Message));
            }
        });
    }

    private async Task<object?> FindAsync(string? text, CancellationToken cancellationToken)
    {
        if (CopiedCitation.Parse(text) is not { } citation) return null;

        if (citation.ArtifactName is { } name)
            return await CopiedCitationResolver.LookUpArtifactAsync(name, cancellationToken) as object ?? new MissingArtifact(name);

        return await _resolver.LookUpAsync(citation, cancellationToken);
    }

    private static bool Opens(object? result) =>
        result is CitedArtifact || result is CitationLookupResult { TextNodeId: not null };

    /// <summary>Return, or the Open button: looks the box up afresh, so a citation pasted a moment ago is not missed.</summary>
    private async Task OpenAsync()
    {
        _typing?.Cancel();
        var text = _box.Text;

        try
        {
            var result = await FindAsync(text, CancellationToken.None);
            if (Opens(result)) Close(result);
            else Show(text, result);
        }
        catch (Exception ex)
        {
            ShowProblem(ex.Message);
        }
    }

    private void Show(string? text, object? result)
    {
        Shown = result;
        _heading.Text = _edition.Text = _passage.Text = _note.Text = string.Empty;
        _passage.ClearValue(TextBlock.FontFamilyProperty);
        _open.Content = "Open";
        _open.IsEnabled = Opens(result);

        var citation = CopiedCitation.Parse(text);
        if (citation == null)
        {
            if (ManuscriptLine.Match(text ?? string.Empty) is { Success: true } line)
                _note.Text = $"That is a line of a manuscript in Medieval Hands, not a passage of the library: open Medieval Hands " +
                             $"from the Tools menu and choose {line.Groups["shelfmark"].Value.Trim()}.";
            else if (!string.IsNullOrWhiteSpace(text))
                _note.Text = "That is not a citation. One starts with an identifier, such as tlg0020.tlg001.perseus-grc2 or " +
                             "urn:menota:anonymous:gylfaginning:am-242-fol, then a space and the reference.";
            return;
        }

        switch (result)
        {
            case CitedArtifact artifact:
                _heading.Text = artifact.Name;
                _edition.Text = string.Join(" · ", new[] { artifact.Type, artifact.Location }.Where(s => !string.IsNullOrWhiteSpace(s)));
                _passage.Text = PassageText.RowText(artifact.Description, 320);
                _note.Text = "A museum object. Open shows its photographs in the Art and Archaeology browser.";
                return;

            case MissingArtifact missing:
                _note.Text = $"This library has no museum object called {missing.Name}. The Art and Archaeology step in Setup " +
                             "adds the Perseus objects.";
                return;

            case CitationLookupResult { Outcome: CitationLookupOutcome.NotInLibrary }:
                _note.Text = $"Nothing in this library has the identifier {citation.Identifier}. If it belongs to a collection " +
                             "you have not installed, Setup can add it.";
                return;

            case CitationLookupResult { Outcome: CitationLookupOutcome.ReferenceNotFound } absent:
                _heading.Text = $"{absent.AuthorName}, {absent.WorkTitle}".Trim(' ', ',');
                _edition.Text = EditionLine(absent.Edition!);
                _note.Text = $"No passage of it is numbered {citation.Reference}." +
                             (absent.TextNodeId != null ? " Open at the Start opens it at its first passage." : string.Empty);
                _open.Content = "Open at the Start";
                Substituted(absent, citation);
                return;

            case CitationLookupResult { Outcome: CitationLookupOutcome.Found } found:
                var reference = PassageCitation.Display(found.CitationRef, found.Milestone);
                _heading.Text = $"{found.AuthorName}, {found.WorkTitle} {reference}".Trim(' ', ',');
                _edition.Text = EditionLine(found.Edition!);
                _passage.Text = PassageText.RowText(found.Text, 320);
                _passage.FontFamily = EchoUi.ReadingFontFor(found.Edition, found.Edition!.Kind == EditionKind.Original);
                if (citation.Reference.Length == 0) _note.Text = "The edition from its first passage.";
                Substituted(found, citation);
                return;
        }
    }

    private void Substituted(CitationLookupResult result, CopiedCitation citation)
    {
        if (result.Substituted)
            _note.Text = $"This library does not have {citation.Identifier}, so this is the same work in another edition, " +
                         "which may number it differently. " + _note.Text;
    }

    private void ShowProblem(string message)
    {
        Shown = null;
        _open.IsEnabled = false;
        _heading.Text = _edition.Text = _passage.Text = string.Empty;
        _note.Text = "The library could not be read: " + message;
    }

    private static string EditionLine(Edition edition) => $"{EditionLabels.Descriptor(edition)} · {edition.CtsUrn}";
}
