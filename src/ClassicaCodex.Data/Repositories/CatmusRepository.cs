using ClassicaCodex.Core;
using ClassicaCodex.Core.Catmus;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// The CATMuS manuscripts and lines this library holds.
///
/// Separate from everything that touches Authors/Works/Editions on purpose:
/// CATMuS lines have no reading order (see the schema comment on
/// CatmusLines), so they can be searched and looked at but never read
/// through, and keeping them out of the reading spine is what stops the
/// difference being lost.
/// </summary>
public class CatmusRepository
{
    /// <summary>
    /// Replaces one shard's lines with the ones given, in a single
    /// transaction, creating or updating the manuscript they belong to.
    ///
    /// Per shard rather than per manuscript because that is the unit that
    /// downloads: a four-part manuscript interrupted after two parts should
    /// keep those two, and running the step again should not duplicate them.
    /// </summary>
    public async Task ReplaceShardAsync(
        string shelfmark,
        string? language,
        int? century,
        string? scriptType,
        string? genre,
        string? verse,
        string? project,
        string shardFile,
        IReadOnlyList<CatmusLine> lines,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);

        var manuscriptId = await UpsertManuscriptAsync(
            conn, transaction, shelfmark, language, century, scriptType, genre, verse, project, cancellationToken);

        await using (var deleteCmd = conn.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM CatmusLines WHERE ShardFile = @ShardFile;";
            deleteCmd.Parameters.AddWithValue("@ShardFile", shardFile);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = @"
                INSERT INTO CatmusLines
                    (ManuscriptId, ShardFile, RowIndex, Text, NormalizedText, Region, LineType)
                VALUES
                    (@ManuscriptId, @ShardFile, @RowIndex, @Text, @NormalizedText, @Region, @LineType);";

            var pManuscriptId = insertCmd.Parameters.Add("@ManuscriptId", SqliteType.Integer);
            var pShardFile = insertCmd.Parameters.Add("@ShardFile", SqliteType.Text);
            var pRowIndex = insertCmd.Parameters.Add("@RowIndex", SqliteType.Integer);
            var pText = insertCmd.Parameters.Add("@Text", SqliteType.Text);
            var pNormalized = insertCmd.Parameters.Add("@NormalizedText", SqliteType.Text);
            var pRegion = insertCmd.Parameters.Add("@Region", SqliteType.Text);
            var pLineType = insertCmd.Parameters.Add("@LineType", SqliteType.Text);

            pManuscriptId.Value = manuscriptId;
            pShardFile.Value = shardFile;

            foreach (var line in lines)
            {
                pRowIndex.Value = line.RowIndex;
                pText.Value = line.Text;
                pNormalized.Value = NormalizeForSearch(line.Text);
                pRegion.Value = (object?)line.Region ?? DBNull.Value;
                pLineType.Value = (object?)line.LineType ?? DBNull.Value;
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await RefreshLineCountAsync(conn, transaction, manuscriptId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The searchable form of a line: every word folded the way the rest of
    /// the application folds words, joined by single spaces.
    ///
    /// Using WordNormalizer rather than a LOWER() in SQL is what lets someone
    /// type "concessisse" in ordinary letters and reach a line the scribe
    /// wrote with a long s or an accented abbreviation - the same fold that
    /// makes the Middle High German and Menota collections searchable. It
    /// drops the abbreviation signs themselves, which is why the search also
    /// offers to match the raw text: for a palaeographer, "every line where
    /// someone wrote ꝯ" is a real question and the folded text cannot answer
    /// it.
    /// </summary>
    private static string NormalizeForSearch(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(WordNormalizer.Normalize)
            .Where(w => w.Length > 0));

    private static async Task<long> UpsertManuscriptAsync(
        SqliteConnection conn, SqliteTransaction transaction,
        string shelfmark, string? language, int? century, string? scriptType,
        string? genre, string? verse, string? project, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;

        // ON CONFLICT rather than a SELECT-then-INSERT so the image pack a
        // manuscript may already have is not disturbed by re-running the text
        // download over it.
        cmd.CommandText = @"
            INSERT INTO CatmusManuscripts
                (Shelfmark, Language, Century, ScriptType, Genre, Verse, Project, LineCount)
            VALUES
                (@Shelfmark, @Language, @Century, @ScriptType, @Genre, @Verse, @Project, 0)
            ON CONFLICT (Shelfmark) DO UPDATE SET
                Language   = COALESCE(excluded.Language, CatmusManuscripts.Language),
                Century    = COALESCE(excluded.Century, CatmusManuscripts.Century),
                ScriptType = COALESCE(excluded.ScriptType, CatmusManuscripts.ScriptType),
                Genre      = COALESCE(excluded.Genre, CatmusManuscripts.Genre),
                Verse      = COALESCE(excluded.Verse, CatmusManuscripts.Verse),
                Project    = COALESCE(excluded.Project, CatmusManuscripts.Project)
            RETURNING ManuscriptId;";

        cmd.Parameters.AddWithValue("@Shelfmark", shelfmark);
        cmd.Parameters.AddWithValue("@Language", (object?)language ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Century", (object?)century ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ScriptType", (object?)scriptType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Genre", (object?)genre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Verse", (object?)verse ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Project", (object?)project ?? DBNull.Value);

        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task RefreshLineCountAsync(
        SqliteConnection conn, SqliteTransaction transaction, long manuscriptId, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            UPDATE CatmusManuscripts
               SET LineCount = (SELECT COUNT(*) FROM CatmusLines WHERE ManuscriptId = @ManuscriptId)
             WHERE ManuscriptId = @ManuscriptId;";
        cmd.Parameters.AddWithValue("@ManuscriptId", manuscriptId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Records where each line's photograph landed in a manuscript's image
    /// pack, and names the pack on the manuscript.
    ///
    /// The pack is named last, inside the same transaction that writes the
    /// offsets, so a manuscript never claims to have pictures whose addresses
    /// were not written.
    /// </summary>
    public async Task SetImagePackAsync(
        string shelfmark, string packFileName,
        IReadOnlyDictionary<(string ShardFile, int RowIndex), (long Offset, int Length)> placements,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                UPDATE CatmusLines
                   SET ImageOffset = @Offset, ImageLength = @Length
                 WHERE ShardFile = @ShardFile AND RowIndex = @RowIndex;";

            var pOffset = cmd.Parameters.Add("@Offset", SqliteType.Integer);
            var pLength = cmd.Parameters.Add("@Length", SqliteType.Integer);
            var pShardFile = cmd.Parameters.Add("@ShardFile", SqliteType.Text);
            var pRowIndex = cmd.Parameters.Add("@RowIndex", SqliteType.Integer);

            foreach (var ((shardFile, rowIndex), (offset, length)) in placements)
            {
                pOffset.Value = offset;
                pLength.Value = length;
                pShardFile.Value = shardFile;
                pRowIndex.Value = rowIndex;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE CatmusManuscripts SET ImagePack = @Pack WHERE Shelfmark = @Shelfmark;";
            cmd.Parameters.AddWithValue("@Pack", packFileName);
            cmd.Parameters.AddWithValue("@Shelfmark", shelfmark);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<List<CatmusHolding>> GetHoldingsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ManuscriptId, Shelfmark, Language, Century, ScriptType, Genre, Verse, Project, LineCount, ImagePack
              FROM CatmusManuscripts
             ORDER BY Shelfmark;";

        var holdings = new List<CatmusHolding>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            holdings.Add(new CatmusHolding
            {
                ManuscriptId = reader.GetInt64(0),
                Shelfmark = reader.GetString(1),
                Language = reader.IsDBNull(2) ? null : reader.GetString(2),
                Century = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ScriptType = reader.IsDBNull(4) ? null : reader.GetString(4),
                Genre = reader.IsDBNull(5) ? null : reader.GetString(5),
                Verse = reader.IsDBNull(6) ? null : reader.GetString(6),
                Project = reader.IsDBNull(7) ? null : reader.GetString(7),
                LineCount = reader.GetInt32(8),
                ImagePack = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return holdings;
    }

    public async Task<List<CatmusLine>> GetLinesAsync(
        long manuscriptId, int limit = 500, int offset = 0, CancellationToken cancellationToken = default)
    {
        const string where = "WHERE l.ManuscriptId = @ManuscriptId";
        return await QueryLinesAsync(where, limit, offset, cancellationToken,
            ("@ManuscriptId", manuscriptId));
    }

    /// <summary>
    /// Lines whose text contains the term.
    ///
    /// Two different questions, because palaeography asks both, and which one
    /// is being asked decides which column is searched.
    ///
    /// <b>Folded</b> (<paramref name="matchRawCharacters"/> false) puts the
    /// query through the same fold the stored text went through, which strips
    /// the marks written over and through letters and leaves the letters:
    /// "nr̃e" is stored as "nre", "uniu̾sitati" as "uniusitati", "dñicalis" as
    /// "dnicalis", "ſtet" as "stet". So a reader who types "nre" finds the
    /// line, and one who types the marked form finds it too, because the
    /// query is folded as well.
    ///
    /// <b>Raw</b> searches the transcription exactly as written. The fold
    /// keeps letter-shaped abbreviations as themselves - ꝯ ꝑ ꝓ ꝰ are all
    /// letters in Unicode and survive it - but drops the Tironian et ⁊, which
    /// is punctuation, and every combining mark. Raw is how to ask "where did
    /// a scribe write this sign", which is the question the photographs
    /// beside the lines exist to answer.
    /// </summary>
    public async Task<List<CatmusLine>> SearchAsync(
        string term, bool matchRawCharacters, long? manuscriptId = null,
        string? language = null, string? scriptType = null, int? century = null,
        int limit = 500, CancellationToken cancellationToken = default)
    {
        var needle = matchRawCharacters ? term : NormalizeForSearch(term);
        if (needle.Length == 0) return new List<CatmusLine>();

        var column = matchRawCharacters ? "l.Text" : "l.NormalizedText";
        var conditions = new List<string> { $"{column} LIKE @Term ESCAPE '\\'" };
        var parameters = new List<(string, object)> { ("@Term", "%" + EscapeLike(needle) + "%") };

        if (manuscriptId.HasValue)
        {
            conditions.Add("l.ManuscriptId = @ManuscriptId");
            parameters.Add(("@ManuscriptId", manuscriptId.Value));
        }

        if (!string.IsNullOrEmpty(language))
        {
            conditions.Add("m.Language = @Language");
            parameters.Add(("@Language", language));
        }

        if (!string.IsNullOrEmpty(scriptType))
        {
            conditions.Add("m.ScriptType = @ScriptType");
            parameters.Add(("@ScriptType", scriptType));
        }

        if (century.HasValue)
        {
            conditions.Add("m.Century = @Century");
            parameters.Add(("@Century", century.Value));
        }

        return await QueryLinesAsync("WHERE " + string.Join(" AND ", conditions), limit, 0,
            cancellationToken, parameters.ToArray());
    }

    /// <summary>
    /// LIKE treats % and _ as wildcards, so a search for a literal underscore
    /// would match any character. The backslash is declared as the escape in
    /// every query that uses this.
    /// </summary>
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static async Task<List<CatmusLine>> QueryLinesAsync(
        string where, int limit, int offset, CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT l.LineId, l.ManuscriptId, l.ShardFile, l.RowIndex, l.Text, l.Region, l.LineType,
                   l.ImageOffset, l.ImageLength, m.Shelfmark, m.ScriptType, m.Century, m.ImagePack
              FROM CatmusLines l
              JOIN CatmusManuscripts m ON m.ManuscriptId = l.ManuscriptId
              {where}
             ORDER BY l.ManuscriptId, l.RowIndex
             LIMIT @Limit OFFSET @Offset;";

        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        cmd.Parameters.AddWithValue("@Limit", limit);
        cmd.Parameters.AddWithValue("@Offset", offset);

        var lines = new List<CatmusLine>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new CatmusLine
            {
                LineId = reader.GetInt64(0),
                ManuscriptId = reader.GetInt64(1),
                ShardFile = reader.GetString(2),
                RowIndex = reader.GetInt32(3),
                Text = reader.GetString(4),
                Region = reader.IsDBNull(5) ? null : reader.GetString(5),
                LineType = reader.IsDBNull(6) ? null : reader.GetString(6),
                ImageOffset = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                ImageLength = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Shelfmark = reader.GetString(9),
                ScriptType = reader.IsDBNull(10) ? null : reader.GetString(10),
                Century = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                ImagePack = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }

        return lines;
    }

    /// <summary>Which shards already have their lines, so a re-run can skip them.</summary>
    public async Task<HashSet<string>> GetIngestedShardsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT ShardFile FROM CatmusLines;";

        var shards = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) shards.Add(reader.GetString(0));
        return shards;
    }

    public async Task<int> CountLinesAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM CatmusLines;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> HasDataAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM CatmusLines LIMIT 1);";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
