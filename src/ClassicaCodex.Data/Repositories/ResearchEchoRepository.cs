using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public sealed class ResearchEchoRepository
{
    public async Task<List<ResearchEchoInvestigation>> GetInvestigationsAsync(
        long projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<ResearchEchoInvestigation>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchEchoInvestigationId,ResearchProjectId,ResearchQuestionId,
            ResearchFindingId,Method,Title,SourceWorkId,SourceTextNodeId,SourceWorkCtsUrn,
            SourceEditionCtsUrn,SourceCitationRef,SourceText,TargetScope,Settings,AiModel,AiPrompt,
            AiGeneratedUtc,CreatedUtc,UpdatedUtc FROM ResearchEchoInvestigations
            WHERE ResearchProjectId=@Project ORDER BY CreatedUtc DESC,ResearchEchoInvestigationId DESC;";
        cmd.Parameters.AddWithValue("@Project", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadInvestigation(reader));
        return result;
    }

    public async Task<List<ResearchEchoResult>> GetResultsAsync(
        long investigationId, CancellationToken cancellationToken = default)
    {
        var result = new List<ResearchEchoResult>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchEchoResultId,ResearchEchoInvestigationId,TargetWorkId,
            TargetTextNodeId,TargetAuthorName,TargetWorkTitle,TargetWorkCtsUrn,TargetEditionCtsUrn,
            TargetCitationRef,TargetText,Score,ScoreLabel,Rationale,Disposition,ResearcherNote,
            EvidenceItemId,SortOrder,CreatedUtc,UpdatedUtc FROM ResearchEchoResults
            WHERE ResearchEchoInvestigationId=@Investigation ORDER BY SortOrder,ResearchEchoResultId;";
        cmd.Parameters.AddWithValue("@Investigation", investigationId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadResult(reader));
        return result;
    }

    public async Task<ResearchEchoInvestigation> SaveCaptureAsync(
        long projectId, long? questionId, long? findingId, EchoCaptureRequest capture,
        CancellationToken cancellationToken = default)
    {
        var targets = new List<(EchoCaptureCandidate Candidate, PassageResearchIdentity Identity)>();
        var textNodes = new TextNodeRepository();
        foreach (var candidate in capture.Candidates)
        {
            var identity = await textNodes.GetPassageResearchIdentityAsync(candidate.TextNodeId, cancellationToken);
            if (identity != null) targets.Add((candidate, identity));
        }
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await ValidateScopeAsync(conn, projectId, questionId, findingId, capture.Source.WorkId, cancellationToken);
        var now = DateTime.UtcNow;
        var investigation = new ResearchEchoInvestigation
        {
            ResearchProjectId = projectId, ResearchQuestionId = questionId, ResearchFindingId = findingId,
            Method = capture.Method, Title = capture.Title, SourceWorkId = capture.Source.WorkId,
            SourceTextNodeId = capture.Source.TextNodeId, SourceWorkCtsUrn = capture.Source.WorkCtsUrn,
            SourceEditionCtsUrn = capture.Source.EditionCtsUrn, SourceCitationRef = capture.Source.CitationRef,
            SourceText = capture.Source.Text, TargetScope = capture.TargetScope, Settings = capture.Settings,
            AiModel = capture.AiModel, AiPrompt = capture.AiPrompt, AiGeneratedUtc = capture.AiGeneratedUtc,
            CreatedUtc = now, UpdatedUtc = now
        };
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = @"INSERT INTO ResearchEchoInvestigations
                (ResearchProjectId,ResearchQuestionId,ResearchFindingId,Method,Title,SourceWorkId,
                 SourceTextNodeId,SourceWorkCtsUrn,SourceEditionCtsUrn,SourceCitationRef,SourceText,
                 TargetScope,Settings,AiModel,AiPrompt,AiGeneratedUtc,CreatedUtc,UpdatedUtc)
                VALUES (@Project,@Question,@Finding,@Method,@Title,@SourceWork,@SourceNode,@WorkUrn,
                        @EditionUrn,@Citation,@Text,@Scope,@Settings,@Model,@Prompt,@Generated,@Now,@Now);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@Project", projectId);
            cmd.Parameters.AddWithValue("@Question", Db(questionId));
            cmd.Parameters.AddWithValue("@Finding", Db(findingId));
            cmd.Parameters.AddWithValue("@Method", Store(capture.Method));
            cmd.Parameters.AddWithValue("@Title", capture.Title.Trim());
            cmd.Parameters.AddWithValue("@SourceWork", capture.Source.WorkId);
            cmd.Parameters.AddWithValue("@SourceNode", capture.Source.TextNodeId);
            cmd.Parameters.AddWithValue("@WorkUrn", capture.Source.WorkCtsUrn);
            cmd.Parameters.AddWithValue("@EditionUrn", capture.Source.EditionCtsUrn);
            cmd.Parameters.AddWithValue("@Citation", capture.Source.CitationRef);
            cmd.Parameters.AddWithValue("@Text", capture.Source.Text);
            cmd.Parameters.AddWithValue("@Scope", Db(capture.TargetScope));
            cmd.Parameters.AddWithValue("@Settings", Db(capture.Settings));
            cmd.Parameters.AddWithValue("@Model", Db(capture.AiModel));
            cmd.Parameters.AddWithValue("@Prompt", Db(capture.AiPrompt));
            cmd.Parameters.AddWithValue("@Generated", capture.AiGeneratedUtc is null ? DBNull.Value : capture.AiGeneratedUtc.Value.ToString("O"));
            cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
            investigation.ResearchEchoInvestigationId = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        }
        for (var i = 0; i < targets.Count; i++)
        {
            var (candidate, target) = targets[i];
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = @"INSERT INTO ResearchEchoResults
                (ResearchEchoInvestigationId,TargetWorkId,TargetTextNodeId,TargetAuthorName,TargetWorkTitle,
                 TargetWorkCtsUrn,TargetEditionCtsUrn,TargetCitationRef,TargetText,Score,ScoreLabel,Rationale,
                 Disposition,SortOrder,CreatedUtc,UpdatedUtc)
                VALUES (@Investigation,@Work,@Node,@Author,@Title,@WorkUrn,@EditionUrn,@Citation,@Text,
                        @Score,@ScoreLabel,@Rationale,'pending',@Sort,@Now,@Now);";
            cmd.Parameters.AddWithValue("@Investigation", investigation.ResearchEchoInvestigationId);
            cmd.Parameters.AddWithValue("@Work", target.WorkId);
            cmd.Parameters.AddWithValue("@Node", target.TextNodeId);
            cmd.Parameters.AddWithValue("@Author", target.AuthorName);
            cmd.Parameters.AddWithValue("@Title", target.WorkTitle);
            cmd.Parameters.AddWithValue("@WorkUrn", target.WorkCtsUrn);
            cmd.Parameters.AddWithValue("@EditionUrn", target.EditionCtsUrn);
            cmd.Parameters.AddWithValue("@Citation", target.CitationRef);
            cmd.Parameters.AddWithValue("@Text", target.Text);
            cmd.Parameters.AddWithValue("@Score", candidate.Score is null ? DBNull.Value : candidate.Score.Value);
            cmd.Parameters.AddWithValue("@ScoreLabel", Db(candidate.ScoreLabel));
            cmd.Parameters.AddWithValue("@Rationale", Db(candidate.Rationale));
            cmd.Parameters.AddWithValue("@Sort", i);
            cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        await LogAsync(projectId, ResearchLogEntryKind.EchoInvestigationSaved,
            $"Saved echo investigation: {capture.Title}",
            $"{capture.Method}; {targets.Count} locally resolved candidate(s)", cancellationToken);
        return investigation;
    }

    public async Task SaveReviewAsync(long resultId, ResearchEchoDisposition disposition,
        string? researcherNote, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = @"SELECT i.ResearchProjectId FROM ResearchEchoResults r
                JOIN ResearchEchoInvestigations i ON i.ResearchEchoInvestigationId=r.ResearchEchoInvestigationId
                WHERE r.ResearchEchoResultId=@Id;";
            read.Parameters.AddWithValue("@Id", resultId);
            var value = await read.ExecuteScalarAsync(cancellationToken);
            if (value == null) throw new ArgumentException("Echo result does not exist.", nameof(resultId));
            projectId = Convert.ToInt64(value);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE ResearchEchoResults SET Disposition=@Disposition,ResearcherNote=@Note,
            UpdatedUtc=@Now WHERE ResearchEchoResultId=@Id;";
        cmd.Parameters.AddWithValue("@Disposition", Store(disposition));
        cmd.Parameters.AddWithValue("@Note", Db(researcherNote));
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Id", resultId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LogAsync(projectId, ResearchLogEntryKind.EchoResultReviewed,
            $"Marked echo candidate {disposition.ToString().ToLowerInvariant()}", $"Result {resultId}", cancellationToken);
    }

    public async Task MarkPromotedAsync(long resultId, long evidenceId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = @"SELECT i.ResearchProjectId FROM ResearchEchoResults r
                JOIN ResearchEchoInvestigations i ON i.ResearchEchoInvestigationId=r.ResearchEchoInvestigationId
                JOIN EvidenceItems e ON e.EvidenceItemId=@Evidence AND e.ResearchProjectId=i.ResearchProjectId
                WHERE r.ResearchEchoResultId=@Result AND r.Disposition='accepted';";
            read.Parameters.AddWithValue("@Evidence", evidenceId);
            read.Parameters.AddWithValue("@Result", resultId);
            var value = await read.ExecuteScalarAsync(cancellationToken);
            if (value == null) throw new ArgumentException("Only an accepted result can be promoted within its own project.");
            projectId = Convert.ToInt64(value);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE ResearchEchoResults SET EvidenceItemId=@Evidence,UpdatedUtc=@Now WHERE ResearchEchoResultId=@Result;";
        cmd.Parameters.AddWithValue("@Evidence", evidenceId);
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Result", resultId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LogAsync(projectId, ResearchLogEntryKind.EchoResultPromoted,
            "Promoted accepted echo candidate to paired-passage evidence", $"Result {resultId}; evidence {evidenceId}", cancellationToken);
    }

    public async Task DeleteInvestigationAsync(long investigationId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ResearchEchoInvestigations WHERE ResearchEchoInvestigationId=@Id;";
        cmd.Parameters.AddWithValue("@Id", investigationId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidateScopeAsync(SqliteConnection conn, long projectId, long? questionId,
        long? findingId, int sourceWorkId, CancellationToken ct)
    {
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM ResearchProjects WHERE ResearchProjectId=@Project AND WorkId=@Work;";
            cmd.Parameters.AddWithValue("@Project", projectId);
            cmd.Parameters.AddWithValue("@Work", sourceWorkId);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 1)
                throw new ArgumentException("The research project must belong to the source passage's work.");
        }
        if (questionId is long question)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ResearchQuestions WHERE ResearchQuestionId=@Id AND ResearchProjectId=@Project;";
            cmd.Parameters.AddWithValue("@Id", question); cmd.Parameters.AddWithValue("@Project", projectId);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 1) throw new ArgumentException("Question is outside this project.");
        }
        if (findingId is long finding)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ResearchFindings WHERE ResearchFindingId=@Id AND ResearchProjectId=@Project;";
            cmd.Parameters.AddWithValue("@Id", finding); cmd.Parameters.AddWithValue("@Project", projectId);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 1) throw new ArgumentException("Finding is outside this project.");
        }
    }

    private static ResearchEchoInvestigation ReadInvestigation(SqliteDataReader r) => new()
    {
        ResearchEchoInvestigationId = r.GetInt64(0), ResearchProjectId = r.GetInt64(1),
        ResearchQuestionId = r.IsDBNull(2) ? null : r.GetInt64(2), ResearchFindingId = r.IsDBNull(3) ? null : r.GetInt64(3),
        Method = Parse(r.GetString(4), ResearchEchoMethod.RareWordOverlap), Title = r.GetString(5),
        SourceWorkId = r.GetInt32(6), SourceTextNodeId = r.GetInt64(7), SourceWorkCtsUrn = r.GetString(8),
        SourceEditionCtsUrn = r.GetString(9), SourceCitationRef = r.GetString(10), SourceText = r.GetString(11),
        TargetScope = Text(r, 12), Settings = Text(r, 13), AiModel = Text(r, 14), AiPrompt = Text(r, 15),
        AiGeneratedUtc = Date(r, 16), CreatedUtc = Date(r, 17)!.Value, UpdatedUtc = Date(r, 18)!.Value
    };
    private static ResearchEchoResult ReadResult(SqliteDataReader r) => new()
    {
        ResearchEchoResultId = r.GetInt64(0), ResearchEchoInvestigationId = r.GetInt64(1),
        TargetWorkId = r.GetInt32(2), TargetTextNodeId = r.GetInt64(3), TargetAuthorName = r.GetString(4),
        TargetWorkTitle = r.GetString(5), TargetWorkCtsUrn = r.GetString(6), TargetEditionCtsUrn = r.GetString(7),
        TargetCitationRef = r.GetString(8), TargetText = r.GetString(9), Score = r.IsDBNull(10) ? null : r.GetDouble(10),
        ScoreLabel = Text(r, 11), Rationale = Text(r, 12),
        Disposition = Parse(r.GetString(13), ResearchEchoDisposition.Pending), ResearcherNote = Text(r, 14),
        EvidenceItemId = r.IsDBNull(15) ? null : r.GetInt64(15), SortOrder = r.GetInt32(16),
        CreatedUtc = Date(r, 17)!.Value, UpdatedUtc = Date(r, 18)!.Value
    };
    private static DateTime? Date(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : DateTime.Parse(r.GetString(i), null, System.Globalization.DateTimeStyles.RoundtripKind);
    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static string Store<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
    private static T Parse<T>(string value, T fallback) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static object Db(long? value) => value.HasValue ? value.Value : DBNull.Value;
    private static Task LogAsync(long projectId, ResearchLogEntryKind kind, string summary, string? details, CancellationToken ct) =>
        new ResearchRepository().AddSystemResearchLogEntryAsync(new ResearchLogEntry
        { ResearchProjectId = projectId, Kind = kind, Summary = summary, Details = details }, ct);
}
