using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A saved run's pool size is in works.
///
/// Runs saved with sampling on recorded their neighbour count plus one, and
/// every neighbour of a sampled run is a sample, so the pool came out as
/// samples: a Faust Book run against 71 works recorded 1,636. Migration 41
/// recounts those runs from their stored neighbours.
/// </summary>
[Collection("Database")]
public class StylometryRunPoolSizeTests
{
    private static StylometrySettings Settings(int chunk) =>
        new(150, FoldAccents: true, StripElisionMarks: true,
            StylometryRunRepository.CurrentAlgorithmVersion, chunk);

    [Fact]
    public async Task UpgradeRecountsASampledRunsPoolInWorksAndLeavesAWholeWorkRunAlone()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new StylometryRunRepository();
        var fingerprint = new[] { ("kai", 0.05) };

        // Five samples from three works, saved as before: five plus the target.
        var sampled = await repo.SaveRunAsync(
            1, 1, "Euripides", "Rhesus", "grc", Settings(3000), poolSize: 6, targetTokenCount: 5431,
            new[]
            {
                (2, "Euripides", "Alcestis [1/2]", 0.80),
                (3, "Sophocles", "Ajax [1/2]", 0.85),
                (2, "Euripides", "Alcestis [2/2]", 0.90),
                (4, "Aeschylus", "Persians [1/1]", 0.95),
                (3, "Sophocles", "Ajax [2/2]", 1.00)
            },
            fingerprint);

        // A whole-work run is not the migration's business, even one whose
        // neighbours repeat a work, as runs from before editions were merged do.
        var whole = await repo.SaveRunAsync(
            5, 5, "Sophocles", "Ajax", "grc", Settings(0), poolSize: 4, targetTokenCount: 9000,
            new[]
            {
                (6, "Sophocles", "Antigone", 0.70),
                (6, "Sophocles", "Antigone", 0.70),
                (7, "Euripides", "Medea", 0.90)
            },
            fingerprint);

        await db.ExecuteAsync("PRAGMA user_version = 40;");
        await SchemaInitializer.EnsureSchemaAsync();

        Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));

        var runs = (await repo.GetAllRunsAsync()).ToDictionary(r => r.RunId);
        Assert.Equal(4, runs[sampled].PoolSize);   // three works plus Rhesus
        Assert.Equal(4, runs[whole].PoolSize);

        // And running it again changes nothing.
        await db.ExecuteAsync("PRAGMA user_version = 40;");
        await SchemaInitializer.EnsureSchemaAsync();
        Assert.Equal(4, (await repo.GetAllRunsAsync()).Single(r => r.RunId == sampled).PoolSize);
    }
}
