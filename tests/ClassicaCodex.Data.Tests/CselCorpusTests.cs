using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// That the CSEL setup step can reuse PerseusIngestService rather than needing an
/// importer of its own.
///
/// The catalog entry claims csel-dev is the same CTS layout as canonical-latinLit and
/// a TEI body the existing parser already reads. That claim is what makes the step two
/// dozen lines instead of a week, so it is worth pinning rather than asserting in a
/// comment: the tree below is the real shape, with the catalog files copied verbatim
/// from the repository and a body carrying the two features that could have broken it -
/// a table-of-contents div, and footnotes sitting inside the reading text.
/// </summary>
[Collection("Database")]
public class CselCorpusTests
{
    private const string TextGroupCatalog =
        @"<ti:textgroup xmlns:ti=""http://chs.harvard.edu/xmlns/cts"" urn=""urn:cts:latinLit:stoa0007"">
            <ti:groupname xml:lang=""eng"">Adamnan, Saint</ti:groupname>
          </ti:textgroup>";

    private const string WorkCatalog =
        @"<ti:work xml:lang=""lat"" xmlns:ti=""http://chs.harvard.edu/xmlns/cts""
                   groupUrn=""urn:cts:latinLit:stoa0007"" urn=""urn:cts:latinLit:stoa0007.stoa002"">
            <ti:title xml:lang=""lat"">De Locis Santis</ti:title>
            <ti:edition xml:lang=""lat"" workUrn=""urn:cts:latinLit:stoa0007.stoa002""
                        urn=""urn:cts:latinLit:stoa0007.stoa002.opp-lat1"">
              <ti:label xml:lang=""lat"">De Locis Santis</ti:label>
              <ti:description xml:lang=""mul"">Itinera hierosolymitana saecvli IIII-VIII (CSEL 39).</ti:description>
            </ti:edition>
          </ti:work>";

    // Deliberately shaped like the real file: an edition div wrapping textpart divs,
    // a toc div with no number, and a footnote inside a paragraph of reading text.
    private const string Edition =
        @"<?xml version=""1.0"" encoding=""UTF-8""?>
          <TEI xmlns=""http://www.tei-c.org/ns/1.0"">
            <teiHeader>
              <fileDesc>
                <titleStmt><title xml:lang=""lat"">De Locis Santis</title><author>Adamnan</author></titleStmt>
                <publicationStmt>
                  <availability>
                    <licence target=""https://creativecommons.org/licenses/by-sa/4.0/"">CC BY-SA 4.0</licence>
                  </availability>
                </publicationStmt>
                <sourceDesc><p>CSEL 39</p></sourceDesc>
              </fileDesc>
            </teiHeader>
            <text>
              <body>
                <div type=""edition"" xml:lang=""lat"" n=""urn:cts:latinLit:stoa0007.stoa002.opp-lat1"">
                  <div subtype=""toc"" type=""textpart"">
                    <ab><title type=""sub"">CAPITVLATIONES LIBRI PRIMI.</title></ab>
                  </div>
                  <div type=""textpart"" subtype=""book"" n=""1"">
                    <div type=""textpart"" subtype=""chapter"" n=""1"">
                      <p>De situ Hierusalem ciuitatis.
                        <note rend=""script"" type=""footnote"">Primum folium codicis P abscissum est</note>
                      </p>
                    </div>
                    <div type=""textpart"" subtype=""chapter"" n=""2"">
                      <p>De ecclesia rotundae formulae.</p>
                    </div>
                  </div>
                </div>
              </body>
            </text>
          </TEI>";

    /// <summary>
    /// That the same work arriving from two collections gains a second edition rather
    /// than replacing the first.
    ///
    /// The Patrologia Latina step tells the researcher exactly this - that where a work
    /// also appears in CSEL, the two sit side by side and the critical edition is not
    /// overwritten by the reprint. It rests on Author and Work upserting by CTS URN
    /// while editions key on their own, which is a property of the repositories rather
    /// than anything either step does, and so is worth pinning where a change to that
    /// upsert would be caught.
    /// </summary>
    [Fact]
    public async Task TheSameWorkFromTwoCollectionsGainsAnEditionRatherThanLosingOne()
    {
        using var db = await TempDatabase.CreateAsync();
        var editions = new EditionRepository();

        static string Group(string urn, string name) =>
            $@"<ti:textgroup xmlns:ti=""http://chs.harvard.edu/xmlns/cts"" urn=""{urn}"">
                 <ti:groupname xml:lang=""eng"">{name}</ti:groupname>
               </ti:textgroup>";

        static string Work(string editionUrn) =>
            $@"<ti:work xml:lang=""lat"" xmlns:ti=""http://chs.harvard.edu/xmlns/cts""
                        groupUrn=""urn:cts:latinLit:stoa0022"" urn=""urn:cts:latinLit:stoa0022.stoa001"">
                 <ti:title xml:lang=""lat"">De Ciuitate Dei</ti:title>
                 <ti:edition xml:lang=""lat"" workUrn=""urn:cts:latinLit:stoa0022.stoa001"" urn=""{editionUrn}"">
                   <ti:label xml:lang=""lat"">De Ciuitate Dei</ti:label>
                 </ti:edition>
               </ti:work>";

        static string Text(string editionUrn, string line) =>
            $@"<TEI xmlns=""http://www.tei-c.org/ns/1.0"">
                 <teiHeader><fileDesc><titleStmt><title>De Ciuitate Dei</title></titleStmt>
                 <publicationStmt><p>t</p></publicationStmt><sourceDesc><p>s</p></sourceDesc></fileDesc></teiHeader>
                 <text><body><div type=""edition"" xml:lang=""lat"" n=""{editionUrn}"">
                   <div type=""textpart"" subtype=""chapter"" n=""1""><p>{line}</p></div>
                 </div></body></text>
               </TEI>";

        async Task IngestAsync(string root, string editionUrn, string line, string collection)
        {
            var dir = Path.Combine(root, "data", "stoa0022", "stoa001");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(root, "data", "stoa0022", "__cts__.xml"),
                Group("urn:cts:latinLit:stoa0022", "Augustine"));
            File.WriteAllText(Path.Combine(dir, "__cts__.xml"), Work(editionUrn));
            File.WriteAllText(Path.Combine(dir, editionUrn.Split(':').Last() + ".xml"), Text(editionUrn, line));
            await new PerseusIngestService().IngestAsync([(Path.Combine(root, "data"), "latinLit")]);
            await editions.StampCollectionAsync(root, collection);
        }

        var cselRoot = Path.Combine(Path.GetTempPath(), "cc-two-" + Guid.NewGuid().ToString("N"));
        var patrologiaRoot = Path.Combine(Path.GetTempPath(), "cc-two-" + Guid.NewGuid().ToString("N"));
        try
        {
            await IngestAsync(cselRoot, "urn:cts:latinLit:stoa0022.stoa001.opp-lat1", "ciuitas dei critica", "csel");
            await IngestAsync(patrologiaRoot, "urn:cts:latinLit:stoa0022.stoa001.opp-lat2", "ciuitas dei migne", "patrologia-latina");

            // One author, one work - they merged on CTS identity, as intended.
            Assert.Equal(1L, await db.CountAsync("Authors"));
            Assert.Equal(1L, await db.CountAsync("Works"));

            // Two editions of it, one per collection, neither overwritten.
            var workId = await db.ScalarAsync<int>(
                "SELECT WorkId FROM Works WHERE CtsUrn = 'urn:cts:latinLit:stoa0022.stoa001';");
            Assert.Equal(2, (await editions.GetByWorkAsync(workId)).Count);
            Assert.Equal(["csel", "patrologia-latina"], await editions.GetCollectionsAsync());

            // And each is reachable on its own, which is the point of having both.
            var repo = new TextNodeRepository();
            var migneOnly = new SearchFilters { Query = "ciuitas" };
            migneOnly.Collections.Add("patrologia-latina");
            Assert.Equal("ciuitas dei migne",
                Assert.Single((await repo.SearchFilteredAsync(migneOnly)).Rows).Text);
        }
        finally
        {
            foreach (var root in new[] { cselRoot, patrologiaRoot })
                try { Directory.Delete(root, recursive: true); } catch { /* temp dir */ }
        }
    }

    /// <summary>
    /// That the Patrologia Latina step imports only the catalogued textgroups.
    ///
    /// That repository publishes 630 textgroups under permanent identifiers beside
    /// 8,770 under placeholders - tmp1, tmp26, tmp990, with CTS URNs to match. Notes in
    /// this application bind to CTS URNs precisely because they are meant to outlast a
    /// re-ingest, so importing an identifier its own project intends to replace would
    /// lose whatever was attached to it, silently, whenever that happened.
    ///
    /// Worth a test rather than a comment because both failure directions are quiet: a
    /// filter that excluded everything would import nothing and look like a slow step,
    /// and a filter that excluded nothing would look exactly like success.
    /// </summary>
    [Fact]
    public async Task ProvisionalTextGroupsAreLeftOutOfTheImport()
    {
        using var db = await TempDatabase.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), "cc-pl-" + Guid.NewGuid().ToString("N"));
        try
        {
            void WriteGroup(string dir, string urn, string name, string workUrn, string editionUrn, string line)
            {
                var workDir = Path.Combine(root, "data", dir, "w1");
                Directory.CreateDirectory(workDir);
                File.WriteAllText(Path.Combine(root, "data", dir, "__cts__.xml"),
                    $@"<ti:textgroup xmlns:ti=""http://chs.harvard.edu/xmlns/cts"" urn=""{urn}"">
                         <ti:groupname xml:lang=""eng"">{name}</ti:groupname>
                       </ti:textgroup>");
                File.WriteAllText(Path.Combine(workDir, "__cts__.xml"),
                    $@"<ti:work xml:lang=""lat"" xmlns:ti=""http://chs.harvard.edu/xmlns/cts""
                                groupUrn=""{urn}"" urn=""{workUrn}"">
                         <ti:title xml:lang=""lat"">Opus</ti:title>
                         <ti:edition xml:lang=""lat"" workUrn=""{workUrn}"" urn=""{editionUrn}"">
                           <ti:label xml:lang=""lat"">Opus</ti:label>
                         </ti:edition>
                       </ti:work>");
                File.WriteAllText(Path.Combine(workDir, "e.xml"),
                    $@"<TEI xmlns=""http://www.tei-c.org/ns/1.0"">
                         <teiHeader><fileDesc><titleStmt><title>Opus</title></titleStmt>
                         <publicationStmt><p>t</p></publicationStmt><sourceDesc><p>s</p></sourceDesc></fileDesc></teiHeader>
                         <text><body><div type=""edition"" xml:lang=""lat"" n=""{editionUrn}"">
                           <div type=""textpart"" subtype=""chapter"" n=""1""><p>{line}</p></div>
                         </div></body></text>
                       </TEI>");
            }

            WriteGroup("stoa0022", "urn:cts:latinLit:stoa0022", "Ambrosius",
                "urn:cts:latinLit:stoa0022.stoa001", "urn:cts:latinLit:stoa0022.stoa001.opp-lat1", "catalogued text");
            WriteGroup("tmp26", "urn:cts:latinLit:tmp26", "Placeholder",
                "urn:cts:latinLit:tmp26.tmp001", "urn:cts:latinLit:tmp26.tmp001.opp-lat1", "provisional text");

            var service = new PerseusIngestService
            {
                IncludeTextGroup = name => name.StartsWith("stoa", StringComparison.OrdinalIgnoreCase)
            };
            await service.IngestAsync([(Path.Combine(root, "data"), "latinLit")]);

            Assert.Equal(1L, await db.CountAsync("Authors"));
            Assert.Equal(1L, await db.ScalarAsync<long>(
                "SELECT COUNT(*) FROM Authors WHERE CtsUrn = 'urn:cts:latinLit:stoa0022';"));
            Assert.Equal(0L, await db.ScalarAsync<long>(
                "SELECT COUNT(*) FROM Authors WHERE CtsUrn LIKE '%tmp%';"));

            // And the text under the provisional identifier never arrives either, rather
            // than arriving orphaned from an author.
            Assert.Equal("catalogued text",
                Assert.Single((await new TextNodeRepository().SearchFilteredAsync(
                    new SearchFilters { Query = "text" })).Rows).Text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp dir */ }
        }
    }

    [Fact]
    public async Task ACselVolumeImportsAsLatinWithItsFootnotesOutOfTheReadingText()
    {
        using var db = await TempDatabase.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), "cc-csel-" + Guid.NewGuid().ToString("N"));
        var workDir = Path.Combine(root, "data", "stoa0007", "stoa002");
        Directory.CreateDirectory(workDir);
        try
        {
            File.WriteAllText(Path.Combine(root, "data", "stoa0007", "__cts__.xml"), TextGroupCatalog);
            File.WriteAllText(Path.Combine(workDir, "__cts__.xml"), WorkCatalog);
            File.WriteAllText(Path.Combine(workDir, "stoa0007.stoa002.opp-lat1.xml"), Edition);

            await new PerseusIngestService().IngestAsync(
                [(Path.Combine(root, "data"), "latinLit")]);

            // The catalogs are read, and the corpus lands in latinLit alongside the
            // classical Latin texts rather than in a namespace of its own.
            Assert.Equal(1L, await db.ScalarAsync<long>(
                "SELECT COUNT(*) FROM Authors WHERE CtsUrn = 'urn:cts:latinLit:stoa0007' AND Namespace = 'latinLit';"));
            var workId = await db.ScalarAsync<int>(
                "SELECT WorkId FROM Works WHERE CtsUrn = 'urn:cts:latinLit:stoa0007.stoa002';");
            Assert.True(workId > 0);

            var edition = Assert.Single(await new EditionRepository().GetByWorkAsync(workId));
            var nodes = await new TextNodeRepository().GetByEditionAsync(edition.EditionId, readingLinesOnly: true);
            var reading = string.Join(" ", nodes.Select(n => n.Text));

            Assert.Contains("De situ Hierusalem", reading);
            Assert.Contains("rotundae formulae", reading);

            // The one that would have ruined every page of it. These files carry
            // editorial footnotes inside the paragraphs of reading text, and a parser
            // that took them at face value would splice a note about a missing folio
            // into the middle of Adamnan's sentence.
            Assert.DoesNotContain("Primum folium codicis", reading);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp dir */ }
        }
    }
}
