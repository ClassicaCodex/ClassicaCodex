using ClassicaCodex.Core;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// One work held twice, and the two editions of it.
///
/// Left and right are only an order, fixed by collection and then by CTS URN so
/// the same pair always presents the same way round - a collation that swapped
/// sides between openings would make the reader re-learn which column is which
/// every time.
/// </summary>
public sealed record CollationPair(
    int WorkId,
    string AuthorName,
    string WorkTitle,
    int LeftEditionId,
    string LeftEditionUrn,
    string LeftCollection,
    int RightEditionId,
    string RightEditionUrn,
    string RightCollection,
    string? Language)
{
    /// <summary>
    /// The CTS version identifier - "perseus-grc2", "1st1K-grc1", "opp-lat3" -
    /// which is what tells two editions of one work apart when they came from
    /// the same collection, and the only thing that does.
    /// </summary>
    public string LeftVersion => Version(LeftEditionUrn);

    /// <summary>See <see cref="LeftVersion"/>.</summary>
    public string RightVersion => Version(RightEditionUrn);

    /// <summary>True when both editions came from the same collection.</summary>
    public bool WithinOneCollection =>
        string.Equals(LeftCollection, RightCollection, StringComparison.OrdinalIgnoreCase);

    private static string Version(string editionUrn) =>
        editionUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
        ?? editionUrn;

    public override string ToString() =>
        $"{AuthorName} - {WorkTitle}  ({LeftVersion} / {RightVersion})";
}

/// <summary>
/// Finds the works this library holds twice, and reads them out for comparison.
///
/// Holding several collections means holding the same work more than once, and
/// that overlap is the point rather than a problem: two independent editions of
/// one text are the raw material of a collation, and no single collection can
/// provide them.
/// </summary>
public sealed class CollationRepository
{
    /// <summary>
    /// Every pairing of original-language editions of one work.
    ///
    /// Every pairing, not every pair of collections: a work with three editions
    /// yields all three combinations, and two editions from the SAME collection
    /// are a pair like any other. Perseus alone carries two editions of Ajax and
    /// of a dozen Plutarch works, and those are two independent printings of one
    /// text - which is the whole definition of something worth collating.
    /// Requiring the collections to differ excluded them for no reason beyond
    /// how the feature was first described.
    ///
    /// Originals only. A work's translations are different texts by different
    /// hands, and lining two of those up would produce a page of differences
    /// at every line that says nothing about either.
    ///
    /// Whether a pair can actually be collated is not decided here - two
    /// editions can divide a work so differently that their references do not
    /// mean the same passages, and that only becomes visible once the passages
    /// are compared. This returns the candidates; Collation judges them.
    /// </summary>
    public async Task<List<CollationPair>> FindPairsAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<CollationPair>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT w.WorkId, a.Name, w.Title,
                   l.EditionId, l.CtsUrn, l.Collection,
                   r.EditionId, r.CtsUrn, r.Collection,
                   COALESCE(l.Language, r.Language)
            FROM Editions l
            JOIN Editions r ON r.WorkId = l.WorkId
                 -- Each unordered pairing once, in a fixed order: by collection,
                 -- then by URN so two editions from one collection still get a
                 -- stable side each.
                 AND (l.Collection < r.Collection
                      OR (l.Collection = r.Collection AND l.CtsUrn < r.CtsUrn))
            JOIN Works w    ON w.WorkId = l.WorkId
            JOIN Authors a  ON a.AuthorId = w.AuthorId
            WHERE l.Kind = 'Original' AND r.Kind = 'Original'
              AND l.Collection IS NOT NULL AND r.Collection IS NOT NULL
            ORDER BY a.Name, w.Title, l.CtsUrn, r.CtsUrn;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new CollationPair(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt32(3), reader.GetString(4), reader.GetString(5),
                reader.GetInt32(6), reader.GetString(7), reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return results;
    }

    /// <summary>
    /// One edition's passages, in reading order, keyed by the passage
    /// reference the two editions have in common.
    ///
    /// The stored citation reference cannot be compared directly: it carries
    /// the edition's own version identifier, which differs between editions by
    /// design, so two printings of the same line never match on it.
    /// PassageAligner.ExtractPassageRef is what the rest of the app already
    /// uses to cut that off, and using anything else here would be a second
    /// answer to a question that has one.
    /// </summary>
    public async Task<List<(string PassageRef, string Text)>> GetPassagesAsync(
        int editionId, CancellationToken cancellationToken = default)
    {
        var results = new List<(string, string)>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT CitationRef, Text FROM TextNodes
            WHERE EditionId = @EditionId AND TRIM(COALESCE(Text, '')) <> ''
            ORDER BY SortOrder;";
        cmd.Parameters.AddWithValue("@EditionId", editionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((PassageAligner.ExtractPassageRef(reader.GetString(0)), reader.GetString(1)));
        }

        return results;
    }

    /// <summary>Reads both sides and compares them.</summary>
    public async Task<CollationResult> CollateAsync(
        CollationPair pair, CancellationToken cancellationToken = default)
    {
        var left = await GetPassagesAsync(pair.LeftEditionId, cancellationToken);
        var right = await GetPassagesAsync(pair.RightEditionId, cancellationToken);

        return Collation.Compare(left, right, pair.Language);
    }
}
