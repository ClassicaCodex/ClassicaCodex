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
