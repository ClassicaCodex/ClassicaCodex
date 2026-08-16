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
    private static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClassicaCodexData");

    /// <summary>
    /// The collections that carry readable text, with the folder each downloads
    /// to - the same folder its setup step counts editions under to decide
    /// whether it is installed, and the same one the search window filters by.
    ///
    /// Here rather than beside each entry below so that a folder name cannot be
    /// changed in one place and left behind in the other. The lemma, lexicon,
    /// map and artifact sources are deliberately absent: they hold no passages,
    /// so there is nothing in them to search.
    /// </summary>
    public static IReadOnlyList<(string Title, string Folder)> TextCollections =>
    [
        ("Ancient Greek (Perseus)", Path.Combine(DataRoot, "greek-texts")),
        ("Ancient Latin (Perseus)", Path.Combine(DataRoot, "latin-texts")),
        ("Post-Classical Greek (First1KGreek)", Path.Combine(DataRoot, "first1k-greek")),
        ("Latin Church Fathers (CSEL)", Path.Combine(DataRoot, "csel")),
        ("English Literature (Renaissance)", Path.Combine(DataRoot, "english-texts")),
        ("Medieval Nordic (Menota)", Path.Combine(DataRoot, "menota"))
    ];

    public static List<SetupDataSource> Build(
        AuthorRepository authorRepo, LemmaRepository lemmaRepo, DefinitionRepository definitionRepo,
        ArtifactRepository artifactRepo, EditionRepository editionRepo)
    {
        var dataRoot = DataRoot;

        // Named once, so the step's download location and its "is this
        // already loaded?" check can't drift apart.
        var first1kDestination = Path.Combine(dataRoot, "first1k-greek");
        var cselDestination = Path.Combine(dataRoot, "csel");

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
                    return IngestOutcome.From(service.FailedFiles);
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
                    return IngestOutcome.From(service.FailedFiles);
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
                    return IngestOutcome.Clean;
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
                    return IngestOutcome.Clean;
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
                    return IngestOutcome.Clean;
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
                    return Task.FromResult(IngestOutcome.Clean);
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
                    return IngestOutcome.Clean;
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
                    return IngestOutcome.Clean;
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
                    var cts = new PerseusIngestService();
                    await cts.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "engLit") }, wrapped, ct);

                    // Pre-CTS layout (english-texts/Renaissance/**/opensource) -
                    // the Shakespeare canon, Marlowe, Holinshed, Hakluyt, etc.
                    // Runs second so its name-based de-dup can fold Sidney and
                    // James I into the author rows the CTS pass just created.
                    var renaissance = Path.Combine(root, "Renaissance");
                    if (!Directory.Exists(renaissance)) return IngestOutcome.From(cts.FailedFiles);

                    var preCts = new RenaissanceIngestService();
                    await preCts.IngestAsync(renaissance, wrapped, ct);

                    return IngestOutcome.Combine(
                        IngestOutcome.From(cts.FailedFiles),
                        IngestOutcome.From(preCts.FailedFiles));
                },
                CheckComplete = async () => await authorRepo.CountByNamespaceAsync("engLit") > 0
            },

            new SetupDataSource
            {
                Title = "Medieval Nordic Texts (Menota)",
                RepoUrl = "https://www.menota.org/EN_forside.xhtml",
                DisplayNote = "manuscripts are downloaded by hand from the Menota catalogue",
                DefaultDestination = Path.Combine(dataRoot, "menota"),

                // Not GitClone and not DirectDownload: Menota publishes per-manuscript XML
                // through a catalogue on its website, with no archive or repository to
                // fetch. Every other source here can be pulled in one request; this one
                // cannot, and pretending otherwise would mean inventing a URL.
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink
                    {
                        Text = "1. Open the Menota catalogue to download manuscripts",
                        Url = "https://www.menota.org/EN_forside.xhtml"
                    },
                    new SetupLink
                    {
                        // Menota manuscripts reference their special characters
                        // by name rather than encoding them directly - &thorn;,
                        // &oslashsupfinal;, seventy-odd distinct ones in a single
                        // manuscript and twenty thousand references. The names are
                        // defined in one file that every manuscript assumes you
                        // have. Without it those characters can only be counted,
                        // not shown, and an offline reader must not fetch it
                        // mid-parse to find out.
                        Text = "2. Download menota-entities.txt into the same folder (see below)",
                        Url = "https://www.menota.org/menota-entities.txt"
                    }
                },

                // The folder is the thing this step is about, so it goes on the form
                // rather than into a sentence.
                ShowDestinationPath = true,

                // Whether menota-entities.txt is there and is what it should be.
                //
                // Checked by parsing it rather than by File.Exists, because the
                // likeliest failure is a file of the right name with the wrong
                // contents: clicking a .txt link opens it in the browser, and
                // saving that page gives you HTML. Exists would say yes, every
                // manuscript would still import full of replacement characters,
                // and nothing on screen would connect the two.
                CheckReadiness = root =>
                {
                    var path = Path.Combine(root, "menota-entities.txt");

                    if (!File.Exists(path))
                        return new SetupReadiness(SetupReadinessState.Missing, "menota-entities.txt not found");

                    var entities = MenotaXmlLoader.LoadEntities(root);

                    return entities.Count == 0
                        ? new SetupReadiness(SetupReadinessState.Problem,
                            "File found but defines no characters")
                        : new SetupReadiness(SetupReadinessState.Ready,
                            $"{entities.Count:N0} characters ready");
                },

                ActionButtonText = "Open Folder",
                SecondaryButtonText = "Import Manuscripts",

                // Shows the division proposal for each manuscript not yet
                // confirmed, before the import starts. The .plan.json is still
                // written and still the record of what was confirmed - it is
                // just no longer something anyone has to open in an editor.
                PrepareSecondary = owner => MenotaPlanReview.Run(
                    owner, Path.Combine(dataRoot, "menota")),

                PlainLanguageDescription =
                    "Medieval texts in Old Norse, Old Norwegian and Old Swedish - sagas, the Eddic poems, " +
                    "and the Norwegian law manuscripts - from the Medieval Nordic Text Archive.\n\n" +
                    "Menota publishes one file per manuscript, with no single archive to fetch, so these " +
                    "are downloaded by hand. Save the XML files into the folder below, then import them.\n\n" +
                    "Save menota-entities.txt into that same folder as well. These manuscripts use medieval " +
                    "letters and abbreviation marks that they refer to by name, and that file is what turns " +
                    "the names into characters - without it they read as \u25AF. Right-click the second link " +
                    "and choose Save link as. It is worth doing before importing, because whatever is " +
                    "unreadable at import time stays unreadable in your library.",

                // Open Folder. Nothing more - it opens the destination in Explorer so the
                // downloaded XML can be dropped in, and reports if the folder is empty.
                RunIngest = (root, progress, ct) =>
                {
                    Directory.CreateDirectory(root);

                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(root)
                        { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        progress.Report($"Couldn't open the folder ({ex.Message}): {root}");
                        return Task.FromResult(IngestOutcome.Clean);
                    }

                    var count = Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories).Count();
                    progress.Report(count == 0
                        ? "Folder is empty. Download manuscripts from the link above into it."
                        : $"{count} XML file(s) in the folder.");

                    return Task.FromResult(IngestOutcome.Clean);
                },

                // Import Manuscripts. Surveys first and prints the report - which
                // orthographic level each manuscript carries, how many words, which MUFI
                // characters could not be resolved - then imports.
                //
                // On a manuscript it has not seen before the import writes an unconfirmed
                // .plan.json beside the XML and imports nothing from that file, reporting
                // it as skipped. So the first press never adds a text to the library, by
                // design: a Menota file is a manuscript containing several works, nothing
                // in it links the catalogue entries to the body divisions, so the division
                // is proposed and a person confirms it. See MenotaIngestPlan.
                RunSecondary = async (root, progress, ct) =>
                {
                    Directory.CreateDirectory(root);

                    if (!Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories).Any())
                    {
                        progress.Report("No XML files in the folder yet.");
                        progress.Report("Use the link above to download manuscripts, then Open Folder to drop them in.");
                        return IngestOutcome.Clean;
                    }

                    var report = new MenotaCorpusReport();
                    var surveyed = await Task.Run(() => report.Survey(root, progress), ct);

                    foreach (var line in MenotaCorpusReport.Format(surveyed)
                                 .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                    {
                        progress.Report(line);
                    }

                    progress.Report("---");

                    var service = new MenotaIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    // Reaching this branch means a manuscript arrived in the folder
                    // after the review dialogs ran, or its review was skipped. The
                    // plan is on disk either way; pressing Import again reviews it.
                    if (service.ApparatusEntries > 0)
                    {
                        progress.Report(
                            $"{service.ApparatusEntries:N0} editorial note(s) kept as apparatus rather than " +
                            "read as text. See Editor's Notes in the reader.");
                    }

                    if (service.RemovedEditions > 0)
                    {
                        progress.Report(
                            $"Removed {service.RemovedEditions:N0} edition(s) the confirmed plans no longer " +
                            "produce - merged or split works, or rows unticked.");
                    }

                    if (service.PlansWritten.Count > 0)
                    {
                        progress.Report(
                            $"{service.PlansWritten.Count} manuscript(s) were not reviewed and so not " +
                            "imported. Press Import Manuscripts again to review them.");
                    }

                    return outcome;
                },

                CheckComplete = async () => await authorRepo.CountByNamespaceAsync(
                    MenotaIngestService.Namespace) > 0
            },


            new SetupDataSource
            {
                Title = "Post-Classical Greek Texts (optional)",
                RepoUrl = "https://github.com/OpenGreekAndLatin/First1KGreek",
                DisplayNote = "extends the Ancient Greek Texts above into late antiquity - same library, not a separate one",
                DefaultDestination = first1kDestination,
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

                    return IngestOutcome.From(service.FailedFiles);
                },
                // Authors.Namespace="greekLit" is already >0 from the
                // classical corpus alone, so the namespace check the Greek and
                // Latin rows use can't tell "loaded" apart from "First1KGreek
                // specifically hasn't run yet". This used to fall back to
                // asking the filesystem whether the repo had been downloaded -
                // which answers a different question entirely, and answers it
                // wrongly the moment the two diverge: delete the database,
                // keep the download, and the step reported "Already loaded"
                // against a database containing none of it. Anyone starting a
                // library over would silently finish setup without this corpus.
                //
                // Every edition records the file it was built from, and the
                // two Greek corpora download to different folders - so "are
                // there editions in this database that came from the
                // First1KGreek folder" is decisive, and rests on no naming
                // convention. (A first attempt matched "1st1K" inside the CTS
                // URN, which is OGL's version identifier for the repo. That's
                // a convention rather than a guarantee, and not worth betting
                // a setup step on.)
                //
                // Caveat, same as the old filesystem probe had: an Advanced
                // Setup run pointed at a custom folder won't match this, and
                // the step will offer to install again. Re-running is
                // harmless - editions upsert by CTS URN - so this errs toward
                // offering redundant work rather than skipping needed work,
                // which is the right direction for a step whose whole failure
                // mode was silently skipping.
                CheckComplete = async () => await editionRepo.CountBySourcePathPrefixAsync(first1kDestination) > 0
            },

            new SetupDataSource
            {
                Title = "Latin Church Fathers (CSEL, optional)",
                RepoUrl = "https://github.com/OpenGreekAndLatin/csel-dev",
                DisplayNote = "extends the Ancient Latin Texts above into late antiquity - same library, not a separate one",
                DefaultDestination = cselDestination,
                PlainLanguageDescription =
                    "The Corpus Scriptorum Ecclesiasticorum Latinorum - the critical editions of the Latin " +
                    "Church Fathers, from the volumes old enough to be out of copyright. Augustine, Ambrose, " +
                    "Jerome, Cyprian and their contemporaries, in the editions scholars actually cite. " +
                    "Authors already in your library gain works and editions rather than duplicates. " +
                    "Around 400 megabytes.",
                RunIngest = async (root, progress, ct) =>
                {
                    // Verified against the repository itself before writing this, not
                    // inferred from the fact that it is an Open Greek and Latin repo:
                    // data/<textgroup>/<work>/__cts__.xml in the same CTS layout as
                    // canonical-latinLit, textgroup URNs already in the latinLit
                    // namespace (urn:cts:latinLit:stoa0007), and a TEI-P5/EpiDoc body -
                    // no DOCTYPE, no entity references beyond the five XML built-ins,
                    // div[@type='edition'] over div[@type='textpart'] to <p> leaves.
                    // So this is the same service the Greek and Latin steps use, with
                    // the namespace it declares for itself, rather than a new importer.
                    //
                    // The apparatus is the reason this needed checking rather than
                    // assuming: these files carry <note type="footnote"> inside the
                    // reading text, and TeiParser already excludes note from the text
                    // and captures it as an apparatus entry instead. Had it not, every
                    // page of Augustine would have arrived with its footnotes spliced
                    // into the sentences.
                    //
                    // Volumes/ holds the same texts unsplit, one file per CSEL volume,
                    // and is left alone: IngestRepoAsync walks data/ only, so the
                    // volume-level files cannot produce a second copy of anything.
                    var service = new PerseusIngestService();
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));
                    await service.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "latinLit") }, wrapped, ct);

                    return IngestOutcome.From(service.FailedFiles);
                },

                // Same reasoning as First1KGreek above: Authors.Namespace="latinLit" is
                // already non-zero from the classical Latin corpus, so it cannot tell
                // "loaded" from "this step has not run". Editions record the file they
                // were built from, and this corpus downloads to a folder of its own.
                CheckComplete = async () => await editionRepo.CountBySourcePathPrefixAsync(cselDestination) > 0
            }
        };
    }
}
