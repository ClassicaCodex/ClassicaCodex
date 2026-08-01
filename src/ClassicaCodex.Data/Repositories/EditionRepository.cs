using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class EditionRepository
{
    public async Task<int> UpsertAsync(Edition edition, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language, Translator, SourcePath)
            VALUES (@WorkId, @CtsUrn, @Kind, @Language, @Translator, @SourcePath)
            ON CONFLICT(CtsUrn) DO UPDATE SET
                Kind = excluded.Kind,
                Language = excluded.Language,
                Translator = excluded.Translator,
                SourcePath = excluded.SourcePath,
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

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<List<Edition>> GetByWorkAsync(int workId, CancellationToken cancellationToken = default)
    {
        var results = new List<Edition>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = @"SELECT EditionId, WorkId, CtsUrn, Kind, Language, Translator, SourcePath
                             FROM Editions WHERE WorkId = @WorkId;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@WorkId", workId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new Edition
            {
                EditionId = reader.GetInt32(0),
                WorkId = reader.GetInt32(1),
                CtsUrn = reader.GetString(2),
                Kind = Enum.TryParse<EditionKind>(reader.GetString(3), out var kind) ? kind : EditionKind.Unknown,
                Language = reader.IsDBNull(4) ? null : reader.GetString(4),
                Translator = reader.IsDBNull(5) ? null : reader.GetString(5),
                SourcePath = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return results;
    }

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
