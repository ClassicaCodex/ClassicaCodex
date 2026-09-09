using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// How much of a translation an export includes.
///
/// Reported from actual use: exporting a single passage with the translation
/// put more than one translated passage in the file. It was worse than that -
/// both export layouts took the whole counterpart edition regardless of how
/// much had been asked for. The combined layout joined every counterpart
/// passage; the interleaved one swept up everything standing before the first
/// match and then everything still unemitted afterwards. Export one passage of
/// a fifty-passage work and you got its translation entire.
///
/// The behaviour is right for a whole-work export and only for that. A
/// translation carries material the original does not divide the same way, or
/// at all - an introduction, a cast list, a chapter heading over sections the
/// original numbers one by one - and a bilingual edition of the whole work
/// should contain it. A five-line extract should not.
/// </summary>
public class PassageExportScopeTests
{
    private static PassageAligner Translation() => new(new[]
    {
        Node("intro", "Introduction by the translator."),
        Node("1.1", "Sing, goddess, the wrath."),
        Node("1.2", "Of Peleus' son Achilles."),
        Node("1.3", "That brought countless ills."),
        Node("cast", "Persons of the drama."),
    });

    private static TextNode Node(string citation, string text) =>
        new() { EditionId = 1, CitationRef = citation, SortOrder = 0, Text = text, NodeKind = "line" };

    /// <summary>The report, reduced to one assertion.</summary>
    [Fact]
    public void ExportingOnePassageTakesOneTranslatedPassage()
    {
        var indices = PassageExportForm.CounterpartIndicesForExport(
            new[] { "1.2" }, Translation(), wholeWork: false);

        Assert.Single(indices);
    }

    [Fact]
    public void AndItIsTheRightOne()
    {
        var aligner = Translation();

        var indices = PassageExportForm.CounterpartIndicesForExport(
            new[] { "1.2" }, aligner, wholeWork: false);

        Assert.Equal("Of Peleus' son Achilles.", aligner.Ordered[indices[0]].Text);
    }

    /// <summary>
    /// The front matter is what made the old behaviour look deliberate. It is
    /// deliberate - for the whole work.
    /// </summary>
    [Fact]
    public void OnePassageDoesNotDragInTheTranslatorsIntroduction()
    {
        var aligner = Translation();

        var texts = PassageExportForm
            .CounterpartIndicesForExport(new[] { "1.2" }, aligner, wholeWork: false)
            .Select(i => aligner.Ordered[i].Text)
            .ToList();

        Assert.DoesNotContain("Introduction by the translator.", texts);
        Assert.DoesNotContain("Persons of the drama.", texts);
    }

    [Fact]
    public void AWholeWorkExportStillTakesEverything()
    {
        var aligner = Translation();

        var indices = PassageExportForm.CounterpartIndicesForExport(
            new[] { "1.1", "1.2", "1.3" }, aligner, wholeWork: true);

        Assert.Equal(aligner.Ordered.Count, indices.Count);
        var texts = indices.Select(i => aligner.Ordered[i].Text).ToList();
        Assert.Contains("Introduction by the translator.", texts);
        Assert.Contains("Persons of the drama.", texts);
    }

    [Fact]
    public void ARangeTakesItsOwnPassagesAndNoOthers()
    {
        var aligner = Translation();

        var texts = PassageExportForm
            .CounterpartIndicesForExport(new[] { "1.1", "1.2" }, aligner, wholeWork: false)
            .Select(i => aligner.Ordered[i].Text)
            .ToList();

        Assert.Equal(2, texts.Count);
        Assert.Contains("Sing, goddess, the wrath.", texts);
        Assert.Contains("Of Peleus' son Achilles.", texts);
    }

    /// <summary>
    /// Reading order, not the order the exported lines happened to resolve in.
    /// </summary>
    [Fact]
    public void CounterpartsComeBackInReadingOrder()
    {
        var indices = PassageExportForm.CounterpartIndicesForExport(
            new[] { "1.3", "1.1" }, Translation(), wholeWork: false);

        Assert.Equal(indices.OrderBy(i => i), indices);
    }

    /// <summary>
    /// A coarse original reference against a finer translation legitimately
    /// resolves to several passages - that is the alignment working, not the
    /// bug. It has to keep working.
    /// </summary>
    [Fact]
    public void ACoarseReferenceStillGathersTheFinerPassagesBeneathIt()
    {
        var aligner = new PassageAligner(new[]
        {
            Node("intro", "Introduction."),
            Node("1.2.1", "First half."),
            Node("1.2.2", "Second half."),
            Node("9.9", "Somewhere else entirely."),
        });

        var texts = PassageExportForm
            .CounterpartIndicesForExport(new[] { "1.2" }, aligner, wholeWork: false)
            .Select(i => aligner.Ordered[i].Text)
            .ToList();

        Assert.Equal(2, texts.Count);
        Assert.Contains("First half.", texts);
        Assert.Contains("Second half.", texts);
        Assert.DoesNotContain("Somewhere else entirely.", texts);
    }

    /// <summary>
    /// A line with no counterpart contributes nothing rather than falling back
    /// to something nearby - showing the original alone is honest.
    /// </summary>
    [Fact]
    public void AnUnpairedLineContributesNothing()
    {
        var indices = PassageExportForm.CounterpartIndicesForExport(
            new[] { "77.77" }, Translation(), wholeWork: false);

        Assert.Empty(indices);
    }

    /// <summary>
    /// Two exported lines resolving to one coarse translation passage - an
    /// English chapter over several numbered sections - include it once.
    /// </summary>
    [Fact]
    public void ACounterpartSharedByTwoLinesAppearsOnce()
    {
        var aligner = new PassageAligner(new[] { Node("1", "The whole first chapter, in English.") });

        var indices = PassageExportForm.CounterpartIndicesForExport(
            new[] { "1.1", "1.2", "1.3" }, aligner, wholeWork: false);

        Assert.Single(indices);
    }
}
