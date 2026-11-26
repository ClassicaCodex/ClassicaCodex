using ClassicaCodex.Core.Quotations;

namespace ClassicaCodex.Data.Repositories;

/// <summary>An edition named by a quotation link, as this library holds it.</summary>
public sealed record QuotationEdition(int EditionId, int WorkId, string CtsUrn, string AuthorName, string WorkTitle, string? Language);

/// <summary>One end of a quotation link, found in this library.</summary>
/// <param name="FingerprintMatches">
/// Whether the passage's text is the one the link was found in. Where it is
/// not, the link's character positions do not fit it and the words have to be
/// found again.
/// </param>
public sealed record QuotationPassage(
    QuotationEdition Edition, long TextNodeId, string CitationRef, string? Milestone, string Text, bool FingerprintMatches);

/// <summary>
/// Finds the passages a quotation link names - by edition URN and citation
/// reference, never by node id; see <see cref="PassageKey"/>.
///
/// An edition is matched with or without its "urn:cts:namespace:" head, as
/// <see cref="QuotationIndex"/> compares them. Where an edition holds the same
/// reference twice - some do - the passage whose text has the link's
/// fingerprint is the one meant; failing that, the first.
/// </summary>
public sealed class QuotationRepository
{
    private readonly Dictionary<string, QuotationEdition?> _editions = new(StringComparer.Ordinal);

    /// <summary>The edition, or null when this library does not hold it. Remembered for the life of the repository.</summary>
    public async Task<QuotationEdition?> GetEditionAsync(string editionUrn, CancellationToken cancellationToken = default)
    {
        var key = QuotationIndex.EditionKey(editionUrn);
        if (_editions.TryGetValue(key, out var known)) return known;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT e.EditionId, e.WorkId, e.CtsUrn, a.Name, w.Title, e.Language
              FROM Editions e
              JOIN Works w ON w.WorkId = e.WorkId
              JOIN Authors a ON a.AuthorId = w.AuthorId
             WHERE e.CtsUrn = @Key OR e.CtsUrn LIKE 'urn:cts:%:' || @Pattern ESCAPE '\'
             ORDER BY CASE WHEN e.CtsUrn = @Key THEN 0 ELSE 1 END, e.EditionId
             LIMIT 1;";
        cmd.Parameters.AddWithValue("@Key", key);
        cmd.Parameters.AddWithValue("@Pattern", key.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_"));

        QuotationEdition? edition = null;
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                edition = new QuotationEdition(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5));
            }
        }

        _editions[key] = edition;
        return edition;
    }

    /// <summary>The passage a link names, or null when this library does not hold it.</summary>
    public async Task<QuotationPassage?> GetPassageAsync(PassageKey key, string fingerprint, CancellationToken cancellationToken = default)
    {
        if (await GetEditionAsync(key.EditionUrn, cancellationToken) is not { } edition) return null;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TextNodeId, CitationRef, Milestone, Text
              FROM TextNodes
             WHERE EditionId = @EditionId AND CitationRef = @Ref
             ORDER BY SortOrder, TextNodeId;";
        cmd.Parameters.AddWithValue("@EditionId", edition.EditionId);
        cmd.Parameters.AddWithValue("@Ref", key.CitationRef);

        QuotationPassage? first = null;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var text = reader.GetString(3);
            var matches = TextFingerprint.Of(text) == fingerprint;
            var passage = new QuotationPassage(
                edition, reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), text, matches);
            if (matches) return passage;
            first ??= passage;
        }

        return first;
    }
}
