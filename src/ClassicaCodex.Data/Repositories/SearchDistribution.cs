namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// How many lines a search matches, and which works they are in - all of
/// them, not the ones a capped result set had room for.
///
/// This is the shape a question like "who uses this word" actually needs, and
/// the reason it is computed rather than counted from the rows on screen is
/// in CountMatchesByWorkAsync. The short version: a filtered search stops at
/// its limit having ordered by author name, so what it stopped with is not a
/// sample of the matches but the front of the alphabet.
/// </summary>
public sealed record SearchDistribution(
    List<(int WorkId, string AuthorName, string WorkTitle, long Matches)> Works,
    long TotalMatches,
    bool ExactlyMatchesTheSearch)
{
    public static SearchDistribution Empty { get; } = new(new(), 0, true);

    /// <summary>Works containing at least one match.</summary>
    public int WorkCount => Works.Count;

    /// <summary>
    /// Distinct authors among them. Counted by name rather than by id
    /// because that is the unit a reader sees, and because one author can
    /// carry several ids across corpora.
    /// </summary>
    public int AuthorCount =>
        Works.Select(w => w.AuthorName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
}
