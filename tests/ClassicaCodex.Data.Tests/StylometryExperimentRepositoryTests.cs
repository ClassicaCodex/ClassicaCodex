using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Saved runs from the validation bench.
///
/// WHY THIS TABLE EXISTS. A perturbation sweep is nineteen works at six levels
/// with a hundred synthetic mixtures each - several thousand runs of the Delta
/// engine and several minutes of wall clock. Until now every one of them
/// existed only as a CSV somebody remembered to export, and several conclusions
/// recorded in docs/stylometry-notes.md rest on runs that no longer exist.
///
/// The property these tests protect is that a stored experiment can be
/// REBUILT, not merely re-read. That means the seed, the exact pool, and every
/// preprocessing setting that makes two runs incomparable when they differ.
/// </summary>
[Collection("Database")]
public class StylometryExperimentRepositoryTests
{
    private static ExperimentDefinition Definition(
        int seed = 42, int iterations = 100, string? label = null) =>
        new(
            ExperimentKinds.Perturbation,
            "Euripides",
            "grc",
            "Aeschylus, Euripides, Sophocles",
            new[] { 11, 12, 13, 21, 22 },
            seed,
            iterations,
            ChunkSize: 2500,
            FeatureWordCount: 150,
            FoldAccents: true,
            AlgorithmVersion: 1,
            new Dictionary<string, string>
            {
                ["mode"] = "replace",
                ["levels"] = "0,0.01,0.05,0.2",
                ["donors"] = "Aeschylus, Sophocles"
            },
            new Dictionary<string, string>
            {
                ["detectionPower"] = "1%: AUC 0.51  5%: AUC 0.55  20%: AUC 0.71"
            },
            label);

    private static List<ExperimentRow> Rows(int count = 3) =>
        Enumerable.Range(0, count).Select(i => new ExperimentRow(
            i, 100 + i, $"Work {i}", "Aeschylus, Sophocles", 0.2 * i,
            0.097 - 0.01 * i, 0.0088, 0.097, 25 - i, 25, "Euripides", 25 - i, 5440)).ToList();

    /// <summary>
    /// A saved experiment comes back with every field it went in with. The
    /// seed, the pool and the settings are the ones that matter: without them
    /// the rows are numbers whose provenance has to be remembered.
    /// </summary>
    [Fact]
    public async Task AnExperimentIsStoredWithEverythingNeededToRebuildIt()
    {
        using var db = await TempDatabase.CreateAsync();
        var _repo = new StylometryExperimentRepository();

        var id = await _repo.SaveAsync(Definition(seed: 43, iterations: 50, label: "seed check"), Rows());

        var back = await _repo.GetDefinitionAsync(id);

        Assert.NotNull(back);
        Assert.Equal(43, back!.Seed);
        Assert.Equal(50, back.Iterations);
        Assert.Equal(2500, back.ChunkSize);
        Assert.Equal(150, back.FeatureWordCount);
        Assert.True(back.FoldAccents);
        Assert.Equal(new[] { 11, 12, 13, 21, 22 }, back.PoolWorkIds);
        Assert.Equal("replace", back.Parameters["mode"]);
        Assert.Equal("seed check", back.Label);
    }

    /// <summary>
    /// The pool is stored as work ids, not as a description.
    ///
    /// "Euripides against Aeschylus and Sophocles" stops meaning the same thing
    /// the moment a corpus is re-ingested and a work is added or a filter
    /// changes - and the pool moved the margin by more than twelvefold in
    /// testing. The ids are what makes a rebuild honest.
    /// </summary>
    [Fact]
    public async Task ThePoolIsStoredAsWorkIdsRatherThanADescription()
    {
        using var db = await TempDatabase.CreateAsync();
        var _repo = new StylometryExperimentRepository();

        var id = await _repo.SaveAsync(Definition(), Rows());

        var back = await _repo.GetDefinitionAsync(id);

        Assert.Equal(5, back!.PoolWorkIds.Count);
        Assert.Contains(21, back.PoolWorkIds);
    }

    /// <summary>
    /// Rows come back in order and at full precision. The table on screen is
    /// rounded for reading; a stored run carrying only the rounded numbers
    /// could not be re-analysed, and the detection power had to be recomputed
    /// from stored values once already after being calculated against the wrong
    /// scatter.
    /// </summary>
    [Fact]
    public async Task RowsRoundTripInOrderAndAtFullPrecision()
    {
        using var db = await TempDatabase.CreateAsync();
        var _repo = new StylometryExperimentRepository();

        var rows = Rows(5);
        var id = await _repo.SaveAsync(Definition(), rows);

        var back = await _repo.GetRowsAsync(id);

        Assert.Equal(5, back.Count);
        Assert.Equal(rows.Select(r => r.RowIndex), back.Select(r => r.RowIndex));
        Assert.Equal(rows[3].MeanMargin, back[3].MeanMargin, 12);
        Assert.Equal(rows[3].BaselineMargin, back[3].BaselineMargin, 12);
    }

    /// <summary>
    /// Newest first, because the list is read to find what was just run far
    /// more often than to find the first thing ever run.
    /// </summary>
    [Fact]
    public async Task ExperimentsAreListedNewestFirst()
    {
        using var db = await TempDatabase.CreateAsync();
        var _repo = new StylometryExperimentRepository();

        await _repo.SaveAsync(Definition(seed: 1, label: "first"), Rows());

        // A short wait, not a second: CreatedUtc is stored round-trip ("O"),
        // which keeps ticks, so two saves in the same millisecond are the only
        // ambiguous case. A full second here would add one to every test run
        // for nothing.
        await Task.Delay(20);

        await _repo.SaveAsync(Definition(seed: 2, label: "second"), Rows());

        var all = await _repo.GetAllAsync();

        Assert.Equal("second", all[0].Label);
        Assert.Equal("first", all[1].Label);
    }

    /// <summary>
    /// Filtering by kind, so the next experiment type is a new value rather
    /// than a new table and the pickers do not have to know about each other.
    /// </summary>
    [Fact]
    public async Task ExperimentsCanBeFilteredByKind()
    {
        using var db = await TempDatabase.CreateAsync();
        var _repo = new StylometryExperimentRepository();

        await _repo.SaveAsync(Definition(), Rows());
        await _repo.SaveAsync(Definition() with { Kind = ExperimentKinds.ParameterGrid }, Rows());

        Assert.Single(await _repo.GetAllAsync(ExperimentKinds.Perturbation));
        Assert.Single(await _repo.GetAllAsync(ExperimentKinds.ParameterGrid));
        Assert.Equal(2, (await _repo.GetAllAsync()).Count);
    }

    /// <summary>
    /// The profile key distinguishes runs that can be compared from runs that
    /// cannot - and includes the seed, because two sweeps at different seeds
    /// are the same experiment repeated and worth comparing, while two at the
    /// same seed are the same run twice.
    /// </summary>
    [Fact]
    public async Task TheProfileKeyDistinguishesSeedsAndSettings()
    {
        using var db = await TempDatabase.CreateAsync();
        var _repo = new StylometryExperimentRepository();

        await _repo.SaveAsync(Definition(seed: 42), Rows());
        await _repo.SaveAsync(Definition(seed: 43), Rows());

        var keys = (await _repo.GetAllAsync()).Select(e => e.ProfileKey).ToList();

        Assert.Equal(2, keys.Distinct().Count());
        Assert.All(keys, k => Assert.Contains("2500tok", k));
    }

    /// <summary>
    /// Deleting takes the rows with it. Left behind they would be orphans that
    /// no query reaches and that quietly grow the file.
    /// </summary>
    [Fact]
    public async Task DeletingAnExperimentRemovesItsRows()
    {
        using var db = await TempDatabase.CreateAsync();
        var _repo = new StylometryExperimentRepository();

        var id = await _repo.SaveAsync(Definition(), Rows(4));

        await _repo.DeleteAsync(id);

        Assert.Empty(await _repo.GetRowsAsync(id));
        Assert.Null(await _repo.GetDefinitionAsync(id));
    }

    /// <summary>
    /// An unknown experiment id is null rather than an exception - the picker
    /// can race a delete in another window.
    /// </summary>
    [Fact]
    public async Task AMissingExperimentReadsAsNull()
    {
        using var db = await TempDatabase.CreateAsync();
        var _repo = new StylometryExperimentRepository();

        Assert.Null(await _repo.GetDefinitionAsync(999999));
        Assert.Empty(await _repo.GetRowsAsync(999999));
    }

    /// <summary>
    /// The nearest-author COUNT survives a round trip, not just the name.
    ///
    /// v15 stored the name alone, so a reloaded experiment showed "Euripides"
    /// where the run had shown "Euripides (14/25)". That count is how Rhesus
    /// was seen to flip to Sophocles in fourteen mixtures of twenty-five, and
    /// how Heracleidae showed nine of twenty-five leaving Euripides at one
    /// percent injection - the first signal in the investigation to move at
    /// all, and exactly the thing a saved run exists to preserve.
    /// </summary>
    [Fact]
    public async Task TheNearestAuthorCountSurvivesAndNotJustTheName()
    {
        using var db = await TempDatabase.CreateAsync();
        var _repo = new StylometryExperimentRepository();

        var rows = new[]
        {
            new ExperimentRow(0, 1, "Rhesus", "Sophocles", 0.2,
                0.007, 0.0093, 0.026, 19, 25, "Sophocles", 14, 6248)
        };

        var id = await _repo.SaveAsync(Definition(), rows);
        var back = await _repo.GetRowsAsync(id);

        Assert.Equal("Sophocles", back[0].NearestAuthor);
        Assert.Equal(14, back[0].NearestCount);
    }
}
