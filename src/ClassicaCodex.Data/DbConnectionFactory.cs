using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data;

/// <summary>
/// Holds the active database file path for the app session. The UI sets
/// this once (Settings dialog) before anything touches the database.
///
/// SQLite means "the database" is just a file on disk - no server, no
/// instance name, no authentication to configure. That's the whole point of
/// this migration: a person installing this app picks a folder (or accepts
/// the default) instead of standing up SQL Server first.
/// </summary>
public static class DbConnectionFactory
{
    private static string? _databasePath;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(_databasePath);

    public static string? DatabasePath => _databasePath;

    public static void Configure(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path cannot be empty.", nameof(databasePath));

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        _databasePath = databasePath;
        SavePathPreference(databasePath);
    }

    /// <summary>Where a database lands if the user never picks anywhere else.</summary>
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "classicacodex.db");

    /// <summary>The last database path used, or the default if none was ever saved.</summary>
    public static string PreferredDatabasePath
    {
        get
        {
            try
            {
                if (File.Exists(PathPreferenceFile))
                {
                    var saved = File.ReadAllText(PathPreferenceFile).Trim();
                    if (saved.Length > 0) return saved;
                }
            }
            catch
            {
                // Unreadable preference - fall through to the default.
            }

            return DefaultDatabasePath;
        }
    }

    /// <summary>
    /// Configures from the remembered path, but only if that database
    /// actually exists on disk. Returns false when there's nothing to open,
    /// which is the signal to ask the user where it should live - so the
    /// prompt only appears on a genuinely first run (or after the file has
    /// been moved or deleted), rather than every single launch.
    /// </summary>
    public static bool TryConfigureFromPreferred()
    {
        var path = PreferredDatabasePath;

        try
        {
            if (!File.Exists(path)) return false;
            Configure(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string PathPreferenceFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "database-path.txt");

    private static void SavePathPreference(string databasePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(PathPreferenceFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(PathPreferenceFile, databasePath);
        }
        catch
        {
            // Losing the preference just means the location prompt appears
            // again next launch - not worth failing a connection over.
        }
    }

    public static SqliteConnection CreateConnection()
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "No database file has been configured. Set it from the Settings dialog first.");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,

            // WAL lets reads and the one writer proceed without blocking
            // each other - the right mode for a single-user desktop app
            // where background searches shouldn't stall on an ingest.
            Cache = SqliteCacheMode.Default
        };

        return new SqliteConnection(builder.ConnectionString);
    }

    /// <summary>
    /// Opens a connection and hands it back ready to use. Caller owns disposal.
    ///
    /// Two PRAGMAs are set on every connection because SQLite defaults them
    /// off/unset per-connection rather than persisting them with the file:
    /// foreign_keys (off by default - without this, every FK constraint in
    /// the schema is silently decorative) and journal_mode=WAL (better
    /// concurrent read/write behavior; cheap to re-assert even though it
    /// only truly needs setting once per database file).
    /// </summary>
    /// <summary>
    /// Forces SQLite to fold the WAL file back into the main database file.
    /// SQLite checkpoints automatically under normal conditions, but a
    /// session with many large sequential writes (repeated full-corpus
    /// reingests, bulk tag operations) can grow the WAL file substantially
    /// in between; calling this explicitly before a big operation avoids
    /// starting it against an already-bloated WAL.
    /// </summary>
    public static async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        cmd.CommandTimeout = 300;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = CreateConnection();
        await conn.OpenAsync(cancellationToken);

        await using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        return conn;
    }
}
