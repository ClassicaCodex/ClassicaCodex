using ClassicaCodex.Core;
using ClassicaCodex.Core.Reactions;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// Turns a citation written the way a classicist writes it into a passage in
/// THIS reader's library.
///
/// <b>Why this is not one line of SQL.</b> A pack says "1.22.1", meaning
/// Thucydides book 1, chapter 22, section 1. Three separate things about how
/// Perseus is stored get in the way, and all three were found by running the
/// obvious query against a real 2.9 GB library and reading what came back.
///
/// <b>The reference usually is not the reference.</b> Perseus writes the whole
/// edition URN into the div's @n for 97.6% of the corpus, so the Iliad's first
/// line is stored as
/// "urn:cts:greekLit:tlg0012.tlg001.perseus-grc2.1.1" and not as "1.1" - while
/// the Aeneid, ingested from a different source, really does store "1.1". A
/// pack has to be able to say "1.1" and mean both, hence the suffix match.
///
/// <b>Prose is stored a line at a time inside its sections.</b> Thucydides
/// carries a fourth level the citation scheme does not have: 1.22.1 exists in
/// the file only as 1.22.1.1. A citation that names a section therefore has to
/// match as a dot-boundary prefix and land on the first line under it.
///
/// <b>The original has to win outright.</b> Without that, the single most
/// interesting thing this finds is wrong: because the Greek of Thucydides
/// carries four levels and a nineteenth-century Italian translation in the
/// same library carries three, "1.22.1" matched the Italian exactly and the
/// Greek not at all, and every Thucydides link in the feature would have
/// opened a translation nobody asked for. Ordering by Kind first is what fixes
/// it, and it is not an optimisation.
///
/// Unresolved is an ordinary answer, not a failure. Which corpora a reader has
/// installed varies, and a passage link that cannot be followed is simply not
/// shown as a link.
/// </summary>
public class ReactionPassageRepository
{
    /// <summary>What a resolved citation gives the window.</summary>
    public sealed record Passage(int WorkId, long TextNodeId, string CitationRef, string Text);

    /// <summary>
    /// A citation inside one known work, addressed by the work's CTS URN -
    /// used for the passage a turn points at, where the work is the one the
    /// reader right-clicked and its URN is already in hand.
    /// </summary>
    public async Task<Passage?> FindInWorkAsync(
        string workCtsUrn, string citationRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workCtsUrn) || string.IsNullOrWhiteSpace(citationRef)) return null;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;

        cmd.CommandText = @"
            SELECT w.WorkId, tn.TextNodeId, tn.CitationRef, tn.Text
            FROM TextNodes tn
            JOIN Editions e ON tn.EditionId = e.EditionId
            JOIN Works w ON e.WorkId = w.WorkId
            WHERE w.CtsUrn = @Urn
              AND (tn.CitationRef = @Ref
                   OR tn.CitationRef LIKE @Suffix ESCAPE '\'
                   OR tn.CitationRef LIKE @Prefix ESCAPE '\'
                   OR tn.CitationRef LIKE @SuffixPrefix ESCAPE '\')
              AND COALESCE(tn.NodeKind, 'line') = 'line'
            ORDER BY CASE WHEN e.Kind = 'Original' THEN 0 ELSE 1 END,
                     CASE WHEN tn.CitationRef = @Ref
                            OR tn.CitationRef LIKE @Suffix ESCAPE '\' THEN 0 ELSE 1 END,
                     tn.SortOrder, tn.TextNodeId
            LIMIT 1;";

        AddCitationParameters(cmd, workCtsUrn, citationRef);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new Passage(
            reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3));
    }

    /// <summary>
    /// A citation in a work named only by author and title keys - used for the
    /// "where this view comes from" reference under a historical critic's
    /// turn, which points at a different work entirely and often at one the
    /// reader may not have.
    ///
    /// The author filter runs in SQL because it is what narrows millions of
    /// rows to a handful; the decision about which candidate is really meant
    /// is made by <see cref="WorkReference.Matches"/>, so there is exactly one
    /// copy of that rule and the tests exercise the same one the application
    /// uses.
    /// </summary>
    public async Task<Passage?> FindByWorkReferenceAsync(
        WorkReference reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference.CitationRef)) return null;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        var candidates = new List<(string Urn, string Author, string Title)>();

        await using (var works = conn.CreateCommand())
        {
            works.CommandTimeout = 30;
            works.CommandText = @"
                SELECT w.CtsUrn, a.Name, w.Title
                FROM Works w JOIN Authors a ON a.AuthorId = w.AuthorId
                WHERE a.Name LIKE @Author
                LIMIT 200;";
            works.Parameters.AddWithValue("@Author", "%" + reference.AuthorKey + "%");

            await using var reader = await works.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        foreach (var candidate in candidates)
        {
            if (!reference.Matches(candidate.Author, candidate.Title)) continue;

            var passage = await FindInWorkAsync(candidate.Urn, reference.CitationRef!, cancellationToken);
            if (passage != null) return passage;
        }

        return null;
    }

    private static void AddCitationParameters(
        Microsoft.Data.Sqlite.SqliteCommand cmd, string workCtsUrn, string citationRef)
    {
        // LIKE is being used with data that could contain its wildcards, so
        // they are escaped. A citation reference has no business containing an
        // underscore or a percent sign, and one that does must match nothing
        // rather than match everything.
        var safe = citationRef.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        cmd.Parameters.AddWithValue("@Urn", workCtsUrn);
        cmd.Parameters.AddWithValue("@Ref", citationRef);
        cmd.Parameters.AddWithValue("@Suffix", "%." + safe);
        cmd.Parameters.AddWithValue("@Prefix", safe + ".%");
        cmd.Parameters.AddWithValue("@SuffixPrefix", "%." + safe + ".%");
    }
}
