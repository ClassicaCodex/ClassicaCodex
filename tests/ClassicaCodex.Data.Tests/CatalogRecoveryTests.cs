using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What happens to a folder whose CTS catalogue is missing.
///
/// It used to be passed over in silence, at both levels: a textgroup with no
/// __cts__.xml was skipped whole, and a work folder with none had its edition
/// files never opened, because the loop that opens them iterates the catalogue
/// entries and there were none. Neither path recorded anything, so setup
/// reported "Done - ready."
///
/// canonical-latinLit ships 65 of 399 work folders that way, and six
/// textgroups. canonical-greekLit ships none, which is why it went unnoticed
/// for as long as it did. The measured cost was 197 edition files - Bede's
/// Historia ecclesiastica, Cato's De agri cultura, Apicius, Sidonius,
/// Augustine's letters, the Appendix Vergiliana, Petronius' fragments, Livy's
/// Periochae and four of his six editions. After the recovery below, that
/// corpus ingests 687 of 687 files rather than 490.
///
/// The recovery has to stop where CorpusFolderExclusionTests begins. A missing
/// catalogue is ALSO what keeps First1KGreek's save/, split/ and volume_xml/
/// working directories out of the corpus, and those hold the same texts as the
/// textgroups they came from. Recovering them would ingest a good part of that
/// corpus twice - which does not make a Delta run fail, it makes it confident
/// and wrong. The guard is the folder's own name: a real textgroup folder is
/// named for the URN segment its files carry, and a working directory is not.
/// </summary>
[Collection("Database")]
public class CatalogRecoveryTests : IDisposable
{
    private readonly string _root;

    public CatalogRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cc-recover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A corpus folder to ingest, alongside a fresh database pointed at by the
    /// static DbConnectionFactory. Both are torn down with the test - hence
    /// the Database collection, since that factory holds one path per process.
    /// </summary>
    private async Task<(string Data, TempDatabase Db)> NewCorpusAsync()
    {
        var db = await TempDatabase.CreateAsync();
        var data = Path.Combine(_root, "data");
        Directory.CreateDirectory(data);
        return (data, db);
    }

    private static void WriteTextGroupCatalog(string dir, string urn, string name)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "__cts__.xml"),
            $@"<ti:textgroup xmlns:ti=""http://chs.harvard.edu/xmlns/cts"" urn=""{urn}"">
                 <ti:groupname xml:lang=""eng"">{name}</ti:groupname>
               </ti:textgroup>");
    }

    /// <summary>
    /// An edition file shaped like the ones that were being dropped: a real
    /// titleStmt with an author and a title, and a body with one line in it.
    /// </summary>
    private static void WriteEdition(
        string dir, string fileName, string author, string title, string line,
        string? sourceCollection = null)
    {
        Directory.CreateDirectory(dir);

        var source = sourceCollection == null
            ? "<sourceDesc><p>Keyboarding</p></sourceDesc>"
            : $@"<sourceDesc><biblStruct><monogr><title>{sourceCollection}</title></monogr></biblStruct></sourceDesc>";

        var authorElement = author.Length == 0 ? "" : $"<author>{author}</author>";

        File.WriteAllText(Path.Combine(dir, fileName),
            $@"<TEI.2><teiHeader><fileDesc>
                 <titleStmt><title>{title}</title>{authorElement}</titleStmt>
                 {source}
               </fileDesc></teiHeader>
               <text><body><div1 type=""book"" n=""1""><p>{line}</p></div1></body></text></TEI.2>");
    }

    private static async Task<List<(string Author, string Work, string Edition)>> LibraryAsync()
    {
        var results = new List<(string, string, string)>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT a.Name, w.Title, e.CtsUrn
                            FROM Editions e
                            JOIN Works w ON w.WorkId = e.WorkId
                            JOIN Authors a ON a.AuthorId = w.AuthorId
                            ORDER BY a.Name, w.Title, e.CtsUrn;";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return results;
    }

    // -------------------------------------------------------- the recovery

    /// <summary>
    /// Livy exactly: the textgroup is catalogued, the per-book work folders are
    /// not. Every edition in them used to go uningested.
    /// </summary>
    [Fact]
    public async Task AWorkFolderWithNoCatalogIsStillIngested()
    {
        var (data, db) = await NewCorpusAsync();
        using var _ = db;
        var group = Path.Combine(data, "phi0914");
        WriteTextGroupCatalog(group, "urn:cts:latinLit:phi0914", "Titus Livius (Livy)");

        WriteEdition(Path.Combine(group, "phi0011"), "phi0914.phi0011.perseus-lat2.xml",
            "Titus Livius (Livy)", "Ab Urbe Condita, books 1-2 - 1", "facturusne operae pretium sim");

        var service = new PerseusIngestService();
        await service.IngestAsync(new[] { (data, "latinLit") });

        var library = await LibraryAsync();
        Assert.Contains(library, r => r.Edition == "phi0914.phi0011.perseus-lat2");
        Assert.Contains(library, r => r.Work == "Ab Urbe Condita, books 1-2 - 1");
        Assert.Empty(service.FailedFiles);
    }

    /// <summary>
    /// Bede, Cato, Apicius and Sidonius: no catalogue for the author either, so
    /// the whole textgroup went missing and with it every work under it.
    /// </summary>
    [Fact]
    public async Task ATextGroupWithNoCatalogIsStillIngested()
    {
        var (data, db) = await NewCorpusAsync();
        using var _ = db;

        WriteEdition(Path.Combine(data, "stoa0054", "stoa006"), "stoa0054.stoa006.perseus-lat1.xml",
            "Bede the Venerable", "Historiam ecclesiasticam gentis Anglorum", "Brittania Oceani insula");

        var service = new PerseusIngestService();
        await service.IngestAsync(new[] { (data, "latinLit") });

        var library = await LibraryAsync();
        Assert.Contains(library, r => r.Author == "Bede the Venerable");
        Assert.Contains(library, r => r.Edition == "stoa0054.stoa006.perseus-lat1");
    }

    /// <summary>
    /// The recovery is reported. Nothing was lost, but the names came from the
    /// TEI header rather than the catalogue and a title may not be the
    /// canonical one - which is worth one line in the setup report and is not
    /// a skip.
    /// </summary>
    [Fact]
    public async Task ARecoveredFolderIsReportedRatherThanPassedOverInSilence()
    {
        var (data, db) = await NewCorpusAsync();
        using var _ = db;

        WriteEdition(Path.Combine(data, "stoa0079", "stoa001"), "stoa0079.stoa001.perseus-lat1.xml",
            "Cato, Marcus Porcius", "De agri cultura", "Est interdum praestare mercaturis rem quaerere");

        var service = new PerseusIngestService();
        await service.IngestAsync(new[] { (data, "latinLit") });

        Assert.NotEmpty(service.RecoveredWithoutCatalog);
        Assert.Empty(service.FailedFiles);

        var outcome = IngestOutcome.From(service.FailedFiles, service.RecoveredWithoutCatalog);
        Assert.True(outcome.HasAnythingToReport);
        Assert.False(outcome.HasSkippedFiles);
        Assert.True(outcome.HasRecoveredFolders);
    }

    /// <summary>
    /// The Appendix Vergiliana: eleven poems whose files name no author,
    /// correctly, because the collection is carmina minora Vergilio
    /// ADTRIBUTA. The printed collection they were digitised from is the next
    /// most specific true thing the files say, so that is what names them -
    /// rather than asserting the attribution their own title hedges.
    /// </summary>
    [Fact]
    public async Task ATextGroupNamingNoAuthorIsFiledUnderItsPrintedCollection()
    {
        var (data, db) = await NewCorpusAsync();
        using var _ = db;

        WriteEdition(Path.Combine(data, "phi0692", "phi003"), "phi0692.phi003.perseus-lat1.xml",
            author: "", title: "Culex, Appendix Vergiliana", line: "Lusimus, Octavi, gracili modulante Thalia",
            sourceCollection: "Appendix Vergiliana, sive carmina minora Vergilio adtributa");

        var service = new PerseusIngestService();
        await service.IngestAsync(new[] { (data, "latinLit") });

        var library = await LibraryAsync();
        Assert.Contains(library, r => r.Author == "Appendix Vergiliana");
        Assert.Contains(library, r => r.Work == "Culex, Appendix Vergiliana");
    }

    /// <summary>
    /// And where there is no author and no printed collection either, the
    /// folder is skipped - but recorded, which is the whole point. A silent
    /// skip is what this class exists to remove, and it does not become
    /// acceptable because the code reaches it deliberately.
    /// </summary>
    [Fact]
    public async Task AFolderThatCannotBeNamedAtAllIsRecordedRatherThanDropped()
    {
        var (data, db) = await NewCorpusAsync();
        using var _ = db;

        WriteEdition(Path.Combine(data, "phi9999", "phi001"), "phi9999.phi001.perseus-lat1.xml",
            author: "", title: "Something", line: "uerba");

        var service = new PerseusIngestService();
        await service.IngestAsync(new[] { (data, "latinLit") });

        Assert.Empty(await LibraryAsync());
        Assert.NotEmpty(service.FailedFiles);
    }

    // ------------------------------------------- and where it has to stop

    /// <summary>
    /// save/ exactly, and the reason the recovery needs a guard at all.
    ///
    /// A working directory holds copies of texts that are already in the
    /// corpus under their own textgroups. It has no catalogue of its own,
    /// which is what used to exclude it - so a recovery that treats "no
    /// catalogue" as "reconstruct it" would ingest all of them a second time.
    ///
    /// The folder is called save and its files are called tlg0062.*, so the
    /// files say it is not a textgroup.
    /// </summary>
    [Fact]
    public async Task AWorkingDirectoryIsStillExcluded()
    {
        var (data, db) = await NewCorpusAsync();
        using var _ = db;

        // The real textgroup.
        var real = Path.Combine(data, "tlg0062");
        WriteTextGroupCatalog(real, "urn:cts:greekLit:tlg0062", "Lucian");
        WriteTextGroupCatalog(Path.Combine(real, "tlg001"), "urn:cts:greekLit:tlg0062.tlg001", "ignored");
        File.WriteAllText(Path.Combine(real, "tlg001", "__cts__.xml"),
            @"<ti:work xmlns:ti=""http://chs.harvard.edu/xmlns/cts"" urn=""urn:cts:greekLit:tlg0062.tlg001"">
                <ti:title xml:lang=""eng"">Phalaris</ti:title>
              </ti:work>");
        WriteEdition(Path.Combine(real, "tlg001"), "tlg0062.tlg001.perseus-grc2.xml",
            "Lucian", "Phalaris", "ἀπέστειλεν ἡμᾶς");

        // The same text again, inside a working directory with no catalogue of
        // its own - catalogues two levels down, as save/ has.
        var save = Path.Combine(data, "save");
        WriteTextGroupCatalog(Path.Combine(save, "tlg0062"), "urn:cts:greekLit:tlg0062", "Lucian");
        WriteEdition(Path.Combine(save, "tlg0062", "tlg001"), "tlg0062.tlg001.perseus-grc2.xml",
            "Lucian", "Phalaris", "ἀπέστειλεν ἡμᾶς");

        var service = new PerseusIngestService();
        await service.IngestAsync(new[] { (data, "greekLit") });

        var library = await LibraryAsync();
        Assert.Single(library);
        Assert.Equal("Lucian", library[0].Author);
    }

    /// <summary>
    /// split/ and volume_xml/, which hold no catalogue at any depth. Nothing
    /// inside them agrees with the folder name either, so the same guard covers
    /// them without needing to know their names.
    /// </summary>
    [Theory]
    [InlineData("split")]
    [InlineData("volume_xml")]
    [InlineData("raw_files")]
    public async Task ADirectoryWithNoCatalogAnywhereIsStillExcluded(string folder)
    {
        var (data, db) = await NewCorpusAsync();
        using var _ = db;

        WriteEdition(Path.Combine(data, folder, "tlg0018"), "tlg0018.tlg001.1st1K-grc1.xml",
            "Philo Judaeus", "De Opificio Mundi", "τῶν ἄλλων νομοθετῶν");

        var service = new PerseusIngestService();
        await service.IngestAsync(new[] { (data, "greekLit") });

        Assert.Empty(await LibraryAsync());
    }

    /// <summary>
    /// A malformed catalogue is a catalogue that is not there, and is recovered
    /// the same way - rather than throwing out of a run that has no handler
    /// above it until the setup step, where one bad file would abandon the rest
    /// of the corpus alphabetically after it.
    /// </summary>
    [Fact]
    public async Task AMalformedCatalogDoesNotAbandonTheRun()
    {
        var (data, db) = await NewCorpusAsync();
        using var _ = db;

        var group = Path.Combine(data, "phi2003");
        Directory.CreateDirectory(group);
        File.WriteAllText(Path.Combine(group, "__cts__.xml"), "<ti:textgroup><unclosed>");
        WriteEdition(Path.Combine(group, "phi001"), "phi2003.phi001.perseus-lat1.xml",
            "Apicius", "De Re Coquinaria", "Fac tibi conditum sic");

        var later = Path.Combine(data, "phi9998");
        WriteTextGroupCatalog(later, "urn:cts:latinLit:phi9998", "Someone Later In The Alphabet");
        WriteEdition(Path.Combine(later, "phi001"), "phi9998.phi001.perseus-lat1.xml",
            "Someone Later In The Alphabet", "A Work", "uerba");

        var service = new PerseusIngestService();
        await service.IngestAsync(new[] { (data, "latinLit") });

        var library = await LibraryAsync();
        Assert.Contains(library, r => r.Author == "Apicius");
        Assert.Contains(library, r => r.Author == "Someone Later In The Alphabet");
    }
}
