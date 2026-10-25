using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Artifacts;

/// <summary>
/// A window around <see cref="ArtifactBrowser"/>, for anywhere that wants to
/// offer "show related artifacts" without a permanent home for the browser
/// the way the Places Map has one in its own layout. The Myth Network opens
/// one for a figure it was asked about, and result lists for a name.
/// </summary>
public sealed class ArtifactBrowserWindow : FeatureWindow
{
    private readonly ArtifactRepository _artifactRepo = new();

    public ArtifactBrowserWindow(string title, string searchTerm)
    {
        SearchTerm = searchTerm;
        Title = $"Artifacts — {title}";
        Width = 440;
        Height = 620;
        MinWidth = 340;
        MinHeight = 420;

        var note = new TextBlock
        {
            Text = $"Objects whose name, description, or findspot mentions “{searchTerm}” - " +
                   "a text search, not a curated list, so it can be noisier than an exact match.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };

        Browser = new ArtifactBrowser { PhotoHeight = double.NaN, CaptionLines = 8 };
        Browser.MinHeight = 0;

        var layout = new DockPanel { Margin = new Thickness(20, 18, 20, 18) };
        DockPanel.SetDock(note, Dock.Top);
        layout.Children.Add(note);
        layout.Children.Add(Browser);
        Content = layout;

        Opened += (_, _) => Loading = LoadAsync();
    }

    /// <summary>What this window searched for, so asking again for the same name brings it forward.</summary>
    public string SearchTerm { get; }

    internal ArtifactBrowser Browser { get; }

    /// <summary>The search, for tests to wait on.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    private async Task LoadAsync()
    {
        try
        {
            var artifacts = await _artifactRepo.SearchByTextAsync(SearchTerm);

            // Nothing found and no catalogue to find it in are different
            // answers, and only the second has something the reader can do.
            if (artifacts.Count == 0 && !await _artifactRepo.HasDataAsync())
                Browser.EmptyNote = ArtifactBrowser.MissingCatalogueNote;

            Browser.LoadArtifacts(artifacts);
            await Browser.PendingRender;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't search the artifacts", ex);
        }
    }
}
