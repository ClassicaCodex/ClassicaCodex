using ClassicaCodex.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// A real SQLite database in a temp directory, created fresh per test and
/// deleted after.
///
/// The repositories talk to a static DbConnectionFactory rather than an
/// injected interface, which normally makes a data layer awkward to test -
/// but SQLite is a file, so there's nothing to mock. Pointing the factory at
/// a throwaway path exercises the actual SQL, the actual schema, and the
/// actual migrations, which is what matters here: the thing being verified is
/// whether real annotations survive a real re-ingest.
///
/// Tests using this must not run in parallel, since DbConnectionFactory holds
/// one path for the process - hence the collection attribute below.
/// </summary>
public sealed class TempDatabase : IDisposable
{
    private readonly string _directory;

    public string Path { get; }

    private TempDatabase()
    {
        _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "classicacodex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "test.db");
        // remember: false - without it, running the test suite overwrites the
        // path preference in %LocalAppData% and the real app reopens pointing
        // at a temp file that was deleted when the test finished.
        DbConnectionFactory.Configure(Path, remember: false);
    }

    /// <summary>A database at the current schema, as a first run would create it.</summary>
    public static async Task<TempDatabase> CreateAsync()
    {
        var db = new TempDatabase();
        await SchemaInitializer.EnsureSchemaAsync();
        return db;
    }

    /// <summary>
    /// A database at the old shape - annotations keyed to TextNodeId - so the
    /// migration is tested against the thing it actually has to upgrade
    /// rather than a reconstruction of it.
    /// </summary>
    /// <param name="userVersion">
    /// Which migrations the file claims to have had already. Defaults to 0,
    /// meaning a database created before the migration framework existed -
    /// user_version = N means "everything up to N has run", so stamping 1
    /// here would silently skip migration 1 rather than exercise it.
    /// Pass 1 to represent a file that has had migration 1 but not 2.
    /// </param>
    public static async Task<TempDatabase> CreateLegacyAsync(int userVersion = 0)
    {
        var db = new TempDatabase();
        await db.ExecuteAsync(LegacyV1Schema);
        if (userVersion >= 1) await db.ExecuteAsync("DROP INDEX IF EXISTS IX_Tags_Name;");
        await db.ExecuteAsync($"PRAGMA user_version = {userVersion};");
        return db;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql) where T : struct
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        if (result == null || result == DBNull.Value) return default;
        return (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>
    /// Runs SQL with foreign key enforcement off. Needed to plant the kind of
    /// dangling row an older file can genuinely be holding - the v1 schema
    /// declared those foreign keys, but enforcement is per-connection and off
    /// by default in SQLite, so rows that violate them really do exist in the
    /// wild.
    ///
    /// The PRAGMA has to be issued explicitly, even though CreateConnection
    /// deliberately sets no pragmas of its own. Microsoft.Data.Sqlite pools
    /// connections by connection string, and PRAGMA state belongs to the
    /// connection rather than the command - so this hands back a connection
    /// some earlier OpenConnectionAsync already turned foreign keys ON for,
    /// and it stays on. Opening a "fresh" connection is not the same thing as
    /// opening a connection in a fresh state.
    ///
    /// Leaving it off doesn't leak into the rest of the suite: every normal
    /// path goes through OpenConnectionAsync, which reasserts the full pragma
    /// batch on each open regardless of what the pooled connection was last
    /// used for.
    /// </summary>
    public async Task ExecuteUnenforcedAsync(string sql)
    {
        await using var conn = DbConnectionFactory.CreateConnection();
        await conn.OpenAsync();

        await using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = OFF;";
            await pragma.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<long> CountAsync(string table) =>
        await ScalarAsync<long>($"SELECT COUNT(*) FROM {table};");

    /// <summary>
    /// Column names in declaration order - for checking that a table built
    /// by the current DDL and one arrived at by migration actually match.
    /// </summary>
    public async Task<string[]> ColumnNamesAsync(string table)
    {
        var names = new List<string>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(1));

        return names.ToArray();
    }

    public async Task<bool> TableExistsAsync(string table) =>
        await ScalarAsync<long>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';") == 1;

    /// <summary>
    /// Inserts one author/work/edition and returns the EditionId, so tests
    /// have somewhere to hang text nodes.
    /// </summary>
    public async Task<int> SeedEditionAsync(string urnSuffix = "test1")
    {
        await ExecuteAsync($@"
            INSERT INTO Authors (CtsUrn, Name, Namespace, Language)
            VALUES ('urn:a:{urnSuffix}', 'Homer', 'greekLit', 'grc');
            INSERT INTO Works (AuthorId, CtsUrn, Title)
            VALUES ((SELECT AuthorId FROM Authors WHERE CtsUrn = 'urn:a:{urnSuffix}'),
                    'urn:w:{urnSuffix}', 'Iliad');
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language)
            VALUES ((SELECT WorkId FROM Works WHERE CtsUrn = 'urn:w:{urnSuffix}'),
                    'urn:e:{urnSuffix}', 'Original', 'grc');");

        return await ScalarAsync<int>($"SELECT EditionId FROM Editions WHERE CtsUrn = 'urn:e:{urnSuffix}';");
    }

    /// <summary>
    /// An author, work, and edition with every field spelled out - for tests
    /// that need a library with more than one shape in it. SeedEditionAsync
    /// above covers the common case; this one exists because the search
    /// filters can only be tested against a corpus that actually varies by
    /// language, kind, and author.
    /// </summary>
    public async Task<int> SeedFullEditionAsync(
        string key, string authorName, string corpus, string workTitle,
        string kind, string language)
    {
        await ExecuteAsync($@"
            INSERT OR IGNORE INTO Authors (CtsUrn, Name, Namespace, Language)
            VALUES ('urn:a:{key}', '{authorName}', '{corpus}', '{language}');
            INSERT INTO Works (AuthorId, CtsUrn, Title)
            VALUES ((SELECT AuthorId FROM Authors WHERE CtsUrn = 'urn:a:{key}'),
                    'urn:w:{key}', '{workTitle}');
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language)
            VALUES ((SELECT WorkId FROM Works WHERE CtsUrn = 'urn:w:{key}'),
                    'urn:e:{key}', '{kind}', '{language}');");

        return await ScalarAsync<int>($"SELECT EditionId FROM Editions WHERE CtsUrn = 'urn:e:{key}';");
    }

    /// <summary>
    /// Another edition of a work that already exists - a translation
    /// alongside its original, which is the arrangement half of these tests
    /// depend on.
    /// </summary>
    public async Task<int> SeedSiblingEditionAsync(
        string existingKey, string newKey, string kind, string language, string? translator = null)
    {
        var translatorValue = translator == null ? "NULL" : $"'{translator}'";

        await ExecuteAsync($@"
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Translator)
            VALUES ((SELECT WorkId FROM Works WHERE CtsUrn = 'urn:w:{existingKey}'),
                    'urn:e:{newKey}', '{kind}', '{language}', {translatorValue});");

        return await ScalarAsync<int>($"SELECT EditionId FROM Editions WHERE CtsUrn = 'urn:e:{newKey}';");
    }

    public async Task<int> WorkIdForAsync(string key) =>
        await ScalarAsync<int>($"SELECT WorkId FROM Works WHERE CtsUrn = 'urn:w:{key}';");

    public async Task<int> AuthorIdForAsync(string key) =>
        await ScalarAsync<int>($"SELECT AuthorId FROM Authors WHERE CtsUrn = 'urn:a:{key}';");

    /// <summary>
    /// Simulates exactly what a corpus re-ingest does: delete every text node
    /// for the edition, then insert fresh ones. The new rows get new
    /// autoincrement ids, which is the whole reason annotations can't be
    /// keyed to them.
    /// </summary>
    public async Task ReingestAsync(int editionId, params (string CitationRef, string Text)[] lines)
    {
        await ExecuteAsync($"DELETE FROM TextNodes WHERE EditionId = {editionId};");
        await InsertLinesAsync(editionId, lines);
    }

    public async Task InsertLinesAsync(int editionId, params (string CitationRef, string Text)[] lines)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync();
        for (var i = 0; i < lines.Length; i++)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text) " +
                "VALUES (@e, @c, @s, @t);";
            cmd.Parameters.AddWithValue("@e", editionId);
            cmd.Parameters.AddWithValue("@c", lines[i].CitationRef);
            cmd.Parameters.AddWithValue("@s", i);
            cmd.Parameters.AddWithValue("@t", lines[i].Text);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<long> TextNodeIdAsync(int editionId, string citationRef) =>
        await ScalarAsync<long>(
            $"SELECT TextNodeId FROM TextNodes WHERE EditionId = {editionId} " +
            $"AND CitationRef = '{citationRef}' ORDER BY TextNodeId LIMIT 1;");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); }
        catch { /* a locked temp file isn't worth failing a test over */ }
    }

    /// <summary>
    /// The schema as it stood at v1, verbatim in the parts that matter -
    /// annotations carrying a foreign key to TextNodes(TextNodeId). Kept here
    /// rather than generated, because the migration's job is to upgrade files
    /// that really look like this.
    /// </summary>
    private const string LegacyV1Schema = @"
        CREATE TABLE Authors (
            AuthorId INTEGER PRIMARY KEY, CtsUrn TEXT NOT NULL, Name TEXT NOT NULL,
            Namespace TEXT NOT NULL, Language TEXT NULL,
            CONSTRAINT UQ_Authors_CtsUrn UNIQUE (CtsUrn));

        CREATE TABLE Works (
            WorkId INTEGER PRIMARY KEY, AuthorId INTEGER NOT NULL, CtsUrn TEXT NOT NULL,
            Title TEXT NOT NULL, CitationScheme TEXT NULL,
            CONSTRAINT UQ_Works_CtsUrn UNIQUE (CtsUrn),
            CONSTRAINT FK_Works_Authors FOREIGN KEY (AuthorId) REFERENCES Authors(AuthorId));

        CREATE TABLE Editions (
            EditionId INTEGER PRIMARY KEY, WorkId INTEGER NOT NULL, CtsUrn TEXT NOT NULL,
            Kind TEXT NOT NULL, Language TEXT NULL, Translator TEXT NULL, SourcePath TEXT NULL,
            CONSTRAINT UQ_Editions_CtsUrn UNIQUE (CtsUrn),
            CONSTRAINT FK_Editions_Works FOREIGN KEY (WorkId) REFERENCES Works(WorkId));

        CREATE TABLE TextNodes (
            TextNodeId INTEGER PRIMARY KEY, EditionId INTEGER NOT NULL,
            CitationRef TEXT NOT NULL, SortOrder INTEGER NOT NULL, Text TEXT NOT NULL,
            CONSTRAINT FK_TextNodes_Editions FOREIGN KEY (EditionId) REFERENCES Editions(EditionId));

        CREATE TABLE Tags (
            TagId INTEGER PRIMARY KEY, Name TEXT NOT NULL, Category TEXT NULL,
            CONSTRAINT UQ_Tags_Name UNIQUE (Name));

        CREATE INDEX IX_Tags_Name ON Tags (Name);

        CREATE TABLE TextNodeTags (
            TextNodeId INTEGER NOT NULL, TagId INTEGER NOT NULL,
            CONSTRAINT PK_TextNodeTags PRIMARY KEY (TextNodeId, TagId),
            CONSTRAINT FK_TextNodeTags_TextNodes FOREIGN KEY (TextNodeId) REFERENCES TextNodes(TextNodeId),
            CONSTRAINT FK_TextNodeTags_Tags FOREIGN KEY (TagId) REFERENCES Tags(TagId));

        CREATE TABLE Bookmarks (
            BookmarkId INTEGER PRIMARY KEY, TextNodeId INTEGER NOT NULL, Note TEXT NULL,
            CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT FK_Bookmarks_TextNodes FOREIGN KEY (TextNodeId) REFERENCES TextNodes(TextNodeId));

        CREATE INDEX IX_Bookmarks_TextNodeId ON Bookmarks (TextNodeId);

        CREATE TABLE Lemmas (
            LemmaId INTEGER PRIMARY KEY, Form TEXT NOT NULL, NormalizedForm TEXT NOT NULL,
            Headword TEXT NOT NULL, Language TEXT NOT NULL, PartOfSpeech TEXT NULL);

        CREATE TABLE WordIndex (NormalizedWord TEXT NOT NULL, TextNodeId INTEGER NOT NULL);

        CREATE TABLE Definitions (
            DefinitionId INTEGER PRIMARY KEY, Headword TEXT NOT NULL,
            NormalizedHeadword TEXT NOT NULL, Language TEXT NOT NULL,
            Entry TEXT NOT NULL, Source TEXT NULL);

        CREATE TABLE Artifacts (
            ArtifactId TEXT PRIMARY KEY, Type TEXT NOT NULL, Name TEXT NULL, Region TEXT NULL,
            Context TEXT NULL, MatchedPlaceName TEXT NULL, Period TEXT NULL, StartDate TEXT NULL,
            EndDate TEXT NULL, Collection TEXT NULL, Material TEXT NULL, Location TEXT NULL,
            Description TEXT NULL, PrimaryCitation TEXT NULL);

        CREATE TABLE ArtifactImages (
            ArtifactId TEXT NOT NULL, ImageId TEXT NOT NULL, Caption TEXT NULL, Credits TEXT NULL,
            CONSTRAINT PK_ArtifactImages PRIMARY KEY (ArtifactId, ImageId));";
}

/// <summary>
/// DbConnectionFactory is process-wide static state, so every test that
/// points it somewhere has to have the process to itself.
/// </summary>
[CollectionDefinition("Database", DisableParallelization = true)]
public class DatabaseCollection { }
