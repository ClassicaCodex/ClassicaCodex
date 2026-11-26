namespace ClassicaCodex.Core.Stylometry;

/// <summary>
/// How many works, and how many samples, a Delta run compared its target
/// against - shared by both apps so the status line and the saved run count the
/// same thing the same way.
///
/// With sampling on, a neighbour is a sample, not a work: a long work appears
/// once for every sample it yields. The status line used to report the number
/// of neighbours as works, so Rhesus at 3,000-token samples was "compared
/// against 6981 other grc works", and a saved run recorded its pool as samples
/// plus one. Without sampling the two counts agree.
///
/// Works are counted as distinct work ids among the neighbours: the works
/// actually compared. A work shorter than one sample yields none, so it is not
/// counted, and the engine has already reduced editions to one per work.
/// </summary>
public static class DeltaRunCounts
{
    /// <summary>The number of distinct works among a run's neighbours.</summary>
    public static int WorksCompared(IEnumerable<int> neighbourWorkIds) =>
        neighbourWorkIds.Distinct().Count();

    /// <summary>
    /// What a saved run records as its pool size: the works compared plus the
    /// target. Without sampling this is the neighbour count plus one, as it
    /// always was; with it, the samples no longer inflate it.
    /// </summary>
    public static int PoolSize(IEnumerable<int> neighbourWorkIds) =>
        WorksCompared(neighbourWorkIds) + 1;

    /// <summary>
    /// The first line of the status after a run: "Compared Euripides, Rhesus
    /// against 6,981 samples from N other grc works." with sampling, and
    /// "Compared Euripides, Rhesus against N other grc works." without.
    /// </summary>
    public static string ComparedSentence(
        string authorName, string workTitle, string language,
        IReadOnlyCollection<int> neighbourWorkIds, int chunkSize)
    {
        var works = WorksCompared(neighbourWorkIds);
        var otherWorks = $"{works:N0} other {language} {(works == 1 ? "work" : "works")}";

        if (chunkSize <= 0)
            return $"Compared {authorName}, {workTitle} against {otherWorks}.";

        var samples = neighbourWorkIds.Count;
        return $"Compared {authorName}, {workTitle} against {samples:N0} {(samples == 1 ? "sample" : "samples")} " +
               $"from {otherWorks}.";
    }
}
