using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public sealed class ResearchEchoRepository
{
    /// <summary>
    /// Every saved echo candidate in scope, with the project and work it belongs to.
    /// </summary>
    /// <param name="projectId">
    /// One project, or null for all of them. A project named here is always included,
    /// archived or not - it was opened deliberately, the same principle the Bench uses.
    /// </param>
    /// <param name="includeArchived">
    /// Whether the all-projects scope reaches into archived projects. Off by default:
    /// archiving is how a researcher says a line of inquiry is closed, and a
    /// cross-project view that ignores that fills up with work they have set aside.
    /// </param>
    public async Task<List<IntertextualAtlasConnection>> GetAtlasConnectionsAsync(
        long? projectId = null, bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        // One connection and three queries, not one per project and one per
        // investigation. The old shape opened 1 + P + (P x I) of them in sequence on the
        // UI thread - Microsoft.Data.Sqlite has no true async, so every one of those
        // blocked the message pump rather than yielding to it.
        var projects = new List<(ResearchProject Project, string Author, string Work)>();
        var investigations = new List<ResearchEchoInvestigation>();
        var resultsByInvestigation = new Dictionary<long, List<ResearchEchoResult>>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        await using (var cmd = conn.CreateCommand())
        {
            // LEFT JOIN, not JOIN. Since schema 31 a project outlives its work, and the
            // Bench lists projects by WorkId, so it cannot show a detached one at all -
            // the Atlas is the only place that research stays visible until a re-import
            // reattaches it. An inner join here would quietly finish the deletion that
            // migration 31 exists to prevent.
            cmd.CommandText = @"SELECT p.ResearchProjectId,p.WorkId,p.Name,p.Status,p.Notes,p.CreatedUtc,p.UpdatedUtc,
                a.Name,w.Title FROM ResearchProjects p LEFT JOIN Works w ON w.WorkId=p.WorkId
                LEFT JOIN Authors a ON a.AuthorId=w.AuthorId
                WHERE (@Project IS NULL OR p.ResearchProjectId=@Project)
                  AND (@Project IS NOT NULL OR @IncludeArchived = 1 OR p.Status <> 'archived')
                ORDER BY p.ResearchProjectId;";
            cmd.Parameters.AddWithValue("@Project", projectId is null ? DBNull.Value : projectId.Value);
            cmd.Parameters.AddWithValue("@IncludeArchived", includeArchived ? 1 : 0);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                projects.Add((new ResearchProject
                {
                    ResearchProjectId = reader.GetInt64(0),
                    WorkId = reader.IsDBNull(1) ? null : reader.GetInt32(1), Name = reader.GetString(2),
                    Status = Parse(reader.GetString(3), ResearchProjectStatus.Active), Notes = Text(reader, 4),
                    CreatedUtc = Date(reader, 5)!.Value, UpdatedUtc = Date(reader, 6)!.Value
                }, Text(reader, 7) ?? "—", Text(reader, 8) ?? "(work no longer in the library)"));
        }

        if (projects.Count == 0) return [];
        var inScope = projects.Select(p => p.Project.ResearchProjectId).ToHashSet();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ResearchEchoInvestigationId,ResearchProjectId,ResearchQuestionId,
                ResearchFindingId,Method,Title,SourceWorkId,SourceTextNodeId,SourceWorkCtsUrn,
                SourceEditionCtsUrn,SourceCitationRef,SourceText,SourceLanguage,TargetScope,Settings,AiModel,AiPrompt,
                AiGeneratedUtc,CreatedUtc,UpdatedUtc,
                (SELECT tn.Milestone FROM TextNodes tn JOIN Editions ed ON ed.EditionId=tn.EditionId
                  WHERE ed.CtsUrn=SourceEditionCtsUrn AND tn.CitationRef=SourceCitationRef LIMIT 1)
                FROM ResearchEchoInvestigations
                ORDER BY CreatedUtc DESC,ResearchEchoInvestigationId DESC;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var investigation = ReadInvestigation(reader);
                if (inScope.Contains(investigation.ResearchProjectId)) investigations.Add(investigation);
            }
        }
        if (investigations.Count == 0) return [];
        var investigationIds = investigations.Select(i => i.ResearchEchoInvestigationId).ToHashSet();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ResearchEchoResultId,ResearchEchoInvestigationId,TargetWorkId,
                TargetTextNodeId,TargetAuthorName,TargetWorkTitle,TargetWorkCtsUrn,TargetEditionCtsUrn,
                TargetCitationRef,TargetText,TargetLanguage,Score,ScoreLabel,Rationale,Disposition,ResearcherNote,
                ConnectionType,Directionality,MotifTags,ParallelNote,EvidenceItemId,SortOrder,CreatedUtc,UpdatedUtc,
                (SELECT tn.Milestone FROM TextNodes tn JOIN Editions ed ON ed.EditionId=tn.EditionId
                  WHERE ed.CtsUrn=TargetEditionCtsUrn AND tn.CitationRef=TargetCitationRef LIMIT 1)
                FROM ResearchEchoResults ORDER BY SortOrder,ResearchEchoResultId;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var result = ReadResult(reader);
                if (!investigationIds.Contains(result.ResearchEchoInvestigationId)) continue;
                if (!resultsByInvestigation.TryGetValue(result.ResearchEchoInvestigationId, out var bucket))
                    resultsByInvestigation[result.ResearchEchoInvestigationId] = bucket = [];
                bucket.Add(result);
            }
        }

        // Assembled in the order the old nested loops produced: projects by id, their
        // investigations newest first, each investigation's results by sort order.
        var connections = new List<IntertextualAtlasConnection>();
        foreach (var (project, author, work) in projects)
        foreach (var investigation in investigations.Where(i => i.ResearchProjectId == project.ResearchProjectId))
        {
            if (!resultsByInvestigation.TryGetValue(investigation.ResearchEchoInvestigationId, out var results)) continue;
            foreach (var result in results)
                connections.Add(new IntertextualAtlasConnection(project, author, work, investigation, result));
        }
        return connections;
    }

    public async Task<List<ResearchEchoInvestigation>> GetInvestigationsAsync(
        long projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<ResearchEchoInvestigation>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchEchoInvestigationId,ResearchProjectId,ResearchQuestionId,
            ResearchFindingId,Method,Title,SourceWorkId,SourceTextNodeId,SourceWorkCtsUrn,
            SourceEditionCtsUrn,SourceCitationRef,SourceText,SourceLanguage,TargetScope,Settings,AiModel,AiPrompt,
            AiGeneratedUtc,CreatedUtc,UpdatedUtc,
                (SELECT tn.Milestone FROM TextNodes tn JOIN Editions ed ON ed.EditionId=tn.EditionId
                  WHERE ed.CtsUrn=SourceEditionCtsUrn AND tn.CitationRef=SourceCitationRef LIMIT 1)
                FROM ResearchEchoInvestigations
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
            TargetCitationRef,TargetText,TargetLanguage,Score,ScoreLabel,Rationale,Disposition,ResearcherNote,
            ConnectionType,Directionality,MotifTags,ParallelNote,EvidenceItemId,SortOrder,CreatedUtc,UpdatedUtc,
                (SELECT tn.Milestone FROM TextNodes tn JOIN Editions ed ON ed.EditionId=tn.EditionId
                  WHERE ed.CtsUrn=TargetEditionCtsUrn AND tn.CitationRef=TargetCitationRef LIMIT 1)
                FROM ResearchEchoResults
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
            SourceText = capture.Source.Text, SourceLanguage = capture.Source.Language,
            TargetScope = capture.TargetScope, Settings = capture.Settings,
            AiModel = capture.AiModel, AiPrompt = capture.AiPrompt, AiGeneratedUtc = capture.AiGeneratedUtc,
            CreatedUtc = now, UpdatedUtc = now
        };
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = (SqliteTransaction)transaction;
            cmd.CommandText = @"INSERT INTO ResearchEchoInvestigations
                (ResearchProjectId,ResearchQuestionId,ResearchFindingId,Method,Title,SourceWorkId,
                 SourceTextNodeId,SourceWorkCtsUrn,SourceEditionCtsUrn,SourceCitationRef,SourceText,SourceLanguage,
                 TargetScope,Settings,AiModel,AiPrompt,AiGeneratedUtc,CreatedUtc,UpdatedUtc)
                VALUES (@Project,@Question,@Finding,@Method,@Title,@SourceWork,@SourceNode,@WorkUrn,
                        @EditionUrn,@Citation,@Text,@SourceLanguage,@Scope,@Settings,@Model,@Prompt,@Generated,@Now,@Now);
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
            cmd.Parameters.AddWithValue("@SourceLanguage", Db(capture.Source.Language));
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
                 TargetWorkCtsUrn,TargetEditionCtsUrn,TargetCitationRef,TargetText,TargetLanguage,Score,ScoreLabel,Rationale,
                 Disposition,SortOrder,CreatedUtc,UpdatedUtc)
                VALUES (@Investigation,@Work,@Node,@Author,@Title,@WorkUrn,@EditionUrn,@Citation,@Text,
                        @TargetLanguage,@Score,@ScoreLabel,@Rationale,'pending',@Sort,@Now,@Now);";
            cmd.Parameters.AddWithValue("@Investigation", investigation.ResearchEchoInvestigationId);
            cmd.Parameters.AddWithValue("@Work", target.WorkId);
            cmd.Parameters.AddWithValue("@Node", target.TextNodeId);
            cmd.Parameters.AddWithValue("@Author", target.AuthorName);
            cmd.Parameters.AddWithValue("@Title", target.WorkTitle);
            cmd.Parameters.AddWithValue("@WorkUrn", target.WorkCtsUrn);
            cmd.Parameters.AddWithValue("@EditionUrn", target.EditionCtsUrn);
            cmd.Parameters.AddWithValue("@Citation", target.CitationRef);
            cmd.Parameters.AddWithValue("@Text", target.Text);
            cmd.Parameters.AddWithValue("@TargetLanguage", Db(target.Language));
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

    public async Task SaveParallelClassificationAsync(long resultId,
        ResearchEchoConnectionType connectionType, ResearchEchoDirectionality directionality,
        string? motifTags, string? parallelNote, CancellationToken cancellationToken = default)
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
        cmd.CommandText = @"UPDATE ResearchEchoResults SET ConnectionType=@Type,Directionality=@Direction,
            MotifTags=@Motifs,ParallelNote=@Note,UpdatedUtc=@Now WHERE ResearchEchoResultId=@Id;";
        cmd.Parameters.AddWithValue("@Type", Store(connectionType));
        cmd.Parameters.AddWithValue("@Direction", Store(directionality));
        cmd.Parameters.AddWithValue("@Motifs", Db(motifTags));
        cmd.Parameters.AddWithValue("@Note", Db(parallelNote));
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Id", resultId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LogAsync(projectId, ResearchLogEntryKind.EchoParallelClassified,
            $"Classified parallel as {connectionType}", $"{directionality}; motifs: {motifTags}", cancellationToken);
    }

    public async Task<List<ResearchEchoParallelAnalysis>> GetParallelAnalysesAsync(long resultId,
        CancellationToken cancellationToken = default)
    {
        var analyses = new List<ResearchEchoParallelAnalysis>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchEchoParallelAnalysisId,ResearchEchoResultId,Model,Prompt,Summary,
            SharedFeatures,ImportantDifferences,LexicalObservations,AlternativeExplanations,VerificationTasks,
            SuggestedMotifs,SuggestedConnectionType,SuggestedDirectionality,CreatedUtc
            FROM ResearchEchoParallelAnalyses WHERE ResearchEchoResultId=@Result
            ORDER BY CreatedUtc DESC,ResearchEchoParallelAnalysisId DESC;";
        cmd.Parameters.AddWithValue("@Result", resultId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) analyses.Add(ReadParallelAnalysis(reader));
        return analyses;
    }

    public async Task<long> SaveParallelAnalysisAsync(long resultId, GeminiParallelAnalysisResult analysis,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = @"SELECT i.ResearchProjectId FROM ResearchEchoResults r
                JOIN ResearchEchoInvestigations i ON i.ResearchEchoInvestigationId=r.ResearchEchoInvestigationId
                WHERE r.ResearchEchoResultId=@Result;";
            read.Parameters.AddWithValue("@Result", resultId);
            var value = await read.ExecuteScalarAsync(cancellationToken);
            if (value == null) throw new ArgumentException("Echo result does not exist.", nameof(resultId));
            projectId = Convert.ToInt64(value);
        }
        var now = DateTime.UtcNow;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO ResearchEchoParallelAnalyses
            (ResearchEchoResultId,Model,Prompt,Summary,SharedFeatures,ImportantDifferences,LexicalObservations,
             AlternativeExplanations,VerificationTasks,SuggestedMotifs,SuggestedConnectionType,
             SuggestedDirectionality,CreatedUtc)
            VALUES (@Result,@Model,@Prompt,@Summary,@Shared,@Differences,@Lexical,@Alternatives,@Tasks,@Motifs,
                    @Type,@Direction,@Created); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@Result", resultId); cmd.Parameters.AddWithValue("@Model", analysis.Model);
        cmd.Parameters.AddWithValue("@Prompt", analysis.PromptProvenance); cmd.Parameters.AddWithValue("@Summary", analysis.Summary);
        cmd.Parameters.AddWithValue("@Shared", Db(analysis.SharedFeatures)); cmd.Parameters.AddWithValue("@Differences", Db(analysis.ImportantDifferences));
        cmd.Parameters.AddWithValue("@Lexical", Db(analysis.LexicalObservations)); cmd.Parameters.AddWithValue("@Alternatives", Db(analysis.AlternativeExplanations));
        cmd.Parameters.AddWithValue("@Tasks", Db(analysis.VerificationTasks)); cmd.Parameters.AddWithValue("@Motifs", Db(analysis.SuggestedMotifs));
        cmd.Parameters.AddWithValue("@Type", Store(Parse(analysis.SuggestedConnectionType, ResearchEchoConnectionType.Unclassified)));
        cmd.Parameters.AddWithValue("@Direction", Store(Parse(analysis.SuggestedDirectionality, ResearchEchoDirectionality.Unknown)));
        cmd.Parameters.AddWithValue("@Created", now.ToString("O"));
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        await LogAsync(projectId, ResearchLogEntryKind.EchoParallelAiAnalyzed,
            "Generated AI parallel-passage analysis", $"{analysis.Model}; result {resultId}", cancellationToken);
        return id;
    }

    public async Task DeleteInvestigationAsync(long investigationId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        // Read the owning project and the size of what is about to go before deleting.
        // The cascade takes the candidates, their dispositions and classifications and
        // every saved parallel analysis with it; without this the research log would
        // show them being reviewed and then simply ceasing to exist.
        long projectId = 0;
        var candidateCount = 0;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = @"
                SELECT i.ResearchProjectId, (SELECT COUNT(*) FROM ResearchEchoResults r
                                            WHERE r.ResearchEchoInvestigationId = i.ResearchEchoInvestigationId)
                FROM ResearchEchoInvestigations i
                WHERE i.ResearchEchoInvestigationId = @Id;";
            read.Parameters.AddWithValue("@Id", investigationId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                projectId = reader.GetInt64(0);
                candidateCount = reader.GetInt32(1);
            }
        }
        if (projectId == 0) return; // already gone; nothing to delete and nothing to record

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM ResearchEchoInvestigations WHERE ResearchEchoInvestigationId=@Id;";
            cmd.Parameters.AddWithValue("@Id", investigationId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await LogAsync(projectId, ResearchLogEntryKind.EchoInvestigationRemoved,
            "Removed echo investigation",
            $"Investigation {investigationId}; {candidateCount} candidate(s) and their reviews removed with it",
            cancellationToken);
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
        SourceLanguage = Text(r, 12), TargetScope = Text(r, 13), Settings = Text(r, 14), AiModel = Text(r, 15), AiPrompt = Text(r, 16),
        AiGeneratedUtc = Date(r, 17), CreatedUtc = Date(r, 18)!.Value, UpdatedUtc = Date(r, 19)!.Value
    };
    private static ResearchEchoResult ReadResult(SqliteDataReader r) => new()
    {
        ResearchEchoResultId = r.GetInt64(0), ResearchEchoInvestigationId = r.GetInt64(1),
        TargetWorkId = r.GetInt32(2), TargetTextNodeId = r.GetInt64(3), TargetAuthorName = r.GetString(4),
        TargetWorkTitle = r.GetString(5), TargetWorkCtsUrn = r.GetString(6), TargetEditionCtsUrn = r.GetString(7),
        TargetCitationRef = r.GetString(8), TargetText = r.GetString(9), TargetLanguage = Text(r, 10),
        Score = r.IsDBNull(11) ? null : r.GetDouble(11), ScoreLabel = Text(r, 12), Rationale = Text(r, 13),
        Disposition = Parse(r.GetString(14), ResearchEchoDisposition.Pending), ResearcherNote = Text(r, 15),
        ConnectionType = Parse(r.GetString(16), ResearchEchoConnectionType.Unclassified),
        Directionality = Parse(r.GetString(17), ResearchEchoDirectionality.Unknown), MotifTags = Text(r, 18), ParallelNote = Text(r, 19),
        EvidenceItemId = r.IsDBNull(20) ? null : r.GetInt64(20), SortOrder = r.GetInt32(21),
        CreatedUtc = Date(r, 22)!.Value, UpdatedUtc = Date(r, 23)!.Value
    };
    private static ResearchEchoParallelAnalysis ReadParallelAnalysis(SqliteDataReader r) => new()
    {
        ResearchEchoParallelAnalysisId = r.GetInt64(0), ResearchEchoResultId = r.GetInt64(1), Model = r.GetString(2), Prompt = r.GetString(3), Summary = r.GetString(4),
        SharedFeatures = Text(r, 5), ImportantDifferences = Text(r, 6), LexicalObservations = Text(r, 7), AlternativeExplanations = Text(r, 8),
        VerificationTasks = Text(r, 9), SuggestedMotifs = Text(r, 10), SuggestedConnectionType = Parse(r.GetString(11), ResearchEchoConnectionType.Unclassified),
        SuggestedDirectionality = Parse(r.GetString(12), ResearchEchoDirectionality.Unknown), CreatedUtc = Date(r, 13)!.Value
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
