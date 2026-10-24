using Avalonia;
using Avalonia.Controls;

namespace ClassicaCodex.Mac.Features.Research.Bench;

/// <summary>
/// The human accept step for AI-gathered corpus evidence. Candidates are shown with the
/// local corpus text they resolved to, and only checked rows are saved.
/// </summary>
internal sealed class EvidenceReviewWindow : ResearchWindow
{
    private readonly CheckTable<EvidenceCandidatePreview> _table;

    public EvidenceReviewWindow(IReadOnlyList<EvidenceCandidatePreview> candidates, string model, int unresolved, bool challengeTheory)
        : base(null)
    {
        Title = challengeTheory ? "Review AI counterevidence candidates" : "Review AI corpus evidence candidates";
        Width = 1150;
        Height = 650;
        MinWidth = 820;
        MinHeight = 480;

        var notice = Ui.Notice(
            $"{candidates.Count} candidate(s) from {model}, each resolved against this edition" +
            (unresolved == 0 ? string.Empty : $"; {unresolved} more were rejected because their citation did not resolve") +
            ". Check the ones worth keeping. Accepted rows are saved as uncertain AI candidates for you to judge; " +
            "nothing is written to the project until you accept.");
        notice.Margin = new Thickness(16, 14, 16, 10);

        _table = new CheckTable<EvidenceCandidatePreview>("Keep",
            new CheckColumn<EvidenceCandidatePreview>("Reference", new GridLength(95), c => ClassicaCodex.Core.PassageCitation.Display(c.Citation)),
            new CheckColumn<EvidenceCandidatePreview>("Candidate", new GridLength(215), c => c.Title, Wrap: true),
            new CheckColumn<EvidenceCandidatePreview>("Relationship", new GridLength(105), c => Words.Of(c.Relationship)),
            new CheckColumn<EvidenceCandidatePreview>("Confidence", new GridLength(85), c => c.Confidence),
            new CheckColumn<EvidenceCandidatePreview>("Corpus text — model's rationale", new GridLength(1, GridUnitType.Star),
                c => $"{Ui.Clip(c.Excerpt, 220)} — {c.Rationale}", Wrap: true, Source: true));
        _table.SetItems(candidates);
        _table.Margin = new Thickness(16, 0, 16, 12);

        var all = Ui.Button("Check All");
        var discard = Ui.Button("Discard All");
        var save = Ui.Button("Save Checked", accent: true);
        discard.IsCancel = true;
        save.IsDefault = true;
        all.Click += (_, _) => _table.CheckAll();
        discard.Click += (_, _) => Close();
        save.Click += (_, _) =>
        {
            Accepted = _table.Checked;
            Close();
        };

        var bar = Ui.ButtonBar(all, discard, save);
        var dock = new DockPanel();
        DockPanel.SetDock(notice, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(notice);
        dock.Children.Add(bar);
        dock.Children.Add(_table);
        Content = dock;
    }

    /// <summary>The checked candidates, or null if the reader discarded them all.</summary>
    public IReadOnlyList<EvidenceCandidatePreview>? Accepted { get; private set; }

    internal CheckTable<EvidenceCandidatePreview> Table => _table;
}
