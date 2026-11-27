using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
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
/// A box for a citation, and beneath it what the citation opens - found as it
/// is typed or pasted, so the reader sees where Return will take them before
/// pressing it.
///
/// Two kinds of citation go in the box. One copied from the Atlas of Ancient
/// Myth or the reports, an edition's identifier and a reference
/// ("tlg0020.tlg001.perseus-grc2 457"), shows its line, and Return opens it
/// and closes the window. Or references as books print them, a footnote's
/// worth at once ("Thuc. 2.40.1; cf. Pl. Resp. 514a"): one shows like a copied
/// citation; several are listed, each with its line, and Return opens the one
/// chosen and moves to the next, the window staying open so a footnote can be
/// read through.
/// </summary>
public sealed class GoToCitationWindow : Window
{
    public const string Example = "tlg0020.tlg001.perseus-grc2 457";

    public const string FootnoteExample = "Thuc. 2.40.1; Pl. Resp. 514a";

    // The reports cite a manuscript line of Medieval Hands as
    // "CatmusLines – Paris, BnF, lat. 9768 #120078": no edition, no reference.
    private static readonly Regex ManuscriptLine = new(@"^\s*CatmusLines\s*[–—-]\s*(?<shelfmark>[^#]+)#", RegexOptions.CultureInvariant);

    private readonly CopiedCitationResolver _resolver;
    private readonly Func<object, Task> _open;
    private readonly string? _clipboard;
    private readonly TextBox _box;
    private readonly StackPanel _found = new() { Spacing = 4, MinHeight = 64 };
    private readonly TextBlock _heading = new() { Classes = { "h3" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _edition = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _passage = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
    private readonly ListBox _references = new() { MaxHeight = 340, IsVisible = false };
    private readonly TextBlock _note = new() { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
    private readonly Button _openButton = new() { Content = "Open", Classes = { "accent" }, IsDefault = true, MinWidth = 84, IsEnabled = false };

    private CancellationTokenSource? _typing;

    /// <summary>A museum object named that the library does not hold.</summary>
    private sealed record MissingArtifact(string Name);

    /// <param name="resolver">Looks citations up.</param>
    /// <param name="open">Opens what a citation names: a <see cref="CitationLookupResult"/> with a passage, or a <see cref="CitedArtifact"/>.</param>
    /// <param name="initial">A citation to start with, already known to read as one.</param>
    /// <param name="clipboard">
    /// What is on the clipboard, put in the box once the window has looked it
    /// up and found it cites something - a footnote copied from an article,
    /// say - and left out otherwise.
    /// </param>
    public GoToCitationWindow(CopiedCitationResolver resolver, Func<object, Task> open, string initial = "", string? clipboard = null)
    {
        _resolver = resolver;
        _open = open;
        _clipboard = clipboard;

        Title = "Go to Citation";
        SizeToContent = SizeToContent.Height;
        Width = 600;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _box = new TextBox
        {
            Text = initial,
            PlaceholderText = $"{Example}   or   {FootnoteExample}",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 120,
        };
        _box.TextChanged += (_, _) => Look(delay: true);

        // Return opens; Shift+Return is a new line in a pasted footnote.
        _box.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;
            e.Handled = true;
            await OpenAsync();
        }, RoutingStrategies.Tunnel);

        _references.ItemTemplate = new FuncDataTemplate<FootnoteLookup>((lookup, _) => Row(lookup));
        _references.SelectionChanged += (_, _) => _openButton.IsEnabled = Opens((_references.SelectedItem as FootnoteLookup)?.Result);
        _references.DoubleTapped += async (_, _) => await OpenAsync();
        _references.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await OpenAsync();
        };

        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 84 };
        close.Click += (_, _) => Close();
        _openButton.Click += async (_, _) => await OpenAsync();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(close);
        buttons.Children.Add(_openButton);

        _found.Children.Add(_heading);
        _found.Children.Add(_edition);
        _found.Children.Add(_passage);

        var layout = new StackPanel { Margin = new Thickness(22, 18), Spacing = 12 };
        layout.Children.Add(new TextBlock
        {
            Text = $"A citation copied from the Atlas of Ancient Myth or the reports ({Example}), or references as books " +
                   $"print them ({FootnoteExample}). Paste a whole footnote to open each reference in turn.",
            TextWrapping = TextWrapping.Wrap
        });
        layout.Children.Add(_box);
        layout.Children.Add(_found);
        layout.Children.Add(_references);
        layout.Children.Add(_note);
        layout.Children.Add(buttons);
        Content = layout;

        Opened += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
            if (string.IsNullOrEmpty(_box.Text) && !string.IsNullOrWhiteSpace(_clipboard)) OfferClipboard(_clipboard);
            else Look(delay: false);
        };
    }

    /// <summary>
    /// What the window shows now: a lookup result, an object, the references
    /// of a footnote, or null. For the tests.
    /// </summary>
    internal object? Shown { get; private set; }

    internal string HeadingText => _heading.Text ?? string.Empty;

    internal string NoteText => _note.Text ?? string.Empty;

    internal TextBox Box => _box;

    internal Button OpenButton => _openButton;

    internal ListBox References => _references;

    /// <summary>The clipboard, put in the box if it cites anything the library can name.</summary>
    private void OfferClipboard(string clipboard)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var lookups = await _resolver.LookUpFootnoteAsync(clipboard);
                if (!lookups.Any(l => l.Recognised)) return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!string.IsNullOrEmpty(_box.Text)) return;
                    _box.Text = clipboard.Trim();
                    _box.SelectAll();
                });
            }
            catch (Exception)
            {
                // The clipboard is only a suggestion; the box stays empty.
            }
        });
    }

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

    /// <summary>
    /// A copied citation looked up, or else the references the text holds;
    /// a footnote with one reference comes back as that reference's result.
    /// </summary>
    private async Task<object?> FindAsync(string? text, CancellationToken cancellationToken)
    {
        CitationLookupResult? missing = null;
        if (CopiedCitation.Parse(text) is { } citation && (citation.ArtifactName != null || LooksCopied(citation)))
        {
            if (citation.ArtifactName is { } name)
                return await CopiedCitationResolver.LookUpArtifactAsync(name, cancellationToken) as object ?? new MissingArtifact(name);

            var copied = await _resolver.LookUpAsync(citation, cancellationToken);
            if (copied.Outcome != CitationLookupOutcome.NotInLibrary) return copied;
            missing = copied;
        }

        // Read as a book's references; failing those, a copied citation the
        // library does not have says so.
        var lookups = await _resolver.LookUpFootnoteAsync(text, cancellationToken);
        if (lookups.Any(l => l.Recognised)) return lookups;
        return missing as object ?? (lookups.Count > 0 ? lookups : null);
    }

    /// <summary>
    /// Whether a citation may be one copied from the pages
    /// ("tlg0020.tlg001.perseus-grc2 457") rather than a book's abbreviation
    /// ("Thuc. 2.40.1"), which <see cref="CopiedCitation"/> would also read,
    /// identifier and reference: an abbreviation ends in its point, and an
    /// identifier never does. One the library does not have is read as a
    /// book's references after all.
    /// </summary>
    private static bool LooksCopied(CopiedCitation citation) => !citation.Identifier.EndsWith('.');

    private static bool Opens(object? result) =>
        result is CitedArtifact || result is CitationLookupResult { TextNodeId: not null };

    /// <summary>
    /// Return, or the Open button. A single citation is looked up afresh, so
    /// one pasted a moment ago is not missed, opened, and the window closes.
    /// In a list, the reference chosen is opened, the next is chosen, and the
    /// window stays.
    /// </summary>
    private async Task OpenAsync()
    {
        if (_references.IsVisible)
        {
            if (_references.SelectedItem is not FootnoteLookup { Result: { } chosen } || !Opens(chosen)) return;
            await _open(chosen);

            var items = _references.ItemsSource?.Cast<FootnoteLookup>().ToList() ?? new();
            var next = items.Skip(_references.SelectedIndex + 1).FirstOrDefault(l => Opens(l.Result));
            if (next != null) _references.SelectedItem = next;
            return;
        }

        _typing?.Cancel();
        var text = _box.Text;

        try
        {
            var result = await FindAsync(text, CancellationToken.None);
            var target = result is IReadOnlyList<FootnoteLookup> { Count: > 0 } lookups && lookups.Count(l => l.Recognised) == 1
                ? lookups.First(l => l.Recognised).Result
                : result;

            if (Opens(target))
            {
                await _open(target!);
                Close();
            }
            else
            {
                Show(text, result);
            }
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
        _openButton.Content = "Open";
        _openButton.IsEnabled = Opens(result);
        _references.IsVisible = false;
        _references.ItemsSource = null;
        _found.IsVisible = true;

        // Numbers with words in front that name nothing: said as such, below.
        var unread = result is IReadOnlyList<FootnoteLookup> all && !all.Any(l => l.Recognised) ? all : null;
        if (unread != null) result = null;

        switch (result)
        {
            case null:
                if (ManuscriptLine.Match(text ?? string.Empty) is { Success: true } line)
                    _note.Text = $"That is a line of a manuscript in Medieval Hands, not a passage of the library: open Medieval Hands " +
                                 $"from the Tools menu and choose {line.Groups["shelfmark"].Value.Trim()}.";
                else if (unread != null)
                    _note.Text = $"No author or work this library knows in {string.Join("; ", unread.Select(l => l.Written))}. " +
                                 $"A book's reference has an author or a work and a number, such as {FootnoteExample}.";
                else if (!string.IsNullOrWhiteSpace(text))
                    _note.Text = $"No citation there. A copied one starts with an identifier, such as {Example}; " +
                                 $"a book's has an author or a work and a number, such as {FootnoteExample}.";
                return;

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

            case CitationLookupResult copied:
                ShowOne(copied, CopiedCitation.Parse(text)?.Identifier, CopiedCitation.Parse(text)?.Reference ?? string.Empty, written: null);
                return;

            case IReadOnlyList<FootnoteLookup> lookups:
                ShowFootnote(lookups);
                return;
        }
    }

    /// <summary>One passage: a copied citation's, or a footnote's only reference.</summary>
    private void ShowOne(CitationLookupResult result, string? identifier, string reference, string? written)
    {
        _openButton.IsEnabled = Opens(result);

        switch (result.Outcome)
        {
            case CitationLookupOutcome.NotInLibrary:
                _note.Text = written != null
                    ? $"{written}: this library does not have the work it names. If it belongs to a collection you have not installed, Setup can add it."
                    : $"Nothing in this library has the identifier {identifier}. If it belongs to a collection you have not installed, Setup can add it.";
                return;

            case CitationLookupOutcome.ReferenceNotFound:
                _heading.Text = $"{result.AuthorName}, {result.WorkTitle}".Trim(' ', ',');
                _edition.Text = EditionLine(result.Edition!);
                _note.Text = $"No passage of it is numbered {reference}." +
                             (result.TextNodeId != null ? " Open at the Start opens it at its first passage." : string.Empty);
                _openButton.Content = "Open at the Start";
                break;

            case CitationLookupOutcome.Found:
                var shown = PassageCitation.Display(result.CitationRef, result.Milestone);
                _heading.Text = $"{result.AuthorName}, {result.WorkTitle} {shown}".Trim(' ', ',');
                _edition.Text = EditionLine(result.Edition!);
                _passage.Text = PassageText.RowText(result.Text, 320);
                _passage.FontFamily = EchoUi.ReadingFontFor(result.Edition, result.Edition!.Kind == EditionKind.Original);
                if (reference.Length == 0) _note.Text = "The edition from its first passage.";
                if (written != null) _note.Text = ($"Read as {written}. " + _note.Text).Trim();
                break;
        }

        if (result.Substituted && identifier != null)
            _note.Text = $"This library does not have {identifier}, so this is the same work in another edition, " +
                         "which may number it differently. " + _note.Text;
    }

    /// <summary>The references of a footnote: one shown as a passage, several listed.</summary>
    private void ShowFootnote(IReadOnlyList<FootnoteLookup> lookups)
    {
        var recognised = lookups.Where(l => l.Recognised).ToList();
        var unread = lookups.Where(l => !l.Recognised).Select(l => l.Written).ToList();

        if (recognised.Count == 1)
        {
            ShowOne(recognised[0].Result!, identifier: null, recognised[0].Reference.Reference, recognised[0].Written);
        }
        else
        {
            _found.IsVisible = false;
            _references.IsVisible = true;
            _references.ItemsSource = recognised;
            _references.SelectedItem = recognised.FirstOrDefault(l => Opens(l.Result)) ?? recognised[0];
            _openButton.IsEnabled = Opens((_references.SelectedItem as FootnoteLookup)?.Result);
            var found = recognised.Count(l => l.Result!.Outcome == CitationLookupOutcome.Found);
            _note.Text = $"{recognised.Count} references, {found} found. Return opens the one chosen and moves to the next.";
        }

        if (unread.Count > 0)
        {
            var before = _note.Text ?? string.Empty;
            _note.Text = before + (before.Length > 0 ? " " : string.Empty) + $"Not read as references: {string.Join("; ", unread)}.";
        }
    }

    /// <summary>A footnote's reference in the list: as written, what it found, and the passage.</summary>
    private static Control Row(FootnoteLookup lookup)
    {
        var row = new StackPanel { Spacing = 2, Margin = new Thickness(2, 4) };
        row.Children.Add(new TextBlock { Text = lookup.Written, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });

        var result = lookup.Result!;
        switch (result.Outcome)
        {
            case CitationLookupOutcome.Found:
                row.Children.Add(new TextBlock
                {
                    Text = $"{result.AuthorName}, {result.WorkTitle} {PassageCitation.Display(result.CitationRef, result.Milestone)} · {EditionLabels.Descriptor(result.Edition!)}",
                    Classes = { "caption" },
                    TextWrapping = TextWrapping.Wrap
                });
                row.Children.Add(new TextBlock
                {
                    Text = PassageText.RowText(result.Text, 180),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = EchoUi.ReadingFontFor(result.Edition, result.Edition!.Kind == EditionKind.Original)
                });
                break;

            case CitationLookupOutcome.ReferenceNotFound:
                row.Children.Add(new TextBlock
                {
                    Text = $"{result.AuthorName}, {result.WorkTitle}: no passage numbered {lookup.Reference.Reference}. Opens at its start.",
                    Classes = { "caption" },
                    TextWrapping = TextWrapping.Wrap
                });
                break;

            default:
                row.Children.Add(new TextBlock { Text = "Not in this library.", Classes = { "caption" } });
                break;
        }

        return row;
    }

    private void ShowProblem(string message)
    {
        Shown = null;
        _openButton.IsEnabled = false;
        _references.IsVisible = false;
        _found.IsVisible = true;
        _heading.Text = _edition.Text = _passage.Text = string.Empty;
        _note.Text = "The library could not be read: " + message;
    }

    private static string EditionLine(Edition edition) => $"{EditionLabels.Descriptor(edition)} · {edition.CtsUrn}";
}
