using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.Translation;

/// <summary>
/// Opened from a work's menu in the library, "Create Translation…".
/// Translates a whole original-language work into a brand new
/// Translation-kind edition, saved permanently to the library - the direct
/// answer to how much of the Renaissance and First1KGreek corpora sit
/// original-only with no translation at all.
///
/// Gemini-only, the same reasoning as Cross-Language Echo: this is optional,
/// bulk, speculative content generation, not core reading comprehension -
/// there's no reason it should need anyone's credit card.
///
/// Two things this window exists to get right, both learned from watching a
/// real 802-line work run:
///
///  - A whole work's translation can't come back in a single API response
///    (output-token budgets, not input context, are the real ceiling), so
///    this sends the work in batches and shows the translation building up
///    live. Results are tracked by citation ref, not insertion order, since
///    any line a batch didn't return gets one consolidated retry pass after
///    the main loop - an append-only approach would put those lines at the
///    end, out of order.
///  - A long work is genuinely slow - dozens of requests, several minutes, a
///    real chance of hitting the free tier's daily limit partway through. So
///    progress is saved to the library after every batch, automatically -
///    closing this window (or the app) mid-run loses nothing already
///    translated. Reopening Create Translation for the same work finds that
///    edition, loads what's done, and only sends the lines still missing.
///
/// Until it covers its source, the reader labels the edition INCOMPLETE with
/// how many lines are done (MainWindow.BuildCoverageNotesAsync).
/// </summary>
public sealed class CreateTranslationWindow : FeatureWindow
{
    private const int InterBatchDelayMs = 2500;
    private const string GeminiTranslatorLabel = "Gemini (AI-generated)";

    private readonly int _workId;
    private readonly string _workCtsUrn;
    private readonly string _authorName;
    private readonly string _workTitle;
    private readonly int _sourceEditionId;
    private readonly string? _sourceLanguage;
    private readonly string? _targetLanguage;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly WordIndexService _wordIndexService = new();
    private readonly WordIndexRepository _wordIndexRepo = new();

    private List<TextNode> _sourceNodes = new();
    private readonly Dictionary<string, string> _translatedByRef = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cancellation;

    /// <summary>
    /// Null until the first successful batch persists something - created
    /// once, then reused for every save after that, so repeated saves update
    /// the same edition instead of multiplying into a new one each time.
    /// "Start a New Attempt Instead" resets this to null on purpose, to begin
    /// a genuinely separate attempt.
    /// </summary>
    private int? _workingEditionId;

    private readonly TextBlock _header;
    private readonly TextBlock _resumeLabel;
    private readonly DockPanel _resumeRow;
    private readonly Button _startFresh;
    private readonly TextBlock _originalHeader;
    private readonly ListBox _originalList;
    private readonly TextBlock _translatedHeader;
    private readonly ListBox _translatedList;
    private readonly Button _translate;
    private readonly IconLabel _translateLabel;
    private readonly Button _stop;
    private readonly Button _saveNow;
    private readonly TextBlock _status;

    public CreateTranslationWindow(Work work, string authorName, int sourceEditionId, string? sourceLanguage, string? targetLanguage)
    {
        _workId = work.WorkId;
        _workCtsUrn = work.CtsUrn;
        _authorName = authorName;
        _workTitle = work.Title;
        _sourceEditionId = sourceEditionId;
        _sourceLanguage = sourceLanguage;
        _targetLanguage = targetLanguage ?? "eng";

        Title = "Create Translation";
        Width = 1060;
        Height = 760;
        MinWidth = 780;
        MinHeight = 560;

        _header = new TextBlock { Text = $"{authorName}, {work.Title} — loading...", Classes = { "h2" }, TextWrapping = TextWrapping.Wrap };

        var disclosure = new TextBlock
        {
            Text = "Sends this work to Gemini's API over the internet, in batches - the only part of " +
                   "Classica Codex that isn't offline. A long work can mean dozens of requests and several " +
                   "minutes. Progress is saved after every batch automatically, so closing this window (or " +
                   "hitting today's free-tier limit) never loses what's already translated - reopen this " +
                   "later to pick up exactly where it left off.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "warning" }
        };

        _resumeLabel = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _startFresh = new Button { Content = "Start a New Attempt Instead", Margin = new Thickness(12, 0, 0, 0) };
        _startFresh.Click += (_, _) => StartFresh();

        _resumeRow = new DockPanel { IsVisible = false };
        DockPanel.SetDock(_startFresh, Dock.Right);
        _resumeRow.Children.Add(_startFresh);
        _resumeRow.Children.Add(_resumeLabel);

        var top = new StackPanel { Spacing = 10 };
        top.Children.Add(_header);
        top.Children.Add(new Border { Classes = { "notice" }, Child = disclosure });
        top.Children.Add(_resumeRow);

        // One node per line rather than run together. Each node is a discrete
        // citable unit and the reader shows them one per row; running them
        // into a single paragraph stopped being tolerable once speech
        // attributions and stage directions joined the prose. What the
        // translator sees matches what the reader shows.
        _originalHeader = new TextBlock { Classes = { "section" } };
        _originalList = LineList("Cc.SourceFont");
        _translatedHeader = new TextBlock { Text = "TRANSLATION (NOT STARTED)", Classes = { "section" } };
        _translatedList = LineList("Cc.TranslationFont");

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*"), RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(0, 14, 0, 0) };
        _originalHeader.Margin = _translatedHeader.Margin = new Thickness(2, 0, 0, 6);
        var originalPage = new Border { Classes = { "page" }, Child = _originalList };
        var translatedPage = new Border { Classes = { "page" }, Child = _translatedList };
        Grid.SetColumn(_translatedHeader, 2);
        Grid.SetRow(originalPage, 1);
        Grid.SetRow(translatedPage, 1);
        Grid.SetColumn(translatedPage, 2);
        columns.Children.Add(_originalHeader);
        columns.Children.Add(_translatedHeader);
        columns.Children.Add(originalPage);
        columns.Children.Add(translatedPage);

        // Wide enough for its longest label, so the footer doesn't shift as
        // the count of remaining lines changes under it.
        _translateLabel = new IconLabel("Sparkle", "Translate with Gemini (free)");
        _translate = new Button { Content = _translateLabel, Classes = { "accent" }, IsEnabled = false, MinWidth = 290, HorizontalContentAlignment = HorizontalAlignment.Center };
        _translate.Click += async (_, _) => await OnTranslateAsync();

        _stop = new Button { Content = new IconLabel("Stop", "Stop"), IsEnabled = false, MinWidth = 84 };
        _stop.Click += (_, _) => _cancellation?.Cancel();

        _saveNow = new Button { Content = "Save Now", IsEnabled = false, MinWidth = 96 };
        _saveNow.Click += async (_, _) => await OnSaveNowAsync();

        var close = new Button { Content = "Close", MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        _status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0) };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(_translate);
        left.Children.Add(_stop);
        left.Children.Add(_saveNow);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(left);
        footer.Children.Add(close);
        footer.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(columns);
        Content = root;

        Opened += async (_, _) => await LoadSourceAndCheckForResumeAsync();
        Closed += (_, _) => _cancellation?.Cancel();
    }

    /// <summary>Whether anything has been written to the library, so the reader knows to reload.</summary>
    public bool PersistedAnything { get; private set; }

    /// <summary>The run in progress, or a finished task - a run's last save can land after the window has closed.</summary>
    public Task Running { get; private set; } = Task.CompletedTask;

    internal string StatusText => _status.Text ?? string.Empty;

    internal string ResumeText => _resumeRow.IsVisible ? _resumeLabel.Text ?? string.Empty : string.Empty;

    internal Button TranslateButton => _translate;

    internal int OriginalLineCount => _sourceNodes.Count;

    /// <summary>
    /// Source lines that actually have a translation attached - not the size
    /// of the dictionary.
    ///
    /// Those two used to be assumed identical, and every progress figure was
    /// computed from the dictionary count. They come apart the moment a key
    /// lands in it that doesn't correspond to a real line, which happened
    /// whenever the model echoed a citation ref back in a form that didn't
    /// match: "all lines translated", an empty preview, and an edition saved
    /// with nothing in it. GeminiTranslationService reconciles refs before
    /// they get here, but this is measured from the source lines anyway, so
    /// if it ever doesn't, the window under-reports rather than claiming a
    /// success that isn't there.
    /// </summary>
    private int TranslatedLineCount => _sourceNodes.Count(n => _translatedByRef.ContainsKey(n.CitationRef));

    private static ListBox LineList(string fontKey)
    {
        var font = (FontFamily)Application.Current!.FindResource(fontKey)!;
        return new ListBox
        {
            Classes = { "reader" },
            Padding = new Thickness(6, 8),
            ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = ScriptFonts.Compose(font, ScriptFonts.FamilyFor(text ?? string.Empty)),
                FontSize = 14,
                LineHeight = 20,
                Margin = new Thickness(8, 1)
            })
        };
    }

    private void SetStatus(string text, bool warning = false)
    {
        _status.Text = text;
        _status.Classes.Set("warning", warning);
        _status.Classes.Set("caption", !warning);
    }

    internal async Task LoadSourceAndCheckForResumeAsync()
    {
        try
        {
            _sourceNodes = await _textNodeRepo.GetByEditionAsync(_sourceEditionId);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't read the original: {ex.Message}", warning: true);
            return;
        }

        _originalHeader.Text = $"ORIGINAL ({TranslationLanguageNames.DisplayName(_sourceLanguage).ToUpperInvariant()})";
        _originalList.ItemsSource = _sourceNodes.Select(n => n.Text).ToList();
        _header.Text = $"{_authorName}, {_workTitle} — {_sourceNodes.Count:N0} line(s)";

        if (_sourceNodes.Count == 0)
        {
            SetStatus("This edition has no text to translate.");
            return;
        }

        // Look for a prior Gemini attempt on this same work - the most
        // recently created one if there's more than one, by EditionId,
        // since that's assigned in creation order. Anything found becomes
        // the working edition: further translation adds to it, and Save
        // updates it in place rather than creating another one.
        var existingEditions = await _editionRepo.GetByWorkAsync(_workId);
        var priorAttempt = existingEditions
            .Where(e => e.Translator == GeminiTranslatorLabel)
            .OrderByDescending(e => e.EditionId)
            .FirstOrDefault();

        if (priorAttempt != null)
        {
            foreach (var node in await _textNodeRepo.GetByEditionAsync(priorAttempt.EditionId))
            {
                // An edition saved by an earlier version of this feature
                // could hold the old "no translation returned" placeholder
                // for gaps rather than simply omitting them. Those aren't real
                // translations, and counting them as done would mean they
                // were never retried.
                if (node.Text == "[Gemini did not return a translation for this line]") continue;
                _translatedByRef[node.CitationRef] = node.Text;
            }

            _workingEditionId = priorAttempt.EditionId;
            _resumeLabel.Text = $"Resuming a previous attempt - {TranslatedLineCount:N0} of " +
                                $"{_sourceNodes.Count:N0} lines already translated.";
            _resumeRow.IsVisible = true;
        }

        RefreshTranslatedPreview();
    }

    /// <summary>
    /// Abandons resuming and starts a genuinely separate attempt - the next
    /// save creates a brand new edition rather than updating the one found.
    /// </summary>
    private void StartFresh()
    {
        _workingEditionId = null;
        _translatedByRef.Clear();
        _resumeRow.IsVisible = false;
        RefreshTranslatedPreview();
    }

    private void UpdateButtonsForCurrentProgress()
    {
        var translated = TranslatedLineCount;
        var remaining = _sourceNodes.Count - translated;
        _translate.IsEnabled = remaining > 0 && _cancellation == null;
        _translateLabel.Text = remaining == _sourceNodes.Count
            ? "Translate with Gemini (free)"
            : $"Translate Remaining {remaining:N0} Line(s)";
        _saveNow.IsEnabled = translated > 0;
        _translatedHeader.Text = $"TRANSLATION ({translated:N0} OF {_sourceNodes.Count:N0} LINES)";
    }

    private async Task OnTranslateAsync()
    {
        if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
        {
            await SettingsFeature.OpenAiSettingsAsync(this);
            if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey)) return;
        }

        // Only what isn't already translated - the whole point of resuming.
        var remainingNodes = _sourceNodes.Where(n => !_translatedByRef.ContainsKey(n.CitationRef)).ToList();
        if (remainingNodes.Count == 0) return;

        // Bounded by characters as well as lines - see TranslationBatches for
        // why a count of lines was the wrong unit. Planned before the
        // confirmation so the number of requests quoted is the real one.
        var plannedBatches = TranslationBatches.Plan(remainingNodes, n => n.Text.Length);

        // Always asked, whatever the confirmation setting: this is minutes of
        // requests, not one.
        var confirmed = await Alert.Confirm(this, "Send to Gemini's API?",
            $"This will send the remaining {remainingNodes.Count:N0} line(s) of {_workTitle} to Gemini's " +
            $"API over the internet, in about {plannedBatches.Count} separate requests. This could take several " +
            "minutes, and may run into today's free-tier usage limit before it finishes - whatever gets " +
            "translated is saved automatically as it goes, so nothing already done would be lost.\n\n" +
            "Continue?",
            "Send", "Cancel", AlertKind.Warning);
        if (!confirmed) return;

        var run = RunAsync(plannedBatches, TranslationSettings.GeminiApiKey!);
        Running = run;
        await run;
    }

    private async Task RunAsync(List<List<TextNode>> batches, string apiKey)
    {
        _cancellation = new CancellationTokenSource();
        _translate.IsEnabled = false;
        _startFresh.IsEnabled = false;
        _stop.IsEnabled = true;

        var missingAfterMainPass = new List<TextNode>();
        var stoppedEarly = false;

        // Why it stopped, kept so the summary can say it. A batch that fails
        // names its reason - a dead key, a retired model, the daily quota -
        // and a bare "Stopped" made every one of those look the same.
        string? stopReason = null;

        try
        {
            for (var b = 0; b < batches.Count; b++)
            {
                if (_cancellation.IsCancellationRequested) { stoppedEarly = true; break; }

                SetStatus($"Translating batch {b + 1} of {batches.Count} " +
                          $"({TranslatedLineCount:N0} of {_sourceNodes.Count:N0} lines so far)...");

                var missing = await TranslateOneBatchAsync(batches[b], apiKey, _cancellation.Token);
                if (missing == null) { stoppedEarly = true; stopReason = _status.Text; break; }

                missingAfterMainPass.AddRange(missing);
                RefreshTranslatedPreview();

                // Saved after every batch, not just at the end - a long run
                // can die partway through and shouldn't lose what came before.
                await PersistProgressAsync();

                if (b < batches.Count - 1 && !_cancellation.IsCancellationRequested)
                {
                    try { await Task.Delay(InterBatchDelayMs, _cancellation.Token); }
                    catch (OperationCanceledException) { stoppedEarly = true; break; }
                }
            }

            if (!stoppedEarly && missingAfterMainPass.Count > 0 && !_cancellation.IsCancellationRequested)
            {
                // Through the same planner as the main pass. Sending every
                // missing line in one request undid the batching: a work whose
                // lines are large enough to need splitting is exactly the work
                // whose retry would be enormous, so the retry timed out on
                // precisely the passages the batching existed to rescue.
                var retryBatches = TranslationBatches.Plan(missingAfterMainPass, n => n.Text.Length);

                for (var r = 0; r < retryBatches.Count; r++)
                {
                    if (_cancellation.IsCancellationRequested) break;

                    SetStatus($"Retrying {missingAfterMainPass.Count:N0} line(s) that didn't come back " +
                              $"the first time - batch {r + 1} of {retryBatches.Count}...");

                    // A null here is a condition the next batch would meet too.
                    // Stopping keeps its message on screen.
                    if (await TranslateOneBatchAsync(retryBatches[r], apiKey, _cancellation.Token) == null)
                    {
                        stoppedEarly = true;
                        stopReason = _status.Text;
                        break;
                    }

                    RefreshTranslatedPreview();
                    await PersistProgressAsync();

                    if (r < retryBatches.Count - 1 && !_cancellation.IsCancellationRequested)
                    {
                        try { await Task.Delay(InterBatchDelayMs, _cancellation.Token); }
                        catch (OperationCanceledException) { stoppedEarly = true; break; }
                    }
                }

                RefreshTranslatedPreview();
                await PersistProgressAsync();
            }
        }
        catch (Exception ex)
        {
            // A save that failed: what is on screen is still the last thing
            // that reached the library, and saying so beats a crash.
            stoppedEarly = true;
            stopReason = $"Stopped: couldn't save - {ex.Message}";
        }

        var translatedCount = TranslatedLineCount;
        var complete = translatedCount == _sourceNodes.Count;

        SetStatus(stoppedEarly
            ? (stopReason is { Length: > 0 } why && why.StartsWith("Stopped: ", StringComparison.Ordinal)
                ? $"{why} {translatedCount:N0} of {_sourceNodes.Count:N0} lines translated and saved - " +
                  "reopen this later to finish the rest."
                : $"Stopped - {translatedCount:N0} of {_sourceNodes.Count:N0} lines translated, already saved. " +
                  "Reopen this later to finish the rest.")
            : complete
                ? $"Finished - all {translatedCount:N0} lines translated and saved."
                : $"Finished this pass, but {_sourceNodes.Count - translatedCount:N0} line(s) never came back " +
                  "from Gemini. Already saved - reopen this later to try those again.",
            warning: !complete);

        _cancellation = null;
        _stop.IsEnabled = false;
        _startFresh.IsEnabled = true;
        UpdateButtonsForCurrentProgress();
    }

    private async Task<List<TextNode>?> TranslateOneBatchAsync(List<TextNode> batch, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            var passages = batch.Select(n => (n.CitationRef, n.Text)).ToList();
            var results = await GeminiTranslationService.TranslateBatchAsync(
                passages, _sourceLanguage, _targetLanguage, _authorName, _workTitle, apiKey, cancellationToken);

            foreach (var (citationRef, translatedText) in results) _translatedByRef[citationRef] = translatedText;

            return batch.Where(n => !_translatedByRef.ContainsKey(n.CitationRef)).ToList();
        }
        catch (OperationCanceledException)
        {
            return batch;
        }
        catch (TimeoutException)
        {
            // One slow batch is not a reason to abandon the other forty.
            //
            // Every failure used to end the run, which made a single timeout
            // as final as a dead API key - and the batch most likely to time
            // out is a big one near the start, so a work would stop on its
            // first request having saved nothing at all. A timeout says this
            // request took too long, not that the next one will. Reported as
            // lines that didn't come back, and picked up by the retry pass.
            return batch;
        }
        catch (Exception ex)
        {
            // Everything else does stop: a bad key, a retired model, or the
            // daily quota being gone are conditions the next batch would meet
            // as well, and grinding through forty more requests to be told the
            // same thing forty more times helps nobody.
            SetStatus($"Stopped: {ex.Message}", warning: true);
            return null;
        }
    }

    /// <summary>
    /// Rebuilds the preview in original source order every time, since the
    /// retry pass finishes after the main loop and would otherwise land at
    /// the end instead of back in place.
    /// </summary>
    private void RefreshTranslatedPreview()
    {
        _translatedList.ItemsSource = _sourceNodes
            .Select(n => _translatedByRef.TryGetValue(n.CitationRef, out var t) ? t : null)
            .Where(t => t != null)
            .Cast<string>()
            .ToList();
        UpdateButtonsForCurrentProgress();
    }

    private async Task OnSaveNowAsync()
    {
        _saveNow.IsEnabled = false;
        try
        {
            await PersistProgressAsync();
            await Alert.Info(this, "Saved",
                $"Saved - {TranslatedLineCount:N0} of {_sourceNodes.Count:N0} lines. It is listed in the reader as " +
                "\"trans. Gemini (AI-generated)\" once this window is closed.");
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Save failed", $"Couldn't save: {ex.Message}");
        }
        finally
        {
            _saveNow.IsEnabled = TranslatedLineCount > 0;
        }
    }

    /// <summary>
    /// Creates the working edition on first use, then matches its lines to
    /// what is translated. Only lines genuinely translated are written; a
    /// line not yet done is absent from the edition rather than filled with a
    /// placeholder, since an absent line is an honest case everywhere else
    /// (PassageAligner reports "no matching passage" correctly), and leaving
    /// it absent is what makes resuming possible to detect at all.
    /// </summary>
    private async Task PersistProgressAsync()
    {
        // Counted against the source lines, not the dictionary: a dictionary
        // holding only unattributable keys would otherwise create an edition
        // here and write no text into it, which is exactly the empty
        // "trans. Gemini (AI-generated)" edition this used to leave behind.
        if (TranslatedLineCount == 0) return;

        if (_workingEditionId == null)
        {
            var newCtsUrn = $"{_workCtsUrn}.ai-gemini-{DateTime.UtcNow:yyyyMMddHHmmss}";
            _workingEditionId = await _editionRepo.UpsertAsync(new Edition
            {
                WorkId = _workId,
                CtsUrn = newCtsUrn,
                Kind = EditionKind.Translation,
                Language = _targetLanguage,
                Translator = GeminiTranslatorLabel,
                SourcePath = null
            });
        }

        var nodesToSave = new List<TextNode>();
        var sortOrder = 0;
        foreach (var sourceNode in _sourceNodes)
        {
            if (!_translatedByRef.TryGetValue(sourceNode.CitationRef, out var translated)) continue;

            nodesToSave.Add(new TextNode
            {
                EditionId = _workingEditionId.Value,
                CitationRef = sourceNode.CitationRef,
                SortOrder = sortOrder++,
                Text = translated,

                // The translation of a stage direction is a stage direction.
                // Without this every row saved as a plain line, so a
                // translated cast list counted towards the translation's word
                // frequencies and could not be switched off in the reader.
                // Carried from the source node because the two editions share
                // citation refs - the same reference means the same thing on
                // both sides.
                NodeKind = sourceNode.NodeKind
            });
        }

        // Matched onto what is already there rather than cleared and
        // rewritten. A line keeps its TextNodeId across saves, which matters
        // because the word index is keyed on those ids: clearing and
        // re-inserting orphaned every index row on every batch, and cleaning
        // them up took a scan of the whole index under SQLite's single write
        // lock - long enough to lock the reader out of its own library mid-run.
        var changes = await _textNodeRepo.SyncEditionAsync(_workingEditionId.Value, nodesToSave);
        PersistedAnything = true;

        // ONLY WHEN THERE IS ALREADY AN INDEX TO KEEP FRESH, and that
        // condition is load-bearing rather than an optimisation.
        //
        // Every search path decides whether to use the word index by asking
        // whether it has any rows at all. On a library where the index was
        // never built that answer is no, and whole-word search correctly
        // falls back to scanning the text. Write one edition's worth of rows
        // into an empty index and the answer becomes yes, so every later
        // search consults an index that contains this AI translation and
        // nothing else: a library of two million lines answering out of a few
        // thousand, silently. Keeping an existing index current is what this
        // call is for; Setup is where a whole index gets built.
        if (changes.Any && await _wordIndexRepo.HasDataAsync())
        {
            await _wordIndexService.ApplyChangesAsync(changes);
        }
    }
}
