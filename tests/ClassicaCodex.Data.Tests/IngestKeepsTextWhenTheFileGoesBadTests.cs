using System.Text;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A re-ingest whose source file has gone bad must not destroy the copy
/// already in the library.
///
/// The ingest services clear an edition's text nodes and then re-parse the
/// file to refill them, and the two are not in one transaction. Ordered the
/// wrong way round - clear, then parse - anything that throws in between is
/// destruction with nothing to put back, and the step most likely to throw is
/// the parse, because it reads a file that may have changed, been truncated,
/// or been half-downloaded since the last run.
///
/// That is not hypothetical. It emptied a work in a real 2.3-million-passage
/// library: the clear succeeded, the parse threw, the run recorded a "failed
/// file" and moved on, and three passages were gone. Re-running the ingest
/// could not bring them back, because the file was still bad. The comment in
/// the catch block claimed the edition kept the text nodes it had, which was
/// the exact opposite of what happened.
///
/// The fix is ordering, not a transaction: parse first, and only start
/// clearing once there is something to put in its place.
/// </summary>
[Collection("Database")]
public class IngestKeepsTextWhenTheFileGoesBadTests
{
    [Fact]
    public async Task ATruncatedSourceFileDoesNotEmptyTheEditionItAlreadyIngested()
    {
        using var db = await TempDatabase.CreateAsync();
        using var corpus = new TempCorpus();

        // First run: a good file, ingested normally.
        await new PerseusIngestService().IngestAsync(new[] { (corpus.DataPath, "latinLit") });

        // This database holds nothing but the fixture, so the whole-table
        // count is the edition's count and needs no lookup.
        var before = await CountAllTextNodesAsync();
        Assert.True(before > 0, "the fixture should have ingested at least one passage");

        // The file goes bad between runs - a truncated download, a repo
        // update that landed mid-write, a disk that lied.
        corpus.TruncateEditionFile();

        var second = new PerseusIngestService();
        await second.IngestAsync(new[] { (corpus.DataPath, "latinLit") });

        // It should say the file failed...
        Assert.NotEmpty(second.FailedFiles);

        // ...and it must not have thrown away the text it already had.
        var after = await CountAllTextNodesAsync();
        Assert.Equal(before, after);
    }

    private static async Task<int> CountAllTextNodesAsync()
    {
        await using var connection = await DbConnectionFactory.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM TextNodes;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// A minimal Perseus-shaped corpus tree: a data folder, an author folder
    /// and a work folder, each with the __cts__.xml the walker needs, plus one
    /// edition file. Without those catalogue files the ingest walks straight
    /// past the directory and the test would pass by doing nothing.
    /// </summary>
    private sealed class TempCorpus : IDisposable
    {
        private readonly string _root;
        private readonly string _editionFile;

        public string DataPath { get; }

        public TempCorpus()
        {
            _root = Path.Combine(Path.GetTempPath(), "classicacodex-tests",
                                 "corpus-" + Guid.NewGuid().ToString("N"));
            DataPath = Path.Combine(_root, "data");

            var workDir = Path.Combine(DataPath, "tmp9999", "tmp9999");
            Directory.CreateDirectory(workDir);

            File.WriteAllText(Path.Combine(DataPath, "tmp9999", "__cts__.xml"), AuthorCatalog, Encoding.UTF8);
            File.WriteAllText(Path.Combine(workDir, "__cts__.xml"), WorkCatalog, Encoding.UTF8);

            _editionFile = Path.Combine(workDir, "tmp9999.tmp9999.test-lat1.xml");
            File.WriteAllText(_editionFile, EditionXml, Encoding.UTF8);
        }

        /// <summary>Cuts the file in half, leaving elements unclosed.</summary>
        public void TruncateEditionFile()
        {
            var text = File.ReadAllText(_editionFile);
            File.WriteAllText(_editionFile, text[..(text.Length / 2)], Encoding.UTF8);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private const string AuthorCatalog = """
            <ti:textgroup xmlns:ti="http://chs.harvard.edu/xmlns/cts" urn="urn:cts:latinLit:tmp9999">
              <ti:groupname xml:lang="lat">Testus</ti:groupname>
            </ti:textgroup>
            """;

        private const string WorkCatalog = """
            <ti:work xmlns:ti="http://chs.harvard.edu/xmlns/cts" xml:lang="lat"
                     urn="urn:cts:latinLit:tmp9999.tmp9999" groupUrn="urn:cts:latinLit:tmp9999">
              <ti:title xml:lang="lat">Opuscula</ti:title>
              <ti:edition urn="urn:cts:latinLit:tmp9999.tmp9999.test-lat1"
                          workUrn="urn:cts:latinLit:tmp9999.tmp9999">
                <ti:label xml:lang="lat">Opuscula</ti:label>
                <ti:description xml:lang="lat">A fixture.</ti:description>
              </ti:edition>
            </ti:work>
            """;

        private const string EditionXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <TEI xmlns="http://www.tei-c.org/ns/1.0">
              <teiHeader>
                <fileDesc>
                  <titleStmt><title>Opuscula</title></titleStmt>
                  <publicationStmt><p>A fixture.</p></publicationStmt>
                  <sourceDesc><p>A fixture.</p></sourceDesc>
                </fileDesc>
              </teiHeader>
              <text>
                <body>
                  <div type="edition" n="urn:cts:latinLit:tmp9999.tmp9999.test-lat1" xml:lang="lat">
                    <div type="textpart" subtype="section" n="1">
                      <p>Prima sententia huius fixturae est.</p>
                    </div>
                    <div type="textpart" subtype="section" n="2">
                      <p>Altera sententia huius fixturae est.</p>
                    </div>
                  </div>
                </body>
              </text>
            </TEI>
            """;
    }
}
