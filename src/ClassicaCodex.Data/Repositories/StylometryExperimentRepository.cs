using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>What sort of experiment a saved row came from.</summary>
public static class ExperimentKinds
{
    public const string Perturbation = "perturbation";
    public const string Validation = "validation";
    public const string ParameterGrid = "grid";
}

/// <summary>
/// Everything needed to rebuild an experiment, and nothing that can be derived.
/// </summary>
/// <param name="Kind">Which bench produced it - see <see cref="ExperimentKinds"/>.</param>
/// <param name="TargetAuthor">The author whose works were validated or contaminated.</param>
/// <param name="Language">Language of the pool, or null when not recorded.</param>
/// <param name="PoolSummary">The pool as it was shown on screen, for reading.</param>
/// <param name="PoolWorkIds">
/// The exact works compared against, not a description of them. "Euripides
/// against Aeschylus and Sophocles" is ambiguous the moment a corpus is
/// re-ingested and a work is added or filtered out - and the pool moved the
/// margin by more than twelvefold in testing.
/// </param>
/// <param name="Seed">
/// Required, with no default. An experiment whose seed was not recorded cannot
/// be regenerated, and a row silently claiming seed 0 would be worse than one
/// that refused to be written.
/// </param>
/// <param name="Iterations">Mixtures drawn per level.</param>
/// <param name="ChunkSize">Sample size in tokens.</param>
/// <param name="FeatureWordCount">How many most-frequent words formed the feature set.</param>
/// <param name="FoldAccents">Whether accents were folded during tokenisation.</param>
/// <param name="AlgorithmVersion">Engine version, so runs across versions are not mixed.</param>
/// <param name="Parameters">Type-specific settings, as JSON in the database.</param>
/// <param name="Metrics">Type-specific results worth listing without loading the rows.</param>
/// <param name="Label">What the run was for, written by whoever saved it.</param>
/// <param name="Notes">Anything longer.</param>
public record ExperimentDefinition(
    string Kind,
    string TargetAuthor,
    string? Language,
    string PoolSummary,
    IReadOnlyList<int> PoolWorkIds,
    int Seed,
    int Iterations,
    int ChunkSize,
    int FeatureWordCount,
    bool FoldAccents,
    int AlgorithmVersion,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyDictionary<string, string> Metrics,
    string? Label = null,
    string? Notes = null);

/// <summary>One work at one level under one donor.</summary>
public record ExperimentRow(
    int RowIndex,
    int? WorkId,
    string WorkTitle,
    string Donor,
    double Level,
    double MeanMargin,
    double StdDev,
    double BaselineMargin,
    int Recovered,
    int Trials,
    string? NearestAuthor,
    // How many of the level's mixtures agreed on that nearest author. Zero on
    // rows written before v16, which did not record it - displayed as a bare
    // author name, which is what they honestly are. A doc comment cannot sit on
    // a positional record parameter; only the record itself takes those.
    int NearestCount,
    int TokenCount);

public record ExperimentSummary(
    long ExperimentId,
    DateTime CreatedUtc,
    string Kind,
    string TargetAuthor,
    string PoolSummary,
    int Seed,
    int Iterations,
    int ChunkSize,
    int FeatureWordCount,
    bool FoldAccents,
    IReadOnlyDictionary<string, string> Metrics,
    string? Label)
{
    /// <summary>
    /// The settings that make two experiments comparable, as one string.
    ///
    /// Deliberately includes the seed. Two perturbation runs at different seeds
    /// are the same experiment repeated, which is worth comparing; two at the
    /// same seed on the same pool are the same run twice, which is not. The key
    /// makes the difference visible in a list.
    /// </summary>
    public string ProfileKey =>
        $"{Kind}/{ChunkSize}tok/{FeatureWordCount}mfw/{(FoldAccents ? "fold" : "keep")}/" +
        $"seed{Seed}/{Iterations}it";

    public override string ToString() =>
        $"{CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {TargetAuthor} - {PoolSummary}  [{ProfileKey}]" +
        (string.IsNullOrWhiteSpace(Label) ? "" : $"  \"{Label}\"");
}

/// <summary>
/// Saved runs from the validation bench.
///
/// WHY THESE ARE NOT StylometryRuns. A Delta run has one target and one ordered
/// neighbour list. A bench experiment has a target AUTHOR, a donor set, a series
/// of injection levels, a seed and an iteration count, and produces one row per
/// work per level per donor - a nineteen-work perturbation sweep is 228 rows and
/// several thousand synthetic mixtures behind them. Storing that in a table
/// shaped for the other thing would leave most columns null in most rows.
///
/// Kind carries what sort of run it was, so the next experiment type is a new
/// value rather than a new table. Parameters and Metrics are JSON because every
/// type has different ones and a column apiece would mean a migration apiece.
/// The columns that are broken out are the ones a query groups by - and they are
/// exactly the settings that make two runs incomparable when they differ.
/// </summary>
public class StylometryExperimentRepository
{
    public async Task<long> SaveAsync(
        ExperimentDefinition definition,
        IReadOnlyList<ExperimentRow> rows,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        long id;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"
                INSERT INTO StylometryExperiments
                    (CreatedUtc, Kind, TargetAuthor, Language, PoolSummary, PoolWorkIds,
                     Seed, Iterations, ChunkSize, FeatureWordCount, FoldAccents,
                     AlgorithmVersion, Parameters, Metrics, Label, Notes)
                VALUES
                    (@CreatedUtc, @Kind, @TargetAuthor, @Language, @PoolSummary, @PoolWorkIds,
                     @Seed, @Iterations, @ChunkSize, @FeatureWordCount, @FoldAccents,
                     @AlgorithmVersion, @Parameters, @Metrics, @Label, @Notes);
                SELECT last_insert_rowid();";

            cmd.Parameters.AddWithValue("@CreatedUtc", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@Kind", definition.Kind);
            cmd.Parameters.AddWithValue("@TargetAuthor", definition.TargetAuthor);
            cmd.Parameters.AddWithValue("@Language", (object?)definition.Language ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PoolSummary", definition.PoolSummary);
            cmd.Parameters.AddWithValue("@PoolWorkIds", JsonSerializer.Serialize(definition.PoolWorkIds));
            cmd.Parameters.AddWithValue("@Seed", definition.Seed);
            cmd.Parameters.AddWithValue("@Iterations", definition.Iterations);
            cmd.Parameters.AddWithValue("@ChunkSize", definition.ChunkSize);
            cmd.Parameters.AddWithValue("@FeatureWordCount", definition.FeatureWordCount);
            cmd.Parameters.AddWithValue("@FoldAccents", definition.FoldAccents ? 1 : 0);
            cmd.Parameters.AddWithValue("@AlgorithmVersion", definition.AlgorithmVersion);
            cmd.Parameters.AddWithValue("@Parameters", JsonSerializer.Serialize(definition.Parameters));
            cmd.Parameters.AddWithValue("@Metrics", JsonSerializer.Serialize(definition.Metrics));
            cmd.Parameters.AddWithValue("@Label", (object?)definition.Label ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Notes", (object?)definition.Notes ?? DBNull.Value);

            id = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = @"
                INSERT INTO StylometryExperimentRows
                    (ExperimentId, RowIndex, WorkId, WorkTitle, Donor, Level, MeanMargin,
                     StdDev, BaselineMargin, Recovered, Trials, NearestAuthor, NearestCount, TokenCount)
                VALUES
                    (@ExperimentId, @RowIndex, @WorkId, @WorkTitle, @Donor, @Level, @MeanMargin,
                     @StdDev, @BaselineMargin, @Recovered, @Trials, @NearestAuthor, @NearestCount, @TokenCount);";

            foreach (var row in rows)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ExperimentId", id);
                cmd.Parameters.AddWithValue("@RowIndex", row.RowIndex);
                cmd.Parameters.AddWithValue("@WorkId", (object?)row.WorkId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@WorkTitle", row.WorkTitle);
                cmd.Parameters.AddWithValue("@Donor", row.Donor);
                cmd.Parameters.AddWithValue("@Level", row.Level);
                cmd.Parameters.AddWithValue("@MeanMargin", row.MeanMargin);
                cmd.Parameters.AddWithValue("@StdDev", row.StdDev);
                cmd.Parameters.AddWithValue("@BaselineMargin", row.BaselineMargin);
                cmd.Parameters.AddWithValue("@Recovered", row.Recovered);
                cmd.Parameters.AddWithValue("@Trials", row.Trials);
                cmd.Parameters.AddWithValue("@NearestAuthor", (object?)row.NearestAuthor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NearestCount", row.NearestCount);
                cmd.Parameters.AddWithValue("@TokenCount", row.TokenCount);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await tx.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// Saved experiments, newest first, optionally of one kind.
    /// </summary>
    public async Task<List<ExperimentSummary>> GetAllAsync(
        string? kind = null, CancellationToken cancellationToken = default)
    {
        var results = new List<ExperimentSummary>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT ExperimentId, CreatedUtc, Kind, TargetAuthor, PoolSummary, Seed, Iterations,
                   ChunkSize, FeatureWordCount, FoldAccents, Metrics, Label
            FROM StylometryExperiments
            WHERE (@Kind IS NULL OR Kind = @Kind)
            ORDER BY CreatedUtc DESC;";
        cmd.Parameters.AddWithValue("@Kind", (object?)kind ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ExperimentSummary(
                reader.GetInt64(0),
                DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9) != 0,
                Deserialize(reader.GetString(10)),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return results;
    }

    public async Task<List<ExperimentRow>> GetRowsAsync(
        long experimentId, CancellationToken cancellationToken = default)
    {
        var rows = new List<ExperimentRow>();

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT RowIndex, WorkId, WorkTitle, Donor, Level, MeanMargin, StdDev,
                   BaselineMargin, Recovered, Trials, NearestAuthor, NearestCount, TokenCount
            FROM StylometryExperimentRows
            WHERE ExperimentId = @Id
            ORDER BY RowIndex;";
        cmd.Parameters.AddWithValue("@Id", experimentId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ExperimentRow(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetInt32(11),
                reader.GetInt32(12)));
        }

        return rows;
    }

    /// <summary>
    /// The definition of one experiment, for rebuilding it.
    /// </summary>
    public async Task<ExperimentDefinition?> GetDefinitionAsync(
        long experimentId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
            SELECT Kind, TargetAuthor, Language, PoolSummary, PoolWorkIds, Seed, Iterations,
                   ChunkSize, FeatureWordCount, FoldAccents, AlgorithmVersion,
                   Parameters, Metrics, Label, Notes
            FROM StylometryExperiments
            WHERE ExperimentId = @Id;";
        cmd.Parameters.AddWithValue("@Id", experimentId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new ExperimentDefinition(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            JsonSerializer.Deserialize<List<int>>(reader.GetString(4)) ?? new List<int>(),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9) != 0,
            reader.GetInt32(10),
            Deserialize(reader.GetString(11)),
            Deserialize(reader.GetString(12)),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14));
    }

    public async Task DeleteAsync(long experimentId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        // The rows go with it through ON DELETE CASCADE, which SQLite only
        // honours when foreign keys are enabled - so they are deleted here as
        // well rather than trusting a pragma set somewhere else.
        cmd.CommandText = @"
            DELETE FROM StylometryExperimentRows WHERE ExperimentId = @Id;
            DELETE FROM StylometryExperiments WHERE ExperimentId = @Id;";
        cmd.Parameters.AddWithValue("@Id", experimentId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads a JSON dictionary, returning an empty one rather than throwing on
    /// anything unexpected.
    ///
    /// These columns hold whatever an experiment type chose to record, and a
    /// future type writing a shape this version does not expect should make the
    /// row unreadable in one field rather than making the whole experiment
    /// unopenable.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
