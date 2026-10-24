using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.Research.Inquiry;

/// <summary>
/// A small, human-first bridge between noticing a passage and constructing a
/// formal research project. AI is absent until Research is deliberately chosen.
/// </summary>
internal sealed class PassageInquiryWindow : ResearchWindow
{
    private readonly PassageResearchIdentity _passage;
    private readonly PassageInquiryRepository _inquiries = new();
    private readonly ResearchRepository _research = new();
    private PassageInquiry _inquiry;

    private readonly TextBox _attention = Ui.Area(92, "A word, image, contradiction, pattern, surprise, or uncertainty…");
    private readonly TextBox _question = Ui.Area(68, "What do you want to understand about this passage?");
    private readonly TextBlock _directionHelp = Ui.Muted("Choose a next step when one feels useful. Your observation and question come first.");
    private readonly ToggleButton _readClosely = DirectionButton("Read Closely", "Show");
    private readonly ToggleButton _compare = DirectionButton("Compare", "CompareTranslations");
    private readonly ToggleButton _researchDirection = DirectionButton("Research", "Research");
    private readonly Border _aiPanel;
    private readonly Button _askAi = Ui.Button("Suggest Questions with Gemini", "Sparkle");
    private readonly ListBox _suggestions;
    private readonly TextBox _suggestionDetail = Ui.Area(double.NaN, readOnly: true);
    private readonly Button _useSuggestion = Ui.Button("Use Selected Question");
    private readonly Button _save = Ui.Button("Save Inquiry", "Check", accent: true, tip: "Save the inquiry (" + Ui.Chord("S") + ")");
    private readonly Button _promote = Ui.Button("Turn This into a Research Bench Project", "Research");
    private readonly TextBlock _status = Ui.Status();
    private List<PassageInquirySuggestion> _suggestionData = new();
    private bool _loading = true;
    private bool _closingAccepted;

    // What was last loaded or saved. "Unsaved" is read against these rather than
    // kept as a flag the text boxes set: Avalonia reports a change of text after
    // the fact, so a flag set that way was raised again just after a save - or
    // just after an existing inquiry was loaded - and closing then asked about
    // edits nobody had made.
    private string _savedAttention = string.Empty;
    private string _savedQuestion = string.Empty;
    private PassageInquiryDirection _savedDirection;

    public PassageInquiryWindow(PassageResearchIdentity passage) : base(null)
    {
        _passage = passage;
        _inquiry = NewInquiry();
        var citation = PassageCitation.Display(passage.CitationRef, passage.Milestone);
        Title = $"Start inquiry — {passage.WorkTitle} {citation}";
        Width = 980;
        Height = 820;
        MinWidth = 780;
        MinHeight = 660;

        var location = new TextBlock
        {
            Text = $"{passage.AuthorName}, {passage.WorkTitle}  •  {citation}",
            Classes = { "h2" },
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var excerpt = Ui.Passage(passage.Text, 15);
        excerpt.MaxHeight = 120;
        excerpt.Margin = new Thickness(0, 8, 0, 16);

        var attentionLabel = new TextBlock { Text = "What caught your attention?", Classes = { "h3" }, Margin = new Thickness(0, 0, 0, 6) };
        var questionLabel = new TextBlock { Text = "Draft a question in your own words", Classes = { "h3" }, Margin = new Thickness(0, 14, 0, 6) };

        var segment = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new UniformGrid { Columns = 3, Children = { _readClosely, _compare, _researchDirection } }
        };
        segment.Bind(Border.BackgroundProperty, segment.GetResourceObservable("Cc.Hover"));
        _directionHelp.Margin = new Thickness(0, 8, 0, 0);
        var directionLabel = new TextBlock { Text = "Next step", Classes = { "h3" }, Margin = new Thickness(0, 16, 0, 6) };

        _suggestions = Ui.TextList<PassageInquirySuggestion>(s => $"{s.Angle}: {s.Question}");
        _aiPanel = BuildAiPanel();

        var form = new StackPanel();
        form.Children.Add(location);
        form.Children.Add(excerpt);
        form.Children.Add(attentionLabel);
        form.Children.Add(_attention);
        form.Children.Add(questionLabel);
        form.Children.Add(_question);
        form.Children.Add(directionLabel);
        form.Children.Add(segment);
        form.Children.Add(_directionHelp);
        form.Children.Add(_aiPanel);

        var close = Ui.Button("Close");
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        _promote.IsVisible = false;
        var bar = Ui.ButtonBar(_status, close, _promote, _save);

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        dock.Children.Add(Ui.Scroll(form, new Thickness(22, 18, 22, 14)));
        Content = dock;

        _attention.TextChanged += (_, _) => MarkDirty();
        _question.TextChanged += (_, _) => MarkDirty();
        _readClosely.Click += (_, _) => SetDirection(PassageInquiryDirection.ReadClosely);
        _compare.Click += (_, _) => SetDirection(PassageInquiryDirection.Compare);
        _researchDirection.Click += (_, _) => SetDirection(PassageInquiryDirection.Research);
        _save.Click += (_, _) => Run(async () => await SaveAsync());
        _promote.Click += (_, _) => Run(PromoteOrOpenAsync);
        _askAi.Click += (_, _) => Run(SuggestWithAiAsync);
        _suggestions.SelectionChanged += (_, _) => ShowSuggestion();
        _useSuggestion.Click += (_, _) => UseSuggestion();

        Opened += (_, _) => Run(LoadAsync);
        Closing += ConfirmDiscard;
    }

    /// <summary>The project to open in the Bench once this closes: the one just made, or the one made before.</summary>
    public long? OpenProjectId { get; private set; }

    private Border BuildAiPanel()
    {
        var disclosure = Ui.Muted("Optional: send this passage and your two notes to Gemini for possible questions.");
        _useSuggestion.IsEnabled = false;
        var tools = Ui.Row(_askAi, _useSuggestion);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("42*,12,58*"), Height = 180 };
        var list = new Border { Classes = { "panel" }, Child = _suggestions };
        Grid.SetColumn(list, 0);
        Grid.SetColumn(_suggestionDetail, 2);
        grid.Children.Add(list);
        grid.Children.Add(_suggestionDetail);

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = "Optional AI suggestions", Classes = { "h3" } });
        stack.Children.Add(disclosure);
        stack.Children.Add(tools);
        stack.Children.Add(grid);

        return new Border { Classes = { "card" }, Padding = new Thickness(16, 14), Margin = new Thickness(0, 16, 0, 0), Child = stack, IsVisible = false };
    }

    protected override bool OnCommandKey(Key key, bool shift)
    {
        if (key != Key.S) return false;
        Run(async () => await SaveAsync());
        return true;
    }

    internal async Task LoadAsync()
    {
        try
        {
            _inquiry = await _inquiries.GetAsync(_passage.EditionCtsUrn, _passage.CitationRef) ?? NewInquiry();
            _attention.Text = _inquiry.AttentionNote;
            _question.Text = _inquiry.DraftQuestion;
            SetDirection(_inquiry.Direction, markDirty: false);
            RememberSaved();
            _status.Text = _inquiry.PassageInquiryId == 0
                ? "Nothing is saved until you choose Save Inquiry."
                : $"Inquiry saved {_inquiry.UpdatedUtc.ToLocalTime():g}.";
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Inquiry", $"The inquiry could not be loaded: {ex.Message}");
        }
        finally
        {
            _loading = false;
            UpdatePromotionOffer();
        }
    }

    private PassageInquiry NewInquiry() => new()
    {
        WorkCtsUrn = _passage.WorkCtsUrn,
        EditionCtsUrn = _passage.EditionCtsUrn,
        CitationRef = _passage.CitationRef,
        AuthorName = _passage.AuthorName,
        WorkTitle = _passage.WorkTitle,
        Excerpt = _passage.Text
    };

    internal void SetDirection(PassageInquiryDirection direction, bool markDirty = true)
    {
        _inquiry.Direction = direction;
        _directionHelp.Text = direction switch
        {
            PassageInquiryDirection.ReadClosely =>
                "Stay with this passage: inspect diction, syntax, form, speaker, imagery, and what the text leaves unresolved.",
            PassageInquiryDirection.Compare =>
                "Put this passage beside another translation, scene, author, genre, or reception and name exactly what changes.",
            PassageInquiryDirection.Research =>
                "Move outward carefully: identify a testable question, useful methods, and scholarship to read. AI suggestions are optional below.",
            _ => "Choose a next step when one feels useful. Your observation and question come first."
        };
        _aiPanel.IsVisible = direction == PassageInquiryDirection.Research;
        _readClosely.IsChecked = direction == PassageInquiryDirection.ReadClosely;
        _compare.IsChecked = direction == PassageInquiryDirection.Compare;
        _researchDirection.IsChecked = direction == PassageInquiryDirection.Research;
        if (markDirty) MarkDirty();
    }

    /// <summary>Whether the notes or the direction differ from what was last saved.</summary>
    private bool Dirty =>
        !_loading &&
        (!string.Equals(Normal(_attention.Text), _savedAttention, StringComparison.Ordinal) ||
         !string.Equals(Normal(_question.Text), _savedQuestion, StringComparison.Ordinal) ||
         _inquiry.Direction != _savedDirection);

    private static string Normal(string? text) => (text ?? string.Empty).Trim();

    private void RememberSaved()
    {
        _savedAttention = Normal(_inquiry.AttentionNote);
        _savedQuestion = Normal(_inquiry.DraftQuestion);
        _savedDirection = _inquiry.Direction;
    }

    private void MarkDirty()
    {
        if (_loading) return;
        if (Dirty) _status.Text = "Unsaved inquiry.";
        UpdatePromotionOffer();
    }

    internal async Task<bool> SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_attention.Text) || string.IsNullOrWhiteSpace(_question.Text))
        {
            await Alert.Info(this, "Begin with your own reading",
                "Write what caught your attention and draft the question in your own words before saving.");
            return false;
        }

        try
        {
            _inquiry.AttentionNote = _attention.Text.Trim();
            _inquiry.DraftQuestion = _question.Text.Trim();
            _inquiry.Excerpt = _passage.Text;
            await _inquiries.SaveAsync(_inquiry);
            RememberSaved();
            _status.Text = "Inquiry saved. It can now become a Research Bench project.";
            UpdatePromotionOffer();
            return true;
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "Save inquiry", $"The inquiry could not be saved: {ex.Message}");
            return false;
        }
    }

    private void UpdatePromotionOffer()
    {
        // Gate on what has been SAVED, not on unsaved edits. Choosing a direction marks the
        // inquiry dirty, and that must not withdraw an offer the researcher has already
        // earned - least of all the way back into a project they already created.
        var savedBothNotes = _inquiry.PassageInquiryId != 0 &&
                             !string.IsNullOrWhiteSpace(_inquiry.AttentionNote) &&
                             !string.IsNullOrWhiteSpace(_inquiry.DraftQuestion);
        var alreadyPromoted = _inquiry.ResearchProjectId.HasValue;

        // Before promotion the visible text must still match what was saved, so a
        // project is never built from a question since rewritten but not saved. After
        // promotion the button only navigates, so edits in progress do not affect it.
        var matchesSaved =
            string.Equals((_attention.Text ?? string.Empty).Trim(), _inquiry.AttentionNote.Trim(), StringComparison.Ordinal) &&
            string.Equals((_question.Text ?? string.Empty).Trim(), _inquiry.DraftQuestion.Trim(), StringComparison.Ordinal);
        _promote.IsVisible = savedBothNotes && (alreadyPromoted || matchesSaved);
        _promote.Content = Ui.Row(Glyphs.Icon("Research", 14), new TextBlock
        {
            Text = alreadyPromoted ? "Open This Research Bench Project" : "Turn This into a Research Bench Project",
            VerticalAlignment = VerticalAlignment.Center
        });
    }

    internal async Task PromoteOrOpenAsync()
    {
        if (_inquiry.ResearchProjectId is { } existing)
        {
            OpenProjectId = existing;
            CloseAccepted();
            return;
        }

        if (Dirty && !await SaveAsync()) return;

        var citation = PassageCitation.Display(_passage.CitationRef, _passage.Milestone);
        ResearchProject? project = null;
        try
        {
            project = new ResearchProject
            {
                WorkId = _passage.WorkId,
                WorkCtsUrn = _passage.WorkCtsUrn,
                Name = ProjectName(_inquiry.DraftQuestion),
                Notes = $"Started from {_passage.AuthorName}, {_passage.WorkTitle} {citation}.\r\n\r\n" +
                        $"What caught my attention:\r\n{_inquiry.AttentionNote}\r\n\r\n" +
                        $"Initial direction: {DirectionLabel(_inquiry.Direction)}"
            };
            await _research.SaveProjectAsync(project);
            var question = new ResearchQuestion
            {
                ResearchProjectId = project.ResearchProjectId,
                Text = _inquiry.DraftQuestion,
                Notes = $"This question began with {_passage.WorkTitle} {citation}."
            };
            await _research.SaveQuestionAsync(question);
            await _research.SaveEvidenceAsync(new EvidenceItem
            {
                ResearchProjectId = project.ResearchProjectId,
                ResearchQuestionId = question.ResearchQuestionId,
                Title = $"{_passage.WorkTitle} {citation}",
                Type = EvidenceType.PrimaryText,
                SourceType = "Local corpus passage",
                StableIdentifier = $"{_passage.EditionCtsUrn}:{_passage.CitationRef}",
                CanonicalReference = $"{_passage.WorkCtsUrn}:{_passage.CitationRef}",
                Provenance = $"Local edition {_passage.EditionCtsUrn}; captured from a passage-first inquiry.",
                Excerpt = _passage.Text,
                Judgment = EvidenceJudgment.Uncertain,
                Relationship = EvidenceRelationship.Contextualizes,
                ResearcherNote = _inquiry.AttentionNote,
                Origin = EvidenceOrigin.Manual
            });
            await _inquiries.LinkProjectAsync(_inquiry.PassageInquiryId, project.ResearchProjectId);
            _inquiry.ResearchProjectId = project.ResearchProjectId;
            OpenProjectId = project.ResearchProjectId;
            CloseAccepted();
        }
        catch (Exception ex)
        {
            // The cleanup runs against the database that just failed, so it can fail
            // for the same reason. It must never escape: an exception thrown from here
            // replaces the real error with a crash report and tells the researcher
            // nothing about the half-built project left behind.
            var cleanupFailed = false;
            if (project?.ResearchProjectId > 0)
            {
                try
                {
                    await _research.DeleteIncompleteProjectAsync(project.ResearchProjectId);
                }
                catch
                {
                    cleanupFailed = true;
                }
            }

            var remains = cleanupFailed
                ? "\n\nA partly-built project may remain in the Research Bench for this work; open it there and remove it before trying again."
                : string.Empty;
            await Alert.Error(this, "Create project", $"The Research Bench project could not be created: {ex.Message}{remains}");
        }
    }

    private async Task SuggestWithAiAsync()
    {
        if (string.IsNullOrWhiteSpace(_attention.Text) || string.IsNullOrWhiteSpace(_question.Text))
        {
            await Alert.Info(this, "Your reading comes first", "Write your observation and draft question before asking AI for alternatives.");
            return;
        }

        if (!await EnsureGeminiKeyAsync()) return;
        if (!await ConfirmSendAsync("Ask Gemini for inquiry suggestions?",
                "This will send the displayed passage, its author/work/citation, and the two notes you wrote " +
                "to Gemini. It will not send the rest of the corpus, your Research Bench, or your database. Continue?"))
            return;

        _askAi.IsEnabled = false;
        _status.Text = "Gemini is suggesting possible questions…";
        try
        {
            var result = await GeminiTranslationService.SuggestPassageInquiryAsync(
                _passage.AuthorName, _passage.WorkTitle, _passage.CitationRef, _passage.Text,
                _attention.Text!.Trim(), _question.Text!.Trim(), TranslationSettings.GeminiApiKey!);
            _suggestionData = result.Suggestions.ToList();
            _suggestions.ItemsSource = _suggestionData;
            _status.Text = _suggestionData.Count == 0
                ? "Gemini returned no usable question suggestions."
                : $"{_suggestionData.Count} optional suggestions from {result.Model}; none has been applied.";
        }
        catch (Exception ex)
        {
            await Alert.Error(this, "AI suggestions", $"Gemini could not suggest questions: {ex.Message}");
            _status.Text = "AI suggestion request failed; your inquiry is unchanged.";
        }
        finally
        {
            _askAi.IsEnabled = true;
        }
    }

    private void ShowSuggestion()
    {
        var selected = _suggestions.SelectedItem as PassageInquirySuggestion;
        _useSuggestion.IsEnabled = selected != null;
        _suggestionDetail.Text = selected == null
            ? string.Empty
            : $"Why it follows from your note\n{selected.Rationale}\n\nPossible next step\n{selected.NextStep}";
    }

    private void UseSuggestion()
    {
        if (_suggestions.SelectedItem is not PassageInquirySuggestion selected) return;
        _question.Text = selected.Question;
        _status.Text = "AI question copied into the editable draft. Revise it into your own words before saving.";
    }

    private void CloseAccepted()
    {
        _closingAccepted = true;
        Close();
    }

    private async void ConfirmDiscard(object? sender, WindowClosingEventArgs e)
    {
        if (!Dirty || _closingAccepted) return;

        e.Cancel = true;
        if (await Alert.Confirm(this, "Unsaved inquiry", "Close without saving this inquiry?", "Close Without Saving", "Keep Editing"))
        {
            _closingAccepted = true;
            Close();
        }
    }

    private static ToggleButton DirectionButton(string text, string glyph) => new()
    {
        Classes = { "segment" },
        Content = Ui.Row(Glyphs.Icon(glyph, 14), new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }),
        MinWidth = 150,
        Height = 34,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        CornerRadius = new CornerRadius(6)
    };

    private static string ProjectName(string question)
    {
        var trimmed = question.Trim().TrimEnd('?');

        // A draft question made only of question marks trims away to nothing, and an
        // empty name then fails the save - repeatedly, with a message about a missing
        // name, for a question the researcher can plainly see they wrote. Keep what
        // they typed rather than refusing it.
        if (trimmed.Length == 0) trimmed = question.Trim();
        return trimmed.Length <= 180 ? trimmed : trimmed[..177] + "…";
    }

    private static string DirectionLabel(PassageInquiryDirection direction) => direction switch
    {
        PassageInquiryDirection.ReadClosely => "Read closely",
        PassageInquiryDirection.Compare => "Compare",
        PassageInquiryDirection.Research => "Research",
        _ => "Not yet chosen"
    };

    // ── What the tests drive ──────────────────────────────────────────────

    internal void Write(string attention, string question)
    {
        _attention.Text = attention;
        _question.Text = question;
    }

    internal bool PromotionOffered => _promote.IsVisible;
    internal bool AiPanelVisible => _aiPanel.IsVisible;
    internal bool IsDirty => Dirty;
    internal string StatusText => _status.Text ?? string.Empty;
}
