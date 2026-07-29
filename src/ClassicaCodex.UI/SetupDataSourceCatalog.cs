using ClassicaCodex.Core.Models;
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
            },

            new SetupDataSource
            {
                Title = "English Lemma Data & Dictionary (WordNet)",
                RepoUrl = "https://wordnet.princeton.edu",
                DisplayNote = "(Princeton WordNet - free for any use)",
                DefaultDestination = Path.Combine(dataRoot, "wordnet"),
                FetchMode = SetupFetchMode.SelfManaged,
                PlainLanguageDescription =
                    "Maps English word forms back to their dictionary headword, and supplies definitions - " +
                    "the same thing the Greek and Latin lemma data does, but for the English translations " +
                    "you already have loaded. Makes search find \"spoke\" when you type \"speak\", and makes " +
                    "Word Study work on the translation side as well as the original.",
                RunIngest = async (root, progress, ct) =>
                {
                    var service = new WordNetIngestService();
                    await service.IngestAsync(root, progress, ct);
                },
                CheckComplete = async () => await lemmaRepo.CountByLanguageAsync("eng") > 0
            },

            new SetupDataSource
            {
                Title = "English Literature (Renaissance, optional)",
                RepoUrl = "https://github.com/PerseusDL/canonical-engLit",
                DisplayNote = "(Marlowe, Shakespeare, Hakluyt - needs English Lemma Data above)",
                DefaultDestination = Path.Combine(dataRoot, "english-texts"),
                PlainLanguageDescription =
                    "Perseus's Renaissance and early modern collection - Marlowe, Shakespeare, Holinshed, " +
                    "Hakluyt. Useful mainly for reception: how later writers reworked classical material. " +
                    "Note that these are 16th and 17th century English, while the English dictionary above " +
                    "is modern, so archaic forms like \"hath\" and \"doth\" won't find a headword.",
                RunIngest = async (root, progress, ct) =>
                {
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));

                    // CTS layout (english-texts/data) - Sidney, James I.
                    await new PerseusIngestService().IngestAsync(
                        new[] { (Path.Combine(root, "data"), "engLit") }, wrapped, ct);

                    // Pre-CTS layout (english-texts/Renaissance/**/opensource) -
                    // the Shakespeare canon, Marlowe, Holinshed, Hakluyt, etc.
                    // Runs second so its name-based de-dup can fold Sidney and
                    // James I into the author rows the CTS pass just created.
                    var renaissance = Path.Combine(root, "Renaissance");
                    if (Directory.Exists(renaissance))
                        await new RenaissanceIngestService().IngestAsync(renaissance, wrapped, ct);
                },
                CheckComplete = async () => await authorRepo.CountByNamespaceAsync("engLit") > 0
            },

            new SetupDataSource
            {
                Title = "Post-Classical Greek Texts (optional)",
                RepoUrl = "https://github.com/OpenGreekAndLatin/First1KGreek",
                DisplayNote = "extends the Ancient Greek Texts above into late antiquity - same library, not a separate one",
                DefaultDestination = Path.Combine(dataRoot, "first1k-greek"),
                PlainLanguageDescription =
                    "The Open Greek and Latin project's sequel to the Ancient Greek Texts above - Greek " +
                    "(and a little Latin) written after the classical period, into late antiquity. Authors " +
                    "and works already in your library (from a handful of famous plays this collection also " +
                    "carries alternate 19th/20th-century editions of) just gain an extra edition to choose " +
                    "from in the original-language dropdown - nothing gets overwritten. Big download, several " +
                    "hundred megabytes.",
                RunIngest = async (root, progress, ct) =>
                {
                    // Verified against a real clone before writing this, not
                    // inferred: same data/<textgroup>/<work>/__cts__.xml CTS
                    // layout as canonical-greekLit, same CTS URN scheme, and
                    // a TEI-P5/EpiDoc body (flat <div><l n="..."> - no DOCTYPE,
                    // no custom entities beyond what XmlEntitySanitizer
                    // already covers) that PerseusIngestService and TeiParser
                    // already read correctly. So this is just a second pass
                    // of the exact same service used above, not a new
                    // importer - the two corpora share the "greekLit"
                    // namespace deliberately, because they're the same
                    // umbrella collection (OGL scopes First1KGreek to avoid
                    // works canonical-greekLit already has - but where the
                    // two DO overlap, e.g. Sophocles' Ajax, it's because this
                    // corpus adds older alternate editions of an
                    // already-covered play, not a duplicate of the same one).
                    //
                    // That overlap is what makes this safe rather than risky:
                    // Author/Work upserts key on CTS URN, so Sophocles and
                    // Ajax merge into the rows the first pass already
                    // created, while each edition file keys on its own
                    // filename (e.g. "tlg0011.tlg003.1st1K-grc1" vs whatever
                    // canonical-greekLit's own Ajax edition is named) - so new
                    // editions land as additional rows under the existing
                    // work rather than clearing anyone's existing text.
                    var service = new PerseusIngestService();
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));
                    await service.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "greekLit") }, wrapped, ct);

                    // One of this corpus's 309 textgroups (heb0001, "Hebrew
                    // Bible") embeds its own urn:cts:hebrewlit: prefix rather
                    // than greekLit - genuinely not Greek literature, just
                    // swept in because it lives in the same repo. The pass
                    // above still labels it "greekLit" (IngestRepoAsync
                    // applies one namespace to everything it walks), so
                    // correct that one row's label afterward rather than
                    // teaching the shared ingest walker about a single
                    // exception. Re-upserting on the same CtsUrn updates the
                    // existing row in place; it's a no-op if this textgroup
                    // isn't present.
                    await authorRepo.UpsertAsync(new Author
                    {
                        CtsUrn = "urn:cts:hebrewlit:heb0001",
                        Name = "Hebrew Bible",
                        Namespace = "hebrewLit"
                    }, ct);
                },
                // Not DB-backed the way the check above this one is, because
                // Authors.Namespace="greekLit" is already >0 from the
                // classical corpus alone and can't tell "loaded" apart from
                // "First1KGreek specifically hasn't run yet". Falls back to
                // the same filesystem check the World Map source above uses:
                // does the destination have a populated data/ folder. Same
                // caveat as that one - a custom Advanced Setup destination
                // won't be seen here.
                CheckComplete = () =>
                {
                    var probe = Path.Combine(dataRoot, "first1k-greek", "data");
                    var complete = Directory.Exists(probe)
                        && Directory.EnumerateFiles(probe, "*.xml", SearchOption.AllDirectories).Any();
                    return Task.FromResult(complete);
                }
            }
        };
    }
}
