namespace ClassicaCodex.Mac.Shell;

/// <summary>Where a tool sits in the sidebar's list and the Tools menu.</summary>
public enum ToolSection { Reading, Analysis, Research, Instruments, Library }

/// <summary>
/// One tool: what the sidebar and the gallery show for it, and whether it
/// needs a work open first.
/// </summary>
public sealed record ToolEntry(
    string Key,
    string Title,
    string Subtitle,
    string Glyph,
    string TileKey,
    ToolSection Section,
    bool NeedsWork = false);

/// <summary>
/// Every tool the Windows toolbar and menus offer, in one list.
///
/// The Windows app puts eighteen illustrated buttons in a row across the top
/// of the window. A Mac toolbar that wide would be the first thing anyone
/// noticed, so here the reader's everyday controls stay in the toolbar and
/// the rest live in the sidebar's Tools list and the menu bar - all of them
/// one click away, each with its painted tile.
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolEntry> All { get; } = new ToolEntry[]
    {
        new("search", "Search", "Every passage, every edition", "Search", "search", ToolSection.Reading),
        new("concordance", "Concordance", "A word in all its contexts", "Concordance", "concordance", ToolSection.Reading),
        new("bookmarks", "Bookmarks", "Lines you marked, with your notes", "Bookmarks", "bookmarks", ToolSection.Reading),
        new("tags", "Tags", "People, places and themes you have tagged", "Tags", "tags", ToolSection.Reading),
        new("compare-translations", "Compare Translations", "Two renderings side by side", "CompareTranslations", "compare", ToolSection.Reading),
        new("collate", "Collate Editions", "Where the editors disagreed", "Collate", "collation", ToolSection.Reading),
        new("vocabulary", "Core Vocabulary", "The words the open work needs", "Vocabulary", "core-vocabulary", ToolSection.Reading, NeedsWork: true),
        new("workbench", "Translate It Yourself", "The open work, a passage at a time", "Workbench", "workbench", ToolSection.Reading, NeedsWork: true),
        new("create-translation", "Create Translation", "An AI translation of the open work", "Sparkle", "ai-translate", ToolSection.Reading, NeedsWork: true),
        new("starting-points", "Where Should I Start?", "Works worth translating first", "StartingPoints", "starting-points", ToolSection.Reading),

        new("morphology", "Morphology Search", "Every line with a given form", "Morphology", "morphology", ToolSection.Analysis),
        new("myth-network", "Myth Network", "Who appears with whom", "MythNetwork", "myth-network", ToolSection.Analysis),
        new("places-map", "Places Map", "The ancient world, passage by passage", "PlacesMap", "places-map", ToolSection.Analysis),
        new("timeline", "Timeline", "The authors across time", "Timeline", "timeline", ToolSection.Analysis),
        new("stylometry", "Stylometry", "Authorial fingerprints, by Burrows's Delta", "Stylometry", "stylometry", ToolSection.Analysis),
        new("stylometry-compare", "Compare Saved Runs", "Many stylometry runs side by side", "StylometryCompare", "validation", ToolSection.Analysis),
        new("medieval-hands", "Medieval Hands", "313 manuscripts, each line beside its photograph", "MedievalHands", "medieval-hands", ToolSection.Analysis),

        new("research", "Research Bench", "Projects, evidence and findings for the open work", "Research", "research", ToolSection.Research, NeedsWork: true),
        new("reactions", "Fictional Ancient Reactions", "The argument a work's first audience might have had", "Reactions", "reactions", ToolSection.Research, NeedsWork: true),

        new("antikythera", "The Antikythera Mechanism", "The bronze calculator from the shipwreck", "Antikythera", "antikythera", ToolSection.Instruments),
        new("almagest", "Ptolemy's Cosmos", "The heavens of the Almagest, turning", "Almagest", "almagest", ToolSection.Instruments),
        new("harmonics", "Ptolemy's Harmony", "A monochord and twenty tetrachords (makes sound)", "Harmonics", "harmonics", ToolSection.Instruments),

        new("setup", "Setup", "Install collections, dictionaries and lemma data", "Setup", "setup", ToolSection.Library),
        new("settings", "Settings", "Appearance, reading, AI keys, voice", "Settings", "settings", ToolSection.Library),
        new("help", "Help", "How each part of the app works", "Help", "starting-points", ToolSection.Library),
        new("about", "About Classica Codex", "Version, sources and credits", "About", "authors", ToolSection.Library)
    };

    public static ToolEntry Get(string key) => All.First(t => t.Key == key);

    public static string Title(ToolSection section) => section switch
    {
        ToolSection.Reading => "READING",
        ToolSection.Analysis => "ANALYSIS",
        ToolSection.Research => "RESEARCH",
        ToolSection.Instruments => "INSTRUMENTS",
        _ => "LIBRARY"
    };
}
