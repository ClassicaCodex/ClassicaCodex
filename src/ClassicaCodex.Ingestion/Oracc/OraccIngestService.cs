using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.Ingestion.Oracc;

/// <summary>
/// Puts an Oracc project into the library.
///
/// HOW A CUNEIFORM TEXT MAPS ONTO A LIBRARY BUILT FOR PERSEUS. Three
/// decisions, and none of them is obvious:
///
/// THE AUTHOR IS THE RULER. A royal inscription was composed by scribes and
/// speaks in the king's voice, so nobody can name its author. Assyriology
/// cites these under the king - "Tiglath-pileser III 01" - and the catalogue
/// carries a ruler field for exactly that. Using it is following the
/// discipline's own convention, not asserting who held the stylus. Where
/// there is no ruler the project stands in, which is honest in a different
/// way: an anonymous text filed under the edition that published it.
///
/// EACH TEXT BECOMES TWO EDITIONS. Oracc gives every line twice - once in
/// transliteration, which is how Assyriology reads and writes, and once in
/// cuneiform signs. Those are the same text in two scripts, and a reader
/// wants them side by side. Rather than invent a display for that, they go
/// in as two editions of one work, which the linked panes already show
/// against each other with synchronised scrolling. No new UI, and both are
/// indexed, so a search can be by sign or by transliteration.
///
/// NO TRANSLATION GOES IN. Oracc's open data has none - see
/// OraccProjectReader - so a work arrives with two originals and no English.
/// That gap is real and the library should show it as a gap rather than
/// filling it with something generated, which would put sentences nobody
/// wrote into the word index.
/// </summary>
public class OraccIngestService
{
    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _editionHeaderRepo = new();

    /// <summary>
    /// The collection key written onto every edition, so the library can say
    /// where a text came from without consulting the folder it arrived in.
    /// </summary>
    public const string CollectionKey = "oracc";

    /// <summary>Texts that could not be imported, and why.</summary>
    public List<(string FilePath, string Error)> FailedFiles { get; } = new();

    /// <summary>
    /// Texts the download names but does not carry, and texts that are
    /// catalogued but entirely lost. Neither is a failure - both are facts
    /// about the corpus - but a library quietly 3 texts short of its
    /// catalogue is the kind of thing nobody notices for a year.
    /// </summary>
    public List<(string FilePath, string Error)> Noted { get; } = new();

    /// <summary>
    /// Downloads and imports the starter projects - what the setup step runs.
    ///
    /// Each project is fetched and imported before the next is fetched, rather
    /// than downloading all of them first. A reader who cancels halfway then
    /// has some of the corpus rather than none of it, and a project that fails
    /// to download costs only itself.
    /// </summary>
    public async Task<IngestOutcome> InstallStarterProjectsAsync(
        string destinationRoot,
        IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        var downloads = new OraccDownloadService();
        var outcomes = new List<IngestOutcome>();

        foreach (var choice in OraccDownloadService.StarterProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var directory = await downloads.FetchAsync(choice, destinationRoot, progress, cancellationToken);
                var outcome = await IngestAsync(directory, progress, cancellationToken);

                // A project that downloaded cleanly and installed nothing is
                // not success. Some Oracc projects are portals rather than
                // corpora - amgg ships an empty corpusjson - and a step that
                // lists one as installed is claiming texts the library does
                // not have.
                if (outcome.FilesAttempted == 0)
                    Noted.Add((choice.Id, "downloaded but contains no texts; it may be a portal project"));

                outcomes.Add(outcome);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One project failing must not lose the seven that worked.
                // Recorded as a skip so the step reports it rather than
                // finishing quietly with a short library.
                progress.Report($"{choice.Title} could not be installed: {ex.Message}");
                FailedFiles.Add((choice.Id, ex.Message));
            }
        }

        return outcomes.Count == 0
            ? IngestOutcome.From(FailedFiles, Noted)
            : IngestOutcome.Combine(outcomes.ToArray());
    }

    public async Task<IngestOutcome> IngestAsync(
        string projectDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var project = OraccProjectReader.Read(projectDirectory);

        progress?.Report($"{project.Abbreviation}: {project.Texts.Count} texts, {project.License}");

        foreach (var skipped in project.Skipped)
            Noted.Add((skipped.TextId, skipped.Reason));

        // One author row per ruler, resolved once. Ninety-three texts by
        // Tiglath-pileser III are ninety-three works by one man, and looking
        // him up per text would be ninety-three upserts to reach the same row.
        var authorIds = new Dictionary<string, int>(StringComparer.Ordinal);

        var attempted = 0;

        foreach (var text in project.Texts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempted++;

            try
            {
                // A text that is catalogued but wholly lost has no lines at
                // all. Importing it would put an empty work in the tree that
                // opens onto nothing.
                if (text.Lines.Count == 0)
                {
                    Noted.Add((text.TextId, "catalogued but carries no readable line"));
                    continue;
                }

                var authorName = text.Ruler ?? project.Name;
                if (!authorIds.TryGetValue(authorName, out var authorId))
                {
                    authorId = await _authorRepo.UpsertAsync(new Author
                    {
                        CtsUrn = $"oracc:{project.ProjectPath}:{Slug(authorName)}",
                        Name = authorName,
                        Namespace = "oracc",
                        // The language column is what reaches the lemma data.
                        // Akkadian has none of ours yet - Oracc ships its own
                        // glossaries, which is a later step - so this says what
                        // the text is, not what we can look up.
                        Language = "akk"
                    }, cancellationToken);

                    authorIds[authorName] = authorId;
                }

                var workId = await _workRepo.UpsertAsync(new Work
                {
                    AuthorId = authorId,
                    CtsUrn = $"oracc:{project.ProjectPath}:{text.TextId}",
                    Title = text.Designation,
                    // Oracc cites by line within a text, and the line labels
                    // are the edition's own - "2'", "i 14" - not ordinals.
                    CitationScheme = "line"
                }, cancellationToken);

                // EVERYTHING IS BUILT BEFORE ANYTHING IS CLEARED. The clear
                // and the re-insert are not one transaction - the same reason
                // PerseusIngestService spells out - so a throw between them
                // destroys text with nothing to put back.
                var built = BuildNodes(text);

                // ORDER IS THE READING ARRANGEMENT. The reader fills its left
                // pane from the first original and its right from what is not
                // an original, so writing the signs first puts the text itself
                // on the left and the meanings on the right - the same shape
                // as a Greek work beside its translation. The transliteration
                // stays in the left dropdown, a click away, which is where
                // someone who reads Akkadian will go first.
                //
                // Skipped rather than saved blank when no line has a sign
                // recorded at all, which happens for texts known only from an
                // old hand copy.
                if (built.Cuneiform.Any(n => n.Text.Length > 0))
                {
                    await SaveEditionAsync(
                        project, text, workId, "cuneiform", "Cuneiform",
                        EditionKind.Original, "akk", null, built.Cuneiform, cancellationToken);
                }
                else
                {
                    Noted.Add((text.TextId, "no cuneiform signs recorded; transliteration only"));
                }

                await SaveEditionAsync(
                    project, text, workId, "transliteration", "Transliteration",
                    EditionKind.Original, "akk", null, built.Transliteration, cancellationToken);

                // The gloss goes in as a translation because that is the pane
                // it belongs in and the machinery has no third word for it -
                // but it is labelled everywhere a reader can see, because it
                // is emphatically not one. See the Translator line below.
                //
                // Written only when something was actually looked up. A text
                // Oracc never lemmatised would otherwise get a "gloss" that is
                // its own transliteration back again, in a pane promising
                // English.
                if (text.Lines.Any(l => l.HasAnyGloss))
                {
                    await SaveEditionAsync(
                        project, text, workId, "gloss", "Word-by-word meanings",
                        EditionKind.Translation, "eng",
                        "Word-by-word dictionary meanings from Oracc's lemmatisation - not a translation",
                        built.Gloss, cancellationToken);
                }
                else
                {
                    Noted.Add((text.TextId, "no lemmatised words; no gloss written"));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad text must not abandon a project. Re-running retries.
                FailedFiles.Add((text.TextId, ex.Message));
            }
        }

        return IngestOutcome.From(FailedFiles, Noted, attempted);
    }

    /// <summary>
    /// One rendering of one text, as an edition.
    ///
    /// The signs and the transliteration are both Original: a transliteration
    /// renders the script in Latin letters, it does not translate the text
    /// into another language, and filing it as a translation would make the
    /// reader offer it as the English side of a pair.
    ///
    /// The gloss is the one that genuinely is in another language, and it
    /// goes in as a Translation so it lands in the second pane. That is the
    /// closest thing the model has and it is a stretch - so the stretch is
    /// named rather than hidden, in the Translator field, in the edition's
    /// title, and in the About page.
    /// </summary>
    private async Task SaveEditionAsync(
        OraccProject project, OraccText text, int workId,
        string urnSuffix, string label,
        EditionKind kind, string language, string? translator,
        List<TextNode> nodes,
        CancellationToken cancellationToken)
    {
        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = $"oracc:{project.ProjectPath}:{text.TextId}:{urnSuffix}",
            Kind = kind,
            Language = language,
            Translator = translator,
            Collection = CollectionKey,
            SourcePath = $"{project.Source}/{text.TextId}"
        }, cancellationToken);

        foreach (var node in nodes) node.EditionId = editionId;

        await _editionRepo.ClearTextNodesAsync(editionId, cancellationToken);

        await _editionHeaderRepo.SaveAsync(editionId, new EditionHeader
        {
            EditionId = editionId,
            Title = $"{text.Designation} ({label})",
            Author = text.Ruler,

            // The credits line, verbatim, because it names the editors and
            // states this text's licence - and that licence is not always the
            // project's. rinap/rinap1 declares CC0 in metadata.json while
            // every text's credits say CC BY-SA 3.0. Both are recorded: the
            // project's on the collection, the text's here, and where they
            // disagree the stricter one governs. Attribution we did not keep
            // is attribution we cannot give.
            Availability = text.Credits ?? project.License,

            SourceDescription = Describe(text, project)
        }, cancellationToken);

        await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);
    }

    /// <summary>
    /// The three renderings of one text, kept row for row.
    /// </summary>
    internal sealed class OraccTextNodes
    {
        /// <summary>The wedges. The left pane's default, being the text itself.</summary>
        public List<TextNode> Cuneiform { get; } = new();

        /// <summary>The same line in Latin letters, as Assyriology reads it.</summary>
        public List<TextNode> Transliteration { get; } = new();

        /// <summary>Each word's dictionary meaning, in order.</summary>
        public List<TextNode> Gloss { get; } = new();
    }

    /// <summary>
    /// The three editions' rows, built together and guaranteed parallel.
    ///
    /// THEY MUST BE THE SAME LENGTH. The reader synchronises linked panes by
    /// row index - MainForm.SyncScroll copies TopIndex straight across - so
    /// two editions of one work that disagree about how many rows they have
    /// do not scroll together, they drift. Building them in one pass is what
    /// makes that impossible rather than merely intended.
    ///
    /// Which matters here more than it sounds, because a line can genuinely
    /// have no cuneiform. Q003622 is an Assyrian monument inscribed in two
    /// languages: Akkadian lines 1-2 in cuneiform, then Aramaic lines 1-3,
    /// which is an alphabetic script Oracc records no signs for. Dropping
    /// those three rows from the sign pane would leave the panes two lines
    /// apart for the rest of the text. So a line with no signs keeps its row
    /// and leaves it blank, which is also the honest statement: there is a
    /// line here and we have no signs for it.
    ///
    /// CitationRef is the edition's own label rather than a counter, because
    /// that is what a citation points at - and because in a bilingual the
    /// counter is not unique: Q003622 numbers its Aramaic from 1 again, so
    /// "Akkadian 1" and "Aramaic 1" are two different lines that "1" cannot
    /// tell apart. SortOrder carries document order separately, since "2'"
    /// and "i 14" do not sort as numbers.
    /// </summary>
    internal static OraccTextNodes BuildNodes(OraccText text)
    {
        var built = new OraccTextNodes();
        var order = 0;

        foreach (var line in text.Lines)
        {
            var written = line.Transliteration;

            // A line with no words at all is a structural artefact rather than
            // a line of text, and it is dropped from ALL THREE so they stay
            // level.
            if (written.Length == 0) continue;

            var reference = line.Label.Length > 0 ? line.Label : (order + 1).ToString();

            built.Cuneiform.Add(new TextNode { CitationRef = reference, SortOrder = order, Text = line.Cuneiform });
            built.Transliteration.Add(new TextNode { CitationRef = reference, SortOrder = order, Text = written });
            built.Gloss.Add(new TextNode { CitationRef = reference, SortOrder = order, Text = line.Gloss });

            order++;
        }

        return built;
    }

    /// <summary>
    /// What this object is, for the edition's source description - genre,
    /// period, and where it came out of the ground.
    /// </summary>
    private static string Describe(OraccText text, OraccProject project)
    {
        var parts = new List<string>(5);

        if (text.Genre is { Length: > 0 }) parts.Add(text.Genre);
        if (text.Period is { Length: > 0 }) parts.Add(text.Period);
        if (text.Provenience is { Length: > 0 }) parts.Add($"found at {text.Provenience}");

        // Said out loud, because the reader will look for an English text and
        // not find one, and the catalogue knowing a translation exists is the
        // explanation for that.
        if (text.TranslationLanguages is { Length: > 0 })
            parts.Add($"a translation exists at {project.Source} but is not in the open data");

        parts.Add($"imported from {project.Abbreviation}");

        return string.Join(". ", parts) + ".";
    }

    /// <summary>A name reduced to something usable inside an identifier.</summary>
    private static string Slug(string value)
    {
        var slug = new string(value
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }
}
