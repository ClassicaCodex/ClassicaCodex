using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Working out how much of a work an AI translation actually covers, so a
/// part-finished one can say so in the edition header.
///
/// The subtle part isn't the arithmetic, it's knowing when the arithmetic
/// means anything. An ingested prose translation legitimately divides a
/// verse original far more coarsely, so counting its passages against the
/// original's scores it near zero - which is why the app only applies this
/// to AI translations, where one line per source line is a guarantee rather
/// than a hope. The last test here is the one that would catch that gate
/// being removed.
/// </summary>
[Collection("Database")]
public class TranslationCoverageTests
{
    [Fact]
    public async Task APartlyTranslatedEditionReportsWhatItCovers()
    {
        using var db = await TempDatabase.CreateAsync();

        var original = await db.SeedFullEditionAsync("iliad", "Homer", "greekLit", "Iliad", "Original", "grc");
        await db.InsertLinesAsync(original,
            ("1.1", "a"), ("1.2", "b"), ("1.3", "c"), ("1.4", "d"), ("1.5", "e"));

        var ai = await db.SeedSiblingEditionAsync("iliad", "iliad-ai", "Translation", "eng", "Gemini (AI-generated)");
        await db.InsertLinesAsync(ai, ("1.1", "A"), ("1.2", "B"));

        var coverage = await new TextNodeRepository()
            .GetTranslationCoverageAsync(ai, await db.WorkIdForAsync("iliad"));

        Assert.NotNull(coverage);
        Assert.Equal((2, 5), coverage!.Value);
    }

    [Fact]
    public async Task AFullyTranslatedEditionCoversEverything()
    {
        using var db = await TempDatabase.CreateAsync();

        var original = await db.SeedFullEditionAsync("iliad", "Homer", "greekLit", "Iliad", "Original", "grc");
        await db.InsertLinesAsync(original, ("1.1", "a"), ("1.2", "b"), ("1.3", "c"));

        var ai = await db.SeedSiblingEditionAsync("iliad", "iliad-ai", "Translation", "eng", "Gemini (AI-generated)");
        await db.InsertLinesAsync(ai, ("1.1", "A"), ("1.2", "B"), ("1.3", "C"));

        var coverage = await new TextNodeRepository()
            .GetTranslationCoverageAsync(ai, await db.WorkIdForAsync("iliad"));

        var (translated, total) = coverage!.Value;

        Assert.Equal(total, translated);
    }

    /// <summary>
    /// Which edition a translation was built from isn't recorded anywhere, so
    /// it's inferred as whichever original shares the most citation refs.
    /// Works with two originals of different lineation are real - Aesop's
    /// Fabulae has exactly this - and picking the wrong one would report a
    /// finished translation as badly incomplete.
    /// </summary>
    [Fact]
    public async Task TheSourceEditionIsInferredFromSharedCitationRefs()
    {
        using var db = await TempDatabase.CreateAsync();

        // The edition it was actually translated from.
        var actualSource = await db.SeedFullEditionAsync(
            "fab", "Aesop", "greekLit", "Fabulae", "Original", "grc");
        await db.InsertLinesAsync(actualSource, ("1.1", "a"), ("1.2", "b"), ("1.3", "c"));

        // A second original of the same work, lineated completely differently.
        var otherOriginal = await db.SeedSiblingEditionAsync("fab", "fab-alt", "Original", "grc");
        await db.InsertLinesAsync(otherOriginal,
            ("A", "x"), ("B", "y"), ("C", "z"), ("D", "w"), ("E", "v"), ("F", "u"));

        var ai = await db.SeedSiblingEditionAsync("fab", "fab-ai", "Translation", "eng", "Gemini (AI-generated)");
        await db.InsertLinesAsync(ai, ("1.1", "A"), ("1.2", "B"), ("1.3", "C"));

        var coverage = await new TextNodeRepository()
            .GetTranslationCoverageAsync(ai, await db.WorkIdForAsync("fab"));

        // Measured against the 3-line edition it matches, not the 6-line one
        // it shares nothing with.
        Assert.Equal((3, 3), coverage!.Value);
    }

    [Fact]
    public async Task AWorkWithNoOriginalReportsNothingRatherThanZero()
    {
        using var db = await TempDatabase.CreateAsync();

        var translationOnly = await db.SeedFullEditionAsync(
            "orphan", "Anon", "greekLit", "Fragment", "Translation", "eng");
        await db.InsertLinesAsync(translationOnly, ("1.1", "A"));

        var coverage = await new TextNodeRepository()
            .GetTranslationCoverageAsync(translationOnly, await db.WorkIdForAsync("orphan"));

        // Null, not (0, 0) - there is nothing to compare against, which is
        // different from having compared and found nothing.
        Assert.Null(coverage);
    }

    /// <summary>
    /// The reason MainForm gates this on IsAiGenerated.
    ///
    /// A published prose translation carries one passage where the verse
    /// original carries many, so by these numbers it looks almost entirely
    /// untranslated. It isn't - it's just divided differently. If this test
    /// ever starts looking like a bug report, the gate has been removed and
    /// every real translation in the library is being labelled INCOMPLETE.
    /// </summary>
    [Fact]
    public async Task AnIngestedProseTranslationScoresLowAndMustNotBeJudgedByThis()
    {
        using var db = await TempDatabase.CreateAsync();

        var original = await db.SeedFullEditionAsync("iliad", "Homer", "greekLit", "Iliad", "Original", "grc");
        await db.InsertLinesAsync(original,
            ("1.1", "a"), ("1.2", "b"), ("1.3", "c"), ("1.4", "d"), ("1.5", "e"));

        var prose = await db.SeedSiblingEditionAsync("iliad", "iliad-butler", "Translation", "eng", "Samuel Butler");
        await db.InsertLinesAsync(prose, ("1", "the whole opening as one paragraph"));

        var coverage = await new TextNodeRepository()
            .GetTranslationCoverageAsync(prose, await db.WorkIdForAsync("iliad"));

        var (translated, total) = coverage!.Value;

        Assert.Equal(0, translated);
        Assert.Equal(5, total);
    }
}
