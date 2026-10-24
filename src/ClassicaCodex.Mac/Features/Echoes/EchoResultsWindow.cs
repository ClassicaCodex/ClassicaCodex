using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Research;

namespace ClassicaCodex.Mac.Features.Echoes;

/// <summary>
/// Find Echoes - the Windows "Intertextual Echoes" window: every other passage
/// in the library ranked by how many of this one's rarest words it shares.
///
/// The technique is the one real intertextuality tools use (the Tesserae
/// Project's, in essence): two authors both using "and" or "king" means
/// nothing, both using an unusual word means something. It only compares like
/// with like - an original against originals, a translation against
/// translations - because a Greek line and an English rendering of something
/// else do not share words at all. Cross-Language Echo exists for that gap.
///
/// The window says plainly what the ranking is: candidates worth a human
/// look, not proof that anyone borrowed anything.
/// </summary>
internal sealed class EchoResultsWindow : FeatureWindow
{
    private readonly TextNode _sourceNode;
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EchoList _results;
    private readonly TextBlock _count;
    private List<EchoHit> _currentResults = new();

    public EchoResultsWindow(TextNode sourceNode, FontFamily readingFont, string? attribution = null)
    {
        _sourceNode = sourceNode;

        Title = "Intertextual Echoes";
        Width = 900;
        Height = 640;
        MinWidth = 560;
        MinHeight = 440;

        // A jump leaves the window open. On Windows it is modal and has to
        // close for the reader to be seen at all; here it sits beside the
        // reader, and a list of candidates is one a reader works down.
        _results = new EchoList(readingFont, () => "Intertextual echoes")
        {
            Activated = hit => NavigateAsync(hit.WorkId, hit.TextNodeId)
        };

        var save = new Button { Content = "Save Investigation…", MinWidth = 150 };
        ToolTip.SetTip(save, $"Save these candidates to a research project for review ({(OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+")}S)");
        save.Click += async (_, _) => await SaveInvestigationAsync();

        _count = new TextBlock { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(save, Dock.Left);
        footer.Children.Add(save);
        footer.Children.Add(_count);

        var top = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(EchoUi.SourceHeader("LOOKING FOR ECHOES OF", sourceNode, readingFont, attribution));
        top.Children.Add(EchoUi.Explainer(
            "Ranked by shared rare words - not proof of borrowing, just candidates worth a human look. " +
            "Only compares against the same kind of text (original-vs-translation) as the source line. " +
            "Double-click a candidate to open it in the reader."));

        var layout = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(footer);
        layout.Children.Add(new Border { Classes = { "page" }, Child = _results });
        Content = layout;

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && EchoUi.IsCommand(e))
            {
                e.Handled = true;
                await SaveInvestigationAsync();
            }
        };

        Opened += (_, _) => LoadTask = LoadEchoesAsync();
    }

    /// <summary>The search, for the tests to wait on.</summary>
    internal Task? LoadTask { get; private set; }

    internal IReadOnlyList<EchoHit> Results => _currentResults;

    internal EchoList ResultList => _results;

    private async Task LoadEchoesAsync()
    {
        _results.ShowMessage("Searching…");
        _count.Text = string.Empty;

        try
        {
            // Off the UI thread, as on Windows: finding echoes means looking
            // every content word of the passage up across the corpus, and the
            // "Searching…" above would not appear until it was over.
            var found = await Task.Run(() => _textNodeRepo.FindEchoesAsync(_sourceNode.TextNodeId));
            _currentResults = EchoHit.From(found);
        }
        catch (Exception ex)
        {
            _currentResults = new List<EchoHit>();
            _results.ShowMessage($"Couldn't search the library: {ex.Message}");
            return;
        }

        _results.Show(_currentResults,
            "(no candidate echoes found - the words in this line may be too common, or too rare to share with anything)");
        _count.Text = _currentResults.Count == 0 ? string.Empty
            : _currentResults.Count == 1 ? "1 candidate"
            : $"{_currentResults.Count:N0} candidates, most shared rare words first";
    }

    private async Task SaveInvestigationAsync()
    {
        if (_currentResults.Count == 0)
        {
            await Alert.Info(this, "Nothing to save", "There are no candidates to save.");
            return;
        }

        var source = await _textNodeRepo.GetPassageResearchIdentityAsync(_sourceNode.TextNodeId);
        if (source == null)
        {
            await Alert.Warn(this, "Passage not found", "The source passage is no longer present in the local corpus.");
            return;
        }

        var request = new EchoCaptureRequest(
            ResearchEchoMethod.RareWordOverlap, source,
            $"Rare-word echoes of {source.WorkTitle} {PassageCitation.Display(source.CitationRef, source.Milestone)}",
            "Local corpus; same original/translation edition kind as the source",
            "Normalized rare-word overlap; ranked by the number of shared rare source words.",
            null, null, null,
            _currentResults.Select(r => new EchoCaptureCandidate(
                r.WorkId, r.TextNodeId, r.AuthorName, r.WorkTitle, r.CitationRef, r.Text,
                r.SharedWordCount, $"{r.SharedWordCount} shared rare word(s)", null)).ToList());

        if (await ResearchFeature.CaptureEchoAsync(this, request))
        {
            await Alert.Info(this, "Investigation saved",
                "The candidates are saved for human review in Research Bench → Project → Echo investigations.");
        }
    }
}
