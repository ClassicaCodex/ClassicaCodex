using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Opened by right-clicking a work in the library tree and choosing
/// "Create Translation...". Translates a whole original-language work into
/// a brand new Translation-kind edition, saved permanently to the library -
/// the direct answer to how much of the Renaissance and First1KGreek
/// corpora sit original-only with no translation at all.
///
/// Gemini-only, same reasoning as Cross-Language Echo: this is optional,
/// bulk, speculative content generation, not core reading comprehension -
/// there's no reason it should need anyone's credit card.
///
/// Two things this form exists to get right, both learned from watching a
/// real 802-line work actually run:
///
///  - A whole work's translation can't come back in a single API response
///    (output-token budgets, not input context, are the real ceiling), so
///    this sends the work in batches and shows the translation building up
///    live. Results are tracked by citation ref in a dictionary, not
///    insertion order, since any line a batch didn't return gets one
///    consolidated retry pass *after* the main loop - an append-only
///    approach would put those lines at the end, out of order.
///  - A long work is genuinely slow - dozens of requests, several minutes,
///    a real and evidenced chance of hitting the free tier's daily limit
///    partway through. So progress is persisted to the database after every
///    single batch, automatically, not only when a Save button is clicked -
///    closing this dialog (or the whole app) mid-run loses nothing already
///    translated. Reopening "Create Translation" for the same work later
///    finds that edition, loads what's already done, and only sends the
///    lines still missing - it doesn't start over and doesn't re-spend
///    quota on lines already finished.
/// </summary>
public class CreateTranslationForm : ScaledForm
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

    /// <summary>
    /// Source lines that actually have a translation attached - not the size
    /// of _translatedByRef.
    ///
    /// Those two used to be assumed identical, and every progress figure in
    /// this dialog was computed from the dictionary count. They come apart
    /// the moment a key lands in the dictionary that doesn't correspond to a
    /// real line, which is what happened whenever the model echoed a citation
    /// ref back in a form that didn't match: "all lines translated", an empty
    /// preview, and an edition saved with nothing in it. GeminiTranslationService
    /// now reconciles refs before they get here, so this should always agree
    /// with the dictionary - but it's measured from the source lines anyway,
    /// so if it ever doesn't, the dialog under-reports rather than claiming a
    /// success that isn't there.
    /// </summary>
    private int TranslatedLineCount =>
        _sourceNodes.Count(n => _translatedByRef.ContainsKey(n.CitationRef));
    private CancellationTokenSource? _cancellation;

    // Null until the first successful batch persists something - created
    // once, then reused for every save after that (including auto-saves),
    // so repeated saves update the same edition instead of multiplying into
    // a new one each time. "Start Fresh Instead" resets this to null on
    // purpose, to begin a genuinely separate attempt.
    private int? _workingEditionId;

    private readonly Label _headerLabel;
    private readonly Label _disclosureLabel;
    private readonly Label _resumeLabel;
    private readonly Button _startFreshButton;
    private readonly Label _originalHeader;
    private readonly TextBox _originalBox;
    private readonly Label _translatedHeader;
    private readonly TextBox _translatedBox;
    private readonly Button _translateButton;
    private readonly Button _stopButton;
    private readonly Button _saveNowButton;
    private readonly Label _statusLabel;
    private readonly Button _closeButton;

    public CreateTranslationForm(
        Work work, string authorName, int sourceEditionId, string? sourceLanguage, string? targetLanguage)
    {
        _workId = work.WorkId;
        _workCtsUrn = work.CtsUrn;
        _authorName = authorName;
        _workTitle = work.Title;
        _sourceEditionId = sourceEditionId;
        _sourceLanguage = sourceLanguage;
        _targetLanguage = targetLanguage ?? "eng";

        Text = "Create Translation";
        AppIcons.ApplyWindowIcon(this, "Translate");
        Width = 1040;
        Height = 740;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 540);

        _headerLabel = new Label
        {
            Left = 16, Top = 12, Width = 990,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = $"{authorName}, {work.Title} \u2014 loading..."
        };

        _disclosureLabel = new Label
        {
            Left = 16, Top = 36, Width = 990, Height = 48,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DarkRed,
            Text = "Sends this work to Gemini's API over the internet, in batches - the only part of " +
                   "Classica Codex that isn't offline. A long work can mean dozens of requests and several " +
                   "minutes. Progress is saved after every batch automatically, so closing this dialog (or " +
                   "hitting today's free-tier limit) never loses what's already translated - reopen this " +
                   "later to pick up exactly where it left off."
        };

        _resumeLabel = new Label
        {
            Left = 16, Top = 84, Width = 780, Height = 20,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            ForeColor = Color.DimGray,
            Visible = false
        };
        _startFreshButton = new Button
        {
            Left = 800, Top = 82, Width = 206, Height = 24,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Text = "Start a New Attempt Instead",
            Visible = false
        };
        _startFreshButton.Click += (_, _) => StartFresh();

        _originalHeader = new Label { Left = 16, Top = 112, Width = 490, Font = new Font(Font, FontStyle.Bold) };
        _originalBox = new TextBox
        {
            Left = 16, Top = 134, Width = 490, Height = 410,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical
        };

        _translatedHeader = new Label
        {
            Left = 516, Top = 112, Width = 490,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font(Font, FontStyle.Bold),
            Text = "Translation (not started)"
        };
        _translatedBox = new TextBox
        {
            Left = 516, Top = 134, Width = 490, Height = 410,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical
        };

        _translateButton = new Button
        {
            Left = 16, Top = 556, Width = 200, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Text = "Translate with Gemini (free)",
            Enabled = false
        };
        _translateButton.Click += async (_, _) => await OnTranslateClickedAsync();

        _stopButton = new Button
        {
            Left = 224, Top = 556, Width = 90, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Text = "Stop",
            Enabled = false
        };
        _stopButton.Click += (_, _) => _cancellation?.Cancel();

        _statusLabel = new Label
        {
            Left = 322, Top = 561, Width = 684,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray
        };

        _saveNowButton = new Button
        {
            Left = 16, Top = 596, Width = 200, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Text = "Save Now",
            Enabled = false
        };
        _saveNowButton.Click += async (_, _) => await OnSaveNowClickedAsync();

        _closeButton = new Button
        {
            Left = 934, Top = 596, Width = 72, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Text = "Close",
            DialogResult = DialogResult.Cancel
        };
        CancelButton = _closeButton;

        Controls.Add(_headerLabel);
        Controls.Add(_disclosureLabel);
        Controls.Add(_resumeLabel);
        Controls.Add(_startFreshButton);
        Controls.Add(_originalHeader);
        Controls.Add(_originalBox);
        Controls.Add(_translatedHeader);
        Controls.Add(_translatedBox);
        Controls.Add(_translateButton);
        Controls.Add(_stopButton);
        Controls.Add(_statusLabel);
        Controls.Add(_saveNowButton);
        Controls.Add(_closeButton);

        Load += async (_, _) => await LoadSourceAndCheckForResumeAsync();
        FormClosed += (_, _) => _cancellation?.Cancel();

        ReadingTheme.AttachTo(this);
    }

    private async Task LoadSourceAndCheckForResumeAsync()
    {
        _sourceNodes = await _textNodeRepo.GetByEditionAsync(_sourceEditionId);
        _originalHeader.Text = $"Original ({TranslationLanguageNames.DisplayName(_sourceLanguage)})";
        // One node per line rather than run together with spaces.
        //
        // Each node is a discrete citable unit and the reader shows them one
        // per row; running them into a single paragraph was tolerable while
        // they were all prose and stopped being so once speech attributions
        // and stage directions joined them - "DRAMATIS PERSONAE LEAR king of
        // Britain ... Ber. Who's there?" is not a passage anyone can work
        // from. What the translator sees now matches what the reader shows.
        _originalBox.Text = string.Join(Environment.NewLine, _sourceNodes.Select(n => n.Text));
        _headerLabel.Text = $"{_authorName}, {_workTitle} \u2014 {_sourceNodes.Count:N0} line(s)";

        if (_sourceNodes.Count == 0)
        {
            _statusLabel.Text = "This edition has no text to translate.";
            return;
        }

        // Look for a prior Gemini attempt on this same work - the most
        // recently created one if there's more than one, by EditionId,
        // since that's assigned in creation order. Anything found here
        // becomes the working edition: further translation appends to it,
        // and Save updates it in place rather than creating another one.
        var existingEditions = await _editionRepo.GetByWorkAsync(_workId);
        var priorAttempt = existingEditions
            .Where(e => e.Translator == GeminiTranslatorLabel)
            .OrderByDescending(e => e.EditionId)
            .FirstOrDefault();

        if (priorAttempt != null)
        {
            var priorNodes = await _textNodeRepo.GetByEditionAsync(priorAttempt.EditionId);
            foreach (var node in priorNodes)
            {
                // Defensive: an edition saved by an earlier version of this
                // feature could contain the old "no translation returned"
                // placeholder text for gaps, rather than simply omitting
                // them. Those aren't real translations and shouldn't count
                // as already done, or they'd never get retried.
                if (node.Text == "[Gemini did not return a translation for this line]") continue;
                _translatedByRef[node.CitationRef] = node.Text;
            }

            _workingEditionId = priorAttempt.EditionId;
            _resumeLabel.Visible = true;
            _startFreshButton.Visible = true;
            _resumeLabel.Text = $"Resuming a previous attempt - {TranslatedLineCount:N0} of " +
                                 $"{_sourceNodes.Count:N0} lines already translated.";
        }

        RefreshTranslatedPreview();
        UpdateButtonsForCurrentProgress();
    }

    /// <summary>Abandons resuming and starts a genuinely separate attempt - the next persist creates a brand new edition rather than updating the one just found.</summary>
    private void StartFresh()
    {
        _workingEditionId = null;
        _translatedByRef.Clear();
        _resumeLabel.Visible = false;
        _startFreshButton.Visible = false;
        RefreshTranslatedPreview();
        UpdateButtonsForCurrentProgress();
    }

    private void UpdateButtonsForCurrentProgress()
    {
        var translated = TranslatedLineCount;
        var remaining = _sourceNodes.Count - translated;
        _translateButton.Enabled = remaining > 0;
        _translateButton.Text = remaining == _sourceNodes.Count
            ? "Translate with Gemini (free)"
            : $"Translate Remaining {remaining:N0} Line(s)";
        _saveNowButton.Enabled = translated > 0;
        _translatedHeader.Text = $"Translation ({translated:N0} of {_sourceNodes.Count:N0} lines)";
    }

    private async Task OnTranslateClickedAsync()
    {
        if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
        {
            using var settingsForm = new TranslateApiSettingsForm();
            settingsForm.ShowDialog(this);
            if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey)) return;
        }

        // Only what isn't already translated - the whole point of resuming.
        var remainingNodes = _sourceNodes.Where(n => !_translatedByRef.ContainsKey(n.CitationRef)).ToList();
        if (remainingNodes.Count == 0) return;

        // Bounded by characters as well as lines - see TranslationBatches for
        // why a count of lines was the wrong unit. Planned before the
        // confirmation so the number of requests quoted is the real one.
        var plannedBatches = TranslationBatches.Plan(remainingNodes, n => n.Text.Length);
        var batchCount = plannedBatches.Count;
        var confirmed = MessageBox.Show(this,
            $"This will send the remaining {remainingNodes.Count:N0} line(s) of {_workTitle} to Gemini's " +
            $"API over the internet, in about {batchCount} separate requests. This could take several " +
            "minutes, and may run into today's free-tier usage limit before it finishes - whatever gets " +
            "translated is saved automatically as it goes, so nothing already done would be lost.\n\n" +
            "Continue?",
            "Send to Gemini's API?",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        if (!confirmed) return;

        _translateButton.Enabled = false;
        _startFreshButton.Enabled = false;
        _stopButton.Enabled = true;
        _cancellation = new CancellationTokenSource();
        var apiKey = TranslationSettings.GeminiApiKey!;

        var missingAfterMainPass = new List<TextNode>();
        var batches = plannedBatches;
        var stoppedEarly = false;

        // Why it stopped, kept so the summary below can say it. A batch that
        // fails names its reason - a dead key, a retired model, the daily
        // quota - and a bare "Stopped" that replaced it made every one of
        // those look the same.
        string? stopReason = null;

        for (var b = 0; b < batches.Count; b++)
        {
            if (_cancellation.IsCancellationRequested) { stoppedEarly = true; break; }

            _statusLabel.ForeColor = Color.DimGray;
            _statusLabel.Text = $"Translating batch {b + 1} of {batches.Count} " +
                                 $"({TranslatedLineCount:N0} of {_sourceNodes.Count:N0} lines so far)...";

            var missing = await TranslateOneBatchAsync(batches[b], apiKey, _cancellation.Token);
            if (missing == null) { stoppedEarly = true; stopReason = _statusLabel.Text; break; }

            missingAfterMainPass.AddRange(missing);
            RefreshTranslatedPreview();

            // Saved after every batch, not just at the end or on an
            // explicit click - this is the actual fix for "a long run can
            // die partway through and shouldn't lose everything before it."
            await PersistProgressAsync();

            if (b < batches.Count - 1 && !_cancellation.IsCancellationRequested)
            {
                try { await Task.Delay(InterBatchDelayMs, _cancellation.Token); }
                catch (OperationCanceledException) { stoppedEarly = true; break; }
            }
        }

        if (!stoppedEarly && missingAfterMainPass.Count > 0 && !_cancellation.IsCancellationRequested)
        {
            // Through the same planner as the main pass. Sending every missing
            // line in one request was how this worked, and it undid the fix
            // one paragraph up: a work whose lines are large enough to need
            // splitting is exactly the work whose retry would be enormous, so
            // the retry timed out on precisely the passages the batching
            // existed to rescue.
            var retryBatches = TranslationBatches.Plan(missingAfterMainPass, n => n.Text.Length);

            for (var r = 0; r < retryBatches.Count; r++)
            {
                if (_cancellation.IsCancellationRequested) break;

                _statusLabel.Text = $"Retrying {missingAfterMainPass.Count:N0} line(s) that didn't come back " +
                                    $"the first time - batch {r + 1} of {retryBatches.Count}...";

                // A null here is a condition the next batch would meet too -
                // see TranslateOneBatchAsync. Stopping keeps its message on
                // screen, which the old code overwrote a line later with
                // "Finished this pass".
                if (await TranslateOneBatchAsync(retryBatches[r], apiKey, _cancellation.Token) == null)
                {
                    stoppedEarly = true;
                    stopReason = _statusLabel.Text;
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

        var translatedCount = TranslatedLineCount;
        _statusLabel.ForeColor = translatedCount == _sourceNodes.Count ? Color.DimGray : Color.DarkRed;
        _statusLabel.Text = stoppedEarly
            ? (stopReason is { Length: > 0 } why && why.StartsWith("Stopped: ", StringComparison.Ordinal)
                ? $"{why} {translatedCount:N0} of {_sourceNodes.Count:N0} lines translated and saved - " +
                  "reopen this later to finish the rest."
                : $"Stopped - {translatedCount:N0} of {_sourceNodes.Count:N0} lines translated, already saved. " +
                  "Reopen this later to finish the rest.")
            : translatedCount == _sourceNodes.Count
                ? $"Finished - all {translatedCount:N0} lines translated and saved."
                : $"Finished this pass, but {_sourceNodes.Count - translatedCount:N0} line(s) never came back " +
                  "from Gemini. Already saved - reopen this later to try those again.";

        _stopButton.Enabled = false;
        _startFreshButton.Enabled = true;
        UpdateButtonsForCurrentProgress();
    }

    private async Task<List<TextNode>?> TranslateOneBatchAsync(
        List<TextNode> batch, string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            var passages = batch.Select(n => (n.CitationRef, n.Text)).ToList();
            var results = await GeminiTranslationService.TranslateBatchAsync(
                passages, _sourceLanguage, _targetLanguage, _authorName, _workTitle, apiKey, cancellationToken);

            foreach (var (citationRef, translatedText) in results)
            {
                _translatedByRef[citationRef] = translatedText;
            }

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
            // first request having saved nothing at all. That is what "it
            // doesn't work for this author" looked like from outside.
            //
            // A timeout says this request took too long, not that the next
            // one will. Reported as lines that didn't come back, which is
            // what they are, and picked up by the retry pass afterwards.
            return batch;
        }
        catch (Exception ex)
        {
            // Everything else does stop: a bad key, a retired model, or the
            // daily quota being gone are all conditions the next batch would
            // meet as well, and grinding through forty more requests to be
            // told the same thing forty more times helps nobody.
            _statusLabel.ForeColor = Color.DarkRed;
            _statusLabel.Text = $"Stopped: {ex.Message}";
            return null;
        }
    }

    /// <summary>Rebuilds the preview in original source order every time, since the retry pass finishes after the main loop and would otherwise land at the end instead of back in place.</summary>
    private void RefreshTranslatedPreview()
    {
        var parts = _sourceNodes
            .Select(n => _translatedByRef.TryGetValue(n.CitationRef, out var t) ? t : null)
            .Where(t => t != null);
        _translatedBox.Text = string.Join(Environment.NewLine, parts);
        UpdateButtonsForCurrentProgress();
    }

    private async Task OnSaveNowClickedAsync()
    {
        _saveNowButton.Enabled = false;
        try
        {
            await PersistProgressAsync();
            MessageBox.Show(this,
                $"Saved - {TranslatedLineCount:N0} of {_sourceNodes.Count:N0} lines. Reopen this work " +
                "(or switch editions) to see it listed as \"trans. Gemini (AI-generated)\".",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save: {ex.Message}", "Save failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _saveNowButton.Enabled = TranslatedLineCount > 0;
        }
    }

    /// <summary>
    /// Creates the working edition on first use, then just clears and
    /// reinserts its TextNodes from the current _translatedByRef state -
    /// the same clear-then-rebuild pattern already used everywhere else in
    /// this app for re-running an ingest. Only ever writes lines that are
    /// genuinely translated; a line not yet done is simply absent from the
    /// edition rather than placeholder-filled, since an absent line is an
    /// honest, already-handled case everywhere else (PassageAligner already
    /// reports "no matching passage" correctly), and leaving it absent is
    /// what makes resuming later possible to detect at all.
    /// </summary>
    private async Task PersistProgressAsync()
    {
        // Counted against the source lines, not the dictionary: a dictionary
        // holding only unattributable keys would otherwise create an edition
        // here and then write no text into it, which is exactly the empty
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

        await _editionRepo.ClearTextNodesAsync(_workingEditionId.Value);

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
                //
                // Without this every row saved as a plain line, so a
                // translated cast list counted towards the translation's word
                // frequencies, could not be switched off in the reader, and
                // left the translation pane offering only "Text" while the
                // original beside it offered five kinds. Carried from the
                // source node because the two editions share citation refs -
                // the same reference means the same thing on both sides.
                NodeKind = sourceNode.NodeKind
            });
        }

        await _textNodeRepo.BulkInsertAsync(nodesToSave);

        // Right after the TextNodes themselves, not before and not as a
        // separate step someone has to remember - this is the actual fix
        // for Create Translation silently going stale in Auto-Tag's
        // lemma-expansion search. Edition-scoped, not a full corpus
        // rebuild: a few thousand lines at most, so this stays fast enough
        // to run after every single batch without slowing anything down.
        //
        // ONLY WHEN THERE IS ALREADY AN INDEX TO KEEP FRESH, and that
        // condition is load-bearing rather than an optimisation.
        //
        // Every search path decides whether to use the word index by asking
        // whether it has any rows at all. On a library where the index was
        // never built that answer is no, and whole-word search - which is
        // now the default - correctly falls back to scanning the text. Write
        // one edition's worth of rows into an empty index and the answer
        // becomes yes, so every later search consults an index that contains
        // this AI translation and nothing else: a library of two million
        // lines answering out of a few thousand, silently, with no error and
        // no empty result to notice.
        //
        // Keeping an existing index current is what this call is for.
        // Bootstrapping one from a single edition is not, and the Setup
        // Wizard is where a whole index gets built.
        if (await _wordIndexRepo.HasDataAsync())
        {
            await _wordIndexService.ReindexEditionAsync(_workingEditionId.Value);
        }
    }
}
