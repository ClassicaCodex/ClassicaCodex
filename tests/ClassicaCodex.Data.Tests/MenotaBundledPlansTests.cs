using System.IO.Compression;
using System.Xml.Linq;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The Menota step without the reader doing anything by hand: the plans that
/// come with the app, the check that one still fits a manuscript, and where
/// the download puts each file.
///
/// The step used to send a reader to Menota's catalogue to save 91 files and an
/// entity table, and then showed them all 91 manuscripts to divide into works.
/// On an iPad that was Safari, the Files app, a zip moved by hand and 91 screens
/// in a row. Now the archive is fetched, and the divisions already confirmed are
/// used wherever the manuscript still matches them.
/// </summary>
[Collection("Database")]
public class MenotaBundledPlansTests : IDisposable
{
    private readonly string _folder;

    public MenotaBundledPlansTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "cc-menota-bundled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Every_manuscript_in_Menotas_archive_comes_with_a_confirmed_plan()
    {
        Assert.Equal(91, MenotaBundledPlans.FileNames.Count);

        foreach (var name in MenotaBundledPlans.FileNames)
        {
            var plan = MenotaBundledPlans.Find(name);

            Assert.NotNull(plan);
            Assert.True(plan!.Confirmed, name);
            Assert.NotEmpty(plan.Works);
            Assert.All(plan.Works, w =>
            {
                Assert.NotEmpty(w.DivPaths);
                Assert.True(w.WordCount > 0, $"{name}: {w.Title}");
                Assert.DoesNotContain('�', w.Title);
            });
        }

        // The three Alcuin works merged by hand, which a fresh proposal would
        // not have, so a refresh that lost them would be noticed.
        Assert.Equal(3, MenotaBundledPlans.Find("AM-619-4to.xml")!.Works.Count(w => w.Author == "Alcuin"));
    }

    [Fact]
    public void Each_lookup_is_a_fresh_copy_and_an_unknown_manuscript_has_none()
    {
        var name = MenotaBundledPlans.FileNames.First();
        MenotaBundledPlans.Find(name)!.Works.Clear();

        Assert.NotEmpty(MenotaBundledPlans.Find(name)!.Works);
        Assert.Null(MenotaBundledPlans.Find("Not-in-Menota.xml"));
    }

    [Fact]
    public async Task A_manuscript_that_still_matches_its_plan_is_imported_without_review()
    {
        using var db = await TempDatabase.CreateAsync();
        var (fileName, plan) = OneDivisionPlan();
        var path = WriteManuscript(fileName, plan.ReadingLevel, plan.Works[0].WordCount, language: "non");

        // The planner's proposal, never confirmed, is replaced as readily as
        // no plan at all.
        new MenotaIngestPlan { FileName = fileName, Works = { new MenotaWorkPlan { Title = "Proposed", DivPaths = { "0" } } } }
            .Save(MenotaIngestPlan.PlanPathFor(path));

        var service = new MenotaIngestService();
        await service.IngestAsync(_folder);

        Assert.Empty(service.FailedFiles);
        Assert.Empty(service.PlansWritten);
        Assert.Equal(path, Assert.Single(service.BundledPlansUsed));

        var saved = MenotaIngestPlan.Load(MenotaIngestPlan.PlanPathFor(path))!;
        Assert.True(saved.Confirmed);
        Assert.Equal(plan.Works[0].Title, Assert.Single(saved.Works).Title);
        Assert.Equal("non", saved.Language);
        Assert.Contains("came with Classica Codex", saved.Notes[0]);
    }

    [Fact]
    public async Task A_manuscript_Menota_has_changed_since_goes_to_the_review()
    {
        using var db = await TempDatabase.CreateAsync();
        var (fileName, plan) = OneDivisionPlan();
        var path = WriteManuscript(fileName, plan.ReadingLevel, plan.Works[0].WordCount - 1);

        var service = new MenotaIngestService();
        await service.IngestAsync(_folder);

        Assert.Empty(service.BundledPlansUsed);
        Assert.Equal(MenotaIngestPlan.PlanPathFor(path), Assert.Single(service.PlansWritten));
        Assert.False(MenotaIngestPlan.Load(MenotaIngestPlan.PlanPathFor(path))!.Confirmed);
    }

    [Fact]
    public async Task A_plan_the_reader_confirmed_is_kept_over_the_one_that_comes_with_the_app()
    {
        using var db = await TempDatabase.CreateAsync();
        var (fileName, plan) = OneDivisionPlan();
        var path = WriteManuscript(fileName, plan.ReadingLevel, plan.Works[0].WordCount);

        var own = MenotaBundledPlans.Find(fileName)!;
        own.Works[0].Title = "Retitled by the reader";
        own.Works[0].UrnSlug = "retitled-by-the-reader";
        own.Save(MenotaIngestPlan.PlanPathFor(path));

        var service = new MenotaIngestService();
        await service.IngestAsync(_folder);

        Assert.Empty(service.FailedFiles);
        Assert.Empty(service.BundledPlansUsed);
        Assert.Equal("Retitled by the reader",
            MenotaIngestPlan.Load(MenotaIngestPlan.PlanPathFor(path))!.Works[0].Title);
    }

    [Fact]
    public void A_section_added_since_or_a_new_reading_level_does_not_fit()
    {
        var (fileName, plan) = OneDivisionPlan();
        var words = plan.Works[0].WordCount;

        Assert.True(MenotaBundledPlans.Fits(plan, Load(WriteManuscript(fileName, plan.ReadingLevel, words))));

        // Every division the plan names is as it was, but there is more text
        // than the plan covers.
        Assert.False(MenotaBundledPlans.Fits(plan, Load(WriteManuscript(fileName, plan.ReadingLevel, words, addedWords: 5))));

        var otherLevel = plan.ReadingLevel == "norm" ? "dipl" : "norm";
        Assert.False(MenotaBundledPlans.Fits(plan, Load(WriteManuscript(fileName, otherLevel, words))));

        var unconfirmed = MenotaBundledPlans.Find(fileName)!;
        unconfirmed.Confirmed = false;
        Assert.False(MenotaBundledPlans.Fits(unconfirmed, Load(WriteManuscript(fileName, plan.ReadingLevel, words))));
    }

    /// <summary>
    /// A manuscript already in the folder is replaced where it is, so its plan
    /// stays beside it; a new one goes where the archive puts it; and nothing
    /// is written outside the folder or that is not a manuscript.
    /// </summary>
    [Fact]
    public void The_archive_replaces_manuscripts_where_they_are_and_adds_the_rest()
    {
        var saved = Path.Combine(_folder, "saved-by-hand");
        Directory.CreateDirectory(saved);
        File.WriteAllText(Path.Combine(saved, "AM-1.xml"), "old");
        File.WriteAllText(Path.Combine(saved, "AM-1.plan.json"), "{}");

        var archive = Path.Combine(Path.GetTempPath(), "cc-menota-archive-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                foreach (var (name, text) in new[]
                         {
                             ("texts/AM-1.xml", "new"),
                             ("texts/AM-2.xml", "second"),
                             ("../outside.xml", "escaped"),
                             ("texts/readme.txt", "not a manuscript")
                         })
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write(text);
                }
            }

            Assert.Equal(2, MenotaDownloadService.Extract(archive, _folder));
        }
        finally
        {
            File.Delete(archive);
        }

        Assert.Equal("new", File.ReadAllText(Path.Combine(saved, "AM-1.xml")));
        Assert.True(File.Exists(Path.Combine(saved, "AM-1.plan.json")));
        Assert.False(File.Exists(Path.Combine(_folder, "texts", "AM-1.xml")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_folder, "texts", "AM-2.xml")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_folder)!, "outside.xml")));
        Assert.False(File.Exists(Path.Combine(_folder, "texts", "readme.txt")));
        Assert.Empty(Directory.GetFiles(_folder, "*.partial", SearchOption.AllDirectories));
    }

    /// <summary>
    /// A bundled plan with one work in the first division, which a manuscript
    /// can be written to match without copying Menota's text into the tests.
    /// </summary>
    private static (string FileName, MenotaIngestPlan Plan) OneDivisionPlan() =>
        MenotaBundledPlans.FileNames
            .Order(StringComparer.Ordinal)
            .Select(name => (name, MenotaBundledPlans.Find(name)!))
            .First(p => p.Item2.Works.Count == 1 && p.Item2.Works[0].DivPaths.SequenceEqual(new[] { "0" }));

    private string WriteManuscript(string fileName, string level, int words, int addedWords = 0, string language = "isl")
    {
        string Words(int count) =>
            "<p>" + string.Concat(Enumerable.Range(0, count).Select(i => $"<w><me:{level}>ord{i}</me:{level}></w>")) + "</p>";

        var body = "<div>" + Words(words) + "</div>";
        if (addedWords > 0) body += "<div>" + Words(addedWords) + "</div>";

        var path = Path.Combine(_folder, fileName);
        File.WriteAllText(path,
            "<TEI xmlns=\"http://www.tei-c.org/ns/1.0\" xmlns:me=\"http://www.menota.org/ns/1.0\">" +
            "<teiHeader><fileDesc><sourceDesc><msDesc>" +
            "<msIdentifier><idno>Test</idno></msIdentifier>" +
            "</msDesc></sourceDesc></fileDesc>" +
            $"<profileDesc><langUsage><language ident=\"{language}\"/></langUsage></profileDesc></teiHeader>" +
            "<text><body>" + body + "</body></text></TEI>");

        return path;
    }

    private static XDocument Load(string path) => XDocument.Load(path);
}
