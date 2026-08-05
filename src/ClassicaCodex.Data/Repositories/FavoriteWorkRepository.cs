using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// The works marked as favourites.
///
/// Keyed on the work's CTS URN rather than its WorkId, which is the same
/// decision reading position and recent searches made and for the same
/// reason: ids are assigned locally and renumber when a corpus is re-ingested
/// into a fresh file. A favourites list keyed on ids would come back after a
/// re-ingest pointing at whatever now holds those numbers - a list that is
/// wrong but looks right, which is worse than one that is empty.
///
/// The whole set is read at once rather than asked per work. A library tree
/// draws a few thousand nodes and needs to know which carry a star while it
/// builds them; a query per node would be a few thousand round trips for a
/// set that is realistically a few dozen entries.
/// </summary>
public class FavoriteWorkRepository
{
    public async Task<HashSet<string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var favorites = new HashSet<string>(StringComparer.Ordinal);

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CtsUrn FROM FavoriteWorks;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            favorites.Add(reader.GetString(0));
        }

        return favorites;
    }

    /// <summary>
    /// INSERT OR IGNORE rather than a read-then-write, so adding a favourite
    /// twice is harmless rather than a primary key violation.
    /// </summary>
    public async Task AddAsync(string ctsUrn, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ctsUrn)) return;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO FavoriteWorks (CtsUrn) VALUES (@CtsUrn);";
        cmd.Parameters.AddWithValue("@CtsUrn", ctsUrn);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(string ctsUrn, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ctsUrn)) return;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM FavoriteWorks WHERE CtsUrn = @CtsUrn;";
        cmd.Parameters.AddWithValue("@CtsUrn", ctsUrn);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
