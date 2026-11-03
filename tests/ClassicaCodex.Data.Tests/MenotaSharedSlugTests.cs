using System.Xml.Linq;
using ClassicaCodex.Data;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Two works in one manuscript with the same heading.
///
/// A Menota work's URN is made from its title, and the planner takes titles
/// from headings, which repeat: AM 677 4to opens seven homilies with the
/// rubric "Gregorius". All seven minted urn:menota:anonymous:gregorius:am-677-4to,
/// and the ingest cleared and refilled that one edition once per work, so the
/// library held Homily 33 and nothing of the other six. The same happened to
/// 158 prayers in Holm A 80 and four chapters of Wolf Aug 9 10 4to - 36,232
/// words in all, gone without a message.
///
/// Plans confirmed before the fix still carry the shared slugs, so the ingest
/// has to number them itself, not only the planner.
/// </summary>
[Collection("Database")]
public class MenotaSharedSlugTests : IDisposable
{
    private static readonly XNamespace Tei = MenotaXmlLoader.Tei;

    private readonly string _folder;

    public MenotaSharedSlugTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "cc-menota-slugs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Two catalogue entries that match no heading, and three divisions - two
    /// of them headed "Gregorius" - which is the shape that sends the planner
    /// to one work per division, titled from its heading.
    /// </summary>
    private string WriteManuscript()
    {
        static string Head(string text) => $"<head><w><me:dipl>{text}</me:dipl></w></head>";

        static string Words(params string[] words) =>
            "<p>" + string.Concat(words.Select(w => $"<w><me:dipl>{w}</me:dipl></w>")) + "</p>";

        var path = Path.Combine(_folder, "AM-677-4to.xml");
        File.WriteAllText(path,
            "<TEI xmlns=\"http://www.tei-c.org/ns/1.0\" xmlns:me=\"http://www.menota.org/ns/1.0\">" +
            "<teiHeader><fileDesc><sourceDesc><msDesc>" +
            "<msIdentifier><idno>AM 677 4to</idno></msIdentifier>" +
            "<msContents>" +
            "<msItem n=\"1\"><title>Homily 33</title></msItem>" +
            "<msItem n=\"2\"><title>Homily 34</title></msItem>" +
            "</msContents></msDesc></sourceDesc></fileDesc></teiHeader>" +
            "<text><body>" +
            "<div n=\"H33hom\">" + Head("Gregorius") + Words("Maria", "Magdalena") + "</div>" +
            "<div n=\"H34hom\">" + Head("Gregorius") + Words("drachma", "perdita", "angeli") + "</div>" +
            "<div n=\"H35gos\">" + Head("Lucam") + Words("in", "illo", "tempore") + "</div>" +
            "</body></text></TEI>");

        return path;
    }

    private static MenotaWorkPlan Work(string title, string divPath, string author = "") => new()
    {
        Title = title,
        Author = author,
        UrnSlug = MenotaIngestPlanner.Slug(title),
        DivPaths = { divPath }
    };

    /// <summary>
    /// The plan a reader confirmed before the fix: two rows, one slug. Both
    /// homilies have to reach the library, and the one the library already
    /// held under the shared URN - the last - has to stay there, so that
    /// whatever points at that URN still finds the same text.
    /// </summary>
    [Fact]
    public async Task TwoDivisionsWithOneHeadingBothSurviveAPlanConfirmedBeforeTheFix()
    {
        using var db = await TempDatabase.CreateAsync();
        var path = WriteManuscript();

        new MenotaIngestPlan
        {
            ManuscriptId = "AM 677 4to",
            Language = "isl",
            Confirmed = true,
            Works = { Work("Gregorius", "0"), Work("Gregorius", "1"), Work("Lucam", "2") }
        }.Save(MenotaIngestPlan.PlanPathFor(path));

        var service = new MenotaIngestService();
        await service.IngestAsync(_folder);

        Assert.Empty(service.FailedFiles);

        Assert.Equal("drachma perdita angeli",
            await TextOfAsync(db, "urn:menota:anonymous:gregorius:am-677-4to"));
        Assert.Equal("Maria Magdalena",
            await TextOfAsync(db, "urn:menota:anonymous:gregorius-1:am-677-4to"));
        Assert.Equal("in illo tempore",
            await TextOfAsync(db, "urn:menota:anonymous:lucam:am-677-4to"));

        // Said, rather than done quietly.
        var (file, note) = Assert.Single(service.SharedSlugs);
        Assert.Equal(path, file);
        Assert.Contains("gregorius-1", note);

        // The confirmed file is the reader's, and is left as they saved it.
        var onDisk = MenotaIngestPlan.Load(MenotaIngestPlan.PlanPathFor(path))!;
        Assert.All(onDisk.Works.Take(2), w => Assert.Equal("gregorius", w.UrnSlug));

        // A second import finds the same URNs and removes nothing.
        var again = new MenotaIngestService();
        await again.IngestAsync(_folder);
        Assert.Equal(0, again.RemovedEditions);
        Assert.Equal(3, await db.ScalarAsync<long>(
            "SELECT COUNT(*) FROM Editions WHERE CtsUrn LIKE 'urn:menota:%';"));
    }

    /// <summary>
    /// A fresh proposal numbers the repeated heading itself, by the same rule,
    /// so a library planned today holds what a library planned before the fix
    /// does under each URN.
    /// </summary>
    [Fact]
    public async Task ThePlannerProposesAUrnForEachOfTwoWorksWithOneHeading()
    {
        var path = WriteManuscript();
        var load = MenotaXmlLoader.Load(path, new Dictionary<string, string>());
        Assert.True(load.Ok, load.Error);

        var plan = new MenotaIngestPlanner().Plan(path, load.Document!);

        Assert.Equal(new[] { "Gregorius", "Gregorius", "Lucam" }, plan.Works.Select(w => w.Title));
        Assert.Equal(new[] { "gregorius-1", "gregorius", "lucam" }, plan.Works.Select(w => w.UrnSlug));
        Assert.Contains(plan.Notes, n => n.Contains("gregorius-1"));

        // Confirmed as proposed, it imports with nothing left to number.
        using var db = await TempDatabase.CreateAsync();
        plan.Confirmed = true;
        plan.Save(MenotaIngestPlan.PlanPathFor(path));

        var service = new MenotaIngestService();
        await service.IngestAsync(_folder);

        Assert.Empty(service.FailedFiles);
        Assert.Empty(service.SharedSlugs);
        Assert.Equal("Maria Magdalena",
            await TextOfAsync(db, "urn:menota:anonymous:gregorius-1:am-677-4to"));
    }

    [Fact]
    public void WorksBySeveralAuthorsDoNotShareAUrnAndAreLeftAlone()
    {
        var plan = new MenotaIngestPlan
        {
            Works = { Work("Prologus", "0", "Alcuin"), Work("Prologus", "1") }
        };

        Assert.Empty(plan.MakeSlugsUnique());
        Assert.All(plan.Works, w => Assert.Equal("prologus", w.UrnSlug));
    }

    /// <summary>
    /// An unticked row mints nothing, so it does not push a ticked one off the
    /// URN - which is also what the ingest did before the fix.
    /// </summary>
    [Fact]
    public void AnExcludedWorkNeitherTakesNorKeepsASlug()
    {
        var plan = new MenotaIngestPlan
        {
            Works = { Work("Gregorius", "0"), Work("Gregorius", "1"), Work("Gregorius", "2") }
        };
        plan.Works[2].Include = false;

        var shared = Assert.Single(plan.MakeSlugsUnique());

        Assert.Equal(2, shared.Works);
        Assert.Equal(new[] { "gregorius-1", "gregorius", "gregorius" }, plan.Works.Select(w => w.UrnSlug));
    }

    /// <summary>
    /// A number the manuscript already uses for a title of its own is passed
    /// over, or the fix would make the collision it exists to remove.
    /// </summary>
    [Fact]
    public void ANumberAnotherTitleAlreadyHasIsSkipped()
    {
        var plan = new MenotaIngestPlan
        {
            Works = { Work("Gregorius", "0"), Work("Gregorius 1", "1"), Work("Gregorius", "2"), Work("Gregorius", "3") }
        };

        plan.MakeSlugsUnique();

        Assert.Equal(
            new[] { "gregorius-2", "gregorius-1", "gregorius-3", "gregorius" },
            plan.Works.Select(w => w.UrnSlug));
    }

    private static async Task<string?> TextOfAsync(TempDatabase db, string editionUrn) =>
        await db.ScalarStringAsync(
            "SELECT group_concat(t.Text, ' ') FROM (SELECT Text FROM TextNodes " +
            "WHERE EditionId = (SELECT EditionId FROM Editions WHERE CtsUrn = '" + editionUrn + "') " +
            "ORDER BY SortOrder) t;");
}
