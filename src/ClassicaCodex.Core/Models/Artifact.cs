namespace ClassicaCodex.Core.Models;

/// <summary>
/// One object from the Perseus Art & Archaeology collection - a vase,
/// coin, gem, sculpture, site, or building. Perseus's copyright terms
/// don't allow bundling their images or redistributing full catalog text
/// outside their own environment, so only what's needed to describe an
/// object and link back to it lives here - the images themselves are
/// always loaded live from Perseus's own IIIF server at view time, never
/// downloaded or cached to disk.
///
/// The six categories don't share a schema (a coin has obverse/reverse
/// types, a vase has a painter and decoration, a building has an
/// architect) - rather than six rigid tables for categories this hasn't
/// even seen the fields of yet (gems, sites), Description holds whichever
/// single field is richest for that category, chosen at ingest time.
/// </summary>
public class Artifact
{
    public string ArtifactId { get; set; } = string.Empty; // e.g. "aa_3955"
    public string Type { get; set; } = string.Empty; // Vase, Coin, Gem, Sculpture, Site, Building
    public string? Name { get; set; }
    public string? Region { get; set; }

    /// <summary>Raw findspot text as Perseus wrote it, e.g. "Athens (from the pyre of a grave near the Acharnian Gate)".</summary>
    public string? Context { get; set; }

    /// <summary>
    /// Resolved once at ingest time against PlaceData's fuzzy match, not
    /// re-matched on every Places Map load - null means no confident place
    /// was found in Context, not that Context is empty.
    /// </summary>
    public string? MatchedPlaceName { get; set; }

    public string? Period { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Collection { get; set; }
    public string? Material { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? PrimaryCitation { get; set; }
}

/// <summary>
/// One photograph of an Artifact - there can be several (a coin's obverse
/// and reverse, a building from multiple angles) or none at all. ImageId
/// is what both identifies the photo in Perseus's own images.json and, via
/// the confirmed IIIF pattern, builds the actual URL to display it.
/// </summary>
public class ArtifactImage
{
    public string ArtifactId { get; set; } = string.Empty;
    public string ImageId { get; set; } = string.Empty; // e.g. "1990.26.0801"
    public string? Caption { get; set; }
    public string? Credits { get; set; }
}
