using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Browses a list of artifacts: a category filter, a live-loaded IIIF
/// image, description text, and Previous/Next paging. Extracted from
/// PlacesMapForm once the Myth Network needed the exact same browsing
/// experience for a different kind of lookup (a text search for a
/// mythological figure, rather than an exact place match) - two
/// independent copies of this would inevitably drift out of sync with
/// each other over time.
///
/// Callers fetch the artifact list themselves however makes sense for
/// them (a place match, a text search, whatever) and hand it to
/// LoadArtifacts; this control only handles displaying and paging through
/// what it's given, and does not know or care where the list came from.
/// </summary>
public class ArtifactBrowserControl : UserControl
{
    private readonly ComboBox _categoryFilterComboBox;
    private readonly Label _headerLabel;
    private readonly PictureBox _pictureBox;
    private readonly Label _captionLabel;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly ArtifactRepository _artifactRepo = new();

    // One HTTP request per artifact shown - reused rather than a new
    // HttpClient per click, which is the standard guidance for this type
    // (each instance holds its own connection pool; creating one per
    // request defeats that and can exhaust sockets under rapid use).
    private static readonly HttpClient s_httpClient = new();

    private List<Artifact> _allArtifacts = new();
    private List<Artifact> _currentArtifacts = new();
    private int _currentIndex;

    public ArtifactBrowserControl()
    {
        Width = 300;
        Height = 304;

        _categoryFilterComboBox = new ComboBox
        {
            Left = 0,
            Top = 0,
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _categoryFilterComboBox.SelectedIndexChanged += async (_, _) =>
        {
            ApplyCategoryFilter();
            await RenderCurrentArtifactAsync();
        };

        _headerLabel = new Label
        {
            Text = "No artifacts to show yet.",
            Left = 0,
            Top = 28,
            Width = 300,
            Height = 32,
            Font = new Font(Font, FontStyle.Bold)
        };

        _pictureBox = new PictureBox
        {
            Left = 0,
            Top = 62,
            Width = 300,
            Height = 160,
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom
        };

        _captionLabel = new Label
        {
            Left = 0,
            Top = 226,
            Width = 300,
            Height = 50,
            ForeColor = Color.DimGray
        };

        _prevButton = new Button { Text = "< Previous", Left = 0, Top = 278, Width = 140, Height = 26, Enabled = false };
        _prevButton.Click += async (_, _) =>
        {
            if (_currentArtifacts.Count == 0) return;
            _currentIndex = (_currentIndex - 1 + _currentArtifacts.Count) % _currentArtifacts.Count;
            await RenderCurrentArtifactAsync();
        };

        _nextButton = new Button { Text = "Next >", Left = 160, Top = 278, Width = 140, Height = 26, Enabled = false };
        _nextButton.Click += async (_, _) =>
        {
            if (_currentArtifacts.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % _currentArtifacts.Count;
            await RenderCurrentArtifactAsync();
        };

        Controls.Add(_categoryFilterComboBox);
        Controls.Add(_headerLabel);
        Controls.Add(_pictureBox);
        Controls.Add(_captionLabel);
        Controls.Add(_prevButton);
        Controls.Add(_nextButton);

        Disposed += (_, _) => _pictureBox.Image?.Dispose();
    }

    /// <summary>Whatever list the caller fetched - a place match, a text search, an empty list if nothing was found.</summary>
    public void LoadArtifacts(List<Artifact> artifacts)
    {
        _allArtifacts = artifacts;
        PopulateCategoryFilter();
        ApplyCategoryFilter();
        _ = RenderCurrentArtifactAsync();
    }

    /// <summary>
    /// One "All categories" option plus one per distinct type actually
    /// present, each labeled with a count. Setting SelectedIndex below does
    /// NOT reliably fire SelectedIndexChanged if the previous load happened
    /// to leave the combo box on the same index (0) - LoadArtifacts calls
    /// ApplyCategoryFilter explicitly afterward rather than relying on this
    /// event alone, which is what actually guarantees correctness here.
    /// </summary>
    private void PopulateCategoryFilter()
    {
        _categoryFilterComboBox.Items.Clear();
        _categoryFilterComboBox.Items.Add(new CategoryFilterOption(null, _allArtifacts.Count));
        foreach (var group in _allArtifacts.GroupBy(a => a.Type).OrderBy(g => g.Key))
        {
            _categoryFilterComboBox.Items.Add(new CategoryFilterOption(group.Key, group.Count()));
        }
        _categoryFilterComboBox.SelectedIndex = 0;
    }

    private void ApplyCategoryFilter()
    {
        var selected = _categoryFilterComboBox.SelectedItem as CategoryFilterOption;
        _currentArtifacts = selected?.Category == null
            ? _allArtifacts
            : _allArtifacts.Where(a => a.Type == selected.Category).ToList();
        _currentIndex = 0;
    }

    /// <summary>One entry in the category filter dropdown - null Category means "All categories".</summary>
    private class CategoryFilterOption
    {
        public string? Category { get; }
        private int Count { get; }

        public CategoryFilterOption(string? category, int count)
        {
            Category = category;
            Count = count;
        }

        public override string ToString() => Category == null ? $"All categories ({Count})" : $"{Category} ({Count})";
    }

    /// <summary>
    /// Renders whichever artifact _currentIndex points at - the image is
    /// loaded live from Perseus's own IIIF server every time this runs,
    /// never cached to disk, per Perseus's copyright terms. An artifact
    /// with no linked photo still shows its description text; only the
    /// image area itself goes blank for those.
    /// </summary>
    private async Task RenderCurrentArtifactAsync()
    {
        _pictureBox.Image?.Dispose();
        _pictureBox.Image = null;

        if (_currentArtifacts.Count == 0)
        {
            _headerLabel.Text = "No linked artifacts found.";
            _captionLabel.Text = string.Empty;
            _prevButton.Enabled = false;
            _nextButton.Enabled = false;
            return;
        }

        var artifact = _currentArtifacts[_currentIndex];
        _headerLabel.Text = $"{artifact.Type} - {artifact.Name ?? artifact.ArtifactId} ({_currentIndex + 1} of {_currentArtifacts.Count})";
        _prevButton.Enabled = _currentArtifacts.Count > 1;
        _nextButton.Enabled = _currentArtifacts.Count > 1;

        var images = await _artifactRepo.GetImagesForArtifactAsync(artifact.ArtifactId);
        var firstImage = images.FirstOrDefault();

        _captionLabel.Text = !string.IsNullOrWhiteSpace(artifact.Description)
            ? artifact.Description
            : firstImage?.Caption ?? "(no description available)";

        if (firstImage == null)
        {
            _captionLabel.Text += "\r\n(no photo available for this object)";
            return;
        }

        try
        {
            var bytes = await s_httpClient.GetByteArrayAsync(BuildIiifThumbnailUrl(firstImage.ImageId));
            _pictureBox.Image = Image.FromStream(new MemoryStream(bytes));
        }
        catch
        {
            // A network hiccup, or Perseus's server briefly unavailable -
            // the caption text above is still useful on its own, so this
            // fails quietly rather than surfacing an error for something
            // this minor. The picture area is simply left blank.
        }
    }

    /// <summary>Perseus's own IIIF pattern, confirmed directly against a live coin image and a live vase image - both resolved correctly at pct:50.</summary>
    private static string BuildIiifThumbnailUrl(string imageId) =>
        $"https://iiif.perseus.tufts.edu/iiif/3/{imageId}/full/pct:50/0/default.png";
}
