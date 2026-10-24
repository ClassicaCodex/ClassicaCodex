using ClassicaCodex.Core.Bfm;
using ClassicaCodex.Core.Catmus;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Ingestion.Antony;
using ClassicaCodex.Ingestion.Aquinas;
using ClassicaCodex.Ingestion.Bfm;
using ClassicaCodex.Ingestion.Catmus;
using ClassicaCodex.Ingestion.Dante;
using ClassicaCodex.Ingestion.Defixiones;
using ClassicaCodex.Ingestion.MagicalPapyri;
using ClassicaCodex.Ingestion.Dionysius;
using ClassicaCodex.Ingestion.Egyptian;
using ClassicaCodex.Ingestion.Geste;
using ClassicaCodex.Ingestion.Goetia;
using ClassicaCodex.Ingestion.EarlyEnglish;
using ClassicaCodex.Ingestion.Hebrew;
using ClassicaCodex.Ingestion.Nights;
using ClassicaCodex.Ingestion.Pseudepigrapha;
using ClassicaCodex.Ingestion.Quran;
using ClassicaCodex.Ingestion.Rabbinic;
using ClassicaCodex.Ingestion.ReM;

namespace ClassicaCodex.UI;

/// <summary>
/// Builds the SetupDataSource definitions - what SetupWizardForm and
/// GuidedSetupForm both actually do their work through. Moved out of
/// SetupWizardForm so a change to how, say, dictionaries get ingested only
/// has to happen in one place.
///
/// THE ORDER OF THIS LIST IS THE ORDER OF THE WIZARD, and both forms present
/// it as written here, so it is worth keeping deliberate. It runs:
///
///   the texts        Greek and Latin from Perseus first, because they are
///                    what the application is for and everything after them
///                    is an extension of one or the other; then the two that
///                    extend those corpora into late antiquity, First1KGreek
///                    and CSEL; then Migne, which is the widest net and the
///                    largest download; then the collections that are their
///                    own thing - the Renaissance English, Bodin, and last
///                    Menota, which is the only source that cannot be fetched
///                    for you and asks the most of a reader.
///
///   the dictionary   what Word Study looks a word up in, and the first step
///                    that is about the texts rather than more of them.
///
///   the lemma data   Greek, Latin, then English - what lets a search for one
///                    form find the others. Ordered to match the texts above,
///                    and after them because they are useless without one.
///
///   the rest         the map and the artefact catalogue, which serve two
///                    windows rather than the reading itself.
///
/// Nothing depends on the positions - the wizard indexes into this list
/// within a single run and stores no step numbers - so this is a judgement
/// about what to offer someone first, and can be revised as one.
/// </summary>
public static class SetupDataSourceCatalog
{
    /// <summary>
    /// Where the downloads go, which the reader chooses - see
    /// <see cref="DataFolderSettings"/>. Read afresh on every Build rather
    /// than captured once, so a folder chosen in the wizard applies to the
    /// steps that come after it in the same sitting.
    /// </summary>
    private static string DataRoot => DataFolderSettings.Root;

    /// <summary>
    /// The stored identity of each collection that carries readable text.
    ///
    /// Written onto every edition as it is imported, so a library can say which
    /// collection a text belongs to without consulting the folder it arrived in -
    /// which stops being true the moment the data is moved, installed somewhere
    /// custom, or opened on another machine.
    ///
    /// The lemma, lexicon, map and artifact sources have no keys: they hold no
    /// passages, so there is nothing in them to search.
    /// </summary>
    public static class CollectionKeys
    {
        public const string PerseusGreek = "perseus-greek";
        public const string PerseusLatin = "perseus-latin";
        public const string First1KGreek = "first1k-greek";
        public const string Csel = "csel";
        public const string PatrologiaLatina = "patrologia-latina";
        public const string PoliticalTheory = "pdl-politics";
        public const string Renaissance = "renaissance";
        public const string Menota = "menota";

        /// <summary>
        /// The Reference Corpus of Middle High German. Taken from the ingest
        /// service rather than repeated here, so the key the editions are
        /// stamped with and the key the filters ask for cannot drift apart.
        /// </summary>
        public const string ReM = ReMIngestService.CollectionKey;

        /// <summary>Old French chansons de geste, from the Geste corpus.</summary>
        public const string Geste = GesteIngestService.CollectionKey;

        /// <summary>The Base de Français Médiéval - Old and Middle French, 9th to 15th century.</summary>
        public const string Bfm = BfmIngestService.CollectionKey;

        /// <summary>Dante's Commedia, annotated - a critical edition, not a manuscript.</summary>
        public const string Dante = DanteIngestService.CollectionKey;

        /// <summary>The Pyramid Texts - Old Egyptian, in hieroglyphs and transliteration.</summary>
        public const string PyramidTexts = PyramidTextsIngestService.CollectionKey;

        /// <summary>Egyptian texts from the Thesaurus Linguae Aegyptiae - the Book of the Dead, medical papyri, literature.</summary>
        public const string TlaEgyptian = AedTeiIngestService.CollectionKey;

        /// <summary>Greek and Latin curse tablets - Attica, Sicily and the Roman provinces.</summary>
        public const string Defixiones = DefixionesIngestService.CollectionKey;

        /// <summary>Greek magical papyri from papyri.info and Coptic spells from Kyprianos.</summary>
        public const string MagicalPapyri = MagicalPapyriIngestService.CollectionKey;

        /// <summary>The Hebrew Bible - the Leningrad Codex, parsed, with the JPS translation of 1917.</summary>
        public const string HebrewBible = HebrewBibleIngestService.CollectionKey;

        /// <summary>Early English books on devils, spirits and angels, from EEBO-TCP.</summary>
        public const string EarlyEnglishSpirits = EarlyEnglishSpiritsIngestService.CollectionKey;

        /// <summary>1 Enoch, the Testament of Solomon, Jubilees and the other books outside the Bible.</summary>
        public const string Pseudepigrapha = PseudepigraphaIngestService.CollectionKey;

        /// <summary>Talmud tractates, Pirkei de-Rabbi Eliezer and tracts of Otzar Midrashim.</summary>
        public const string Rabbinic = RabbinicIngestService.CollectionKey;

        /// <summary>The Quran in Arabic, Uthmani and simple, with Pickthall's English.</summary>
        public const string Quran = QuranIngestService.CollectionKey;

        /// <summary>The First Part of Aquinas's Summa Theologiae, in the English Dominican translation.</summary>
        public const string Aquinas = AquinasIngestService.CollectionKey;

        /// <summary>Pseudo-Dionysius the Areopagite in John Parker's English.</summary>
        public const string Dionysius = DionysiusIngestService.CollectionKey;

        /// <summary>The Goetia of Mathers and Crowley (1904): the seventy-two spirits and their conjuration.</summary>
        public const string Goetia = GoetiaIngestService.CollectionKey;

        /// <summary>Athanasius's Life of Antony in Ellershaw's English.</summary>
        public const string LifeOfAntony = AntonyIngestService.CollectionKey;

        /// <summary>The Arabian Nights in Burton's translation, with the third Supplemental volume.</summary>
        public const string ArabianNights = NightsIngestService.CollectionKey;
    }

    /// <summary>
    /// How each collection key should be named to a person. Anything not listed -
    /// a library predating this, or a collection installed to a custom folder the
    /// backfill could not recognise - is shown under its own key rather than
    /// hidden, so an unfamiliar name is at least a name.
    /// </summary>
    public static string DescribeCollection(string key) => key switch
    {
        CollectionKeys.PerseusGreek => "Ancient Greek (Perseus)",
        CollectionKeys.PerseusLatin => "Ancient Latin (Perseus)",
        CollectionKeys.First1KGreek => "Post-Classical Greek (First1KGreek)",
        CollectionKeys.Csel => "Latin Church Fathers (CSEL)",
        CollectionKeys.PatrologiaLatina => "Patrologia Latina (Migne)",
        CollectionKeys.PoliticalTheory => "Political Theory (Bodin)",
        CollectionKeys.Renaissance => "English Literature (Renaissance)",
        CollectionKeys.Menota => "Medieval Nordic (Menota)",
        CollectionKeys.ReM => "Middle High German (ReM)",
        CollectionKeys.Geste => "Old French chansons de geste (Geste)",
        CollectionKeys.Bfm => "Medieval French (BFM)",
        CollectionKeys.Dante => "Dante, Commedia (annotated)",
        CollectionKeys.PyramidTexts => "Pyramid Texts (Old Egyptian)",
        CollectionKeys.TlaEgyptian => "Egyptian texts (Thesaurus Linguae Aegyptiae)",
        CollectionKeys.Defixiones => "Curse tablets (Greek and Latin)",
        CollectionKeys.MagicalPapyri => "Magical papyri (Greek and Coptic)",
        CollectionKeys.HebrewBible => "Hebrew Bible (Leningrad Codex)",
        CollectionKeys.EarlyEnglishSpirits => "Early English Books: devils, spirits and angels (EEBO-TCP)",
        CollectionKeys.Pseudepigrapha => "Pseudepigrapha: Enoch, Solomon, Jubilees",
        CollectionKeys.Rabbinic => "Rabbinic texts: Talmud and midrash",
        CollectionKeys.Quran => "The Quran (Arabic, with Pickthall's English)",
        CollectionKeys.Aquinas => "Aquinas, Summa Theologiae, First Part (English)",
        CollectionKeys.Dionysius => "Pseudo-Dionysius the Areopagite (Parker's English)",
        CollectionKeys.Goetia => "The Goetia (Mathers and Crowley, 1904)",
        CollectionKeys.LifeOfAntony => "Athanasius, Life of Antony (English)",
        CollectionKeys.ArabianNights => "The Arabian Nights (Burton)",
        _ => key
    };

    public static List<SetupDataSource> Build(
        AuthorRepository authorRepo, LemmaRepository lemmaRepo, DefinitionRepository definitionRepo,
        ArtifactRepository artifactRepo, EditionRepository editionRepo,
        CatmusRepository? catmusRepo = null)
    {
        // Optional so the three callers that predate it keep compiling, and
        // defaulted rather than left null so the step is always built - a
        // setup list that silently loses a step depending on which overload
        // was called would be worse than a repository allocated for nothing.
        catmusRepo ??= new CatmusRepository();

        var dataRoot = DataRoot;

        // Named once, so the step's download location and its "is this
        // already loaded?" check can't drift apart.
        var first1kDestination = Path.Combine(dataRoot, "first1k-greek");
        var cselDestination = Path.Combine(dataRoot, "csel");
        var patrologiaDestination = Path.Combine(dataRoot, "patrologia-latina");
        var politicalDestination = Path.Combine(dataRoot, "political-theory");

        return new List<SetupDataSource>
        {
            new SetupDataSource
            {
                Title = "Ancient Greek Texts",
                RepoUrl = "https://github.com/PerseusDL/canonical-greekLit",
                DefaultDestination = Path.Combine(dataRoot, "greek-texts"),
                PlainLanguageDescription =
                    "The actual Greek texts themselves - everything from Homer to late antiquity. " +
                    "This is a few hundred megabytes.",
                RunIngest = async (root, progress, ct) =>
                {
                    var service = new PerseusIngestService();
                    service.CollectionKey = CollectionKeys.PerseusGreek;
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));
                    await service.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "greekLit") }, wrapped, ct);
                    await editionRepo.StampCollectionAsync(root, CollectionKeys.PerseusGreek, ct);
                    return IngestOutcome.From(service.FailedFiles, service.RecoveredWithoutCatalog, service.FilesAttempted);
                },
                // Checked by collection, which is the only thing that answers
                // for this step alone.
                //
                // Not by edition language: Perseus's Greek corpus legitimately
                // contains Latin-language editions - old Latin translations of
                // Greek works - so a Language='grc' count cannot tell "the
                // Greek corpus is loaded" from "isn't yet".
                //
                // And not by Authors.Namespace, which is what this used to
                // ask. A namespace is shared - First1KGreek is greekLit
                // exactly as canonical-greekLit is - so from 3.2.0 on,
                // installing either one reported the other as present and the
                // wizard skipped a corpus that had never been fetched. See
                // AuthorRepository.CountByNamespaceAsync.
                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.PerseusGreek),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.PerseusGreek) > 0
            },

            new SetupDataSource
            {
                Title = "Ancient Latin Texts",
                RepoUrl = "https://github.com/PerseusDL/canonical-latinLit",
                DefaultDestination = Path.Combine(dataRoot, "latin-texts"),
                PlainLanguageDescription =
                    "The Latin counterpart to the Greek texts above - Caesar, Cicero, Virgil, and the rest. This is a few hundred megabytes.",
                RunIngest = async (root, progress, ct) =>
                {
                    var service = new PerseusIngestService();
                    service.CollectionKey = CollectionKeys.PerseusLatin;
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));
                    await service.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "latinLit") }, wrapped, ct);
                    await editionRepo.StampCollectionAsync(root, CollectionKeys.PerseusLatin, ct);
                    return IngestOutcome.From(service.FailedFiles, service.RecoveredWithoutCatalog, service.FilesAttempted);
                },
                // Same reasoning as the Greek row above, and this is the row
                // where it was actually observed. A library holding only the
                // Patrologia Latina and CSEL has 335 latinLit authors and none
                // of the ones this step exists to fetch, so the old namespace
                // check reported "Ancient Latin Texts" as already installed
                // and Virgil never arrived - with nothing looking wrong
                // afterwards, because the library was full of Latin.
                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.PerseusLatin),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.PerseusLatin) > 0
            },

            new SetupDataSource
            {
                Title = "Post-Classical Greek Texts",
                RepoUrl = "https://github.com/OpenGreekAndLatin/First1KGreek",
                DisplayNote = "extends the Ancient Greek Texts above into late antiquity - same library, not a separate one",
                DefaultDestination = first1kDestination,
                PlainLanguageDescription =
                    "The Open Greek and Latin project's sequel to the Ancient Greek Texts above - Greek " +
                    "(and a little Latin) written after the classical period, into late antiquity. Authors " +
                    "and works already in your library (from a handful of famous plays this collection also " +
                    "carries alternate 19th/20th-century editions of) just gain an extra edition to choose " +
                    "from in the original-language dropdown - nothing gets overwritten. This is several " +
                    "hundred megabytes and takes a few minutes.",
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
                    service.CollectionKey = CollectionKeys.First1KGreek;
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

                    await editionRepo.StampCollectionAsync(root, CollectionKeys.First1KGreek, ct);
                    return IngestOutcome.From(service.FailedFiles, service.RecoveredWithoutCatalog, service.FilesAttempted);
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
                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.First1KGreek),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.First1KGreek) > 0
            },

            new SetupDataSource
            {
                Title = "Latin Church Fathers (CSEL)",
                RepoUrl = "https://github.com/OpenGreekAndLatin/csel-dev",
                DisplayNote = "extends the Ancient Latin Texts above into late antiquity - same library, not a separate one",
                DefaultDestination = cselDestination,
                PlainLanguageDescription =
                    "The Corpus Scriptorum Ecclesiasticorum Latinorum - the critical editions of the Latin " +
                    "Church Fathers, from the volumes old enough to be out of copyright. Augustine, Ambrose, " +
                    "Jerome, Cyprian and their contemporaries, in the editions scholars actually cite. " +
                    "Authors already in your library gain works and editions rather than duplicates. " +
                    "Around 400 megabytes and takes a few minutes.",
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
                    service.CollectionKey = CollectionKeys.Csel;
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));
                    await service.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "latinLit") }, wrapped, ct);

                    await editionRepo.StampCollectionAsync(root, CollectionKeys.Csel, ct);
                    return IngestOutcome.From(service.FailedFiles, service.RecoveredWithoutCatalog, service.FilesAttempted);
                },

                // Same reasoning as First1KGreek above: Authors.Namespace="latinLit" is
                // already non-zero from the classical Latin corpus, so it cannot tell
                // "loaded" from "this step has not run". Editions record the file they
                // were built from, and this corpus downloads to a folder of its own.
                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Csel),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Csel) > 0
            },

            new SetupDataSource
            {
                Title = "Patrologia Latina (Migne)",
                RepoUrl = "https://github.com/OpenGreekAndLatin/patrologia_latina-dev",
                DisplayNote = "the widest net for Latin Christian writing - a reprint, not a critical edition",
                DefaultDestination = patrologiaDestination,
                PlainLanguageDescription =
                    "Migne's Patrologia Latina - Latin Christian writing from Tertullian to the twelfth " +
                    "century. Worth knowing what it is before you cite from it: Migne reprinted whatever " +
                    "editions he could obtain in the 1840s and 50s, so where a work also appears in the " +
                    "Church Fathers collection above, that one is the edition scholars cite and this one " +
                    "is the wider net. Both can sit side by side - the same work gains a second edition " +
                    "in the dropdown rather than being overwritten.\r\n\r\n" +
                    "One caveat worth knowing before you build on it. Most of this collection is still " +
                    "under temporary reference numbers that the publishing project intends to replace. " +
                    "The texts and their authors are sound, and nothing about reading them is affected - " +
                    "but if those numbers change and you re-import, notes tied to those particular " +
                    "passages will need re-attaching. Works whose author Migne could not name arrive " +
                    "under “Incertus”, his own word for it. Around 1.4 gigabytes and only takes a few minutes.",
                RunIngest = async (root, progress, ct) =>
                {
                    // The whole repository is imported, placeholders included.
                    //
                    // It holds 9,400 textgroups: 630 named stoa0022, stoa0040 and so on,
                    // and 8,770 named tmp1, tmp26, tmp990, from a conversion still in
                    // progress. Those were excluded for a while on the strength of their
                    // CTS URNs being provisional - urn:cts:latinLit:tmp26 - since notes
                    // in this application bind to CTS identity precisely because it is
                    // meant to outlast a re-ingest.
                    //
                    // Included now, deliberately, because the objection is narrower than
                    // it looked. Each file holds one work, correctly attributed by its
                    // groupname; roughly nine in ten name their author outright. The
                    // cost of excluding them is eight thousand texts today. The cost of
                    // including them is that notes on those passages may need
                    // re-attaching if the numbering changes, which the step says plainly
                    // rather than leaving to be discovered.
                    var service = new PerseusIngestService
                    {
                        // Migne returns to an author across volumes and each appearance
                        // became its own textgroup, so without this the tree lists
                        // Alphanus of Benevento six times and Anonymus nine, a work or
                        // two under each. They are the same people.
                        MergeAuthorsByName = true,

                        // Migne leaves the author empty where a work has none - the
                        // Council of Carthage, an appendix to Cyprian, an anonymous
                        // passion - and writes "Incertus" wherever he does fill it in.
                        // Borrowing his own word keeps those texts and keeps the author
                        // column readable. Roughly one textgroup in eleven, so they
                        // gather under a single row rather than eight hundred alike.
                        UnnamedTextGroupAuthor = ("urn:cts:latinLit:pl.incertus", "Incertus")
                    };
                    service.CollectionKey = CollectionKeys.PatrologiaLatina;
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));
                    await service.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "latinLit") }, wrapped, ct);

                    await editionRepo.StampCollectionAsync(root, CollectionKeys.PatrologiaLatina, ct);
                    return IngestOutcome.From(service.FailedFiles, service.RecoveredWithoutCatalog, service.FilesAttempted);
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.PatrologiaLatina),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.PatrologiaLatina) > 0
            },

            new SetupDataSource
            {
                Title = "English Literature (Renaissance)",
                RepoUrl = "https://github.com/PerseusDL/canonical-engLit",
                DisplayNote = "(Shakespeare, Holinshed, Hakluyt - needs English Lemma Data, a later step)",
                DefaultDestination = Path.Combine(dataRoot, "english-texts"),
                PlainLanguageDescription =
                    "Perseus's Renaissance and early modern collection - Shakespeare, Holinshed, Hakluyt, " +
                    "Sidney, James I. Useful mainly for reception: how later writers reworked classical " +
                    "material. Half of Perseus's copy is still in copyright and is not imported, which " +
                    "means no Marlowe - see About. These are 16th and 17th century English, while the " +
                    "English dictionary (WordNet, a later step) is modern, so archaic forms like \"hath\" and \"doth\" won't " +
                    "find a headword. About a minute, and under a hundred megabytes.",
                RunIngest = async (root, progress, ct) =>
                {
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));

                    // CTS layout (english-texts/data) - Sidney, James I.
                    var cts = new PerseusIngestService();
                    cts.CollectionKey = CollectionKeys.Renaissance;
                    await cts.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "engLit") }, wrapped, ct);

                    // Pre-CTS layout (english-texts/Renaissance/**/opensource) -
                    // the Shakespeare canon, Marlowe, Holinshed, Hakluyt, etc.
                    // Runs second so its name-based de-dup can fold Sidney and
                    // James I into the author rows the CTS pass just created.
                    var renaissance = Path.Combine(root, "Renaissance");
                    if (!Directory.Exists(renaissance))
                    {
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Renaissance, ct);
                        return IngestOutcome.From(cts.FailedFiles, cts.RecoveredWithoutCatalog, cts.FilesAttempted);
                    }

                    var preCts = new RenaissanceIngestService();
                    await preCts.IngestAsync(renaissance, wrapped, ct);

                    // Both passes land under the same download folder, so one stamp
                    // covers the CTS works and the pre-CTS ones alike.
                    await editionRepo.StampCollectionAsync(root, CollectionKeys.Renaissance, ct);

                    // A text this pass left out because the catalogued tree
                    // already supplied it goes in beside the recoveries: both
                    // mean the library has what it should and something was
                    // decided on the reader's behalf that the log should
                    // record.
                    return IngestOutcome.Combine(
                        IngestOutcome.From(cts.FailedFiles, cts.RecoveredWithoutCatalog, cts.FilesAttempted),
                        IngestOutcome.From(preCts.FailedFiles, preCts.SupersededByCatalogue));
                },
                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Renaissance),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Renaissance) > 0
            },

            new SetupDataSource
            {
                Title = "Political Theory: Bodin",
                RepoUrl = "https://github.com/PerseusDL/canonical-pdlpsci",
                DisplayNote = "one work in three languages - small, and the only thing in this collection",
                DefaultDestination = politicalDestination,
                PlainLanguageDescription =
                    "Jean Bodin's Six Books of the Commonwealth, the founding text of modern sovereignty, " +
                    "in all three of the versions that matter: the French of 1577 he wrote first, the Latin " +
                    "of 1586 he made himself, and Richard Knolles's English of 1606. Reading his own Latin " +
                    "against his own French is the kind of comparison the reader panes were built for. " +
                    "About nine megabytes and very quick - it is one work, not a corpus.",
                RunIngest = async (root, progress, ct) =>
                {
                    // Same CTS layout as everything else here, in its own namespace:
                    // urn:cts:pdlpsci:bodin rather than latinLit. Passed through as
                    // declared rather than folded into Latin, because it is not Latin
                    // literature - the French is the original and the Latin is Bodin
                    // translating himself, which is exactly the distinction the
                    // edition/translation split exists to keep.
                    var service = new PerseusIngestService();
                    service.CollectionKey = CollectionKeys.PoliticalTheory;
                    var wrapped = new Progress<IngestProgress>(p =>
                        progress.Report($"{p.CurrentAuthor}: {p.CurrentWork} ({p.WorksProcessed}/{p.TotalWorks})"));
                    await service.IngestAsync(
                        new[] { (Path.Combine(root, "data"), "pdlpsci") }, wrapped, ct);

                    await editionRepo.StampCollectionAsync(root, CollectionKeys.PoliticalTheory, ct);
                    return IngestOutcome.From(service.FailedFiles, service.RecoveredWithoutCatalog, service.FilesAttempted);
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.PoliticalTheory),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.PoliticalTheory) > 0
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
                    "and the Norwegian law manuscripts - from the Medieval Nordic Text Archive. Menota's " +
                    "site has a Download all XML Files button that fetches all 91 in about a minute; on " +
                    "import you are then asked, one manuscript at a time, whether each should be merged.\r\n\r\n" +
                    "Menota publishes one file per manuscript, with no single archive to fetch, so these " +
                    "are downloaded by hand. Save the XML files into the folder below, then import them.\r\n\r\n" +
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
                    await editionRepo.StampCollectionAsync(root, CollectionKeys.Menota, ct);

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

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Menota),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Menota) > 0
            },

            new SetupDataSource
            {
                Title = "Middle High German Texts (ReM)",
                RepoUrl = ReMIngestService.ArchiveUrl,
                DisplayNote = "one 27 MB download; 406 texts, 1050-1350",
                DefaultDestination = Path.Combine(dataRoot, "rem"),

                // DirectDownload, unlike Menota next door: ReM publishes the
                // whole corpus as one archive on Zenodo, with no login and no
                // form, so there is nothing for a reader to do by hand.
                FetchMode = SetupFetchMode.DirectDownload,
                DownloadFileName = ReMIngestService.ArchiveFileName,

                Links =
                {
                    new SetupLink
                    {
                        Text = "About the Reference Corpus of Middle High German",
                        Url = "https://linguistics.rub.de/rem/"
                    }
                },

                PlainLanguageDescription =
                    "German as it was written between 1050 and 1350, transcribed from the manuscripts "
                    + "themselves - the Nibelungenlied, Wolfram's Parzival, Hartmann's Iwein, Gottfried's "
                    + "Tristan, sermons, charters, charms and saints' lives, 406 texts in all. Each one "
                    + "arrives twice: once spelled as the scribe wrote it, and once in the normalised "
                    + "form a printed edition would use. The reader switches between them in the edition "
                    + "dropdown, so you can read the text and see the manuscript behind it. "
                    + "Most of these texts are anonymous, and are filed that way; the citation is the "
                    + "manuscript's own folio and line, or the editor's line numbers where the corpus "
                    + "cites by those instead.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new ReMIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    progress.Report(
                        $"{service.TextsInstalled:N0} texts installed, {service.LinesInstalled:N0} lines, "
                        + "each in two readings.");

                    // Guarded on something having actually arrived. Stamping
                    // records the collection as FINISHED, and a run where every
                    // text failed must not leave a green tick over an empty
                    // collection - which is the one state the completion table
                    // exists to prevent.
                    if (service.TextsInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.ReM, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.ReM),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.ReM) > 0
            },

            new SetupDataSource
            {
                Title = "Old French Texts (Geste)",
                RepoUrl = GesteIngestService.ArchiveUrl,
                DisplayNote = "one 6 MB download; 33 texts, annotated, with their manuscripts' spelling",
                DefaultDestination = Path.Combine(dataRoot, "geste"),
                FetchMode = SetupFetchMode.DirectDownload,
                DownloadFileName = GesteIngestService.ArchiveFileName,

                Links =
                {
                    new SetupLink
                    {
                        Text = "About Geste, a corpus of chansons de geste",
                        Url = "https://github.com/Jean-Baptiste-Camps/Geste"
                    }
                },

                PlainLanguageDescription =
                    "The Old French epics - Floovant, Otinel in five manuscripts, Garin le Lorrain in "
                    + "ten, Aspremont, Fierabras, Huon de Bordeaux, Girart de Vienne - transcribed from "
                    + "the manuscripts at the École nationale des chartes. 33 texts in Old French, "
                    + "Anglo-Norman and Walloon. "
                    + "Nineteen of them arrive twice: once letter for letter as the scribe wrote it, "
                    + "abbreviations and all, and once in the reading a modern edition prints. The "
                    + "edition dropdown switches between them. "
                    + "Every word is also mapped to its dictionary headword with its grammar, so Word "
                    + "Study works on this collection the moment it finishes - there is no second "
                    + "download for it, because the corpus annotated its own text. "
                    + "The poems are anonymous, as chansons de geste almost always are, and are filed "
                    + "that way; the citation is the edition's verse number, or the line's position "
                    + "where the corpus gives no number.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new GesteIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    progress.Report(
                        $"{service.TextsInstalled:N0} texts installed, {service.LinesInstalled:N0} verse "
                        + $"lines, {service.MappingsLoaded:N0} word-form mappings.");

                    if (service.TextsInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Geste, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Geste),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Geste) > 0
            },

            new SetupDataSource
            {
                Title = "Medieval French Texts (BFM)",
                RepoUrl = BfmCatalogue.HomeUrl,
                DisplayNote = $"{BfmCatalogue.Count} texts, {BfmCatalogue.TotalBytes / 1048576.0:F0} MB over " +
                              "many small downloads",
                DefaultDestination = Path.Combine(dataRoot, "bfm"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink
                    {
                        Text = "About the Base de Français Médiéval",
                        Url = BfmCatalogue.HomeUrl
                    }
                },

                PlainLanguageDescription =
                    "French as it was written between the ninth century and the fifteenth - the Chanson "
                    + "de Roland, all four of Chrétien de Troyes's romances, Aucassin et Nicolette, the "
                    + "Queste del saint Graal, the Roman de Renart, Marie de France, Rutebeuf, Villon, "
                    + "and 281 fabliaux. 500 texts from the Base de Français Médiéval at the ENS de "
                    + "Lyon, about 220 MB, fetched one text at a time because that is how they are "
                    + "published. "
                    + "Most of them arrive twice: once letter for letter as the scribe wrote it, "
                    + "abbreviations unexpanded and words run together where the manuscript runs them "
                    + "together, and once in the reading a modern editor prints. The edition dropdown "
                    + "switches between them. "
                    + "Verse is cited by line and prose by section. Twenty-three of the texts carry "
                    + "their own word-by-word annotation, so Word Study answers on those with no "
                    + "further download. "
                    + "Six texts are marked by the corpus as not redistributable and are skipped; the "
                    + "step says how many when it finishes. The licences differ by text - most are "
                    + "Licence Ouverte, the fabliaux and a few others are non-commercial share-alike - "
                    + "and the README lists which.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new BfmIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.TextsInstalled > 0)
                    {
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Bfm, ct);
                    }

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Bfm),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Bfm) > 0
            },

            new SetupDataSource
            {
                Title = "Dante's Commedia, annotated",
                RepoUrl = DanteIngestService.ArchiveUrl,
                DisplayNote = "2.4 MB; a critical edition, not a manuscript transcription",
                DefaultDestination = Path.Combine(dataRoot, "dante"),
                FetchMode = SetupFetchMode.DirectDownload,
                DownloadFileName = DanteIngestService.ArchiveFileName,

                Links =
                {
                    new SetupLink
                    {
                        Text = "About the Universal Dependencies Old Italian treebank",
                        Url = "https://github.com/UniversalDependencies/UD_Italian-Old"
                    }
                },

                PlainLanguageDescription =
                    "The whole Commedia - Inferno, Purgatorio, Paradiso, 14,233 lines - with every one "
                    + "of its 122,000 words mapped to its dictionary headword and parsed. Click a word "
                    + "and Word Study answers, with no second download: the annotation comes in the "
                    + "same file as the text. Lines are cited as Dante has been cited for seven "
                    + "centuries, by canto and verse, so Inf. 5.142 here is Inf. 5.142 in any printed "
                    + "edition. "
                    + "Be clear about what this is: Petrocchi's critical edition, not a transcription "
                    + "of a manuscript. It has no scribe's spelling and no second reading to switch to, "
                    + "unlike the Norse, Middle High German and Old French collections. It is also one "
                    + "poem rather than a corpus - the large Old Italian corpora exist but none of them "
                    + "can be downloaded at all, so this is the whole of what is openly available.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new DanteIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    progress.Report(
                        $"{service.VersesInstalled:N0} verses installed across three canticles, "
                        + $"{service.MappingsLoaded:N0} word-form mappings.");

                    if (service.VersesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Dante, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Dante),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Dante) > 0
            },

            new SetupDataSource
            {
                Title = "The Pyramid Texts (Old Egyptian)",
                RepoUrl = PyramidTextsIngestService.ArchiveUrl,
                DisplayNote = "1 MB; hieroglyphs and transliteration, but no translation",
                DefaultDestination = Path.Combine(dataRoot, "pyramid-texts"),
                FetchMode = SetupFetchMode.DirectDownload,
                DownloadFileName = PyramidTextsIngestService.ArchiveFileName,

                Links =
                {
                    new SetupLink
                    {
                        Text = "About the Universal Dependencies Egyptian treebank",
                        Url = "https://github.com/UniversalDependencies/UD_Egyptian-PC"
                    }
                },

                PlainLanguageDescription =
                    "The oldest religious literature that survives anywhere: spells carved into the "
                    + "burial chambers of six pyramids at Saqqara between about 2350 and 2200 BC, so "
                    + "that the dead king might eat, breathe, rise and cross the sky. 519 spells in "
                    + "3,089 passages, from Unas - whose pyramid is the first with any writing in it "
                    + "at all - through Teti, Pepi I, Merenre and Pepi II to Queen Neith. "
                    + "Each pyramid arrives twice: once in hieroglyphs and once in the transliteration "
                    + "Egyptologists read them in, and the edition dropdown switches between them. "
                    + "Every word is parsed and carries its dictionary headword, so Word Study answers "
                    + "with no second download. Passages are cited by spell and section, which is how "
                    + "these are cited - Pyr. 23.16a. "
                    + "Two things to know before you take it. There is no translation: the treebank "
                    + "has none, and the standard English translations are in copyright, so you get "
                    + "the signs, the transliteration and the grammar but not the sense. And about one "
                    + "passage in five shows a small empty box for a sign, because Unicode added four "
                    + "thousand hieroglyphs in 2023 and no font that ships with Windows has them yet.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new PyramidTextsIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.UtterancesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.PyramidTexts, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.PyramidTexts),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.PyramidTexts) > 0
            },

            new SetupDataSource
            {
                Title = "Egyptian texts: the Book of the Dead, medical papyri and literature",
                RepoUrl = AedTeiIngestService.ArchiveUrl,
                DisplayNote = "75 MB; transliteration with German translation, hieroglyphs for some",
                DefaultDestination = Path.Combine(dataRoot, "tla-egyptian"),
                FetchMode = SetupFetchMode.DirectDownload,
                DownloadFileName = AedTeiIngestService.ArchiveFileName,

                Links =
                {
                    new SetupLink
                    {
                        Text = "About AED-TEI, the Thesaurus Linguae Aegyptiae in TEI",
                        Url = "https://github.com/simondschweitzer/aed-tei"
                    }
                },

                PlainLanguageDescription =
                    "Egyptian texts from the Thesaurus Linguae Aegyptiae, the Berlin and Leipzig "
                    + "academies' dictionary of Ancient Egyptian, as it stood in 2018: about 3,400 texts "
                    + "in 59,000 passages. The Book of the Dead from 24 papyri, the Papyrus of Nu and the "
                    + "Turin papyrus among them; the medical papyri, Ebers and Edwin Smith, with their "
                    + "healing spells; the Bremner-Rhind papyrus and the other late funerary and temple "
                    + "books; the literature, from Sinuhe and the Shipwrecked Sailor to the love songs; "
                    + "and letters, archives, stelae and rock inscriptions. Every passage is in "
                    + "Egyptological transliteration with the TLA's German translation beside it, and "
                    + "the literary, medical and funerary papyri also arrive in hieroglyphs. Book of the "
                    + "Dead passages are cited by spell, so 125.12 is the twelfth sentence of spell 125, "
                    + "the Judgement of the Dead. Word Study looks every word up in the TLA dictionary "
                    + "and gives its German meaning. Two things to know. The translation is German, "
                    + "because that is the TLA's language. And the tomb inscriptions are not here: the "
                    + "conversion this comes from keeps which wall a text is on but not whose tomb, so "
                    + "they could only arrive as hundreds of works called \"West wall\".",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new AedTeiIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.SentencesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.TlaEgyptian, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.TlaEgyptian),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.TlaEgyptian) > 0
            },

            new SetupDataSource
            {
                Title = "Curse tablets (Greek and Latin)",
                RepoUrl = "https://telota.bbaw.de/ig/",
                DisplayNote = "About 870 small downloads; English and German for the Attic tablets",
                DefaultDestination = Path.Combine(dataRoot, "defixiones"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink { Text = "Inscriptiones Graecae: Defixiones Atticae", Url = "https://telota.bbaw.de/ig/" },
                    new SetupLink { Text = "I.Sicily", Url = "https://github.com/ISicily/ISicily" },
                    new SetupLink { Text = "Epigraphic Database Heidelberg", Url = "https://edh.ub.uni-heidelberg.de/data" }
                },

                PlainLanguageDescription =
                    "The lead sheets on which Greeks and Romans bound a rival's tongue, a thief's body or a "
                    + "charioteer's horses, and buried in graves, dropped in wells or threw into sacred "
                    + "springs. About 840 of them. Every curse tablet found in Attica - 486, the "
                    + "Inscriptiones Graecae's own volume - each with an English and a German translation "
                    + "line for line beside the Greek. The Greek tablets of Sicily, from Selinous, Kamarina "
                    + "and Gela, from I.Sicily. And the Latin tablets of the Roman provinces from the "
                    + "Epigraphic Database Heidelberg: Bath and Uley under Britannia, Mainz under Germania "
                    + "superior, and Spain, Pannonia, Africa and the rest. One work per region; a passage "
                    + "is a line of a tablet, cited tablet and line, so 125.6 is line 6 of IG II/III3 8, 1, "
                    + "125. The texts are printed in the brackets epigraphers use - [restored], [- - -] for "
                    + "a loss, a dot under an uncertain letter. Two things to know. Only the Attic tablets "
                    + "and a few Sicilian ones have translations; the Heidelberg database has none. And "
                    + "each source publishes one file per tablet, so this step fetches about 870 small "
                    + "files and takes a few minutes.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new DefixionesIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.TabletsInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Defixiones, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Defixiones),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Defixiones) > 0
            },

            new SetupDataSource
            {
                Title = "Magical papyri (Greek and Coptic)",
                RepoUrl = "https://papyri.info/",
                DisplayNote = "About 150 small downloads and one 2.4 MB file; English for the Coptic texts",
                DefaultDestination = Path.Combine(dataRoot, "magical-papyri"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink { Text = "papyri.info (DCLP and DDbDP)", Url = "https://papyri.info/" },
                    new SetupLink { Text = "Kyprianos, the Coptic Magical Papyri database", Url = "https://www.coptic-magic.phil.uni-wuerzburg.de/index.php/kyprianos-help-page/" }
                },

                PlainLanguageDescription =
                    "The working papers of ancient magicians: handbooks of spells, single spells written out "
                    + "for a client, and amulets folded small and worn. About 150 Greek papyri from papyri.info "
                    + "- every one that the Digital Corpus of Literary Papyri or the documentary databank tags as "
                    + "magic and that has an edited text, among them PGM I complete (the spell for getting a "
                    + "divine assistant), most of the Supplementum Magicum, the Oxyrhynchus formularies and a "
                    + "spell to bring down the horses of a chariot race. And about 350 Coptic spells from the "
                    + "Kyprianos database of the Würzburg Coptic Magical Papyri project - curses, love spells, "
                    + "healing and protection - each with its English translation beside it line by line. One "
                    + "work per papyrus or spell, cited column and line. Two things to know. The great "
                    + "handbooks - PGM IV in Paris, the London and Leiden papyri - have no open digital text "
                    + "anywhere and are not here. And the Greek papyri have no translations.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new MagicalPapyriIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.TextsInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.MagicalPapyri, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.MagicalPapyri),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.MagicalPapyri) > 0
            },

            new SetupDataSource
            {
                Title = "The Hebrew Bible, with the JPS translation",
                RepoUrl = "https://github.com/openscriptures/morphhb",
                DisplayNote = "about 23 MB in 41 downloads; Hebrew, English and a Hebrew dictionary",
                DefaultDestination = Path.Combine(dataRoot, "hebrew-bible"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink
                    {
                        Text = "About the Open Scriptures Hebrew Bible",
                        Url = "https://github.com/openscriptures/morphhb"
                    },
                    new SetupLink
                    {
                        Text = "The JPS translation of 1917, on Sefaria",
                        Url = "https://www.sefaria.org/Genesis.1?ven=The_Holy_Scriptures:_A_New_Translation_(JPS_1917)"
                    }
                },

                PlainLanguageDescription =
                    "The whole Hebrew Bible - Torah, Prophets and Writings, 39 books and 23,213 verses - "
                    + "in the text of the Leningrad Codex, the oldest complete manuscript of it, copied in "
                    + "1008. Every word is parsed and keyed to Strong's dictionary, which comes with it, so "
                    + "Word Study answers on the Hebrew with no further download. "
                    + "Beside it is the translation the Jewish Publication Society published in 1917, which "
                    + "numbers its chapters and verses the way the Hebrew does, so the two panes stay "
                    + "together - unlike the King James, which numbers the Psalms, Joel and Malachi "
                    + "differently. Where the 1917 translation divides a verse differently - the Ten "
                    + "Commandments in Exodus 20 and Deuteronomy 5, and Numbers 25.19 - it is put back on "
                    + "the Hebrew's numbers as it is installed. "
                    + "Hebrew reads from right to left and is shown that way, in a font with the vowel "
                    + "points and accents. The text and the 1917 translation are in the public domain; the "
                    + "parsing and the dictionary's encoding are the Open Scriptures project's, under "
                    + "CC BY 4.0. The translation is fetched from Sefaria one book at a time.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new HebrewBibleIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.VersesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.HebrewBible, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.HebrewBible),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.HebrewBible) > 0
            },

            new SetupDataSource
            {
                Title = "Early English Books: devils, spirits and angels",
                RepoUrl = "https://www.textpartnership.net/pages/eebo-tcp.html",
                DisplayNote = $"{EarlyEnglishSpiritsIngestService.Books.Count} books, about 20 MB, one download each",
                DefaultDestination = Path.Combine(dataRoot, "early-english-spirits"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink
                    {
                        Text = "About the Text Creation Partnership's EEBO transcriptions",
                        Url = "https://www.textpartnership.net/pages/eebo-tcp.html"
                    }
                },

                PlainLanguageDescription =
                    "Twenty-one English books printed between 1572 and 1681 on devils, spirits, angels, "
                    + "witches and ghosts: James VI's Daemonologie, Reginald Scot's Discovery of Witchcraft "
                    + "with its catalogue of the names and shapes of devils, Heywood's Hierarchie of the "
                    + "Blessed Angells, Paradise Lost in its first edition of 1667 and its second of 1674, "
                    + "Paradise Regain'd, both texts of Marlowe's Doctor Faustus - the 1604 and the 1616 - "
                    + "Agrippa's Occult Philosophy, the angel conversations of John Dee, Burton's Anatomy of "
                    + "Melancholy, Lavater on ghosts, Gifford, Perkins, Glanvill, Henry More and Thomas "
                    + "Browne. "
                    + "These are transcriptions of particular printings, keyed by the Text Creation "
                    + "Partnership from Early English Books Online, in the spelling of the page: long s, u "
                    + "for v, and a bullet where the microfilm could not be read. Verse is cited by book and "
                    + "line, prose by book, chapter and paragraph, plays by line. "
                    + "All of it is free to reuse for any purpose, commercial included: the transcriptions "
                    + "are public domain, released under CC0.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new EarlyEnglishSpiritsIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.BooksInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.EarlyEnglishSpirits, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.EarlyEnglishSpirits),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.EarlyEnglishSpirits) > 0
            },

            new SetupDataSource
            {
                Title = "Pseudepigrapha: Enoch, the Testament of Solomon, Jubilees",
                RepoUrl = "https://github.com/OnlineCriticalPseudepigrapha/Online-Critical-Pseudepigrapha",
                DisplayNote = "about 4 MB in some 120 small downloads",
                DefaultDestination = Path.Combine(dataRoot, "pseudepigrapha"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink
                    {
                        Text = "Charles's Book of Enoch on Wikisource",
                        Url = "https://en.wikisource.org/wiki/The_Book_of_Enoch_(Charles)"
                    },
                    new SetupLink
                    {
                        Text = "About the Online Critical Pseudepigrapha",
                        Url = "https://github.com/OnlineCriticalPseudepigrapha/Online-Critical-Pseudepigrapha"
                    }
                },

                PlainLanguageDescription =
                    "The books outside the Bible that tell where angels and demons came from. 1 Enoch in "
                    + "R. H. Charles's translation of 1917 - the Watchers, the giants, the demons born of "
                    + "them - filed beside the Greek fragments the library already has, so chapters 1-32 read "
                    + "side by side. The Testament of Solomon, with its catalogue of the demons Solomon bound "
                    + "with his ring, in McCown's Greek and Conybeare's English. The Testaments of the Twelve "
                    + "Patriarchs in Greek and in Charles's English, Jubilees in Charles's English, the Greek "
                    + "Apocalypse of Moses, the Latin Life of Adam and Eve, and 2 Enoch. "
                    + "Two things to know. Conybeare numbered his translation of the Testament of Solomon in "
                    + "130 sections of his own, from a different manuscript, so it is cited §1 to §130 and "
                    + "does not scroll with McCown's chapters. And Jubilees has no original-language pane: "
                    + "the Hebrew beside the English is a nineteenth-century retranslation from the Ethiopic. "
                    + "The English translations are in the public domain; the Greek texts are the Online "
                    + "Critical Pseudepigrapha's, under CC BY 4.0.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new PseudepigraphaIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.PassagesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Pseudepigrapha, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Pseudepigrapha),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Pseudepigrapha) > 0
            },

            new SetupDataSource
            {
                Title = "Rabbinic texts: Talmud and midrash on angels and demons",
                RepoUrl = "https://www.sefaria.org",
                DisplayNote = "about 20 MB in 33 downloads",
                DefaultDestination = Path.Combine(dataRoot, "rabbinic"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink { Text = "Sefaria", Url = "https://www.sefaria.org" }
                },

                PlainLanguageDescription =
                    "Five tractates of the Babylonian Talmud - Berakhot, Shabbat, Pesachim, Hagigah and "
                    + "Gittin, the ones the demons are in - with Pirkei de-Rabbi Eliezer and eight tracts of "
                    + "Eisenstein's Otzar Midrashim: the Alphabet of Ben Sira, where Lilith is, Hekhalot, the "
                    + "Metatron texts, the Book of Raziel, Eden and Gehinnom, and the midrashim on King "
                    + "Solomon. "
                    + "The Talmud is the Vilna text as transcribed on Hebrew Wikisource (CC BY-SA), cited by "
                    + "folio, side and segment (Berakhot 6a.10), with Lazarus Goldschmidt's German "
                    + "translation of 1929 beside every tractate, A. Cohen's English of Berakhot (1921) and "
                    + "Sefaria's community English of Hagigah. There is no free English of Shabbat, Pesachim "
                    + "or Gittin to give you: the complete modern English is non-commercial, and is left "
                    + "out for that reason. Pirkei de-Rabbi Eliezer comes in Hebrew and in Friedlander's "
                    + "English of 1916; the Otzar tracts in Hebrew, and in English where Sefaria's "
                    + "community translation has reached them. Everything is fetched from Sefaria, and each "
                    + "file's licence is checked as it is read.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new RabbinicIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.PassagesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Rabbinic, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Rabbinic),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Rabbinic) > 0
            },

            new SetupDataSource
            {
                Title = "The Quran, in Arabic and in Pickthall's English",
                RepoUrl = "https://tanzil.net",
                DisplayNote = "about 6 MB in three downloads",
                DefaultDestination = Path.Combine(dataRoot, "quran"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink { Text = "The Tanzil Project", Url = "https://tanzil.net" },
                    new SetupLink { Text = "Tanzil's updates to the text", Url = "https://tanzil.net/updates/" }
                },

                PlainLanguageDescription =
                    "The Quran in Arabic, all 114 suras, with Marmaduke Pickthall's English translation of "
                    + "1930 beside it: Iblis who would not bow to Adam, Harut and Marut, the jinn, the angels "
                    + "who take souls and the angels who record. "
                    + "The Arabic comes twice, from the Tanzil Project: in the Uthmani script of the Madina "
                    + "mushaf, with every vowel and recitation mark, and in modern spelling. Both are the "
                    + "same text, and the edition list calls them Uthmani script and modern spelling. Arabic "
                    + "reads from right to left and is shown that way, and search ignores the vowel and "
                    + "recitation marks: a word typed in modern spelling is always found in the modern "
                    + "text, and in the Uthmani about nine times in ten - the rest it spells its own way, "
                    + "al-kitab, the book, as al-ktb with the long a as a small mark. Cited by sura and "
                    + "verse, 2.255. "
                    + "Tanzil's text is CC BY 3.0 and may be copied only unaltered, with its notice, which is "
                    + "installed with it and shown with each sura's details. Pickthall's translation is in "
                    + "the public domain.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new QuranIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.VersesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Quran, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Quran),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Quran) > 0
            },

            new SetupDataSource
            {
                Title = "Aquinas: Summa Theologiae, First Part, in English",
                RepoUrl = "https://www.gutenberg.org/ebooks/17611",
                DisplayNote = "about 3 MB in one download",
                DefaultDestination = Path.Combine(dataRoot, "aquinas"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink { Text = "The Summa, Part I, at Project Gutenberg", Url = "https://www.gutenberg.org/ebooks/17611" }
                },

                PlainLanguageDescription =
                    "The First Part of Thomas Aquinas's Summa Theologiae, all 119 questions, in the English "
                    + "translation by the Fathers of the English Dominican Province: God, the Trinity, "
                    + "creation, man, and the treatise on the angels - what they are, how they know and will, "
                    + "how some of them fell (questions 50 to 64) - with the angels' orders, their "
                    + "guardianship of men and the demons' assaults on them (106 to 114). "
                    + "Cited as the Summa is cited, by question, article and part: 63.2.co is the body of "
                    + "question 63, article 2; ob1 is the first objection, sc On the contrary, ad1 the reply "
                    + "to the first objection. English only: Aquinas's Latin is free, but no digital Latin "
                    + "text of it is. Public domain.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new AquinasIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.PassagesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Aquinas, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Aquinas),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Aquinas) > 0
            },

            new SetupDataSource
            {
                Title = "Pseudo-Dionysius: the Heavenly Hierarchy and the other works",
                RepoUrl = "https://www.ccel.org/ccel/dionysius/works",
                DisplayNote = "under 1 MB in one download",
                DefaultDestination = Path.Combine(dataRoot, "dionysius"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink { Text = "Parker's translation at the Christian Classics Ethereal Library", Url = "https://www.ccel.org/ccel/dionysius/works" }
                },

                PlainLanguageDescription =
                    "The works of the writer who called himself Dionysius the Areopagite, in John Parker's "
                    + "English of 1897 and 1899: On the Heavenly Hierarchy, which first sets the angels in "
                    + "nine orders and three triads - seraphim, cherubim and thrones; dominions, powers and "
                    + "authorities; principalities, archangels and angels - and which the later lists of "
                    + "the choirs follow; On the Ecclesiastical Hierarchy; On Divine Names; the Mystic "
                    + "Theology; and the letters. "
                    + "Cited by chapter and section in Parker's numbering (Heavenly Hierarchy 7.2). English "
                    + "only, without Parker's footnotes. Public domain.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new DionysiusIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.PassagesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Dionysius, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Dionysius),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Dionysius) > 0
            },

            new SetupDataSource
            {
                Title = "The Goetia: the seventy-two spirits of the Lesser Key of Solomon",
                RepoUrl = "https://www.gutenberg.org/ebooks/72679",
                DisplayNote = "about 160 KB in one download",
                DefaultDestination = Path.Combine(dataRoot, "goetia"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink { Text = "The Lesser Key of Solomon at Project Gutenberg", Url = "https://www.gutenberg.org/ebooks/72679" },
                    new SetupLink { Text = "The 1904 edition, scanned", Url = "https://en.wikisource.org/wiki/Index:The_Book_of_the_Goetia_(Mathers-Crowley,_1904).djvu" }
                },

                PlainLanguageDescription =
                    "The Goetia, the first book of the Lesser Key of Solomon, in the edition of S. L. "
                    + "MacGregor Mathers and Aleister Crowley (1904): the seventy-two spirits King Solomon shut "
                    + "in a vessel of brass, each with its rank, its shape, its office and its legions, cited by "
                    + "number as they always are (Goetia 1 is Bael); the art of calling them, with the circle, "
                    + "the triangle, the conjurations, the curses and the licence to depart; and Crowley's preface, "
                    + "invocation and essay on what magic is. Read the spirits beside the catalogue in Scot's "
                    + "Discovery of Witchcraft, from which most of them come. "
                    + "The text is Project Gutenberg's proofread transcription of de Laurence's 1916 reprint. "
                    + "The seals are pictures and are not included. Public domain.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new GoetiaIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.PassagesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.Goetia, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.Goetia),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.Goetia) > 0
            },

            new SetupDataSource
            {
                Title = "Athanasius: the Life of Antony, in English",
                RepoUrl = "https://www.ccel.org/ccel/schaff/npnf204",
                DisplayNote = "about 6 MB in one download (the whole Athanasius volume)",
                DefaultDestination = Path.Combine(dataRoot, "life-of-antony"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink { Text = "The Athanasius volume at the Christian Classics Ethereal Library", Url = "https://www.ccel.org/ccel/schaff/npnf204" }
                },

                PlainLanguageDescription =
                    "The Life of Antony, Athanasius's account of the first desert hermit, written about 360, in "
                    + "the English of the Rev. H. Ellershaw (1892): the devil beating Antony senseless in a tomb, "
                    + "demons breaking in through the walls as lions, bears, serpents and scorpions, the long "
                    + "sermon on how to tell a demon from an angel, and the tall figure who catches souls as they "
                    + "rise. Every later saint tormented by devils goes back to it. "
                    + "Cited by its 94 sections, with the editor's table of contents as headings; filed under "
                    + "Athanasius beside his Greek works. English only. Public domain.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new AntonyIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.PassagesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.LifeOfAntony, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.LifeOfAntony),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.LifeOfAntony) > 0
            },

            new SetupDataSource
            {
                Title = "The Arabian Nights, in Burton's translation",
                RepoUrl = "https://www.gutenberg.org/ebooks/51252",
                DisplayNote = "about 15 MB in twelve downloads",
                DefaultDestination = Path.Combine(dataRoot, "arabian-nights"),
                FetchMode = SetupFetchMode.SelfManaged,

                Links =
                {
                    new SetupLink { Text = "Volume 1 at Project Gutenberg", Url = "https://www.gutenberg.org/ebooks/51252" }
                },

                PlainLanguageDescription =
                    "The Thousand Nights and a Night in Richard Burton's translation (1885), all ten volumes and "
                    + "all 1,001 nights, with the third volume of his Supplemental Nights (1887), which holds "
                    + "Aladdin and Ali Baba. The jinn of the Quran as storytellers knew them: the Ifrit in the "
                    + "fisherman's brass jar, sealed with Solomon's seal; the jinni with a woman in a casket under "
                    + "the sea; the slaves of the ring and the lamp. "
                    + "Cited by night and paragraph (3.12 is the Third Night); the Supplemental tales by tale and "
                    + "paragraph. Burton's English is deliberately archaic and his tales are frank about sex. His "
                    + "5,000 footnotes, often prurient and frequently racist, and his Terminal Essay are left out. "
                    + "Public domain.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new NightsIngestService();
                    var outcome = await service.IngestAsync(root, progress, ct);

                    if (service.PassagesInstalled > 0)
                        await editionRepo.StampCollectionAsync(root, CollectionKeys.ArabianNights, ct);

                    return outcome;
                },

                CheckComplete = async () =>
                    await editionRepo.IsCollectionCompleteAsync(CollectionKeys.ArabianNights),
                CheckHasSomeContent = async () =>
                    await editionRepo.CountByCollectionAsync(CollectionKeys.ArabianNights) > 0
            },

            new SetupDataSource
            {
                Title = "Dictionaries (LSJ + Lewis & Short)",
                RepoUrl = "https://github.com/PerseusDL/lexica",
                DefaultDestination = Path.Combine(dataRoot, "lexica"),
                PlainLanguageDescription =
                    "The dictionary definitions Word Study looks words up in while you're reading - " +
                    "one dictionary for Greek, one for Latin, both loaded in this one step. This is about five hundred megabytes.",
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
                    "This is a large amount of data (3.6 gigabytes) and may take about an hour to gather. " +
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
                PlainLanguageDescription = "The same kind of word-form mapping as the Greek lemma data, for Latin. This will take about six minutes and is about two gigabytes.",
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
                Title = "Middle High German Lemma Data",
                RepoUrl = ReMLemmaIngestService.ArchiveUrl,
                DisplayNote = "158 MB; ReM's own annotation, read straight from the archive",
                DefaultDestination = Path.Combine(dataRoot, "rem-lemmas"),
                FetchMode = SetupFetchMode.DirectDownload,
                DownloadFileName = ReMLemmaIngestService.ArchiveFileName,

                PlainLanguageDescription =
                    "What turns the Middle High German texts from something to read into something to "
                    + "study: every word mapped to its dictionary headword, with its part of speech and "
                    + "its grammar. Click a word and you get the headword, every attested spelling of "
                    + "it, and everywhere else it occurs - the same thing Word Study does for Greek and "
                    + "Latin. "
                    + "This is the one collection that brings its own: the mappings were made by the "
                    + "same project, over the same manuscripts, so they cover the texts completely "
                    + "rather than approximately. Install the Middle High German texts first - on their "
                    + "own these mappings have nothing to attach to.",

                RunIngest = async (root, progress, ct) =>
                {
                    var service = new ReMLemmaIngestService();

                    var wrapped = new Progress<ReMLemmaProgress>(p => progress.Report(
                        $"{p.CurrentFile} ({p.FilesProcessed}/{p.TotalFiles} texts, {p.MappingsLoaded:N0} mappings)"));

                    await service.IngestAsync(root, wrapped, ct);

                    // Two different things counted two different ways, and said
                    // so: the mappings are deduplicated across the corpus, the
                    // dictionary coverage is a share of the words in it.
                    var covered = service.TokensRead == 0
                        ? 0
                        : service.WithDictionaryId * 100.0 / service.TokensRead;

                    progress.Report(
                        $"{service.MappingsLoaded:N0} word-form mappings loaded from "
                        + $"{service.TokensRead:N0} annotated words, {covered:F0}% of which are keyed "
                        + "to the Mittelhochdeutsches Wörterbuch.");

                    return IngestOutcome.Clean;
                },

                CheckComplete = async () =>
                    await lemmaRepo.CountByLanguageAsync(ReMIngestService.Language) > 0
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
                    "Word Study work on the translation side as well as the original. This is about sixty megabytes and takes about a minute.",
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
                Title = "Stephanus & Bekker Citations (Plato & Aristotle)",
                RepoUrl = "https://github.com/PerseusDL/canonical-greekLit",
                DisplayNote = "reads texts you already have - nothing is downloaded",
                DefaultDestination = dataRoot,
                FetchMode = SetupFetchMode.SelfManaged,
                ActionButtonText = "Update Citations",
                PlainLanguageDescription =
                    "Plato is cited by Stephanus page and Aristotle by Bekker number - Republic 327a, " +
                    "Ethics 1094a1 - and every article, syllabus and commentary uses them. Perseus " +
                    "records both, but earlier versions of this application discarded them while " +
                    "reading, so a library built before now shows Euthyphro 2a as \"2.1\", which cannot " +
                    "be looked up anywhere. This reads those markers back out of the texts you already " +
                    "have and writes them onto the passages. It downloads nothing, changes no text, and " +
                    "leaves your bookmarks and tags exactly where they are - about twenty seconds " +
                    "against a full library.",
                RunIngest = async (root, progress, ct) =>
                {
                    var report = await new CanonicalCitationBackfill(root).RunAsync(progress, ct);
                    if (report.FilesMissing > 0)
                    {
                        progress.Report($"{report.FilesMissing} editions could not be checked because their " +
                                        "source files are no longer in the data folder. Anything you " +
                                        "translated yourself is expected to be among them.");
                    }
                    return IngestOutcome.Clean;
                },
                CheckComplete = async () => await editionRepo.CountPassagesWithCanonicalCitationsAsync() > 0
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
                    // reads from, then dropping any cached "file wasn't
                    // there" so an already-open session picks it up.
                    //
                    // Both paths now come from DataFolderSettings.Root, so
                    // they agree except in one narrow case: the download
                    // folder changing between the moment this step was built
                    // and the moment it ran. The copy stays as the guard
                    // against that. This note used to say the two differ when
                    // Advanced Setup points somewhere custom, which stopped
                    // being how either of them is decided.
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
                Title = "Medieval Manuscript Hands (CATMuS)",
                RepoUrl = CatmusCatalogue.DatasetUrl,
                DisplayNote = "transcriptions only - a few megabytes, not the 24.7 GB of photographs",
                DefaultDestination = Path.Combine(dataRoot, CatmusImageDownloadService.ImagesFolder),
                FetchMode = SetupFetchMode.SelfManaged,
                ActionButtonText = "Download Transcriptions",
                PlainLanguageDescription =
                    $"Every other collection here arrives already transcribed, with the step where " +
                    $"somebody looked at a manuscript and decided what it said left out. This is that " +
                    $"step: {CatmusCatalogue.TotalLines:N0} lines from {CatmusCatalogue.Manuscripts.Count} " +
                    "medieval manuscripts, seventh to sixteenth century, each one photographed and " +
                    "transcribed beside itself - Latin, French, Castilian, Middle Dutch, Italian and six " +
                    "more, in Caroline, Textualis, Cursiva and nine other hands. It opens in Medieval " +
                    "Hands on the toolbar, not in the reader, because CATMuS shuffles its lines and " +
                    "records no page numbers, so a manuscript here is a collection of specimens rather " +
                    "than something to read through.\r\n\r\n" +
                    "This step fetches the transcriptions only: about eight megabytes, five to ten " +
                    "minutes, and nothing saved to the download folder. The line photographs are the " +
                    "other 24.7 GB, so they are fetched one manuscript at a time from inside the window, " +
                    "where each one's size is shown before you agree to it.",
                Links =
                {
                    new SetupLink { Text = "CATMuS-Medieval on HuggingFace", Url = CatmusCatalogue.DatasetUrl }
                },
                RunIngest = async (_, progress, ct) =>
                {
                    var service = new CatmusIngestService(catmusRepo);
                    await service.IngestAsync(CatmusCatalogue.Shards, progress, ct);

                    if (service.FailedShards.Count > 0)
                    {
                        progress.Report(
                            $"{service.FailedShards.Count} of {CatmusCatalogue.Shards.Count} files could not " +
                            "be read. Running this step again retries only those.");
                    }

                    return IngestOutcome.From(service.FailedShards, attempted: CatmusCatalogue.Shards.Count);
                },

                // Complete means every file, not merely some - an interrupted
                // run leaves a library that works and is missing manuscripts,
                // and calling that finished would hide the ones it lost.
                CheckComplete = async () =>
                    (await catmusRepo.GetIngestedShardsAsync()).Count >= CatmusCatalogue.Shards.Count,
                CheckHasSomeContent = async () => await catmusRepo.HasDataAsync()
            }
        };
    }
}
