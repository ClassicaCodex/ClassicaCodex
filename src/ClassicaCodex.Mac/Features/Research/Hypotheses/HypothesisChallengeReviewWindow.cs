using Avalonia;
using Avalonia.Controls;
using ClassicaCodex.Core;

namespace ClassicaCodex.Mac.Features.Research.Hypotheses;

/// <summary>
/// The accept step for the Hypothesis Lab's AI challenge: rival explanations and
/// discriminating tests, none of them added until checked.
/// </summary>
internal sealed class HypothesisChallengeReviewWindow : ResearchWindow
{
    private readonly CheckTable<HypothesisChallengeProposal> _table;

    public HypothesisChallengeReviewWindow(GeminiHypothesisChallengeResult result) : base(null)
    {
        Title = "Review AI challenge proposals";
        Width = 1150;
        Height = 650;
        MinWidth = 800;
        MinHeight = 500;

        var notice = Ui.Notice($"Candidate proposals from {result.Model}. Check only rivals or tests worth adding; acceptance preserves AI provenance but does not mark the proposal true.");
        notice.Margin = new Thickness(16, 14, 16, 10);

        _table = new CheckTable<HypothesisChallengeProposal>("Add",
            new CheckColumn<HypothesisChallengeProposal>("Kind", new GridLength(110), p => p.Kind == "rivalHypothesis" ? "Rival" : "Experiment"),
            new CheckColumn<HypothesisChallengeProposal>("Proposal", new GridLength(210), p => p.Title, Wrap: true),
            new CheckColumn<HypothesisChallengeProposal>("Method", new GridLength(115), p => p.Method),
            new CheckColumn<HypothesisChallengeProposal>("Statement / rationale / falsification", new GridLength(1, GridUnitType.Star),
                p => $"{p.Statement} — {p.Rationale}" + (string.IsNullOrWhiteSpace(p.FalsificationCriterion) ? string.Empty : $" Falsified by: {p.FalsificationCriterion}"),
                Wrap: true));
        _table.SetItems(result.Proposals);
        _table.Margin = new Thickness(16, 0, 16, 12);

        var cancel = Ui.Button("Cancel");
        var accept = Ui.Button("Accept Checked", accent: true);
        cancel.IsCancel = true;
        accept.IsDefault = true;
        cancel.Click += (_, _) => Close();
        accept.Click += (_, _) =>
        {
            Accepted = _table.Checked;
            Close();
        };

        var bar = Ui.ButtonBar(null, cancel, accept);
        var dock = new DockPanel();
        DockPanel.SetDock(notice, Dock.Top);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(notice);
        dock.Children.Add(bar);
        dock.Children.Add(_table);
        Content = dock;
    }

    /// <summary>The checked proposals, or null if the reader cancelled.</summary>
    public IReadOnlyList<HypothesisChallengeProposal>? Accepted { get; private set; }
}
