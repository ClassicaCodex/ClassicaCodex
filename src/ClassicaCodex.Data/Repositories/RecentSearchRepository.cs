using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// The searches most recently run, recorded automatically.
///
/// There is deliberately no save step and no delete. An earlier version of
/// this asked people to name and file their searches, which put the work of
/// remembering onto the person doing the reading - the list is more useful
/// when it just reflects what they have actually been doing.
/// </summary>
public class RecentSearchRepository
{
    /// <summary>How many are kept. Ten is about a session's worth of work.</summary>
    public const int MaxRecent = 10;

    /// <summary>
    /// Records a search that was just run, then trims the list back to
    /// MaxRecent.
    ///
    /// Re-running something already in the list moves it to the top rather
    /// than adding a second copy - that is what the unique constraint on the
    /// description is for. Without it, an hour spent going back and forth
    /// between two queries would push everything else out and leave ten
    /// near-identical rows.
    /// </summary>
    public async Task RecordAsync(RecentSearch search, CancellationToken cancellationToken = default)
    {
        search.CreatedAt = DateTime.UtcNow;
        await UpsertAsync(search, cancellationToken);
        await TrimAsync(cancellationToken);
    }

    /// <summary>
    /// Drops everything past the most recent MaxRecent. Ordered by the
    /// stored timestamp, which is written in round-trip format and so sorts
    /// correctly as plain text - no date parsing needed in SQL.
    /// </summary>
    private async Task TrimAsync(CancellationToken cancellationToken)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM RecentSearches
            WHERE RecentSearchId NOT IN (
                SELECT RecentSearchId FROM RecentSearches
                ORDER BY CreatedAt DESC
                LIMIT @Keep);";
        cmd.Parameters.AddWithValue("@Keep", MaxRecent);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Writes the entry, replacing any row already using this description.
    ///
    /// CreatedAt is among the updated columns deliberately: leaving it at
    /// its original value would keep a re-run search wherever it already sat
    /// in the list, when the whole point of re-running it is that it belongs
    /// at the front.
    /// </summary>
    private async Task<int> UpsertAsync(RecentSearch search, CancellationToken cancellationToken)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            INSERT INTO RecentSearches
                (Name, Query, MatchMode, Languages, Corpora, Collections, OriginalsOnly,
                 AuthorName, TagName, BookmarkedOnly, EraLabel, CreatedAt)
            VALUES
                (@Name, @Query, @MatchMode, @Languages, @Corpora, @Collections, @OriginalsOnly,
                 @AuthorName, @TagName, @BookmarkedOnly, @EraLabel, @CreatedAt)
            ON CONFLICT(Name) DO UPDATE SET
                Query          = excluded.Query,
                MatchMode      = excluded.MatchMode,
                Languages      = excluded.Languages,
                Corpora        = excluded.Corpora,
                Collections    = excluded.Collections,
                OriginalsOnly  = excluded.OriginalsOnly,
                AuthorName     = excluded.AuthorName,
                TagName        = excluded.TagName,
                BookmarkedOnly = excluded.BookmarkedOnly,
                EraLabel       = excluded.EraLabel,
                CreatedAt      = excluded.CreatedAt
            RETURNING RecentSearchId;";

        cmd.Parameters.AddWithValue("@Name", search.Name);
        cmd.Parameters.AddWithValue("@Query", search.Query);
        cmd.Parameters.AddWithValue("@MatchMode", search.MatchMode);
        cmd.Parameters.AddWithValue("@Languages", search.Languages);
        cmd.Parameters.AddWithValue("@Corpora", search.Corpora);
        cmd.Parameters.AddWithValue("@Collections", search.Collections);
        cmd.Parameters.AddWithValue("@OriginalsOnly",
            search.OriginalsOnly == null ? DBNull.Value : search.OriginalsOnly.Value ? 1 : 0);
        cmd.Parameters.AddWithValue("@AuthorName", (object?)search.AuthorName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TagName", (object?)search.TagName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@BookmarkedOnly", search.BookmarkedOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("@EraLabel", (object?)search.EraLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", search.CreatedAt.ToString("O"));

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>Most recently run first, which is the order the picker shows them in.</summary>
    public async Task<List<RecentSearch>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<RecentSearch>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT RecentSearchId, Name, Query, MatchMode, Languages, Corpora, Collections,
                   OriginalsOnly, AuthorName, TagName, BookmarkedOnly, EraLabel, CreatedAt
            FROM RecentSearches
            ORDER BY CreatedAt DESC;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new RecentSearch
            {
                RecentSearchId = reader.GetInt32(0),
                Name = reader.GetString(1),
                Query = reader.GetString(2),
                MatchMode = reader.GetString(3),
                Languages = reader.GetString(4),
                Corpora = reader.GetString(5),
                Collections = reader.GetString(6),
                OriginalsOnly = reader.IsDBNull(7) ? null : reader.GetInt32(7) != 0,
                AuthorName = reader.IsDBNull(8) ? null : reader.GetString(8),
                TagName = reader.IsDBNull(9) ? null : reader.GetString(9),
                BookmarkedOnly = reader.GetInt32(10) != 0,
                EraLabel = reader.IsDBNull(11) ? null : reader.GetString(11),
                CreatedAt = DateTime.TryParse(reader.GetString(12), out var created) ? created : DateTime.UtcNow
            });
        }

        return results;
    }

    /// <summary>Empties the list, for anyone who would rather it were gone.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM RecentSearches;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
