using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Geste;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Putting Geste into the library.
///
/// The two tests that matter most here are about files rather than about XML:
/// which of a text's several variants to install, and what a work is keyed by.
/// Both were measured against the real corpus and both, done the obvious way,
/// lose a text without reporting anything.
/// </summary>
[Collection("Database")]
public class GesteIngestTests
{
    private const string Tei = "http://www.tei-c.org/ns/1.0";

    private static string File(string title, string body, string language = "fro", string? xmlId = null) =>
        $@"<TEI xmlns=""{Tei}""{(xmlId == null ? "" : $" xml:id=\"{xmlId}\"")}>
  <teiHeader>
    <fileDesc><titleStmt><title>{title}</title></titleStmt></fileDesc>
    <profileDesc><langUsage><language ident=""{language}"">x</language></langUsage></profileDesc>
  </teiHeader>
  <text><body>{body}</body></text>
</TEI>";

    /// <summary>A tree shaped like the extracted archive, so no download happens.</summary>
    private static string Corpus(params (string Directory, string Stem, string Xml)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccx-geste-tests", Guid.NewGuid().ToString("N"));

        foreach (var (dir, stem, xml) in files)
        {
            var folder = Path.Combine(root, "Geste-pinned", dir);
            Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(Path.Combine(folder, stem + ".xml"), xml);
        }

        return root;
    }

    private const string TwoDifferingLines =
        @"<lg><l n=""1""><w><choice><orig>KJ</orig><reg>ki</reg></choice></w></l>
          <l n=""2""><w><choice><orig>uolt</orig><reg>volt</reg></choice></w></l></lg>";

    private const string TwoIdenticalLines =
        @"<lg><l n=""1""><w>Dés</w></l><l n=""2""><w>vos</w></l></lg>";

    private static async Task<List<Work>> WorksAsync()
    {
        var author = Assert.Single(await new AuthorRepository().GetAllAsync());
        return await new WorkRepository().GetByAuthorAsync(author.AuthorId);
    }

    /// <summary>
    /// <b>The base file is not always the fuller one.</b> Otinel's ed_OtinG.xml
    /// holds 731 tokens and no verse lines at all, while ed_OtinG_pos.xml holds
    /// 2,193 lines. A rule preferring the plainer name would install an empty
    /// work and throw the text away - and report success doing it, since the
    /// empty file parses perfectly.
    /// </summary>
    [Fact]
    public async Task TheFullerVariantOfATextIsTheOneInstalled()
    {
        using var db = await TempDatabase.CreateAsync();

        var root = Corpus(
            ("xml_silver", "ed_OtinG", File("Otinel", "<lg></lg>")),
            ("xml_silver", "ed_OtinG_pos", File("Otinel", TwoDifferingLines)));

        var service = new GesteIngestService();
        await service.IngestAsync(root);

        Assert.Equal(1, service.TextsInstalled);
        Assert.Equal(2, service.LinesInstalled);

        var work = Assert.Single(await WorksAsync());
        var editions = await new EditionRepository().GetByWorkAsync(work.WorkId);
        Assert.NotEmpty(await new TextNodeRepository().GetByEditionAsync(editions[0].EditionId));
    }

    /// <summary>
    /// <b>Three of the 34 files carry someone else's xml:id.</b> ed_HuonG.xml
    /// says xml:id="ed_GuiBourgG", and ed_GuiBourgG.xml exists - so keying on
    /// xml:id would give Huon de Bordeaux and Gui de Bourgogne one CtsUrn and
    /// install one over the other. Huon is 10,495 lines.
    /// </summary>
    [Fact]
    public async Task TwoFilesSharingAnXmlIdStayTwoWorks()
    {
        using var db = await TempDatabase.CreateAsync();

        var root = Corpus(
            ("xml_gold", "ed_GuiBourgG", File("Gui de Bourgogne", TwoIdenticalLines, xmlId: "ed_GuiBourgG")),
            ("xml_silver", "ed_HuonG", File("Huon de Bordeaux", TwoIdenticalLines, xmlId: "ed_GuiBourgG")));

        var service = new GesteIngestService();
        await service.IngestAsync(root);

        Assert.Equal(2, service.TextsInstalled);

        var titles = (await WorksAsync()).Select(w => w.Title).OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "Gui de Bourgogne (G)", "Huon de Bordeaux (G)" }, titles);
    }

    /// <summary>
    /// Two editions where the readings differ, one where they do not. Nineteen of
    /// the 33 texts get two; the fourteen editions carry sic/corr rather than
    /// orig/reg, so their two readings would be the same string - and two
    /// identical entries in the edition dropdown is a worse answer than one.
    /// </summary>
    [Fact]
    public async Task ASecondEditionOnlyAppearsWhenTheReadingsDiffer()
    {
        using var db = await TempDatabase.CreateAsync();

        await new GesteIngestService().IngestAsync(Corpus(
            ("xml_gold", "transcr_Otin_B", File("Otinel", TwoDifferingLines)),
            ("xml_gold", "ed_FloovG", File("Floovant", TwoIdenticalLines))));

        var eds = new EditionRepository();
        var works = await WorksAsync();

        var otinel = works.Single(w => w.Title.StartsWith("Otinel", StringComparison.Ordinal));
        var floovant = works.Single(w => w.Title.StartsWith("Floovant", StringComparison.Ordinal));

        Assert.Equal(
            new[] { "diplomatic", "normalised" },
            (await eds.GetByWorkAsync(otinel.WorkId)).Select(e => e.Orthography)
                .OrderBy(o => o, StringComparer.Ordinal));

        Assert.Equal("diplomatic", Assert.Single(await eds.GetByWorkAsync(floovant.WorkId)).Orthography);
    }

    /// <summary>
    /// Anglo-Norman lemmas are stored under xno, not folded into Old French. A
    /// form looked up under the wrong language finds nothing, and the reason is
    /// invisible.
    /// </summary>
    [Fact]
    public async Task LemmasAreKeyedByTheTextsOwnLanguage()
    {
        using var db = await TempDatabase.CreateAsync();

        await new GesteIngestService().IngestAsync(Corpus(
            ("xml_gold", "transcr_Otin_B", File("Otinel",
                @"<lg><l n=""1""><w lemma=""voloir"" pos=""VERcjg"">uolt</w></l></lg>", language: "xno")),
            ("xml_gold", "ed_FloovG", File("Floovant",
                @"<lg><l n=""1""><w lemma=""seignor"" pos=""NOMcom"">SOIGNORS</w></l></lg>"))));

        var lemmas = new LemmaRepository();

        Assert.Equal("voloir", Assert.Single(await lemmas.GetHeadwordsForFormAsync("uolt", "xno")).Headword);
        Assert.Empty(await lemmas.GetHeadwordsForFormAsync("uolt", "fro"));
        Assert.Equal("seignor", Assert.Single(await lemmas.GetHeadwordsForFormAsync("soignors", "fro")).Headword);
    }

    /// <summary>
    /// Every text in this corpus is verse - laisses of assonanced lines, no prose
    /// anywhere in it - so IsVerse is set from that rather than left at its
    /// default. It is what the hexameter scanner asks about, and ReM's ingest
    /// leaves it unset.
    /// </summary>
    [Fact]
    public async Task EveryLineIsMarkedAsVerse()
    {
        using var db = await TempDatabase.CreateAsync();

        await new GesteIngestService().IngestAsync(Corpus(
            ("xml_gold", "ed_FloovG", File("Floovant", TwoIdenticalLines))));

        var work = Assert.Single(await WorksAsync());
        var edition = Assert.Single(await new EditionRepository().GetByWorkAsync(work.WorkId));

        Assert.All(await new TextNodeRepository().GetByEditionAsync(edition.EditionId),
            n => Assert.True(n.IsVerse));
    }

    /// <summary>
    /// Only xml_gold and xml_silver. The repository also holds xml_src, and other
    /// revisions a deprec folder, with further copies of the same transcriptions -
    /// a recursive glob would install several of them as separate works.
    /// </summary>
    [Fact]
    public async Task OtherCopiesOfTheSameTextsAreNotInstalled()
    {
        using var db = await TempDatabase.CreateAsync();

        await new GesteIngestService().IngestAsync(Corpus(
            ("xml_gold", "transcr_Otin_B", File("Otinel", TwoDifferingLines)),
            ("xml_src", "transcr_Otin_B", File("Otinel", TwoDifferingLines)),
            ("deprec", "transcr_Otin_B", File("Otinel", TwoDifferingLines))));

        Assert.Single(await WorksAsync());
    }

    /// <summary>
    /// One unreadable file must not throw away the others, and has to be named
    /// rather than quietly missing.
    /// </summary>
    [Fact]
    public async Task OneBadFileIsReportedAndTheRestArrive()
    {
        using var db = await TempDatabase.CreateAsync();

        var root = Corpus(("xml_gold", "ed_FloovG", File("Floovant", TwoIdenticalLines)));
        Directory.CreateDirectory(Path.Combine(root, "Geste-pinned", "xml_gold"));
        System.IO.File.WriteAllText(
            Path.Combine(root, "Geste-pinned", "xml_gold", "ed_Broken.xml"), "<TEI><not-closed>");

        var service = new GesteIngestService();
        var outcome = await service.IngestAsync(root);

        Assert.Equal(1, service.TextsInstalled);
        Assert.Contains("ed_Broken", Assert.Single(outcome.SkippedFiles).FilePath);
    }
}
