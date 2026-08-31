using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public class AuthorRepository
{
    /// <summary>
    /// Inserts the author if its CtsUrn is new, otherwise updates the existing
    /// row and returns its id. Safe to call repeatedly during ingestion.
    /// </summary>
    public async Task<int> UpsertAsync(Author author, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // SQLite's UPSERT: INSERT ... ON CONFLICT(...) DO UPDATE, with
        // RETURNING to get the row's id back in the same round trip - the
        // same job SQL Server's MERGE ... OUTPUT was doing.
        const string sql = @"
            INSERT INTO Authors (CtsUrn, Name, Namespace, Language)
            VALUES (@CtsUrn, @Name, @Namespace, @Language)
            ON CONFLICT(CtsUrn) DO UPDATE SET
                -- The name an author already has is kept. Collections disagree about
                -- what to call the same man - urn:cts:latinLit:stoa0022 is 'Sanctus
                -- Ambrosius' in CSEL and 'Ambrosius' in the Patrologia Latina - and
                -- overwriting meant the row was named by whichever collection had been
                -- imported most recently. Installing a corpus would rename authors
                -- already in the library, so someone who knew where Ambrose was could
                -- no longer find him, with nothing on screen to say why.
                --
                -- An import adds texts; it does not get to rename what is already
                -- there. A row whose name is somehow blank still takes one.
                Name = CASE
                    WHEN Authors.Name IS NULL OR TRIM(Authors.Name) = '' THEN excluded.Name
                    ELSE Authors.Name
                END,
                Namespace = excluded.Namespace,
                Language = excluded.Language
            RETURNING AuthorId;";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@CtsUrn", author.CtsUrn);
        cmd.Parameters.AddWithValue("@Name", author.Name);
        cmd.Parameters.AddWithValue("@Namespace", author.Namespace);
        cmd.Parameters.AddWithValue("@Language", (object?)author.Language ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    /// <summary>Single-author lookup by id - CreateTranslationForm needs the author's name for its prompt context and doesn't need the whole table for that.</summary>
    public async Task<Author?> GetByIdAsync(int authorId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = "SELECT AuthorId, CtsUrn, Name, Namespace, Language FROM Authors WHERE AuthorId = @AuthorId;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@AuthorId", authorId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new Author
        {
            AuthorId = reader.GetInt32(0),
            CtsUrn = reader.GetString(1),
            Name = reader.GetString(2),
            Namespace = reader.GetString(3),
            Language = reader.IsDBNull(4) ? null : reader.GetString(4)
        };
    }

    public async Task<List<Author>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Author>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        const string sql = "SELECT AuthorId, CtsUrn, Name, Namespace, Language FROM Authors ORDER BY Name;";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new Author
            {
                AuthorId = reader.GetInt32(0),
                CtsUrn = reader.GetString(1),
                Name = reader.GetString(2),
                Namespace = reader.GetString(3),
                Language = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        return results;
    }

    /// <summary>
    /// How many authors came from a given corpus - "greekLit" or
    /// "latinLit", the exact string PerseusIngestService is handed for each
    /// repo.
    ///
    /// NOT the setup wizard's signal for "has this collection been
    /// installed", though it was until 3.2.0 and its doc comment said so for
    /// longer. A namespace is shared: CSEL and the Patrologia Latina are
    /// latinLit exactly as canonical-latinLit is, and First1KGreek is
    /// greekLit exactly as canonical-greekLit is. Once two collections could
    /// answer for one namespace, installing either one reported the other as
    /// already present, and the wizard skipped a step whose corpus had never
    /// been fetched. See EditionRepository.CountByCollectionAsync, which
    /// asks the question this was standing in for.
    /// </summary>
    public async Task<int> CountByNamespaceAsync(string ns, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Authors WHERE Namespace = @Namespace;";
        cmd.Parameters.AddWithValue("@Namespace", ns);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }
}
