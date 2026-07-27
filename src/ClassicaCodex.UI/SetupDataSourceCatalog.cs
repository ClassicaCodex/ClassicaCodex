using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Builds the five SetupDataSource definitions - what SetupWizardForm and
/// GuidedSetupForm both actually do their work through. Moved out of
/// SetupWizardForm so a change to how, say, dictionaries get ingested only
/// has to happen in one place.
/// </summary>
public static class SetupDataSourceCatalog
{
    public static List<SetupDataSource> Build(
        AuthorRepository authorRepo, LemmaRepository lemmaRepo, DefinitionRepository definitionRepo,
        ArtifactRepository artifactRepo)
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClassicaCodexData");

        return new List<SetupDataSource>
        {
            new SetupDataSource
            {
                Title = "Ancient Greek Texts",
                RepoUrl = "https://github.com/PerseusDL/canonical-greekLit",
                DefaultDestination = Path.Combine(dataRoot, "greek-texts"),
                PlainLanguageDescription =
                    "The actual Greek texts themselves - everything from Homer to late antiquity. " +
                    "This is the biggest download here, usually a few hundred megabytes.",
                RunIngest = async (root, progress, ct) =>
                {
                    var service = new PerseusIngestService();
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));
                    await service.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "greekLit") }, wrapped, ct);
                },
                // Checked by corpus (Authors.Namespace), not by edition
                // language - Perseus's Greek corpus legitimately contains
                // some Latin-language editions (old Latin translations of
                // Greek works), so a Language='grc' count alone can't tell
                // "the Greek corpus is loaded" apart from "isn't yet".
                CheckComplete = async () => await authorRepo.CountByNamespaceAsync("greekLit") > 0
            },

            new SetupDataSource
            {
                Title = "Ancient Latin Texts",
                RepoUrl = "https://github.com/PerseusDL/canonical-latinLit",
                DefaultDestination = Path.Combine(dataRoot, "latin-texts"),
                PlainLanguageDescription =
                    "The Latin counterpart to the Greek texts above - Caesar, Cicero, Virgil, and the rest.",
                RunIngest = async (root, progress, ct) =>
                {
                    var service = new PerseusIngestService();
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));
                    await service.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "latinLit") }, wrapped, ct);
                },
                // Same reasoning as the Greek row above, inverted - and this
                // is exactly the direction that actually bites: ingesting
                // only the Greek corpus already creates some Language='lat'
                // editions (the Latin translations it carries), which
                // wrongly reported this row as already done before the
                // Latin corpus had ever been fetched.
                CheckComplete = async () => await authorRepo.CountByNamespaceAsync("latinLit") > 0
            },

            new SetupDataSource
            {
                Title = "Dictionaries (LSJ + Lewis & Short)",
                RepoUrl = "https://github.com/PerseusDL/lexica",
                DefaultDestination = Path.Combine(dataRoot, "lexica"),
                PlainLanguageDescription =
                    "The dictionary definitions Word Study looks words up in while you're reading - " +
                    "one dictionary for Greek, one for Latin, both loaded in this one step.",
                RunIngest = async (root, progress, ct) =>
                {
                    var service = new LexiconIngestService();
                    var wrapped = new Progress<LexiconIngestProgress>(p =>
                        progress.Report($"{p.CurrentFile} ({p.FilesProcessed}/{p.TotalFiles} files, {p.EntriesLoaded:N0} entries)"));

                    progress.Report("Ingesting Greek (LSJ)...");
                    await service.IngestAsync(Path.Combine(root, "CTS_XML_TEI", "perseus", "pdllex", "grc"), "grc", "LSJ", wrapped, ct);

                    progress.Report("Ingesting Latin (Lewis & Short)...");
                    await service.IngestAsync(Path.Combine(root, "CTS_XML_TEI", "perseus", "pdllex", "lat"), "lat", "Lewis & Short", wrapped, ct);
                },
                CheckComplete = async () =>
                {
                    var byLanguage = await definitionRepo.CountByLanguageAsync();
                    return byLanguage.Any(l => l.Language == "grc" && l.Count > 0)
                        && byLanguage.Any(l => l.Language == "lat" && l.Count > 0);
                }
            },

            new SetupDataSource
            {
                Title = "Greek Lemma Data",
                RepoUrl = "https://github.com/gcelano/LemmatizedAncientGreekXML",
                DisplayNote = "CC BY-NC - see About",
                DefaultDestination = Path.Combine(dataRoot, "greek-lemmas"),
                PlainLanguageDescription =
                    "Maps inflected Greek word forms back to their dictionary headword - what lets " +
                    "search and Word Study understand a word regardless of which form it's in. " +
                    "This dataset is free to use but can't be sold, which is fine - ClassicaCodex is free too.",
                RunIngest = async (root, progress, ct) =>
                {
                    var service = new LemmaIngestService();
                    var wrapped = new Progress<LemmaIngestProgress>(p =>
                        progress.Report($"{p.CurrentFile} ({p.FilesProcessed}/{p.TotalFiles} files, {p.LemmasLoaded:N0} mappings)"));
                    await service.IngestAsync(root, "grc", wrapped, ct);
                },
                CheckComplete = async () => await lemmaRepo.CountByLanguageAsync("grc") > 0
            },

            new SetupDataSource
            {
                Title = "Latin Lemma Data",
                RepoUrl = "https://github.com/lascivaroma/latin-lemmatized-texts",
                DefaultDestination = Path.Combine(dataRoot, "latin-lemmas"),
                PlainLanguageDescription = "The same kind of word-form mapping as the Greek lemma data, for Latin.",
                RunIngest = async (root, progress, ct) =>
                {
                    var service = new LemmaIngestService();
                    var wrapped = new Progress<LemmaIngestProgress>(p =>
                        progress.Report($"{p.CurrentFile} ({p.FilesProcessed}/{p.TotalFiles} files, {p.LemmasLoaded:N0} mappings)"));
                    await service.IngestAsync(root, "lat", wrapped, ct);
                },
                CheckComplete = async () => await lemmaRepo.CountByLanguageAsync("lat") > 0
            },

            new SetupDataSource
            {
                Title = "World Map Data (Natural Earth)",
                RepoUrl = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_land.geojson",
                DisplayNote = "public domain - single-file download, not a clone",
                DefaultDestination = Path.Combine(dataRoot, "map"),
                FetchMode = SetupFetchMode.DirectDownload,
                DownloadFileName = "ne_110m_land.geojson",
                PlainLanguageDescription =
                    "Real coastline shapes for the Places Map, from the public-domain Natural Earth " +
                    "project - a single small file, not a big repository. Entirely optional: without it " +
                    "the map still works, just with rougher hand-drawn landmasses.",
                RunIngest = (root, progress, ct) =>
                {
                    // No database ingest - "installing" here means making
                    // sure the file sits at the one canonical path the map
                    // reads from (which only differs from the download
                    // location if Advanced Setup pointed at a custom
                    // folder), then dropping any cached "file wasn't
                    // there" so an already-open session picks it up.
                    var downloaded = Path.Combine(root, "ne_110m_land.geojson");
                    var canonical = NaturalEarthCoastline.CanonicalPath;
                    if (!string.Equals(Path.GetFullPath(downloaded), Path.GetFullPath(canonical),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
                        File.Copy(downloaded, canonical, overwrite: true);
                    }

                    NaturalEarthCoastline.InvalidateCache();
                    var loaded = NaturalEarthCoastline.Load();
                    progress.Report(loaded != null
                        ? $"Map data ready - {loaded.Count} landmasses in range."
                        : "Downloaded, but the file did not parse - the map will use its built-in shapes.");
                    return Task.CompletedTask;
                },
                CheckComplete = () => Task.FromResult(File.Exists(NaturalEarthCoastline.CanonicalPath))
            },

            new SetupDataSource
            {
                Title = "Art & Archaeology Data (Perseus)",
                RepoUrl = "https://github.com/perseus-aa/json",
                DisplayNote = "images stay on Perseus's own server, never downloaded",
                DefaultDestination = Path.Combine(dataRoot, "artifacts"),
                FetchMode = SetupFetchMode.SelfManaged,
                PlainLanguageDescription =
                    "Real objects from the ancient world - vases, coins, gems, sculptures, sites, and " +
                    "buildings - with descriptions and photos, for the Places Map and Myth Network. " +
                    "The catalog data downloads here; the photos themselves are always loaded live from " +
                    "Perseus's own server when you view one, never saved to your computer, since Perseus's " +
                    "copyright terms don't allow redistributing their images outside their own site.",
                RunIngest = async (root, progress, ct) =>
                {
                    var service = new ArtifactIngestService();
                    await service.IngestAsync(root, progress, ct);
                },
                CheckComplete = async () => await artifactRepo.HasDataAsync()
            }
        };
    }
}
