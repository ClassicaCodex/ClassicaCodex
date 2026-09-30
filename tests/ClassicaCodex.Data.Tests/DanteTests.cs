using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Dante;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading Dante's Commedia out of the Universal Dependencies Old Italian
/// treebank.
///
/// A treebank is not a text, and three of its conventions will put words into
/// the poem that Dante did not write, or leave out words he did. Each is
/// measured against the pinned release r2.18, which the loader reconstructs to
/// 14,233 verses over 100 cantos - matching the canonical count of every printed
/// Commedia exactly: Inferno 4,720, Purgatorio 4,755, Paradiso 4,758.
/// </summary>
public class DanteTextLoaderTests
{
    private static string Conllu(string body)
    {
        var path = Path.Combine(
            Path.GetTempPath(), "ccx-dante-tests", Guid.NewGuid().ToString("N"), "it_old-ud-train.conllu");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body.ReplaceLineEndings("\n"));
        return path;
    }

    private static List<DanteCanticle> Load(string body) =>
        DanteTextLoader.Load(new[] { Conllu(body) });

    /// <summary>
    /// A tab-separated row, written out so the tests below read as the poem
    /// rather than as column counting.
    /// </summary>
    private static string Row(string id, string form, string lemma, string upos, string misc) =>
        string.Join('\t', id, form, lemma, upos, "_", "_", "0", "root", "_", misc);

    /// <summary>
    /// The ordinary case: words carry Canto and Verso, and a verse is the words
    /// that share them.
    /// </summary>
    [Fact]
    public void AVerseIsTheWordsSharingACantoAndLine()
    {
        var canticle = Assert.Single(Load($@"# sent_id = OldItalian_Dante_Inferno-1
{Row("1", "Nel", "nel", "ADP", "Canto=1|Verso=1")}
{Row("2", "mezzo", "mezzo", "NOUN", "Canto=1|Verso=1")}
{Row("3", "mi", "mi", "PRON", "Canto=1|Verso=2")}
"));

        Assert.Equal("Inferno", canticle.Name);
        Assert.Equal(new[] { "Nel mezzo", "mi" }, canticle.Verses.Select(v => v.Text));
        Assert.Equal(new[] { 1, 2 }, canticle.Verses.Select(v => v.Verso));
    }

    /// <summary>
    /// <b>Enhanced-UD empty nodes are not words.</b> A decimal id - "2.1 dissi" -
    /// is an ellipsis an editor reconstructs to complete a clause Dante left
    /// elliptical, and it carries no Verso. There are 306 of them, and taken as
    /// text they interpolate words into the poem that are not in it.
    /// </summary>
    [Fact]
    public void AnEmptyNodeIsNotPartOfThePoem()
    {
        var canticle = Assert.Single(Load($@"# sent_id = OldItalian_Dante_Paradiso-1
{Row("1", "Vergine", "vergine", "NOUN", "Canto=33|Verso=1")}
{Row("1.1", "dissi", "dire", "VERB", "Antec=Yes|AntecPosit=Canto1-24_10")}
{Row("2", "madre", "madre", "NOUN", "Canto=33|Verso=1")}
"));

        Assert.Equal("Vergine madre", Assert.Single(canticle.Verses).Text);
        Assert.Equal(new[] { "vergine", "madre" }, Assert.Single(canticle.Verses).Words.Select(w => w.Headword));
    }

    /// <summary>
    /// A multiword token appears three times over: a range row with the surface
    /// form the poem prints, and one row per component with the analysis. The
    /// range row is the text; the components are the grammar. Taking both would
    /// give "Nel in il mezzo".
    /// </summary>
    [Fact]
    public void AMultiwordTokenContributesItsSurfaceFormOnce()
    {
        var verse = Assert.Single(Assert.Single(Load($@"# sent_id = OldItalian_Dante_Inferno-1
{Row("1-2", "Nel", "_", "_", "Canto=1|Verso=1")}
{Row("1", "in", "in", "ADP", "Canto=1|Verso=1")}
{Row("2", "il", "il", "DET", "Canto=1|Verso=1")}
{Row("3", "mezzo", "mezzo", "NOUN", "Canto=1|Verso=1")}
")).Verses);

        Assert.Equal("Nel mezzo", verse.Text);

        // But both components keep their annotation - that is what a reader
        // clicking "Nel" needs.
        Assert.Equal(new[] { "in", "il", "mezzo" }, verse.Words.Select(w => w.Headword));
    }

    /// <summary>
    /// <b>Spacing lives in the annotation.</b> Without SpaceAfter=No the poem
    /// comes out as "selva oscura ," and "voi ch' intrate" - spaces Dante's line
    /// does not have.
    /// </summary>
    [Fact]
    public void SpaceAfterNoClosesUpPunctuationAndElision()
    {
        var verse = Assert.Single(Assert.Single(Load($@"# sent_id = OldItalian_Dante_Inferno-3
{Row("1", "voi", "voi", "PRON", "Canto=3|Verso=9")}
{Row("2", "ch'", "che", "PRON", "Canto=3|SpaceAfter=No|Verso=9")}
{Row("3", "intrate", "entrare", "VERB", "Canto=3|SpaceAfter=No|Verso=9")}
{Row("4", ".", ".", "PUNCT", "_")}
")).Verses);

        Assert.Equal("voi ch'intrate.", verse.Text);
    }

    /// <summary>
    /// Punctuation carries no Verso - 18,670 rows - and belongs to the verse it
    /// follows, which is always the line it ends. The rows that would have broken
    /// that rule are the empty nodes, and those are gone before this applies.
    /// </summary>
    [Fact]
    public void PunctuationJoinsTheVerseItFollows()
    {
        var canticle = Assert.Single(Load($@"# sent_id = OldItalian_Dante_Inferno-1
{Row("1", "smarrita", "smarrire", "VERB", "Canto=1|SpaceAfter=No|Verso=3")}
{Row("2", ".", ".", "PUNCT", "_")}
{Row("3", "Ahi", "ahi", "INTJ", "Canto=1|Verso=4")}
"));

        Assert.Equal(new[] { "smarrita.", "Ahi" }, canticle.Verses.Select(v => v.Text));
    }

    /// <summary>
    /// The three treebank files are a machine-learning split that cuts through
    /// cantos, so they have to be merged. Reading one would give a Commedia with
    /// holes in it.
    /// </summary>
    [Fact]
    public void TheSplitsAreMergedIntoOnePoem()
    {
        var first = Conllu($@"# sent_id = OldItalian_Dante_Inferno-1
{Row("1", "Nel", "nel", "ADP", "Canto=1|Verso=1")}
");
        var second = Conllu($@"# sent_id = OldItalian_Dante_Inferno-2
{Row("1", "mi", "mi", "PRON", "Canto=1|Verso=2")}
");

        var canticle = Assert.Single(DanteTextLoader.Load(new[] { second, first }));

        // In the poem's order, not the files'.
        Assert.Equal(new[] { 1, 2 }, canticle.Verses.Select(v => v.Verso));
    }

    /// <summary>
    /// And the canticles come back in the poem's order rather than the
    /// alphabet's, which would open the library on Paradiso.
    /// </summary>
    [Fact]
    public void TheCanticlesAreInThePoemsOrder()
    {
        var canticles = Load($@"# sent_id = OldItalian_Dante_Paradiso-1
{Row("1", "La", "la", "DET", "Canto=1|Verso=1")}

# sent_id = OldItalian_Dante_Inferno-1
{Row("1", "Nel", "nel", "ADP", "Canto=1|Verso=1")}

# sent_id = OldItalian_Dante_Purgatorio-1
{Row("1", "Per", "per", "ADP", "Canto=1|Verso=1")}
");

        Assert.Equal(new[] { "Inferno", "Purgatorio", "Paradiso" }, canticles.Select(c => c.Name));
    }
}

/// <summary>The Commedia as the library stores it.</summary>
[Collection("Database")]
public class DanteIngestTests
{
    private static string Archive()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccx-dante-ingest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "it_old-ud-train.conllu"),
            string.Join('\n',
                "# sent_id = OldItalian_Dante_Inferno-1",
                string.Join('\t', "1", "Nel", "nel", "ADP", "_", "_", "0", "root", "_", "Canto=1|Verso=1"),
                "",
                "# sent_id = OldItalian_Dante_Purgatorio-1",
                string.Join('\t', "1", "Per", "per", "ADP", "_", "_", "0", "root", "_", "Canto=1|Verso=1"),
                "",
                "# sent_id = OldItalian_Dante_Paradiso-1",
                string.Join('\t', "1", "La", "la", "DET", "_", "_", "0", "root", "_", "Canto=1|Verso=1"),
                ""));

        return root;
    }

    /// <summary>
    /// One work per canticle, numbered so the library tree lists them in the
    /// poem's order - it orders an author's works by title and has no sort column
    /// to use instead, so bare names would give Inferno, Paradiso, Purgatorio.
    /// </summary>
    [Fact]
    public async Task TheThreeCanticlesAreThreeWorksInOrder()
    {
        using var db = await TempDatabase.CreateAsync();

        var service = new DanteIngestService();
        await service.IngestAsync(Archive());

        Assert.Equal(3, service.VersesInstalled);

        var author = Assert.Single(await new AuthorRepository().GetAllAsync());
        Assert.Equal("Dante Alighieri", author.Name);

        Assert.Equal(
            new[] { "Commedia I: Inferno", "Commedia II: Purgatorio", "Commedia III: Paradiso" },
            (await new WorkRepository().GetByAuthorAsync(author.AuthorId)).Select(w => w.Title));
    }

    /// <summary>
    /// The citation is Dante's own, which is the firmest thing in this library:
    /// Inf. 5.142 here resolves in any printed edition.
    /// </summary>
    [Fact]
    public async Task VersesAreCitedByCantoAndLine()
    {
        using var db = await TempDatabase.CreateAsync();
        await new DanteIngestService().IngestAsync(Archive());

        var author = Assert.Single(await new AuthorRepository().GetAllAsync());
        var work = (await new WorkRepository().GetByAuthorAsync(author.AuthorId)).First();
        var edition = Assert.Single(await new EditionRepository().GetByWorkAsync(work.WorkId));
        var node = Assert.Single(await new TextNodeRepository().GetByEditionAsync(edition.EditionId));

        Assert.Equal("1.1", node.CitationRef);
        Assert.Equal("Canto.Verso", work.CitationScheme);
        Assert.True(node.IsVerse);
    }

    /// <summary>
    /// <b>No orthography, deliberately.</b> The two-reading split the manuscript
    /// collections use says something true about them - the scribe's spelling
    /// against a modern reading - and a critical edition has no such layer.
    /// Setting one would imply a manuscript behind this text that is not there.
    /// </summary>
    [Fact]
    public async Task ACriticalEditionCarriesNoOrthography()
    {
        using var db = await TempDatabase.CreateAsync();
        await new DanteIngestService().IngestAsync(Archive());

        var author = Assert.Single(await new AuthorRepository().GetAllAsync());
        var eds = new EditionRepository();

        foreach (var work in await new WorkRepository().GetByAuthorAsync(author.AuthorId))
        {
            var edition = Assert.Single(await eds.GetByWorkAsync(work.WorkId));
            Assert.Null(edition.Orthography);

            // And the collection is set at ingest rather than stamped from the
            // download folder, because these editions' SourcePath is the poem's
            // own address. Without it Dante would be absent from every
            // collection filter while sitting in the library tree.
            Assert.Equal(DanteIngestService.CollectionKey, edition.Collection);
        }
    }

    [Fact]
    public async Task TheAnnotationBecomesLemmaData()
    {
        using var db = await TempDatabase.CreateAsync();
        await new DanteIngestService().IngestAsync(Archive());

        Assert.Equal("nel",
            Assert.Single(await new LemmaRepository().GetHeadwordsForFormAsync("nel", "ita")).Headword);
    }
}
