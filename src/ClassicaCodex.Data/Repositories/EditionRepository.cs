using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class EditionRepository
{
    public async Task<int> UpsertAsync(Edition edition, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Translator, SourcePath, Orthography)
            VALUES (@WorkId, @CtsUrn, @Kind, @Language, @Translator, @SourcePath, @Orthography)
            ON CONFLICT(CtsUrn) DO UPDATE SET
                Kind = excluded.Kind,
                Language = excluded.Language,
                Translator = excluded.Translator,
                SourcePath = excluded.SourcePath,
                Orthography = excluded.Orthography,
                WorkId = excluded.WorkId
            RETURNING EditionId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@WorkId", edition.WorkId);
        cmd.Parameters.AddWithValue("@CtsUrn", edition.CtsUrn);
        cmd.Parameters.AddWithValue("@Kind", edition.Kind.ToString());
        cmd.Parameters.AddWithValue("@Language", (object?)edition.Language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Translator", (object?)edition.Translator ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SourcePath", (object?)edition.SourcePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Orthography", (object?)edition.Orthography ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    /// <summary>One edition by id, or null if it has since been removed.</summary>
    public async Task<Edition?> GetByIdAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"SELECT EditionId, WorkId, CtsUrn, Kind, Language, Translator, SourcePath, Orthography, Collection
              FROM Editions WHERE EditionId = @EditionId;";
        cmd.Parameters.AddWithValue("@EditionId", editionId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<List<Edition>> GetByWorkAsync(int workId, CancellationToken cancellationToken = default)
    {
        var results = new List<Edition>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"SELECT EditionId, WorkId, CtsUrn, Kind, Language, Translator, SourcePath, Orthography, Collection
                             FROM Editions WHERE WorkId = @WorkId;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@WorkId", workId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    /// <summary>
    /// Maps a row selected by the column list the two readers above share.
    /// One mapper because they must agree: an ordinal corrected in one and
    /// missed in the other reads every field after it out of the wrong column.
    /// </summary>
    private static Edition Read(System.Data.Common.DbDataReader reader) => new()
    {
        EditionId = reader.GetInt32(0),
        WorkId = reader.GetInt32(1),
        CtsUrn = reader.GetString(2),
        Kind = Enum.TryParse<EditionKind>(reader.GetString(3), out var kind) ? kind : EditionKind.Unknown,
        Language = reader.IsDBNull(4) ? null : reader.GetString(4),
        Translator = reader.IsDBNull(5) ? null : reader.GetString(5),
        SourcePath = reader.IsDBNull(6) ? null : reader.GetString(6),
        Orthography = reader.IsDBNull(7) ? null : reader.GetString(7),
        Collection = reader.IsDBNull(8) ? null : reader.GetString(8)
    };

    /// <summary>
    /// Deletes all TextNodes for an edition, ahead of a re-ingest.
    ///
    /// This used to fail on any edition the reader had tagged or bookmarked:
    /// both annotation tables carried a plain foreign key to
    /// TextNodes(TextNodeId), so the delete tripped a constraint, the
    /// ingest service caught it per-file and recorded the edition as failed,
    /// and the texts someone had actually worked with were the ones that
    /// silently stopped receiving updates. Annotations are now keyed to
    /// (EditionId, CitationRef) with no foreign key here, so this deletes
    /// cleanly and the tags and bookmarks reattach to the re-inserted
    /// passages by citation.
    /// </summary>
    public async Task ClearTextNodesAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = "DELETE FROM TextNodes WHERE EditionId = @EditionId;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@EditionId", editionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Every original-language (not translation) edition in the library,
    /// with its language code - the scope for stylometric comparison, since
    /// function-word frequency only means something when comparing texts in
    /// the same language.
    /// </summary>
    public async Task<List<(int WorkId, int EditionId, string AuthorName, string WorkTitle, string? Language)>> GetAllOriginalEditionsAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<(int, int, string, string, string?)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT w.WorkId, e.EditionId, a.Name, w.Title, e.Language
            FROM Editions e
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE e.Kind = 'Original'
              AND (e.Orthography IS NULL OR e.Orthography = 'normalised')
            ORDER BY e.Language, a.Name, w.Title;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return results;
    }

    /// <summary>
    /// Works that have two or more translation editions ingested - the
    /// candidate pool for comparing translators against each other, as
    /// opposed to comparing different authors under Compare Sources. A work
    /// with zero or one translation has nothing to compare.
    /// </summary>
    public async Task<List<(int WorkId, string AuthorName, string WorkTitle)>> GetWorksWithMultipleTranslationsAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<(int, string, string)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            SELECT w.WorkId, a.Name, w.Title
            FROM Editions e
            JOIN Works w ON e.WorkId = w.WorkId
            JOIN Authors a ON w.AuthorId = a.AuthorId
            WHERE e.Kind = 'Translation'
            GROUP BY w.WorkId, a.Name, w.Title
            HAVING COUNT(*) >= 2
            ORDER BY a.Name, w.Title;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader2 = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader2.ReadAsync(cancellationToken))
        {
            results.Add((reader2.GetInt32(0), reader2.GetString(1), reader2.GetString(2)));
        }

        return results;
    }

    /// <summary>
    /// How many editions were ingested from files under a given folder.
    ///
    /// Exists to tell two corpora apart when they share a namespace.
    /// First1KGreek and canonical-greekLit are both ingested as "greekLit"
    /// deliberately - they're the same umbrella collection - so an author or
    /// namespace count can't say whether First1KGreek specifically has been
    /// loaded into this database.
    ///
    /// This asks the one question that is actually decisive and involves no
    /// guesswork about naming: every edition records the path of the file it
    /// was built from, and the two corpora are downloaded to different
    /// folders. An earlier attempt at this matched "1st1K" inside the CTS URN
    /// instead, on the theory that OGL's version identifier for that repo is
    /// unique to it - a convention, not a guarantee, and not something worth
    /// betting a setup step on.
    /// </summary>
    /// <summary>
    /// The CTS URNs of every edition ingested from one source file.
    ///
    /// Needed because re-importing a manuscript is not only an insert. A work
    /// merged or split in the review mints different URNs from the ones it had
    /// before, and the previous ones stay in the library untouched - so the
    /// tree keeps showing thirty-five chapters beside the one work they were
    /// merged into, and the only way out was to delete the database.
    /// </summary>
    public async Task<List<(int EditionId, int WorkId, string CtsUrn)>> GetBySourcePathAsync(
        string sourcePath, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT EditionId, WorkId, CtsUrn FROM Editions WHERE SourcePath = @SourcePath;";
        cmd.Parameters.AddWithValue("@SourcePath", sourcePath);

        var result = new List<(int, int, string)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));

        return result;
    }

    /// <summary>
    /// Removes an edition and its text, then the work and author it belonged
    /// to if nothing else uses them.
    ///
    /// Work and author are cleaned up because an orphaned one is worse than
    /// useless: it shows in the author browser with nothing under it. The
    /// checks are counts rather than cascades, so a work with a second witness
    /// or an author with other works survives.
    /// </summary>
    public async Task DeleteEditionAsync(int editionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        async Task<int> ScalarAsync(string sql, string name, object value)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue(name, value);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
        }

        async Task ExecuteAsync(string sql, string name, object value)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue(name, value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var workId = await ScalarAsync(
            "SELECT IFNULL(WorkId, 0) FROM Editions WHERE EditionId = @EditionId;", "@EditionId", editionId);

        if (workId == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return;
        }

        var authorId = await ScalarAsync(
            "SELECT IFNULL(AuthorId, 0) FROM Works WHERE WorkId = @WorkId;", "@WorkId", workId);

        await ExecuteAsync("DELETE FROM TextNodes WHERE EditionId = @EditionId;", "@EditionId", editionId);
        await ExecuteAsync("DELETE FROM ApparatusEntries WHERE EditionId = @EditionId;", "@EditionId", editionId);
        await ExecuteAsync("DELETE FROM Editions WHERE EditionId = @EditionId;", "@EditionId", editionId);

        if (await ScalarAsync(
                "SELECT COUNT(*) FROM Editions WHERE WorkId = @WorkId;", "@WorkId", workId) == 0)
        {
            await ExecuteAsync("DELETE FROM Works WHERE WorkId = @WorkId;", "@WorkId", workId);

            if (authorId != 0 && await ScalarAsync(
                    "SELECT COUNT(*) FROM Works WHERE AuthorId = @AuthorId;", "@AuthorId", authorId) == 0)
            {
                await ExecuteAsync("DELETE FROM Authors WHERE AuthorId = @AuthorId;", "@AuthorId", authorId);
            }
        }

        await tx.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Records which collection the editions under a folder belong to, run by a
    /// setup step once its ingest has finished.
    ///
    /// The folder is how the step recognises what it just imported; the key is
    /// what gets stored, because the folder is where the files were and the key
    /// is what the text is. Stamping here rather than inside the ingest services
    /// means a step that installs to a custom folder still labels its editions
    /// correctly - it passes the folder it actually used.
    /// </summary>
    public async Task<int> StampCollectionAsync(
        string folder, string collection, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = "UPDATE Editions SET Collection = @Collection WHERE SourcePath LIKE @Prefix ESCAPE '\\';";
        cmd.Parameters.AddWithValue("@Collection", collection);
        cmd.Parameters.AddWithValue("@Prefix", $"{EscapeForLike(folder)}%");
        cmd.CommandTimeout = 120;

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The collections actually present in this library, asked of the editions
    /// themselves. Nothing here consults the filesystem, so it answers correctly
    /// for a library whose downloads have been deleted or moved.
    /// </summary>
    public async Task<List<string>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT DISTINCT Collection FROM Editions WHERE Collection IS NOT NULL ORDER BY Collection;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }

    /// <summary>
    /// Which works have an edition in any of these collections.
    ///
    /// Works rather than authors, because the library tree needs both and one follows
    /// from the other: an author belongs in the filtered tree exactly when one of their
    /// works does. Asking the other way round would show an author whose works had all
    /// been filtered out.
    /// </summary>
    public async Task<HashSet<int>> GetWorkIdsForCollectionsAsync(
        IEnumerable<string> collections, CancellationToken cancellationToken = default)
    {
        var wanted = collections.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        var result = new HashSet<int>();
        if (wanted.Count == 0) return result;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        var names = new List<string>();
        for (var i = 0; i < wanted.Count; i++)
        {
            names.Add($"@c{i}");
            cmd.Parameters.AddWithValue($"@c{i}", wanted[i]);
        }

        cmd.CommandText =
            $"SELECT DISTINCT WorkId FROM Editions WHERE Collection IN ({string.Join(",", names)});";
        cmd.CommandTimeout = 60;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetInt32(0));
        return result;
    }

    private static string EscapeForLike(string value) => value
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    public async Task<int> CountBySourcePathPrefixAsync(
        string folder, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        // Windows paths are full of backslashes, which is also LIKE's escape
        // character here - so the prefix has to be escaped before it becomes
        // a pattern, or "C:\data" would silently mean something else.
        var escaped = folder
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        cmd.CommandText = "SELECT COUNT(*) FROM Editions WHERE SourcePath LIKE @Prefix ESCAPE '\\';";
        cmd.Parameters.AddWithValue("@Prefix", $"{escaped}%");
        cmd.CommandTimeout = 60;

        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>
    /// How many editions exist for a given language - the Setup Wizard's
    /// way of telling whether "Ancient Greek Texts" or "Ancient Latin Texts"
    /// has actually been ingested yet, rather than trusting a status label
    /// that resets to blank the moment the dialog is reopened.
    /// </summary>
    public async Task<int> CountByLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Editions WHERE Language = @Language;";
        cmd.Parameters.AddWithValue("@Language", language);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }
}
