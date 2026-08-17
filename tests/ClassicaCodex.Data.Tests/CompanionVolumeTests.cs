using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The notes, appendices and indexes published alongside a text.
///
/// CTS versions these off their parent rather than numbering them separately -
/// the Cambridge Septuagint Isaiah is 1st1K-eng1, its notes are 1st1K-eng1a -
/// and marks them with a third body-div type, "commentary", beside "edition"
/// and "translation".
///
/// Both of those used to fall through to EditionKind.Unknown, which had no
/// consequence anyone would notice at ingest time and a bad one later: the
/// reader sorts editions into two dropdowns by Kind, so an Unknown edition
/// appeared in neither. Its text ingested, and searched, and could not be
/// opened - a search result you could read in the results list and nowhere
/// else.
/// </summary>
[Collection("Database")]
public class CompanionVolumeTests
{
    private static string Group(string urn, string name) =>
        $@"<ti:textgroup xmlns:ti=""http://chs.harvard.edu/xmlns/cts"" urn=""{urn}"">
             <ti:groupname xml:lang=""eng"">{name}</ti:groupname>
           </ti:textgroup>";

    private static string Work(params string[] editionUrns) =>
        $@"<ti:work xml:lang=""grc"" xmlns:ti=""http://chs.harvard.edu/xmlns/cts""
                    groupUrn=""urn:cts:greekLit:tlg0527"" urn=""urn:cts:greekLit:tlg0527.tlg048"">
             <ti:title xml:lang=""lat"">Isaias</ti:title>
             {string.Join("\n", editionUrns.Select(u =>
                 $@"<ti:edition xml:lang=""grc"" workUrn=""urn:cts:greekLit:tlg0527.tlg048"" urn=""{u}"">
                      <ti:label xml:lang=""lat"">Isaias</ti:label>
                    </ti:edition>"))}
           </ti:work>";

    private static string Text(string divType, string lang, string line) =>
        $@"<TEI xmlns=""http://www.tei-c.org/ns/1.0"">
             <teiHeader><fileDesc><titleStmt><title>Isaias</title></titleStmt>
             <publicationStmt><p>t</p></publicationStmt><sourceDesc><p>s</p></sourceDesc></fileDesc></teiHeader>
             <text><body><div type=""{divType}"" xml:lang=""{lang}"">
               <div type=""textpart"" subtype=""paragraph"" n=""1""><p>{line}</p></div>
             </div></body></text>
           </TEI>";

    /// <summary>
    /// Ingests one work whose files are named as given, and returns its editions.
    /// </summary>
    private static async Task<List<Edition>> IngestAsync(
        TempDatabase db, params (string Version, string DivType, string Lang, string Line)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "cc-companion-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(root, "data", "tlg0527", "tlg048");
            Directory.CreateDirectory(dir);

            File.WriteAllText(Path.Combine(root, "data", "tlg0527", "__cts__.xml"),
                Group("urn:cts:greekLit:tlg0527", "Old Testament"));

            var urns = files.Select(f => $"urn:cts:greekLit:tlg0527.tlg048.{f.Version}").ToArray();
            File.WriteAllText(Path.Combine(dir, "__cts__.xml"), Work(urns));

            foreach (var (version, divType, lang, line) in files)
            {
                File.WriteAllText(
                    Path.Combine(dir, $"tlg0527.tlg048.{version}.xml"), Text(divType, lang, line));
            }

            await new PerseusIngestService().IngestAsync([(Path.Combine(root, "data"), "greekLit")]);

            var workId = await db.ScalarAsync<int>(
                "SELECT WorkId FROM Works WHERE CtsUrn = 'urn:cts:greekLit:tlg0527.tlg048';");
            return await new EditionRepository().GetByWorkAsync(workId);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp dir */ }
        }
    }

    /// <summary>
    /// The reported case, end to end. "1st1K-eng1a" is English, and English in
    /// a Greek corpus is not the original - so it belongs in the second pane,
    /// not in neither.
    /// </summary>
    [Fact]
    public async Task ALetteredCompanionVolumeIsClassifiedLikeItsParent()
    {
        using var db = await TempDatabase.CreateAsync();

        var editions = await IngestAsync(db,
            ("1st1K-grc1", "edition", "grc", "λόγος"),
            ("1st1K-eng1", "translation", "eng", "the word"),
            ("1st1K-eng1a", "commentary", "eng", "14. Here Heb. has Lilith."));

        var notes = Assert.Single(editions, e => e.CtsUrn.EndsWith("1st1K-eng1a"));

        Assert.Equal(EditionKind.Translation, notes.Kind);
        Assert.Equal("eng", notes.Language);

        // And the parent it is versioned off is untouched by the change.
        Assert.Equal(EditionKind.Original,
            Assert.Single(editions, e => e.CtsUrn.EndsWith("1st1K-grc1")).Kind);
        Assert.Equal(EditionKind.Translation,
            Assert.Single(editions, e => e.CtsUrn.EndsWith("1st1K-eng1")).Kind);
    }

    /// <summary>
    /// The fallback path, for a file whose name says nothing useful. A
    /// commentary is not a translation, but the reader has two panes and this
    /// is read against the original, so the second one is where it goes -
    /// Unknown would mean no pane at all.
    /// </summary>
    [Fact]
    public async Task ACommentaryDivIsRecognisedWhenTheFilenameSaysNothing()
    {
        using var db = await TempDatabase.CreateAsync();

        var editions = await IngestAsync(db,
            ("notes-companionvolume", "commentary", "eng", "14. Here Heb. has Lilith."));

        Assert.Equal(EditionKind.Translation, Assert.Single(editions).Kind);
    }

    /// <summary>
    /// The narrowing that came with the fix. Reading a version identifier as
    /// "three letters, then a number with at most one letter after it" must
    /// not become "the first three letters of anything" - that would start
    /// inventing language codes out of identifiers that carry none.
    /// </summary>
    [Fact]
    public async Task AVersionIdentifierThatIsNotALanguageCodeStillSaysNothing()
    {
        using var db = await TempDatabase.CreateAsync();

        // "engelbert1" opens with three letters that spell a language and is
        // not one. Nothing in the file says edition or translation either, so
        // there is genuinely nothing to go on and Unknown is the honest answer.
        var editions = await IngestAsync(db, ("x-engelbert1", "textpart", "grc", "λόγος"));

        var only = Assert.Single(editions);
        Assert.Equal(EditionKind.Unknown, only.Kind);
        Assert.Null(only.Language);
    }

    /// <summary>
    /// A version identifier with no number at all - the plain "-grc" form -
    /// still resolves. Stripping trailing letters before checking for digits
    /// would have eaten the whole code.
    /// </summary>
    [Fact]
    public async Task AVersionIdentifierWithNoNumberStillResolves()
    {
        using var db = await TempDatabase.CreateAsync();

        var editions = await IngestAsync(db, ("opp-grc", "edition", "grc", "λόγος"));

        var only = Assert.Single(editions);
        Assert.Equal(EditionKind.Original, only.Kind);
        Assert.Equal("grc", only.Language);
    }
}
