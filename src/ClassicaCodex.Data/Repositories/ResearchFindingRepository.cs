using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public sealed class ResearchFindingRepository
{
    public async Task<List<ResearchFinding>> GetAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<ResearchFinding>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchFindingId,ResearchProjectId,ResearchQuestionId,Title,Statement,Status,
            ResearcherConclusion,AiCandidateSynthesis,AiModel,AiPrompt,AiGeneratedUtc,SortOrder,CreatedUtc,UpdatedUtc
            FROM ResearchFindings WHERE ResearchProjectId=@Project ORDER BY SortOrder,ResearchFindingId;";
        cmd.Parameters.AddWithValue("@Project", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadFinding(reader));
        return result;
    }

    public async Task<List<ResearchFindingEvidenceLink>> GetLinksAsync(
        long findingId, CancellationToken cancellationToken = default)
    {
        var result = new List<ResearchFindingEvidenceLink>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchFindingId,EvidenceItemId,Relationship,Note
            FROM ResearchFindingEvidence WHERE ResearchFindingId=@Finding ORDER BY EvidenceItemId;";
        cmd.Parameters.AddWithValue("@Finding", findingId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new ResearchFindingEvidenceLink
            {
                ResearchFindingId = reader.GetInt64(0),
                EvidenceItemId = reader.GetInt64(1),
                Relationship = Parse(reader.GetString(2), EvidenceRelationship.Contextualizes),
                Note = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        return result;
    }

    public async Task<long> SaveAsync(ResearchFinding finding, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(finding.Title) || string.IsNullOrWhiteSpace(finding.Statement))
            throw new ArgumentException("A finding needs both a title and a proposition.", nameof(finding));
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await ValidateOwnershipAsync(conn, finding, cancellationToken);
        var isNew = finding.ResearchFindingId == 0;
        var now = DateTime.UtcNow;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = isNew
            ? @"INSERT INTO ResearchFindings
                (ResearchProjectId,ResearchQuestionId,Title,Statement,Status,ResearcherConclusion,
                 AiCandidateSynthesis,AiModel,AiPrompt,AiGeneratedUtc,SortOrder,CreatedUtc,UpdatedUtc)
                VALUES (@Project,@Question,@Title,@Statement,@Status,@Conclusion,@AiCandidate,@AiModel,
                        @AiPrompt,@AiGenerated,@Sort,@Now,@Now); SELECT last_insert_rowid();"
            : @"UPDATE ResearchFindings SET ResearchQuestionId=@Question,Title=@Title,Statement=@Statement,
                Status=@Status,ResearcherConclusion=@Conclusion,AiCandidateSynthesis=@AiCandidate,AiModel=@AiModel,
                AiPrompt=@AiPrompt,AiGeneratedUtc=@AiGenerated,SortOrder=@Sort,UpdatedUtc=@Now
                WHERE ResearchFindingId=@Id AND ResearchProjectId=@Project; SELECT @Id;";
        if (!isNew) cmd.Parameters.AddWithValue("@Id", finding.ResearchFindingId);
        cmd.Parameters.AddWithValue("@Project", finding.ResearchProjectId);
        cmd.Parameters.AddWithValue("@Question", Db(finding.ResearchQuestionId));
        cmd.Parameters.AddWithValue("@Title", finding.Title.Trim());
        cmd.Parameters.AddWithValue("@Statement", finding.Statement.Trim());
        cmd.Parameters.AddWithValue("@Status", Store(finding.Status));
        cmd.Parameters.AddWithValue("@Conclusion", Db(finding.ResearcherConclusion));
        cmd.Parameters.AddWithValue("@AiCandidate", Db(finding.AiCandidateSynthesis));
        cmd.Parameters.AddWithValue("@AiModel", Db(finding.AiModel));
        cmd.Parameters.AddWithValue("@AiPrompt", Db(finding.AiPrompt));
        cmd.Parameters.AddWithValue("@AiGenerated", finding.AiGeneratedUtc is null ? DBNull.Value : finding.AiGeneratedUtc.Value.ToString("O"));
        cmd.Parameters.AddWithValue("@Sort", finding.SortOrder);
        cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        finding.ResearchFindingId = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        if (finding.CreatedUtc == default) finding.CreatedUtc = now;
        finding.UpdatedUtc = now;
        await LogAsync(finding.ResearchProjectId,
            isNew ? ResearchLogEntryKind.FindingAdded : ResearchLogEntryKind.FindingUpdated,
            $"{(isNew ? "Added" : "Updated")} finding: {finding.Title}", finding.Status.ToString(), cancellationToken);
        return finding.ResearchFindingId;
    }

    public async Task SaveLinksAsync(long findingId, IReadOnlyCollection<ResearchFindingEvidenceLink> links,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT ResearchProjectId FROM ResearchFindings WHERE ResearchFindingId=@Id;";
            read.Parameters.AddWithValue("@Id", findingId);
            var value = await read.ExecuteScalarAsync(cancellationToken);
            if (value == null) throw new ArgumentException("Finding does not exist.", nameof(findingId));
            projectId = Convert.ToInt64(value);
        }
        foreach (var link in links)
        {
            await using var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM EvidenceItems WHERE EvidenceItemId=@Evidence AND ResearchProjectId=@Project;";
            check.Parameters.AddWithValue("@Evidence", link.EvidenceItemId);
            check.Parameters.AddWithValue("@Project", projectId);
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new ArgumentException("Every linked evidence item must belong to the finding's project.", nameof(links));
        }
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        await using (var delete = conn.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM ResearchFindingEvidence WHERE ResearchFindingId=@Finding;";
            delete.Parameters.AddWithValue("@Finding", findingId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var link in links)
        {
            await using var insert = conn.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = @"INSERT INTO ResearchFindingEvidence
                (ResearchFindingId,EvidenceItemId,Relationship,Note) VALUES (@Finding,@Evidence,@Relationship,@Note);";
            insert.Parameters.AddWithValue("@Finding", findingId);
            insert.Parameters.AddWithValue("@Evidence", link.EvidenceItemId);
            insert.Parameters.AddWithValue("@Relationship", Store(link.Relationship));
            insert.Parameters.AddWithValue("@Note", Db(link.Note));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        await LogAsync(projectId, ResearchLogEntryKind.FindingEvidenceChanged,
            $"Updated finding evidence links ({links.Count})", null, cancellationToken);
    }

    public async Task DeleteAsync(long findingId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        string title;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT ResearchProjectId,Title FROM ResearchFindings WHERE ResearchFindingId=@Id;";
            read.Parameters.AddWithValue("@Id", findingId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return;
            projectId = reader.GetInt64(0);
            title = reader.GetString(1);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ResearchFindings WHERE ResearchFindingId=@Id;";
        cmd.Parameters.AddWithValue("@Id", findingId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LogAsync(projectId, ResearchLogEntryKind.FindingRemoved, $"Removed finding: {title}", null, cancellationToken);
    }

    private static async Task ValidateOwnershipAsync(SqliteConnection conn, ResearchFinding finding, CancellationToken ct)
    {
        await using var project = conn.CreateCommand();
        project.CommandText = "SELECT COUNT(*) FROM ResearchProjects WHERE ResearchProjectId=@Project;";
        project.Parameters.AddWithValue("@Project", finding.ResearchProjectId);
        if (Convert.ToInt32(await project.ExecuteScalarAsync(ct)) != 1)
            throw new ArgumentException("Research project does not exist.", nameof(finding));
        if (finding.ResearchQuestionId is long questionId)
        {
            await using var question = conn.CreateCommand();
            question.CommandText = "SELECT COUNT(*) FROM ResearchQuestions WHERE ResearchQuestionId=@Question AND ResearchProjectId=@Project;";
            question.Parameters.AddWithValue("@Question", questionId);
            question.Parameters.AddWithValue("@Project", finding.ResearchProjectId);
            if (Convert.ToInt32(await question.ExecuteScalarAsync(ct)) != 1)
                throw new ArgumentException("Question does not belong to this project.", nameof(finding));
        }
    }

    private static ResearchFinding ReadFinding(SqliteDataReader r) => new()
    {
        ResearchFindingId = r.GetInt64(0),
        ResearchProjectId = r.GetInt64(1),
        ResearchQuestionId = r.IsDBNull(2) ? null : r.GetInt64(2),
        Title = r.GetString(3),
        Statement = r.GetString(4),
        Status = Parse(r.GetString(5), ResearchFindingStatus.Hypothesis),
        ResearcherConclusion = Text(r, 6),
        AiCandidateSynthesis = Text(r, 7),
        AiModel = Text(r, 8),
        AiPrompt = Text(r, 9),
        AiGeneratedUtc = r.IsDBNull(10) ? null : DateTime.Parse(r.GetString(10), null,
            System.Globalization.DateTimeStyles.RoundtripKind),
        SortOrder = r.GetInt32(11),
        CreatedUtc = DateTime.Parse(r.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedUtc = DateTime.Parse(r.GetString(13), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };
    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static string Store<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
    private static T Parse<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static object Db(long? value) => value.HasValue ? value.Value : DBNull.Value;
    private static Task LogAsync(long projectId, ResearchLogEntryKind kind, string summary,
        string? details, CancellationToken ct) => new ResearchRepository().AddSystemResearchLogEntryAsync(
        new ResearchLogEntry { ResearchProjectId = projectId, Kind = kind, Summary = summary, Details = details }, ct);
}
