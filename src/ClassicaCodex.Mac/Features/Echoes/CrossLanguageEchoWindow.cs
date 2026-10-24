using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Research;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Echoes;

/// <summary>
/// Find Cross-Language Echo: a passage set against one chosen work in any
/// language, with Gemini asked where the second shares an image or an idea
/// with the first.
///
/// Find Echoes ranks by shared rare words - fast, free, offline, and honest
/// that it cannot see an echo between a Greek original and an English
/// translation of something else, since those are not the same words. This
/// exists for exactly that gap.
///
/// One work at a time, deliberately. There is no useful way to send a whole
/// multi-hundred-author library to a model in one request, and if there were,
/// the cost and the room for invention would grow with it. Pick one work; get
/// a real answer about that one relationship.
///
/// Gemini only, on purpose: this is newer and more speculative than
/// Translate, and a free feature should not need anyone's credit card. It uses
/// the Gemini key already kept for Translate.
///
/// What matters most is in <see cref="CrossLanguageEchoCheck"/>: every
/// citation the model reports is checked against the comparison work's real
/// passages before it is shown, and one that resolves to nothing is dropped
/// and counted, never displayed as if it were genuine.
/// </summary>
internal sealed class CrossLanguageEchoWindow : FeatureWindow
{
    private readonly TextNode _sourceNode;
    private readonly string? _sourceLanguage;
    private readonly string _sourceAuthorName;
    private readonly string _sourceWorkTitle;
    private readonly int _sourceEditionId;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionRepository _editionRepo = new();

    private readonly TextBox _workFilter;
    private readonly ListBox _workList;
    private readonly Button _findButton;
    private readonly TextBlock _findLabel;
    private readonly PathIcon _findIcon;
    private readonly TextBlock _status;
    private readonly ListBox _results;
    private readonly TextBlock _resultsMessage;

    /// <summary>
    /// Collapses a burst of typing in the work filter into one refill of the
    /// picker - the library filter's interval, for the library filter's
    /// reason: the list is one row per original edition, four thousand on a
    /// full library, and a refill per letter is felt.
    /// </summary>
    private readonly DispatcherTimer _workFilterDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };

    private List<(int WorkId, int EditionId, string AuthorName, string WorkTitle, string? Language)> _allEditions = new();
    private IReadOnlyList<VerifiedEcho> _verifiedResults = Array.Empty<VerifiedEcho>();
    private bool _busy;

    private int _comparisonWorkId;
    private GeminiEchoResult? _lastAiResult;
    private DateTime? _lastAiGeneratedUtc;
    private string? _lastTruncatedAtRef;

    // Captured with the work id when a comparison is run. The results are
    // bare passages that carry no attribution, and an exported passage with
    // no author or work on it is unusable.
    private string _comparisonAuthorName = string.Empty;
    private string _comparisonWorkTitle = string.Empty;

    public CrossLanguageEchoWindow(
        TextNode sourceNode, string? sourceLanguage, string sourceAuthorName, string sourceWorkTitle,
        int sourceEditionId, FontFamily sourceFont)
    {
        _sourceNode = sourceNode;
        _sourceLanguage = sourceLanguage;
        _sourceAuthorName = sourceAuthorName;
        _sourceWorkTitle = sourceWorkTitle;
        _sourceEditionId = sourceEditionId;

        Title = "Find Cross-Language Echo";
        Width = 840;
        Height = 760;
        MinWidth = 640;
        MinHeight = 560;

        // The source: who, where, and the whole passage, scrollable - unlike
        // the rare-word windows this is the text being sent, so all of it
        // shows.
        var sourceText = EchoUi.PassageText(sourceNode.Text, sourceFont, 15);
        var sourceBlock = new StackPanel { Spacing = 4 };
        sourceBlock.Children.Add(new TextBlock { Text = "LOOKING FOR ECHOES OF", Classes = { "section" } });
        sourceBlock.Children.Add(new TextBlock
        {
            Text = $"{sourceAuthorName}, {sourceWorkTitle} [{PassageCitation.Display(sourceNode.CitationRef, sourceNode.Milestone)}]",
            Classes = { "h3" },
            TextWrapping = TextWrapping.Wrap
        });
        sourceBlock.Children.Add(new ScrollViewer { MaxHeight = 86, Content = sourceText });
        var sourcePanel = new Border { Classes = { "panel" }, Padding = new Thickness(14, 10), Child = sourceBlock };

        // A funnel, not a magnifying glass: this narrows a list, it does not
        // search the library.
        _workFilter = new TextBox
        {
            Classes = { "search" },
            PlaceholderText = "Filter by author, work or language",
            InnerLeftContent = new PathIcon { Data = Glyphs.Get("Filter"), Width = 13, Height = 13, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.6 }
        };
        _workFilter.TextChanged += (_, _) =>
        {
            _workFilterDebounce.Stop();
            _workFilterDebounce.Start();
        };
        _workFilterDebounce.Tick += (_, _) =>
        {
            _workFilterDebounce.Stop();
            RefreshWorkList();
        };

        _workList = new ListBox
        {
            Height = 150,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = EchoUi.Rows<WorkOption>(option => new TextBlock
            {
                Text = option.Label,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, 0)
            })
        };
        _workList.SelectionChanged += (_, _) => RefreshFindButtonState();
        var workPanel = new Border { Classes = { "page" }, Child = _workList, Margin = new Thickness(0, 6, 0, 0) };

        _findIcon = new PathIcon { Width = 14, Height = 14 };
        _findLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var findContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        findContent.Children.Add(_findIcon);
        findContent.Children.Add(_findLabel);
        _findButton = new Button { Content = findContent, Classes = { "accent" }, MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
        _findButton.Click += async (_, _) => await OnFindButtonClickAsync();

        // Under the button rather than beside it: after a search it runs to
        // four sentences - what survived, what is flagged, what was dropped,
        // how far the search reached - and each is worth reading.
        _status = new TextBlock
        {
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 8, 0, 0),
            IsVisible = false
        };

        var findRow = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        findRow.Children.Add(_findButton);
        findRow.Children.Add(_status);

        _results = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Padding = new Thickness(6),
            ItemTemplate = EchoUi.Rows<VerifiedEcho>(BuildResultRow)
        };
        EchoUi.CompactRows(_results);
        _results.DoubleTapped += async (_, e) =>
        {
            if (EchoUi.OnARow(e.Source)) await OnResultActivatedAsync();
        };
        _results.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await OnResultActivatedAsync();
        };

        // The rationale is the point of this view: it is why a candidate is
        // offered at all, and unlike the passage it exists nowhere else in the
        // library. Exporting the passages without it would throw away the one
        // part the reader cannot already reach.
        var menu = new ContextMenu();
        menu.Items.Add(EchoUi.MenuItem("Export All Passages…", "Export", ExportAllAsync));
        _results.ContextMenu = menu;
        EchoUi.SelectRowOnContextRequest(_results);

        _resultsMessage = new TextBlock
        {
            Classes = { "muted" },
            Text = "Pick a work to compare against, then ask Gemini. Verified candidates appear here.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 380,
            Margin = new Thickness(24)
        };
        var resultsGrid = new Grid();
        resultsGrid.Children.Add(_results);
        resultsGrid.Children.Add(_resultsMessage);

        var save = new Button { Content = "Save Investigation…", MinWidth = 150 };
        save.Click += async (_, _) => await SaveInvestigationAsync();
        var close = new Button { Content = "Close", MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        DockPanel.SetDock(save, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(save);
        footer.Children.Add(close);

        var top = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(sourcePanel);
        top.Children.Add(new TextBlock
        {
            Text = "Compare against (type to filter - any author, any language):",
            Classes = { "h3" },
            Margin = new Thickness(0, 16, 0, 6)
        });
        top.Children.Add(_workFilter);
        top.Children.Add(workPanel);
        top.Children.Add(findRow);
        top.Children.Add(new TextBlock
        {
            Text = "Ranked by the AI's own confidence, not proof of borrowing - candidates worth a human " +
                   "look, the same standard Find Echoes holds itself to. Double-click a result to open it.",
            Classes = { "muted" },
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0)
        });

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(footer);
        layout.Children.Add(new Border { Classes = { "page" }, Child = resultsGrid });
        Content = layout;

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && EchoUi.IsCommand(e))
            {
                e.Handled = true;
                await SaveInvestigationAsync();
            }
        };

        RefreshFindButtonState();
        Opened += (_, _) => LoadTask = LoadWorkListAsync();
        Closed += (_, _) => _workFilterDebounce.Stop();
    }

    internal Task? LoadTask { get; private set; }

    internal ListBox WorkList => _workList;

    internal TextBox WorkFilter => _workFilter;

    internal Button FindButton => _findButton;

    internal string FindButtonText => _findLabel.Text ?? string.Empty;

    internal string StatusText => _status.Text ?? string.Empty;

    /// <summary>Runs the filter now rather than after the pause, for the tests.</summary>
    internal void FlushWorkFilter()
    {
        _workFilterDebounce.Stop();
        RefreshWorkList();
    }

    internal Task ClickFindAsync() => OnFindButtonClickAsync();

    private async Task LoadWorkListAsync()
    {
        try
        {
            _allEditions = await _editionRepo.GetAllOriginalEditionsAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't read the library: {ex.Message}", warning: true);
            return;
        }

        RefreshWorkList();
    }

    private void RefreshWorkList()
    {
        var filter = (_workFilter.Text ?? string.Empty).Trim();
        var selectedEdition = (_workList.SelectedItem as WorkOption)?.Edition.EditionId;

        var wanted = new List<WorkOption>();
        foreach (var edition in _allEditions)
        {
            // The source's own edition is left out: "does this echo itself" is
            // not the question this answers, and it would only ever trivially
            // match.
            if (edition.EditionId == _sourceEditionId) continue;

            var label = $"{edition.AuthorName} — {edition.WorkTitle} ({edition.Language?.ToUpperInvariant() ?? "?"})";
            if (filter.Length == 0 || label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                wanted.Add(new WorkOption(edition, label));
        }

        _workList.ItemsSource = wanted;
        if (selectedEdition != null)
            _workList.SelectedItem = wanted.FirstOrDefault(w => w.Edition.EditionId == selectedEdition);

        RefreshFindButtonState();
    }

    private void RefreshFindButtonState()
    {
        var hasSelection = _workList.SelectedItem is WorkOption;
        var hasKey = !string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey);

        // The other side of the argument that filters the comparison corpus to
        // reading lines: if the line this was opened on is not the author's own
        // words, there is nothing to look for an echo of. Right-clicking a
        // speaker's name would otherwise spend an API call asking which
        // passages of Aeschylus resemble "Ber.".
        _findButton.IsEnabled = !_busy && ((hasSelection && SourceIsReadingText) || !hasKey);
        _findLabel.Text = !hasKey ? "Configure Gemini Key…"
            : !SourceIsReadingText
                ? $"{NodeKindVisibility.Label(_sourceNode.NodeKind)} can't be echoed - pick a line of text"
            : hasSelection ? "Find Echoes with Gemini (free)"
            : "Pick a work above first";
        _findIcon.Data = Glyphs.Get(hasKey ? "Sparkle" : "Key");
    }

    /// <summary>
    /// Whether the line this was opened on is the author's own words rather
    /// than a speech attribution, a stage direction or a heading. Blank counts
    /// as reading text, so an edition ingested before node kinds existed
    /// behaves as it always did.
    /// </summary>
    private bool SourceIsReadingText =>
        string.IsNullOrWhiteSpace(_sourceNode.NodeKind)
        || string.Equals(_sourceNode.NodeKind, TextNodeKinds.Line, StringComparison.OrdinalIgnoreCase);

    private async Task OnFindButtonClickAsync()
    {
        if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
        {
            await SettingsFeature.OpenAiSettingsAsync(this);
            RefreshFindButtonState();
            return;
        }

        if (_workList.SelectedItem is not WorkOption selected || !SourceIsReadingText) return;

        if (TranslationSettings.AlwaysConfirmBeforeSending)
        {
            var confirmed = await Alert.Confirm(this, "Send to Gemini's API?",
                $"This will send the selected passage, and the text of {selected.Edition.AuthorName}'s " +
                $"{selected.Edition.WorkTitle}, to Gemini's API over the internet to look for echoes. This " +
                "is the same kind of network use Translate already discloses.\n\n" +
                "Continue? (You can turn this confirmation off in AI Translation Settings.)",
                "Send", "Cancel", AlertKind.Warning);

            if (!confirmed) return;
        }

        _busy = true;
        _findButton.IsEnabled = false;
        SetStatus("Reading the comparison work…", warning: false);
        _verifiedResults = Array.Empty<VerifiedEcho>();
        _results.ItemsSource = null;
        _resultsMessage.IsVisible = false;
        _lastAiResult = null;
        _lastAiGeneratedUtc = null;
        _comparisonWorkId = selected.Edition.WorkId;
        _comparisonAuthorName = selected.Edition.AuthorName;
        _comparisonWorkTitle = selected.Edition.WorkTitle;

        try
        {
            // Reading lines only, for the prompt and for the check after it.
            //
            // An echo is a resemblance between things an author wrote. "Ber."
            // and "Enter the Ghost" are not candidates, and offering them
            // invites a match on the stage business two plays happen to share
            // rather than on their language.
            //
            // They also cost room. The prompt stops at the cap and says where,
            // so anything included is something else left out: in Hamlet the
            // non-line nodes are 11.2% of the tagged text, in Gorgias 10.2%,
            // and the play is close enough to the ceiling that a tenth matters.
            var comparisonNodes = await _textNodeRepo.GetByEditionAsync(selected.Edition.EditionId, readingLinesOnly: true);

            var (taggedText, truncatedAtRef) = CrossLanguageEchoCheck.BuildTaggedComparisonText(comparisonNodes);
            _lastTruncatedAtRef = truncatedAtRef;

            SetStatus("Asking Gemini…", warning: false);
            var apiKey = TranslationSettings.GeminiApiKey!;
            _lastAiResult = await GeminiTranslationService.FindEchoesWithProvenanceAsync(
                _sourceNode.Text, _sourceLanguage, _sourceAuthorName, _sourceWorkTitle, _sourceNode.CitationRef,
                selected.Edition.AuthorName, selected.Edition.WorkTitle, selected.Edition.Language,
                taggedText, apiKey);
            _lastAiGeneratedUtc = DateTime.UtcNow;

            ShowVerification(
                CrossLanguageEchoCheck.Verify(_lastAiResult.Candidates.ToList(), comparisonNodes, _sourceNode.Text),
                truncatedAtRef);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't finish: {ex.Message}", warning: true);
        }
        finally
        {
            _busy = false;
            RefreshFindButtonState();
        }
    }

    /// <summary>Shows the candidates that survived the checks, and the line saying what was dropped and flagged.</summary>
    internal void ShowVerification(EchoVerification verification, string? truncatedAtRef)
    {
        _verifiedResults = verification.Verified;
        _results.ItemsSource = _verifiedResults;
        _resultsMessage.Text = "No verified echoes in this work.";
        _resultsMessage.IsVisible = _verifiedResults.Count == 0;
        // The reference as the reader would cite it; the stored form, which
        // is what a lookup matches, stays in the saved investigation's scope.
        SetStatus(CrossLanguageEchoCheck.Summary(verification,
            truncatedAtRef == null ? null : PassageCitation.Display(truncatedAtRef)), warning: false);
    }

    private void SetStatus(string text, bool warning)
    {
        _status.Text = text;
        _status.IsVisible = !string.IsNullOrEmpty(text);
        _status.Classes.Set("warning", warning);
        _status.Classes.Set("caption", !warning);
    }

    private async Task SaveInvestigationAsync()
    {
        if (_verifiedResults.Count == 0 || _lastAiResult == null)
        {
            await Alert.Info(this, "Nothing to save", "Run a search with verified candidates before saving it.");
            return;
        }

        var source = await _textNodeRepo.GetPassageResearchIdentityAsync(_sourceNode.TextNodeId);
        if (source == null)
        {
            await Alert.Warn(this, "Passage not found", "The source passage is no longer present in the local corpus.");
            return;
        }

        var scope = $"{_comparisonAuthorName}, {_comparisonWorkTitle}" +
                    (_lastTruncatedAtRef == null ? " (complete ingested edition)" : $" (through {_lastTruncatedAtRef})");
        var request = new EchoCaptureRequest(
            ResearchEchoMethod.AiCrossLanguage, source,
            $"Cross-language echoes: {source.WorkTitle} {PassageCitation.Display(source.CitationRef, source.Milestone)} → {_comparisonWorkTitle}",
            scope,
            "Gemini thematic/imagistic comparison. Returned citations were resolved exactly against local reading-text nodes; unsupported words in rationales remain flagged for human checking.",
            _lastAiResult.Model, _lastAiResult.PromptProvenance, _lastAiGeneratedUtc,
            _verifiedResults.Select(r => new EchoCaptureCandidate(
                _comparisonWorkId, r.Node.TextNodeId, _comparisonAuthorName, _comparisonWorkTitle,
                r.Node.CitationRef, r.Node.Text, null, r.Candidate.Confidence, r.Candidate.Rationale)).ToList());

        if (await ResearchFeature.CaptureEchoAsync(this, request))
        {
            await Alert.Info(this, "Investigation saved",
                "The verified candidates and Gemini provenance are saved for review in Research Bench → Project → Echo investigations.");
        }
    }

    private Task ExportAllAsync() =>
        PassageSetExport.OpenAsync(this,
            $"Cross-language echoes of [{PassageCitation.Display(_sourceNode.CitationRef, _sourceNode.Milestone)}]",
            _verifiedResults.Select(r => new ExportPassage(
                _comparisonWorkId,
                r.Node.TextNodeId,
                _comparisonAuthorName,
                _comparisonWorkTitle,
                r.Node.CitationRef,
                r.Node.Text,
                $"[{r.Candidate.Confidence}] {r.Candidate.Rationale}",
                r.Node.Milestone)).ToList(),
            "why each was suggested");

    private async Task OnResultActivatedAsync()
    {
        if (_results.SelectedItem is VerifiedEcho echo) await NavigateAsync(_comparisonWorkId, echo.Node.TextNodeId);
    }

    private static Control BuildResultRow(VerifiedEcho echo)
    {
        var confidence = new Border
        {
            Classes = { "notice" },
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = echo.Candidate.Confidence, FontSize = 11, FontWeight = FontWeight.SemiBold }
        };

        var reference = new TextBlock
        {
            Text = PassageCitation.Bracketed(echo.Node.CitationRef, echo.Node.Milestone),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(confidence);
        head.Children.Add(reference);

        var stack = new StackPanel { Spacing = 4, Margin = new Thickness(4, 6) };
        stack.Children.Add(head);
        stack.Children.Add(EchoUi.PassageText(EchoUi.Cut(echo.Node.Text, EchoUi.RowTextLimit), EchoUi.SourceFont, 14.5, maxLines: 3));
        stack.Children.Add(new TextBlock { Text = echo.Candidate.Rationale, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });

        if (CrossLanguageEchoCheck.Flag(echo.UnsupportedWords) is { } flag)
        {
            var warning = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var icon = new PathIcon { Data = Glyphs.Get("Warning"), Width = 12, Height = 12 };
            icon.Bind(PathIcon.ForegroundProperty, icon.GetResourceObservable("Cc.Danger"));
            warning.Children.Add(icon);
            warning.Children.Add(new TextBlock { Text = flag, Classes = { "warning" }, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(warning);
        }

        return stack;
    }

    private sealed record WorkOption(
        (int WorkId, int EditionId, string AuthorName, string WorkTitle, string? Language) Edition,
        string Label)
    {
        public override string ToString() => Label;
    }
}
