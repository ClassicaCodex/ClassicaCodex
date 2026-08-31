using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Which of a setup step's two outcomes is worth interrupting somebody for.
///
/// They are not the same news. A skipped file is a work that is NOT in the
/// library. A recovered folder is a work that IS in the library, under a name
/// read from the text rather than from a catalogue. The first is worth a
/// dialog; the second is worth a line in the status bar and an entry in the
/// log.
///
/// Reporting both in one box was worse than either. Ingesting the Latin corpus
/// recovers 71 folders and skips 3 files, and a box headed "files skipped"
/// with the three skips first and seventy-one lines of recoveries after them
/// made a step that had just worked correctly read as a failure on a first
/// run. The Patrologia Latina, which leaves roughly one textgroup in eleven
/// unnamed, would have made that several hundred lines.
/// </summary>
public class SetupReportTests
{
    private static IngestOutcome Outcome(int skipped, int recovered) =>
        IngestOutcome.From(
            Enumerable.Range(0, skipped).Select(i => ($"C:\\corpus\\bad{i}.xml", "malformed")).ToList(),
            Enumerable.Range(0, recovered).Select(i => ($"C:\\corpus\\folder{i}", "read from the TEI headers")).ToList());

    [Fact]
    public void ACleanStepSaysSoAndNothingElse() =>
        Assert.Equal("Ancient Latin Texts is ready.",
            IngestOutcome.Clean.Describe("Ancient Latin Texts"));

    /// <summary>
    /// The Latin corpus exactly: both outcomes, both named, in one line.
    /// </summary>
    [Fact]
    public void BothOutcomesReachTheStatusLine() =>
        Assert.Equal("Ancient Latin Texts is ready - 3 file(s) skipped, 71 named from their texts.",
            Outcome(skipped: 3, recovered: 71).Describe("Ancient Latin Texts"));

    /// <summary>
    /// A recovery is not a skip and must not be described as one. This is the
    /// wording that made a working step look broken.
    /// </summary>
    [Fact]
    public void ARecoveryIsNotDescribedAsASkip()
    {
        var described = Outcome(skipped: 0, recovered: 812).Describe("Patrologia Latina");

        Assert.Contains("812 named from their texts", described);
        Assert.DoesNotContain("skipped", described);
    }

    /// <summary>
    /// Nothing lost means nothing to interrupt for, however many folders were
    /// named from their texts.
    /// </summary>
    [Fact]
    public void RecoveriesAloneDoNotWarrantADialog()
    {
        Assert.False(Outcome(skipped: 0, recovered: 812).HasSkippedFiles);
        Assert.True(Outcome(skipped: 0, recovered: 812).HasAnythingToReport);
    }

    /// <summary>
    /// And a skip always does, however few.
    /// </summary>
    [Fact]
    public void OneSkippedFileStillWarrantsADialog() =>
        Assert.True(Outcome(skipped: 1, recovered: 0).HasSkippedFiles);

    /// <summary>
    /// Combine keeps both lists, so a step running several passes reports the
    /// sum of them rather than the last one.
    /// </summary>
    [Fact]
    public void CombineKeepsBothKindsOfNews()
    {
        var combined = IngestOutcome.Combine(Outcome(2, 5), Outcome(1, 3), IngestOutcome.Clean);

        Assert.Equal(3, combined.SkippedCount);
        Assert.Equal(8, combined.RecoveredCount);
    }

    // ------------------------------- when a skip is worth interrupting for

    /// <summary>
    /// The Latin corpus exactly. Three files in 687 are malformed in the
    /// Perseus repository itself - the same three on every machine, every run,
    /// until somebody upstream fixes the XML. A modal about them is a modal
    /// about another project's typo, shown forever, and it is what made a step
    /// that worked read as a failure.
    /// </summary>
    [Fact]
    public void AHandfulOfBadFilesInALargeCorpusDoesNotInterrupt() =>
        Assert.False(Attempted(687, skipped: 3).SkipsAreWorthInterrupting);

    /// <summary>
    /// A clone that went wrong looks nothing like that - it fails in bulk, and
    /// re-running fixes it, so it is worth stopping for.
    /// </summary>
    [Theory]
    [InlineData(687, 200)]
    [InlineData(687, 40)]
    [InlineData(60, 6)]
    public void BulkFailureDoesInterrupt(int attempted, int skipped) =>
        Assert.True(Attempted(attempted, skipped).SkipsAreWorthInterrupting);

    /// <summary>
    /// A step that does not count its files keeps the old behaviour and always
    /// shows, because without a denominator there is no way to tell three bad
    /// files from three that were all there was.
    /// </summary>
    [Fact]
    public void AStepThatCountsNothingStillInterrupts() =>
        Assert.True(Outcome(skipped: 3, recovered: 0).SkipsAreWorthInterrupting);

    [Fact]
    public void NoSkipsNeverInterrupts() =>
        Assert.False(Attempted(687, skipped: 0).SkipsAreWorthInterrupting);

    /// <summary>
    /// The status line leads with what went in, not with what did not.
    /// </summary>
    [Fact]
    public void TheStatusLineLeadsWithWhatWasInstalled() =>
        Assert.Equal(
            "Ancient Latin Texts is ready - 684 of 687 files installed, 69 named from their texts.",
            new IngestOutcome(
                Enumerable.Range(0, 3).Select(i => ($"bad{i}.xml", "malformed")).ToList(),
                Enumerable.Range(0, 69).Select(i => ($"folder{i}", "read from the headers")).ToList(),
                687).Describe("Ancient Latin Texts"));

    [Fact]
    public void ACleanRunThatCountedFilesSaysHowMany() =>
        Assert.Equal("Ancient Greek Texts is ready - 1,612 files installed.",
            Attempted(1612, skipped: 0).Describe("Ancient Greek Texts"));

    private static IngestOutcome Attempted(int attempted, int skipped) =>
        new(Enumerable.Range(0, skipped).Select(i => ($"bad{i}.xml", "malformed")).ToList(),
            Array.Empty<(string, string)>(),
            attempted);
}
