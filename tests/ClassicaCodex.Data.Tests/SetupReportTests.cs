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
}
