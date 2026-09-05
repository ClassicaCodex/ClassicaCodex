using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// A small, human-first bridge between noticing a passage and constructing a
/// formal research project. AI is absent until Research is deliberately chosen.
/// </summary>
public sealed class PassageInquiryForm : ScaledForm
{
    private readonly PassageResearchIdentity _passage;
    private readonly PassageInquiryRepository _inquiries = new();
    private readonly ResearchRepository _research = new();
    private PassageInquiry _inquiry;
    private readonly TextBox _attention = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _question = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _directionHelp = new();
    private readonly Button _readClosely = DirectionButton("Read closely");
    private readonly Button _compare = DirectionButton("Compare");
    private readonly Button _researchDirection = DirectionButton("Research");
    private readonly GroupBox _aiPanel = new() { Text = "Optional AI suggestions" };
    private readonly Button _askAi = new() { Text = "Suggest questions with Gemini", Width = 210, Height = 30 };
    private readonly ListBox _suggestions = new();
    private readonly TextBox _suggestionDetail = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical
    };
    private readonly Button _useSuggestion = new() { Text = "Use selected question", Width = 155, Height = 28 };
    private readonly Button _save = new() { Text = "Save inquiry", Width = 105, Height = 30 };
    private readonly Button _promote = new()
    {
        Text = "Turn this into a Research Bench project", Width = 245, Height = 30, Visible = false
    };
    private readonly Label _status = new();
    private List<PassageInquirySuggestion> _suggestionData = [];
    private bool _loading = true;
    private bool _dirty;

    public long? OpenProjectId { get; private set; }

    public PassageInquiryForm(PassageResearchIdentity passage)
    {
        _passage = passage;
        _inquiry = NewInquiry();
        Text = $"Start inquiry — {passage.WorkTitle} {PassageCitation.Display(passage.CitationRef, passage.Milestone)}";
        AppIcons.ApplyWindowIcon(this, "WordStudy");
        Width = 980;
        Height = 790;
        MinimumSize = new Size(780, 660);
        StartPosition = FormStartPosition.CenterParent;

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 9
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var location = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"{passage.AuthorName}, {passage.WorkTitle}  •  {PassageCitation.Display(passage.CitationRef, passage.Milestone)}",
            Font = new Font(Font, FontStyle.Bold)
        };
        var excerpt = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = passage.Text
        };
        var attentionLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "What caught your attention?",
            Font = new Font(Font, FontStyle.Bold)
        };
        _attention.Dock = DockStyle.Fill;
        _attention.PlaceholderText = "A word, image, contradiction, pattern, surprise, or uncertainty…";
        var questionLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Draft a question in your own words",
            Font = new Font(Font, FontStyle.Bold)
        };
        _question.Dock = DockStyle.Fill;
        _question.PlaceholderText = "What do you want to understand about this passage?";

        var directionArea = new Panel { Dock = DockStyle.Fill };
        var directionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        directionPanel.Controls.AddRange([_readClosely, _compare, _researchDirection]);
        _directionHelp.Dock = DockStyle.Fill;
        _directionHelp.Padding = new Padding(4, 3, 4, 0);
        _directionHelp.Text = "Choose a next step when one feels useful. Your observation and question come first.";
        directionArea.Controls.Add(_directionHelp);
        directionArea.Controls.Add(directionPanel);

        BuildAiPanel();

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false
        };
        var close = new Button { Text = "Close", Width = 80, Height = 30, DialogResult = DialogResult.Cancel };
        bottom.Controls.AddRange([close, _promote, _save]);
        _status.AutoSize = true;
        _status.Margin = new Padding(12, 7, 10, 0);
        bottom.Controls.Add(_status);

        content.Controls.Add(location, 0, 0);
        content.Controls.Add(excerpt, 0, 1);
        content.Controls.Add(attentionLabel, 0, 2);
        content.Controls.Add(_attention, 0, 3);
        content.Controls.Add(questionLabel, 0, 4);
        content.Controls.Add(_question, 0, 5);
        content.Controls.Add(directionArea, 0, 6);
        content.Controls.Add(_aiPanel, 0, 7);
        content.Controls.Add(bottom, 0, 8);
        Controls.Add(content);

        _attention.TextChanged += (_, _) => MarkDirty();
        _question.TextChanged += (_, _) => MarkDirty();
        _readClosely.Click += (_, _) => SetDirection(PassageInquiryDirection.ReadClosely);
        _compare.Click += (_, _) => SetDirection(PassageInquiryDirection.Compare);
        _researchDirection.Click += (_, _) => SetDirection(PassageInquiryDirection.Research);
        _save.Click += async (_, _) => await SaveAsync();
        _promote.Click += async (_, _) => await PromoteOrOpenAsync();
        _askAi.Click += async (_, _) => await SuggestWithAiAsync();
        _suggestions.SelectedIndexChanged += (_, _) => ShowSuggestion();
        _useSuggestion.Click += (_, _) => UseSuggestion();

        ReadingTheme.AttachTo(this, ApplyDirectionTheme);
        WindowShortcuts.CloseOnEscape(this);
        Shown += async (_, _) => await LoadAsync();
        FormClosing += ConfirmDiscard;
    }

    private void BuildAiPanel()
    {
        _aiPanel.Dock = DockStyle.Fill;
        _aiPanel.Visible = false;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            ColumnCount = 2,
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var disclosure = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Optional: send this passage and your two notes to Gemini for possible questions.",
            Padding = new Padding(8, 6, 0, 0)
        };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        tools.Controls.AddRange([_askAi, _useSuggestion]);
        _suggestions.Dock = DockStyle.Fill;
        _suggestionDetail.Dock = DockStyle.Fill;
        layout.Controls.Add(disclosure, 0, 0);
        layout.SetColumnSpan(disclosure, 1);
        layout.Controls.Add(tools, 1, 0);
        layout.Controls.Add(_suggestions, 0, 1);
        layout.Controls.Add(_suggestionDetail, 1, 1);
        _aiPanel.Controls.Add(layout);
    }

    private async Task LoadAsync()
    {
        try
        {
            _inquiry = await _inquiries.GetAsync(_passage.EditionCtsUrn, _passage.CitationRef)
                ?? NewInquiry();
            _attention.Text = _inquiry.AttentionNote;
            _question.Text = _inquiry.DraftQuestion;
            SetDirection(_inquiry.Direction, markDirty: false);
            _dirty = false;
            _status.Text = _inquiry.PassageInquiryId == 0
                ? "Nothing is saved until you choose Save inquiry."
                : $"Inquiry saved {_inquiry.UpdatedUtc.ToLocalTime():g}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The inquiry could not be loaded: {ex.Message}", "Inquiry",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    private void SetDirection(PassageInquiryDirection direction, bool markDirty = true)
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
        _aiPanel.Visible = direction == PassageInquiryDirection.Research;
        if (markDirty) MarkDirty();
        ApplyDirectionTheme();
    }

    private void ApplyDirectionTheme()
    {
        _directionHelp.ForeColor = ReadingTheme.MutedText;
        foreach (var pair in new[]
                 {
                     (_readClosely, PassageInquiryDirection.ReadClosely),
                     (_compare, PassageInquiryDirection.Compare),
                     (_researchDirection, PassageInquiryDirection.Research)
                 })
        {
            pair.Item1.BackColor = _inquiry.Direction == pair.Item2
                ? ReadingTheme.SelectionBackground
                : ReadingTheme.Background;
            pair.Item1.ForeColor = _inquiry.Direction == pair.Item2
                ? ReadingTheme.SelectionText
                : ReadingTheme.Text;
        }
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        _status.Text = "Unsaved inquiry.";
        UpdatePromotionOffer();
    }

    private async Task<bool> SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_attention.Text) || string.IsNullOrWhiteSpace(_question.Text))
        {
            MessageBox.Show(this,
                "Write what caught your attention and draft the question in your own words before saving.",
                "Begin with your own reading", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        try
        {
            _inquiry.AttentionNote = _attention.Text.Trim();
            _inquiry.DraftQuestion = _question.Text.Trim();
            _inquiry.Excerpt = _passage.Text;
            await _inquiries.SaveAsync(_inquiry);
            _dirty = false;
            _status.Text = "Inquiry saved. It can now become a Research Bench project.";
            UpdatePromotionOffer();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The inquiry could not be saved: {ex.Message}", "Save inquiry",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void UpdatePromotionOffer()
    {
        // Gate on what has been SAVED, not on _dirty. Choosing a direction marks the
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
            string.Equals(_attention.Text.Trim(), _inquiry.AttentionNote.Trim(), StringComparison.Ordinal) &&
            string.Equals(_question.Text.Trim(), _inquiry.DraftQuestion.Trim(), StringComparison.Ordinal);
        _promote.Visible = savedBothNotes && (alreadyPromoted || matchesSaved);
        _promote.Text = alreadyPromoted
            ? "Open this Research Bench project"
            : "Turn this into a Research Bench project";
    }

    private async Task PromoteOrOpenAsync()
    {
        if (_inquiry.ResearchProjectId is { } existing)
        {
            OpenProjectId = existing;
            _dirty = false;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        if (_dirty && !await SaveAsync()) return;
        ResearchProject? project = null;
        try
        {
            project = new ResearchProject
            {
                WorkId = _passage.WorkId,
                WorkCtsUrn = _passage.WorkCtsUrn,
                Name = ProjectName(_inquiry.DraftQuestion),
                Notes = $"Started from {_passage.AuthorName}, {_passage.WorkTitle} {PassageCitation.Display(_passage.CitationRef, _passage.Milestone)}.\r\n\r\n" +
                        $"What caught my attention:\r\n{_inquiry.AttentionNote}\r\n\r\n" +
                        $"Initial direction: {DirectionLabel(_inquiry.Direction)}"
            };
            await _research.SaveProjectAsync(project);
            var question = new ResearchQuestion
            {
                ResearchProjectId = project.ResearchProjectId,
                Text = _inquiry.DraftQuestion,
                Notes = $"This question began with {_passage.WorkTitle} {PassageCitation.Display(_passage.CitationRef, _passage.Milestone)}."
            };
            await _research.SaveQuestionAsync(question);
            await _research.SaveEvidenceAsync(new EvidenceItem
            {
                ResearchProjectId = project.ResearchProjectId,
                ResearchQuestionId = question.ResearchQuestionId,
                Title = $"{_passage.WorkTitle} {PassageCitation.Display(_passage.CitationRef, _passage.Milestone)}",
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
            _dirty = false;
            DialogResult = DialogResult.OK;
            Close();
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
                try { await _research.DeleteIncompleteProjectAsync(project.ResearchProjectId); }
                catch { cleanupFailed = true; }
            }
            var remains = cleanupFailed
                ? "\r\n\r\nA partly-built project may remain in the Research Bench for this work; "
                  + "open it there and remove it before trying again."
                : "";
            MessageBox.Show(this, $"The Research Bench project could not be created: {ex.Message}{remains}",
                "Create project", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SuggestWithAiAsync()
    {
        if (string.IsNullOrWhiteSpace(_attention.Text) || string.IsNullOrWhiteSpace(_question.Text))
        {
            MessageBox.Show(this, "Write your observation and draft question before asking AI for alternatives.",
                "Your reading comes first", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey))
        {
            using var settings = new TranslateApiSettingsForm();
            settings.ShowDialog(this);
            if (string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey)) return;
        }
        if (TranslationSettings.AlwaysConfirmBeforeSending &&
            MessageBox.Show(this,
                "This will send the displayed passage, its author/work/citation, and the two notes you wrote " +
                "to Gemini. It will not send the rest of the corpus, your Research Bench, or your database. Continue?",
                "Ask Gemini for inquiry suggestions?", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _askAi.Enabled = false;
        _status.Text = "Gemini is suggesting possible questions…";
        try
        {
            var result = await GeminiTranslationService.SuggestPassageInquiryAsync(
                _passage.AuthorName, _passage.WorkTitle, _passage.CitationRef, _passage.Text,
                _attention.Text.Trim(), _question.Text.Trim(), TranslationSettings.GeminiApiKey!);
            _suggestionData = result.Suggestions.ToList();
            _suggestions.DataSource = null;
            _suggestions.DataSource = _suggestionData.Select(s => $"{s.Angle}: {s.Question}").ToList();
            _status.Text = _suggestionData.Count == 0
                ? "Gemini returned no usable question suggestions."
                : $"{_suggestionData.Count} optional suggestions from {result.Model}; none has been applied.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Gemini could not suggest questions: {ex.Message}",
                "AI suggestions", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "AI suggestion request failed; your inquiry is unchanged.";
        }
        finally
        {
            _askAi.Enabled = true;
        }
    }

    private void ShowSuggestion()
    {
        var index = _suggestions.SelectedIndex;
        _useSuggestion.Enabled = index >= 0;
        _suggestionDetail.Text = index < 0 || index >= _suggestionData.Count
            ? string.Empty
            : $"Why it follows from your note\r\n{_suggestionData[index].Rationale}\r\n\r\n" +
              $"Possible next step\r\n{_suggestionData[index].NextStep}";
    }

    private void UseSuggestion()
    {
        var index = _suggestions.SelectedIndex;
        if (index < 0 || index >= _suggestionData.Count) return;
        _question.Text = _suggestionData[index].Question;
        _status.Text = "AI question copied into the editable draft. Revise it into your own words before saving.";
    }

    private void ConfirmDiscard(object? sender, FormClosingEventArgs e)
    {
        if (!_dirty || DialogResult == DialogResult.OK) return;
        if (MessageBox.Show(this, "Close without saving this inquiry?", "Unsaved inquiry",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) return;
        e.Cancel = true;
    }

    private static Button DirectionButton(string text) => new()
    {
        Text = text,
        Width = 145,
        Height = 38,
        Margin = new Padding(0, 0, 10, 0),
        FlatStyle = FlatStyle.Flat
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
}
