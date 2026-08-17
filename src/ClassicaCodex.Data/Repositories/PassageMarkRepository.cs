using ClassicaCodex.Core;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// Which passages of an edition carry an inquiry, a tag or a bookmark.
///
/// Answered for a whole edition in one query rather than per line. The reader
/// needs this for every line it draws, and a work can run to tens of thousands
/// of them - a lookup each would make opening a text slower than reading it.
///
/// Keyed on the citation reference, which is what all three of those tables
/// key on. That is the durable identity: node ids are handed out at ingest and
/// renumber completely when a corpus is rebuilt, but "Iliad 1.1 in this
/// edition" still names the same line afterwards, so the marks survive a
/// re-ingest exactly as the annotations they stand for do.
/// </summary>
public sealed class PassageMarkRepository
{
    /// <summary>
    /// Every marked passage in one edition. Passages with nothing recorded
    /// against them are absent rather than present with None, since the caller
    /// is asking "which of these are marked" and the answer is usually few.
    ///
    /// Takes both the edition's id and its CTS URN because the three tables
    /// disagree about which to use: tags and bookmarks are keyed on the id,
    /// while inquiries - written to outlive the library they were made in -
    /// are keyed on the URN.
    /// </summary>
    public async Task<Dictionary<string, PassageMarks>> GetForEditionAsync(
        int editionId, string editionCtsUrn, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, PassageMarks>(StringComparer.Ordinal);

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        // One round trip for all three. The flag values are this code's own
        // constants rather than anything read from the database, so they are
        // written into the SQL directly.
        cmd.CommandText = $@"
            SELECT CitationRef, {(int)PassageMarks.Inquiry} AS Mark
              FROM PassageInquiries WHERE EditionCtsUrn = @Urn
            UNION ALL
            SELECT CitationRef, {(int)PassageMarks.Tag}
              FROM PassageTags WHERE EditionId = @EditionId
            UNION ALL
            SELECT CitationRef, {(int)PassageMarks.Bookmark}
              FROM Bookmarks WHERE EditionId = @EditionId;";

        cmd.Parameters.AddWithValue("@Urn", editionCtsUrn);
        cmd.Parameters.AddWithValue("@EditionId", editionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var citationRef = reader.GetString(0);
            var mark = (PassageMarks)reader.GetInt32(1);

            // A passage tagged three times is still one hash, so these are
            // combined rather than counted.
            results[citationRef] = results.TryGetValue(citationRef, out var existing)
                ? existing | mark
                : mark;
        }

        return results;
    }
}
