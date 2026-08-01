namespace ClassicaCodex.UI;

/// <summary>
/// One selectable period in the search form's era filter.
/// </summary>
public sealed record SearchEra(string Label, int? StartYear, int EndYear)
{
    public override string ToString() => Label;
}

/// <summary>
/// The periods offered in the search form's era filter.
///
/// Broad on purpose. These are drawn against AuthorEraData, whose own
/// remarks are blunt that its dates are rough consensus estimates rather
/// than settled fact - so offering a filter finer than the underlying data
/// can support would imply a precision that isn't there. Century-scale
/// buckets are about as fine as the dates honestly justify.
///
/// An author counts as being in a period if their span overlaps it at all,
/// not if it falls entirely inside - a life that straddles a boundary
/// belongs to both sides, and the alternative would silently drop exactly
/// the transitional figures a reader is most likely to be looking for.
/// </summary>
public static class SearchEras
{
    public static IReadOnlyList<SearchEra> All { get; } = new[]
    {
        new SearchEra("(any era)", null, 0),
        new SearchEra("Archaic (before 500 BCE)", -3000, -500),
        new SearchEra("Classical (500-323 BCE)", -500, -323),
        new SearchEra("Hellenistic (323-31 BCE)", -323, -31),
        new SearchEra("Roman Imperial (31 BCE-300 CE)", -31, 300),
        new SearchEra("Late Antique (300-600 CE)", 300, 600),
        new SearchEra("Byzantine and later (after 600 CE)", 600, 3000)
    };
}
