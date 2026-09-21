using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What a batch re-run is allowed to destroy.
///
/// A saved stylometry run is user-authored work in the sense that matters: it
/// costs a full corpus pass to produce, there is no command anywhere in the UI
/// to delete one, and nothing in the application can rebuild it from anything
/// else. The only thing that removes one is the clear a batch performs before
/// re-running, and until 3.10.1 that clear was scoped to the language and the
/// settings profile and not to the author.
///
/// So: batch Sophocles, get seven runs. Batch Euripides afterwards at the same
/// settings - which is the default, because the form resets to its constants
/// every time it opens - and the seven Sophocles runs were deleted, their
/// results and features cascading after them. No count was reported and there
/// was no undo. Comparing authors across saved runs is the entire reason the
/// Compare Saved Runs window exists, so the bug fired the first time anyone
/// used the feature the way it was meant to be used.
///
/// These tests pin the scope on all three axes at once, because the fix is one
/// clause in a WHERE and the failure is silent: nothing throws, nothing warns,
/// the batch reports success, and the loss is only visible later when a chart
/// has fewer authors in it than it should.
/// </summary>
[Collection("Database")]
public class StylometryRunDeletionTests
{
    private static StylometrySettings Settings(int features = 150, int chunk = 3000) =>
        new(features, FoldAccents: true, StripElisionMarks: true,
            StylometryRunRepository.CurrentAlgorithmVersion, chunk);

    /// <summary>
    /// One saved run, with the minimum a run needs to exist. The neighbour list
    /// and fingerprint are not what these tests are about, but they are written
    /// anyway so the cascade has something to cascade to.
    /// </summary>
    private static Task<long> SaveAsync(
        StylometryRunRepository repo, string author, string title, string language,
        StylometrySettings settings) =>
        repo.SaveRunAsync(
            targetWorkId: Math.Abs(HashCode.Combine(author, title)) % 100000,
            targetEditionId: 1,
            targetAuthorName: author,
            targetWorkTitle: title,
            language: language,
            settings: settings,
            poolSize: 3,
            targetTokenCount: 5000,
            orderedResults: new[] { (2, "Someone Else", "Another Work", 0.42) },
            fingerprint: new[] { ("kai", 0.05) });

    private static async Task<List<(string Author, string Title)>> RemainingAsync(
        StylometryRunRepository repo) =>
        (await repo.GetAllRunsAsync())
            .Select(r => (r.TargetAuthorName, r.TargetWorkTitle))
            .OrderBy(r => r.TargetAuthorName, StringComparer.Ordinal)
            .ThenBy(r => r.TargetWorkTitle, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The reported bug, as a rule: another author's runs survive.
    /// </summary>
    [Fact]
    public async Task BatchingOneAuthorLeavesAnotherAuthorsRunsAlone()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new StylometryRunRepository();
        var settings = Settings();

        await SaveAsync(repo, "Sophocles", "Ajax", "grc", settings);
        await SaveAsync(repo, "Sophocles", "Antigone", "grc", settings);
        await SaveAsync(repo, "Euripides", "Medea", "grc", settings);

        await repo.DeleteRunsForAuthorAndSettingsAsync("grc", "Euripides", settings);

        var remaining = await RemainingAsync(repo);

        Assert.Equal(
            new[] { ("Sophocles", "Ajax"), ("Sophocles", "Antigone") },
            remaining);
    }

    /// <summary>
    /// And that it still does the job it was written for: re-batching the same
    /// author replaces that author's runs rather than accumulating a second
    /// copy of each, which would halve the apparent variance of the reference
    /// distribution they are averaged into.
    /// </summary>
    [Fact]
    public async Task BatchingTheSameAuthorTwiceDoesNotAccumulateDuplicates()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new StylometryRunRepository();
        var settings = Settings();

        await SaveAsync(repo, "Sophocles", "Ajax", "grc", settings);
        await SaveAsync(repo, "Sophocles", "Antigone", "grc", settings);

        await repo.DeleteRunsForAuthorAndSettingsAsync("grc", "Sophocles", settings);
        await SaveAsync(repo, "Sophocles", "Ajax", "grc", settings);
        await SaveAsync(repo, "Sophocles", "Antigone", "grc", settings);

        Assert.Equal(
            new[] { ("Sophocles", "Ajax"), ("Sophocles", "Antigone") },
            await RemainingAsync(repo));
    }

    /// <summary>
    /// The settings scope was never the broken axis, and it has to stay
    /// unbroken: runs at a different profile are not comparable with the ones
    /// being replaced, so replacing them would destroy work for no benefit.
    /// </summary>
    [Fact]
    public async Task RunsAtAnotherSettingsProfileSurvive()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new StylometryRunRepository();

        await SaveAsync(repo, "Sophocles", "Ajax", "grc", Settings(features: 150));
        await SaveAsync(repo, "Sophocles", "Ajax", "grc", Settings(features: 300));

        await repo.DeleteRunsForAuthorAndSettingsAsync("grc", "Sophocles", Settings(features: 150));

        var remaining = await repo.GetAllRunsAsync();
        Assert.Single(remaining);
        Assert.Equal(300, Assert.Single(remaining).Settings.FeatureWordCount);
    }

    /// <summary>
    /// The same author's name in the other language is a different corpus and a
    /// different run. This is the axis that was always right; it is here so
    /// that a future fix to one clause cannot quietly drop another.
    /// </summary>
    [Fact]
    public async Task RunsInAnotherLanguageSurvive()
    {
        using var db = await TempDatabase.CreateAsync();
        var repo = new StylometryRunRepository();
        var settings = Settings();

        await SaveAsync(repo, "Seneca", "Medea", "lat", settings);
        await SaveAsync(repo, "Seneca", "Medea", "grc", settings);

        await repo.DeleteRunsForAuthorAndSettingsAsync("grc", "Seneca", settings);

        var remaining = await repo.GetAllRunsAsync();
        Assert.Equal("lat", Assert.Single(remaining).Language);
    }
}
