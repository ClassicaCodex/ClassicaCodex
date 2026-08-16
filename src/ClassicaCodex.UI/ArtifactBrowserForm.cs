using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// A standalone popup around ArtifactBrowserControl, for anywhere in the
/// app that wants to offer "show related artifacts" without a permanent
/// home for the browser the way Places Map has one built into its own
/// layout. Right-clicking a Myth Network node opens one of these, searching
/// by that figure's name.
/// </summary>
public class ArtifactBrowserForm : ScaledForm
{
    private readonly ArtifactBrowserControl _browser;
    private readonly ArtifactRepository _artifactRepo = new();

    public ArtifactBrowserForm(string title, string searchTerm)
    {
        Text = $"Artifacts - {title}";
        AppIcons.ApplyWindowIcon(this, "Images");
        ClientSize = new Size(324, 344);
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;

        var noteLabel = new Label
        {
            Text = $"Objects whose name, description, or findspot mentions \"{searchTerm}\" - " +
                   "a text search, not a curated list, so it can be noisier than an exact match.",
            Left = 12,
            Top = 8,
            Width = 300,
            Height = 32
        };

        _browser = new ArtifactBrowserControl
        {
            Left = 12,
            Top = 44
        };

        Controls.Add(noteLabel);
        Controls.Add(_browser);

        Load += async (_, _) =>
        {
            var artifacts = await _artifactRepo.SearchByTextAsync(searchTerm);
            _browser.LoadArtifacts(artifacts);
        };

        ReadingTheme.AttachTo(this);

        WindowShortcuts.CloseOnEscape(this);
    }
}
