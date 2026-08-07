using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>
/// The preprocessing that produced a run. Every field here materially moves the
/// result, so a Delta figure without its settings is not interpretable - which
/// is why these are stored alongside every run rather than assumed.
/// </summary>
public record StylometrySettings(
    int FeatureWordCount,
    bool FoldAccents,
    bool StripElisionMarks,
    int AlgorithmVersion,
    int ChunkSize)
{
    /// <summary>
    /// A short stable key for grouping runs that share preprocessing. Used to
    /// bucket runs in the analysis form so a reference distribution is never
    /// built from a mixture of settings.
    /// </summary>
    public string ProfileKey =>
        $"v{AlgorithmVersion}/{FeatureWordCount}mfw/{(FoldAccents ? "fold" : "keep")}/" +
        $"{(StripElisionMarks ? "elide" : "raw")}/{(ChunkSize > 0 ? $"{ChunkSize}tok" : "whole")}";

    public string Describe() =>
        $"{FeatureWordCount} features, accents {(FoldAccents ? "folded" : "kept")}, " +
        $"elision marks {(StripElisionMarks ? "stripped" : "kept")}, " +
        $"{(ChunkSize > 0 ? $"{ChunkSize:N0}-token samples" : "whole works")}, algorithm v{AlgorithmVersion}";
}

public record StylometryNeighbor(int Rank, int WorkId, string AuthorName, string WorkTitle, double Delta);

public record StylometryFeature(int Rank, string Word, double RelativeFrequency);

public record StylometryRunSummary(
    long RunId,
    DateTime CreatedUtc,
    int TargetWorkId,
    string TargetAuthorName,
    string TargetWorkTitle,
    string Language,
    StylometrySettings Settings,
    int PoolSize,
    string? Label)
{
    public override string ToString() =>
        $"{TargetAuthorName}, {TargetWorkTitle}  [{Settings.ProfileKey}]" +
        (string.IsNullOrWhiteSpace(Label) ? "" : $"  \"{Label}\"");
}

/// <summary>
/// One work's position in a run, reduced to the numbers worth comparing.
///
/// DepthToFirstOutsider is the measure that matters most here. It is the rank at
/// which the first work by a different author appears in the target's own
/// neighbour list - "how far down before the analysis stops agreeing with the
/// attribution". It survived preprocessing changes in testing that scrambled
/// raw rank order, which makes it a better summary statistic than nearest
/// neighbour identity.
///
/// Null DepthToFirstOutsider means every work in the pool shares the target's
/// author, which for a single-author pool is expected and not a signal.
/// </summary>
public record StylometryRunMetrics(
    long RunId,
    string TargetAuthorName,
    string TargetWorkTitle,
    StylometrySettings Settings,
    int? DepthToFirstOutsider,
    double DeltaFloor,
    string NearestAuthor,
    string NearestTitle,
    double AuthorPurityAt10,
    int? TargetTokenCount);

public class StylometryRunRepository
{
    /// <summary>
    /// Bump when ComputeDelta's behaviour changes in a way that makes new runs
    /// incomparable with old ones.
    ///
    /// v1 - initial saved-run support. Pool de-duplicated by WorkId, elision
    ///      marks stripped, accent folding optional.
    ///
    /// Runs carry the version they were produced under, and the analysis form
    /// refuses to build a reference distribution across versions. That refusal
    /// is the point: the alternative is a chart that silently mixes two
    /// different algorithms and looks fine.
    /// </summary>
    public const int CurrentAlgorithmVersion = 1;

    /// <summary>
    /// Saves a run and its full neighbour list in one transaction.
    ///
    /// The whole neighbour list is written, not the visible top 20. Storage is
    /// negligible and the reference-distribution analysis needs to find the
    /// first outsider wherever it falls - which for a strongly-attributed work
    /// can be well past rank 20.
    /// </summary>
    public async Task<long> SaveRunAsync(
        int targetWorkId,
        int targetEditionId,
        string targetAuthorName,
        string targetWorkTitle,
        string language,
        StylometrySettings settings,
        int poolSize,
        int targetTokenCount,
        IReadOnlyList<(int WorkId, string AuthorName, string WorkTitle, double Delta)> orderedResults,
        IReadOnlyList<(string Word, double Frequency)> fingerprint,
        string? label = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        long runId;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"
                INSERT INTO StylometryRuns
                    (CreatedUtc, TargetWorkId, TargetEditionId, TargetAuthorName, TargetWorkTitle,
                     Language, FeatureWordCount, FoldAccents, StripElisionMarks, PoolSize,
                     AlgorithmVersion, Label, Notes, TargetTokenCount, ChunkSize)
                VALUES
                    (@CreatedUtc, @TargetWorkId, @TargetEditionId, @TargetAuthorName, @TargetWorkTitle,
                     @Language, @FeatureWordCount, @FoldAccents, @StripElisionMarks, @PoolSize,
                     @AlgorithmVersion, @Label, @Notes, @TargetTokenCount, @ChunkSize);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@CreatedUtc", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@TargetWorkId", targetWorkId);
            cmd.Parameters.AddWithValue("@TargetEditionId", targetEditionId);
            cmd.Parameters.AddWithValue("@TargetAuthorName", targetAuthorName);
            cmd.Parameters.AddWithValue("@TargetWorkTitle", targetWorkTitle);
            cmd.Parameters.AddWithValue("@Language", language);
            cmd.Parameters.AddWithValue("@FeatureWordCount", settings.FeatureWordCount);
            cmd.Parameters.AddWithValue("@FoldAccents", settings.FoldAccents ? 1 : 0);
            cmd.Parameters.AddWithValue("@StripElisionMarks", settings.StripElisionMarks ? 1 : 0);
            cmd.Parameters.AddWithValue("@PoolSize", poolSize);
            cmd.Parameters.AddWithValue("@AlgorithmVersion", settings.AlgorithmVersion);
            cmd.Parameters.AddWithValue("@Label", (object?)label ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TargetTokenCount", targetTokenCount);
            cmd.Parameters.AddWithValue("@ChunkSize", settings.ChunkSize);

            runId = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        }

        // Parameters are created once and their values reassigned per row.
        // Rebuilding the command for each of a few thousand neighbours is the
        // difference between a save that is instant and one that is noticed.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"
                INSERT INTO StylometryRunResults (RunId, Rank, WorkId, AuthorName, WorkTitle, Delta)
                VALUES (@RunId, @Rank, @WorkId, @AuthorName, @WorkTitle, @Delta);";

            var pRunId = cmd.Parameters.Add("@RunId", SqliteType.Integer);
            var pRank = cmd.Parameters.Add("@Rank", SqliteType.Integer);
            var pWorkId = cmd.Parameters.Add("@WorkId", SqliteType.Integer);
            var pAuthor = cmd.Parameters.Add("@AuthorName", SqliteType.Text);
            var pTitle = cmd.Parameters.Add("@WorkTitle", SqliteType.Text);
            var pDelta = cmd.Parameters.Add("@Delta", SqliteType.Real);

            pRunId.Value = runId;

            for (var i = 0; i < orderedResults.Count; i++)
            {
                var r = orderedResults[i];
                pRank.Value = i + 1;
                pWorkId.Value = r.WorkId;
                pAuthor.Value = r.AuthorName;
                pTitle.Value = r.WorkTitle;
                pDelta.Value = r.Delta;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"
                INSERT INTO StylometryRunFeatures (RunId, Rank, Word, RelativeFrequency)
                VALUES (@RunId, @Rank, @Word, @Frequency);";

            var pRunId = cmd.Parameters.Add("@RunId", SqliteType.Integer);
            var pRank = cmd.Parameters.Add("@Rank", SqliteType.Integer);
            var pWord = cmd.Parameters.Add("@Word", SqliteType.Text);
            var pFreq = cmd.Parameters.Add("@Frequency", SqliteType.Real);

            pRunId.Value = runId;

            for (var i = 0; i < fingerprint.Count; i++)
            {
                pRank.Value = i + 1;
                pWord.Value = fingerprint[i].Word;
                pFreq.Value = fingerprint[i].Frequency;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await tx.CommitAsync(cancellationToken);
        return runId;
    }

    public async Task<List<StylometryRunSummary>> GetAllRunsAsync(CancellationToken cancellationToken = default)
    {
        var runs = new List<StylometryRunSummary>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT RunId, CreatedUtc, TargetWorkId, TargetAuthorName, TargetWorkTitle, Language,
                   FeatureWordCount, FoldAccents, StripElisionMarks, AlgorithmVersion, PoolSize, Label,
                   COALESCE(ChunkSize, 0)
            FROM StylometryRuns
            ORDER BY TargetAuthorName, TargetWorkTitle, CreatedUtc DESC;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(new StylometryRunSummary(
                reader.GetInt64(0),
                DateTime.TryParse(reader.GetString(1), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var created)
                    ? created
                    : DateTime.MinValue,
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                new StylometrySettings(
                    reader.GetInt32(6),
                    reader.GetInt32(7) != 0,
                    reader.GetInt32(8) != 0,
                    reader.GetInt32(9),
                    reader.GetInt32(12)),
                reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return runs;
    }

    public async Task<List<StylometryNeighbor>> GetNeighborsAsync(
        long runId, int? limit = null, CancellationToken cancellationToken = default)
    {
        var neighbors = new List<StylometryNeighbor>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Rank, WorkId, AuthorName, WorkTitle, Delta
            FROM StylometryRunResults
            WHERE RunId = @RunId
            ORDER BY Rank" + (limit.HasValue ? " LIMIT @Limit;" : ";");
        cmd.Parameters.AddWithValue("@RunId", runId);
        if (limit.HasValue) cmd.Parameters.AddWithValue("@Limit", limit.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            neighbors.Add(new StylometryNeighbor(
                reader.GetInt32(0), reader.GetInt32(1),
                reader.GetString(2), reader.GetString(3), reader.GetDouble(4)));
        }

        return neighbors;
    }

    public async Task<List<StylometryFeature>> GetFeaturesAsync(
        long runId, CancellationToken cancellationToken = default)
    {
        var features = new List<StylometryFeature>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Rank, Word, RelativeFrequency
            FROM StylometryRunFeatures WHERE RunId = @RunId ORDER BY Rank;";
        cmd.Parameters.AddWithValue("@RunId", runId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            features.Add(new StylometryFeature(
                reader.GetInt32(0), reader.GetString(1), reader.GetDouble(2)));
        }

        return features;
    }

    /// <summary>
    /// The per-run summary numbers, computed in SQL rather than by pulling every
    /// neighbour list into memory. Optionally narrowed to one settings profile,
    /// which the analysis form always does - a reference distribution built
    /// across mixed preprocessing would be meaningless.
    ///
    /// DepthToFirstOutsider is a correlated subquery rather than a join because
    /// it is a MIN over a filtered subset per run, and IX_StylometryRunResults_Author
    /// makes it an index seek.
    ///
    /// AuthorPurityAt10 is the share of the ten nearest neighbours sharing the
    /// target's author. It complements depth: depth is where agreement first
    /// breaks, purity is how much agreement there is overall. A work can have
    /// shallow depth but high purity (one intruder near the top) or the reverse.
    /// </summary>
    public async Task<List<StylometryRunMetrics>> GetRunMetricsAsync(
        string? language = null,
        StylometrySettings? settingsFilter = null,
        CancellationToken cancellationToken = default)
    {
        var metrics = new List<StylometryRunMetrics>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        var where = new List<string>();
        if (language != null)
        {
            where.Add("r.Language = @Language");
            cmd.Parameters.AddWithValue("@Language", language);
        }
        if (settingsFilter != null)
        {
            where.Add("r.FeatureWordCount = @Features AND r.FoldAccents = @Fold " +
                      "AND r.StripElisionMarks = @Strip AND r.AlgorithmVersion = @AlgVersion " +
                      "AND COALESCE(r.ChunkSize, 0) = @ChunkSize");
            cmd.Parameters.AddWithValue("@Features", settingsFilter.FeatureWordCount);
            cmd.Parameters.AddWithValue("@Fold", settingsFilter.FoldAccents ? 1 : 0);
            cmd.Parameters.AddWithValue("@Strip", settingsFilter.StripElisionMarks ? 1 : 0);
            cmd.Parameters.AddWithValue("@AlgVersion", settingsFilter.AlgorithmVersion);
            cmd.Parameters.AddWithValue("@ChunkSize", settingsFilter.ChunkSize);
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;

        cmd.CommandText = $@"
            SELECT
                r.RunId,
                r.TargetAuthorName,
                r.TargetWorkTitle,
                r.FeatureWordCount,
                r.FoldAccents,
                r.StripElisionMarks,
                r.AlgorithmVersion,
                COALESCE(r.ChunkSize, 0)                                          AS ChunkSize,
                (SELECT MIN(x.Rank) FROM StylometryRunResults x
                  WHERE x.RunId = r.RunId AND x.AuthorName <> r.TargetAuthorName)  AS DepthToFirstOutsider,
                (SELECT MIN(x.Delta) FROM StylometryRunResults x WHERE x.RunId = r.RunId) AS DeltaFloor,
                (SELECT x.AuthorName FROM StylometryRunResults x
                  WHERE x.RunId = r.RunId ORDER BY x.Rank LIMIT 1)                 AS NearestAuthor,
                (SELECT x.WorkTitle FROM StylometryRunResults x
                  WHERE x.RunId = r.RunId ORDER BY x.Rank LIMIT 1)                 AS NearestTitle,
                (SELECT CAST(SUM(CASE WHEN x.AuthorName = r.TargetAuthorName THEN 1 ELSE 0 END) AS REAL)
                        / MAX(COUNT(*), 1)
                   FROM (SELECT AuthorName FROM StylometryRunResults
                          WHERE RunId = r.RunId ORDER BY Rank LIMIT 10) x)         AS PurityAt10,
                r.TargetTokenCount
            FROM StylometryRuns r
            {whereClause}
            ORDER BY r.TargetAuthorName, r.TargetWorkTitle;";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            metrics.Add(new StylometryRunMetrics(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                new StylometrySettings(
                    reader.GetInt32(3), reader.GetInt32(4) != 0,
                    reader.GetInt32(5) != 0, reader.GetInt32(6), reader.GetInt32(7)),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? 0d : reader.GetDouble(9),
                reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                reader.IsDBNull(12) ? 0d : reader.GetDouble(12),
                reader.IsDBNull(13) ? null : reader.GetInt32(13)));
        }

        return metrics;
    }

    /// <summary>The distinct settings profiles that have saved runs, for the filter dropdown.</summary>
    public async Task<List<StylometrySettings>> GetSettingsProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        var profiles = new List<StylometrySettings>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT FeatureWordCount, FoldAccents, StripElisionMarks, AlgorithmVersion,
                   COALESCE(ChunkSize, 0)
            FROM StylometryRuns
            ORDER BY AlgorithmVersion DESC, FeatureWordCount, FoldAccents, StripElisionMarks, COALESCE(ChunkSize, 0);";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new StylometrySettings(
                reader.GetInt32(0), reader.GetInt32(1) != 0,
                reader.GetInt32(2) != 0, reader.GetInt32(3), reader.GetInt32(4)));
        }

        return profiles;
    }

    public async Task DeleteRunAsync(long runId, CancellationToken cancellationToken = default)
    {
        // Children go via ON DELETE CASCADE. That only fires because
        // OpenConnectionAsync sets PRAGMA foreign_keys = ON per connection -
        // without it the constraint is decorative and this would orphan rows.
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM StylometryRuns WHERE RunId = @RunId;";
        cmd.Parameters.AddWithValue("@RunId", runId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetLabelAsync(long runId, string? label, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE StylometryRuns SET Label = @Label WHERE RunId = @RunId;";
        cmd.Parameters.AddWithValue("@RunId", runId);
        cmd.Parameters.AddWithValue("@Label", (object?)label ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes every run matching a target and settings profile. Used before a
    /// batch re-run so repeated batches do not accumulate duplicates that would
    /// then be averaged into the same reference distribution.
    /// </summary>
    public async Task DeleteRunsForSettingsAsync(
        string language, StylometrySettings settings, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM StylometryRuns
            WHERE Language = @Language AND FeatureWordCount = @Features AND FoldAccents = @Fold
              AND StripElisionMarks = @Strip AND AlgorithmVersion = @AlgVersion
              AND COALESCE(ChunkSize, 0) = @ChunkSize;";
        cmd.Parameters.AddWithValue("@Language", language);
        cmd.Parameters.AddWithValue("@Features", settings.FeatureWordCount);
        cmd.Parameters.AddWithValue("@Fold", settings.FoldAccents ? 1 : 0);
        cmd.Parameters.AddWithValue("@Strip", settings.StripElisionMarks ? 1 : 0);
        cmd.Parameters.AddWithValue("@AlgVersion", settings.AlgorithmVersion);
        cmd.Parameters.AddWithValue("@ChunkSize", settings.ChunkSize);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
