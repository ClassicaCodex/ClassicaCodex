using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>Persistence for the Research Bench aggregate.</summary>
public class ResearchRepository
{
    public async Task<List<ResearchProject>> GetProjectsForWorkAsync(
        int workId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var result = new List<ResearchProject>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ResearchProjectId, WorkId, Name, Status, Notes, CreatedUtc, UpdatedUtc
            FROM ResearchProjects
            WHERE WorkId = @WorkId AND (@IncludeArchived = 1 OR Status <> 'archived')
            ORDER BY UpdatedUtc DESC, ResearchProjectId DESC;";
        cmd.Parameters.AddWithValue("@WorkId", workId);
        cmd.Parameters.AddWithValue("@IncludeArchived", includeArchived ? 1 : 0);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadProject(reader));
        return result;
    }

    public async Task<long> SaveProjectAsync(
        ResearchProject project, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
            throw new ArgumentException("A research project needs a working theory.", nameof(project));

        var now = DateTime.UtcNow;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();

        var isNew = project.ResearchProjectId == 0;
        if (isNew)
        {
            cmd.CommandText = @"
                INSERT INTO ResearchProjects (WorkId, Name, Status, Notes, CreatedUtc, UpdatedUtc)
                VALUES (@WorkId, @Name, @Status, @Notes, @Now, @Now);
                SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = @"
                UPDATE ResearchProjects
                SET Name=@Name, Status=@Status, Notes=@Notes, UpdatedUtc=@Now
                WHERE ResearchProjectId=@Id;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", project.ResearchProjectId);
        }

        cmd.Parameters.AddWithValue("@WorkId", project.WorkId);
        cmd.Parameters.AddWithValue("@Name", project.Name.Trim());
        cmd.Parameters.AddWithValue("@Status", Store(project.Status));
        cmd.Parameters.AddWithValue("@Notes", Db(project.Notes));
        cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));

        project.ResearchProjectId = id;
        if (project.CreatedUtc == default) project.CreatedUtc = now;
        project.UpdatedUtc = now;
        await AppendLogAsync(conn, new ResearchLogEntry
        {
            ResearchProjectId = id,
            Kind = isNew ? ResearchLogEntryKind.ProjectCreated : ResearchLogEntryKind.ProjectUpdated,
            Summary = isNew ? $"Created project: {project.Name.Trim()}" : $"Updated project: {project.Name.Trim()}",
            Details = project.Notes
        }, cancellationToken);
        return id;
    }

    public Task ArchiveProjectAsync(long projectId, CancellationToken cancellationToken = default) =>
        SetProjectStatusAsync(projectId, ResearchProjectStatus.Archived, cancellationToken);

    public async Task SetProjectStatusAsync(long projectId, ResearchProjectStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE ResearchProjects SET Status=@Status, UpdatedUtc=@Now WHERE ResearchProjectId=@Id;";
        cmd.Parameters.AddWithValue("@Status", Store(status));
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Id", projectId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await AppendLogAsync(conn, new ResearchLogEntry
        {
            ResearchProjectId = projectId,
            Kind = ResearchLogEntryKind.StatusChanged,
            Summary = $"Changed project status to {status}"
        }, cancellationToken);
    }

    public async Task<List<ResearchQuestion>> GetQuestionsAsync(
        long projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<ResearchQuestion>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ResearchQuestionId, ResearchProjectId, Text, Notes, SortOrder, CreatedUtc, UpdatedUtc
            FROM ResearchQuestions WHERE ResearchProjectId=@ProjectId
            ORDER BY SortOrder, ResearchQuestionId;";
        cmd.Parameters.AddWithValue("@ProjectId", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ResearchQuestion
            {
                ResearchQuestionId = reader.GetInt64(0), ResearchProjectId = reader.GetInt64(1),
                Text = reader.GetString(2), Notes = reader.IsDBNull(3) ? null : reader.GetString(3),
                SortOrder = reader.GetInt32(4), CreatedUtc = ParseDate(reader.GetString(5)),
                UpdatedUtc = ParseDate(reader.GetString(6))
            });
        }
        return result;
    }

    public async Task<long> SaveQuestionAsync(
        ResearchQuestion question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question.Text))
            throw new ArgumentException("A research question cannot be empty.", nameof(question));
        var now = DateTime.UtcNow;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        var isNew = question.ResearchQuestionId == 0;
        if (isNew)
            cmd.CommandText = @"INSERT INTO ResearchQuestions
                (ResearchProjectId, Text, Notes, SortOrder, CreatedUtc, UpdatedUtc)
                VALUES (@ProjectId,@Text,@Notes,@Sort,@Now,@Now); SELECT last_insert_rowid();";
        else
        {
            cmd.CommandText = @"UPDATE ResearchQuestions SET Text=@Text, Notes=@Notes,
                SortOrder=@Sort, UpdatedUtc=@Now WHERE ResearchQuestionId=@Id; SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", question.ResearchQuestionId);
        }
        cmd.Parameters.AddWithValue("@ProjectId", question.ResearchProjectId);
        cmd.Parameters.AddWithValue("@Text", question.Text.Trim());
        cmd.Parameters.AddWithValue("@Notes", Db(question.Notes));
        cmd.Parameters.AddWithValue("@Sort", question.SortOrder);
        cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        question.ResearchQuestionId = id;
        await TouchProjectAsync(conn, question.ResearchProjectId, cancellationToken);
        await AppendLogAsync(conn, new ResearchLogEntry
        {
            ResearchProjectId = question.ResearchProjectId,
            ResearchQuestionId = id,
            Kind = isNew ? ResearchLogEntryKind.QuestionAdded : ResearchLogEntryKind.QuestionUpdated,
            Summary = isNew ? $"Added question: {question.Text.Trim()}" : $"Updated question: {question.Text.Trim()}",
            Details = question.Notes
        }, cancellationToken);
        return id;
    }

    public async Task DeleteQuestionAsync(long questionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        string questionText;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT ResearchProjectId, Text FROM ResearchQuestions WHERE ResearchQuestionId=@Id;";
            read.Parameters.AddWithValue("@Id", questionId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return;
            projectId = reader.GetInt64(0);
            questionText = reader.GetString(1);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ResearchQuestions WHERE ResearchQuestionId=@Id;";
        cmd.Parameters.AddWithValue("@Id", questionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await TouchProjectAsync(conn, projectId, cancellationToken);
        await AppendLogAsync(conn, new ResearchLogEntry
        {
            ResearchProjectId = projectId,
            Kind = ResearchLogEntryKind.QuestionRemoved,
            Summary = $"Removed question: {questionText}"
        }, cancellationToken);
    }

    public async Task ReorderQuestionsAsync(IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT ResearchProjectId FROM ResearchQuestions WHERE ResearchQuestionId=@Id;";
            read.Parameters.AddWithValue("@Id", ids[0]);
            projectId = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken));
        }
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        for (var i = 0; i < ids.Count; i++)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "UPDATE ResearchQuestions SET SortOrder=@Sort, UpdatedUtc=@Now WHERE ResearchQuestionId=@Id;";
            cmd.Parameters.AddWithValue("@Sort", i);
            cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@Id", ids[i]);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
        await TouchProjectAsync(conn, projectId, cancellationToken);
        await AppendLogAsync(conn, new ResearchLogEntry
        {
            ResearchProjectId = projectId,
            Kind = ResearchLogEntryKind.QuestionsReordered,
            Summary = "Reordered research questions"
        }, cancellationToken);
    }

    public async Task<List<EvidenceItem>> GetEvidenceAsync(
        long projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<EvidenceItem>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT e.EvidenceItemId, e.ResearchProjectId, e.ResearchQuestionId, e.Title,
            e.EvidenceType, e.SourceType, e.StableIdentifier, e.CanonicalReference, e.Provenance, e.Excerpt,
            e.Judgment, e.Relationship, e.ResearcherNote, e.SortOrder, e.CreatedUtc, e.UpdatedUtc,
            COALESCE(m.Origin, 'manual'), m.Interpretation, m.InterpretationAuthor,
            m.GeneratorPrompt, m.GeneratedUtc
            FROM EvidenceItems e
            LEFT JOIN EvidenceGenerationMetadata m ON m.EvidenceItemId=e.EvidenceItemId
            WHERE e.ResearchProjectId=@ProjectId ORDER BY e.SortOrder, e.EvidenceItemId;";
        cmd.Parameters.AddWithValue("@ProjectId", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadEvidence(reader));
        return result;
    }

    public async Task<long> SaveEvidenceAsync(EvidenceItem item,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
            throw new ArgumentException("Evidence needs a title.", nameof(item));
        var now = DateTime.UtcNow;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        var isNew = item.EvidenceItemId == 0;
        if (isNew)
            cmd.CommandText = @"INSERT INTO EvidenceItems
                (ResearchProjectId,ResearchQuestionId,Title,EvidenceType,SourceType,StableIdentifier,
                 CanonicalReference,Provenance,Excerpt,Judgment,Relationship,ResearcherNote,
                 SortOrder,CreatedUtc,UpdatedUtc)
                VALUES (@ProjectId,@QuestionId,@Title,@Type,@SourceType,@StableId,@Reference,@Provenance,
                        @Excerpt,@Judgment,@Relationship,@Note,@Sort,@Now,@Now);
                SELECT last_insert_rowid();";
        else
        {
            cmd.CommandText = @"UPDATE EvidenceItems SET ResearchQuestionId=@QuestionId,Title=@Title,
                EvidenceType=@Type,SourceType=@SourceType,StableIdentifier=@StableId,
                CanonicalReference=@Reference,Provenance=@Provenance,Excerpt=@Excerpt,
                Judgment=@Judgment,Relationship=@Relationship,ResearcherNote=@Note,
                SortOrder=@Sort,UpdatedUtc=@Now WHERE EvidenceItemId=@Id; SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", item.EvidenceItemId);
        }
        cmd.Parameters.AddWithValue("@ProjectId", item.ResearchProjectId);
        cmd.Parameters.AddWithValue("@QuestionId", item.ResearchQuestionId is null ? DBNull.Value : item.ResearchQuestionId.Value);
        cmd.Parameters.AddWithValue("@Title", item.Title.Trim());
        cmd.Parameters.AddWithValue("@Type", Store(item.Type));
        cmd.Parameters.AddWithValue("@SourceType", Db(item.SourceType));
        cmd.Parameters.AddWithValue("@StableId", Db(item.StableIdentifier));
        cmd.Parameters.AddWithValue("@Reference", Db(item.CanonicalReference));
        cmd.Parameters.AddWithValue("@Provenance", Db(item.Provenance));
        cmd.Parameters.AddWithValue("@Excerpt", Db(item.Excerpt));
        cmd.Parameters.AddWithValue("@Judgment", Store(item.Judgment));
        cmd.Parameters.AddWithValue("@Relationship", Store(item.Relationship));
        cmd.Parameters.AddWithValue("@Note", Db(item.ResearcherNote));
        cmd.Parameters.AddWithValue("@Sort", item.SortOrder);
        cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        item.EvidenceItemId = id;
        await SaveEvidenceGenerationMetadataAsync(conn, item, cancellationToken);
        await TouchProjectAsync(conn, item.ResearchProjectId, cancellationToken);
        await AppendLogAsync(conn, new ResearchLogEntry
        {
            ResearchProjectId = item.ResearchProjectId,
            ResearchQuestionId = item.ResearchQuestionId,
            EvidenceItemId = id,
            Kind = isNew ? ResearchLogEntryKind.EvidenceAdded : ResearchLogEntryKind.EvidenceUpdated,
            Summary = isNew ? $"Added evidence: {item.Title.Trim()}" : $"Updated evidence: {item.Title.Trim()}",
            Details = $"{item.Judgment}; {item.Relationship}"
        }, cancellationToken);
        return id;
    }

    public async Task DeleteEvidenceAsync(long evidenceId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        string evidenceTitle;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT ResearchProjectId, Title FROM EvidenceItems WHERE EvidenceItemId=@Id;";
            read.Parameters.AddWithValue("@Id", evidenceId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return;
            projectId = reader.GetInt64(0);
            evidenceTitle = reader.GetString(1);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM EvidenceItems WHERE EvidenceItemId=@Id;";
        cmd.Parameters.AddWithValue("@Id", evidenceId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await TouchProjectAsync(conn, projectId, cancellationToken);
        await AppendLogAsync(conn, new ResearchLogEntry
        {
            ResearchProjectId = projectId,
            Kind = ResearchLogEntryKind.EvidenceRemoved,
            Summary = $"Removed evidence: {evidenceTitle}"
        }, cancellationToken);
    }

    public async Task<List<ResearchLogEntry>> GetResearchLogAsync(
        long projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<ResearchLogEntry>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchLogEntryId, ResearchProjectId, Kind, Summary, Details,
            ResearchQuestionId, EvidenceItemId, CreatedUtc
            FROM ResearchLogEntries WHERE ResearchProjectId=@ProjectId
            ORDER BY CreatedUtc DESC, ResearchLogEntryId DESC;";
        cmd.Parameters.AddWithValue("@ProjectId", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ResearchLogEntry
            {
                ResearchLogEntryId = reader.GetInt64(0),
                ResearchProjectId = reader.GetInt64(1),
                Kind = Parse(reader.GetString(2), ResearchLogEntryKind.ManualNote),
                Summary = reader.GetString(3),
                Details = reader.IsDBNull(4) ? null : reader.GetString(4),
                ResearchQuestionId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                EvidenceItemId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                CreatedUtc = ParseDate(reader.GetString(7))
            });
        }
        return result;
    }

    public async Task<long> AddResearchLogEntryAsync(
        ResearchLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Summary))
            throw new ArgumentException("A research log entry cannot be empty.", nameof(entry));
        entry.Kind = ResearchLogEntryKind.ManualNote;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        var id = await AppendLogAsync(conn, entry, cancellationToken);
        await TouchProjectAsync(conn, entry.ResearchProjectId, cancellationToken);
        return id;
    }

    public async Task<long> AddSystemResearchLogEntryAsync(
        ResearchLogEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.Kind == ResearchLogEntryKind.ManualNote)
            throw new ArgumentException("Use AddResearchLogEntryAsync for manual notes.", nameof(entry));
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        var id = await AppendLogAsync(conn, entry, cancellationToken);
        await TouchProjectAsync(conn, entry.ResearchProjectId, cancellationToken);
        return id;
    }

    public async Task<List<ScholarlyClaim>> GetScholarlyClaimsAsync(
        long projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<ScholarlyClaim>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ScholarlyClaimId, ResearchProjectId, ResearchQuestionId,
            SourceEvidenceItemId, Claimant, ClaimText, Locator, Relationship, Judgment,
            Notes, SortOrder, CreatedUtc, UpdatedUtc
            FROM ScholarlyClaims WHERE ResearchProjectId=@ProjectId
            ORDER BY SortOrder, ScholarlyClaimId;";
        cmd.Parameters.AddWithValue("@ProjectId", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ScholarlyClaim
            {
                ScholarlyClaimId = reader.GetInt64(0),
                ResearchProjectId = reader.GetInt64(1),
                ResearchQuestionId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                SourceEvidenceItemId = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                Claimant = reader.GetString(4),
                ClaimText = reader.GetString(5),
                Locator = reader.IsDBNull(6) ? null : reader.GetString(6),
                Relationship = Parse(reader.GetString(7), EvidenceRelationship.Contextualizes),
                Judgment = Parse(reader.GetString(8), EvidenceJudgment.Uncertain),
                Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                SortOrder = reader.GetInt32(10),
                CreatedUtc = ParseDate(reader.GetString(11)),
                UpdatedUtc = ParseDate(reader.GetString(12))
            });
        }
        return result;
    }

    public async Task<long> SaveScholarlyClaimAsync(
        ScholarlyClaim claim, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(claim.Claimant))
            throw new ArgumentException("A scholarly claim needs an attributed claimant.", nameof(claim));
        if (string.IsNullOrWhiteSpace(claim.ClaimText))
            throw new ArgumentException("A scholarly claim cannot be empty.", nameof(claim));

        var now = DateTime.UtcNow;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureClaimLinksBelongToProjectAsync(conn, claim, cancellationToken);
        await using var cmd = conn.CreateCommand();
        var isNew = claim.ScholarlyClaimId == 0;
        if (isNew)
        {
            cmd.CommandText = @"INSERT INTO ScholarlyClaims
                (ResearchProjectId,ResearchQuestionId,SourceEvidenceItemId,Claimant,ClaimText,
                 Locator,Relationship,Judgment,Notes,SortOrder,CreatedUtc,UpdatedUtc)
                VALUES (@ProjectId,@QuestionId,@SourceId,@Claimant,@ClaimText,@Locator,@Relationship,
                        @Judgment,@Notes,@Sort,@Now,@Now); SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = @"UPDATE ScholarlyClaims SET ResearchQuestionId=@QuestionId,
                SourceEvidenceItemId=@SourceId,Claimant=@Claimant,ClaimText=@ClaimText,Locator=@Locator,
                Relationship=@Relationship,Judgment=@Judgment,Notes=@Notes,SortOrder=@Sort,UpdatedUtc=@Now
                WHERE ScholarlyClaimId=@Id AND ResearchProjectId=@ProjectId; SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", claim.ScholarlyClaimId);
        }
        cmd.Parameters.AddWithValue("@ProjectId", claim.ResearchProjectId);
        cmd.Parameters.AddWithValue("@QuestionId", claim.ResearchQuestionId is null ? DBNull.Value : claim.ResearchQuestionId.Value);
        cmd.Parameters.AddWithValue("@SourceId", claim.SourceEvidenceItemId is null ? DBNull.Value : claim.SourceEvidenceItemId.Value);
        cmd.Parameters.AddWithValue("@Claimant", claim.Claimant.Trim());
        cmd.Parameters.AddWithValue("@ClaimText", claim.ClaimText.Trim());
        cmd.Parameters.AddWithValue("@Locator", Db(claim.Locator));
        cmd.Parameters.AddWithValue("@Relationship", Store(claim.Relationship));
        cmd.Parameters.AddWithValue("@Judgment", Store(claim.Judgment));
        cmd.Parameters.AddWithValue("@Notes", Db(claim.Notes));
        cmd.Parameters.AddWithValue("@Sort", claim.SortOrder);
        cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        claim.ScholarlyClaimId = id;
        if (claim.CreatedUtc == default) claim.CreatedUtc = now;
        claim.UpdatedUtc = now;
        await TouchProjectAsync(conn, claim.ResearchProjectId, cancellationToken);
        await AppendLogAsync(conn, new ResearchLogEntry
        {
            ResearchProjectId = claim.ResearchProjectId,
            ResearchQuestionId = claim.ResearchQuestionId,
            EvidenceItemId = claim.SourceEvidenceItemId,
            Kind = isNew ? ResearchLogEntryKind.ClaimAdded : ResearchLogEntryKind.ClaimUpdated,
            Summary = isNew ? $"Added claim by {claim.Claimant.Trim()}" : $"Updated claim by {claim.Claimant.Trim()}",
            Details = claim.ClaimText.Trim()
        }, cancellationToken);
        return id;
    }

    public async Task DeleteScholarlyClaimAsync(
        long claimId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        string claimant;
        string claimText;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT ResearchProjectId,Claimant,ClaimText FROM ScholarlyClaims WHERE ScholarlyClaimId=@Id;";
            read.Parameters.AddWithValue("@Id", claimId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return;
            projectId = reader.GetInt64(0);
            claimant = reader.GetString(1);
            claimText = reader.GetString(2);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ScholarlyClaims WHERE ScholarlyClaimId=@Id;";
        cmd.Parameters.AddWithValue("@Id", claimId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await TouchProjectAsync(conn, projectId, cancellationToken);
        await AppendLogAsync(conn, new ResearchLogEntry
        {
            ResearchProjectId = projectId,
            Kind = ResearchLogEntryKind.ClaimRemoved,
            Summary = $"Removed claim by {claimant}",
            Details = claimText
        }, cancellationToken);
    }

    private static async Task EnsureClaimLinksBelongToProjectAsync(
        SqliteConnection conn, ScholarlyClaim claim, CancellationToken cancellationToken)
    {
        if (claim.ScholarlyClaimId > 0)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ScholarlyClaims WHERE ScholarlyClaimId=@Id AND ResearchProjectId=@ProjectId;";
            cmd.Parameters.AddWithValue("@Id", claim.ScholarlyClaimId);
            cmd.Parameters.AddWithValue("@ProjectId", claim.ResearchProjectId);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new ArgumentException("The scholarly claim does not belong to this research project.", nameof(claim));
        }
        if (claim.ResearchQuestionId is long questionId)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ResearchQuestions WHERE ResearchQuestionId=@Id AND ResearchProjectId=@ProjectId;";
            cmd.Parameters.AddWithValue("@Id", questionId);
            cmd.Parameters.AddWithValue("@ProjectId", claim.ResearchProjectId);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new ArgumentException("The linked question does not belong to this research project.", nameof(claim));
        }
        if (claim.SourceEvidenceItemId is long evidenceId)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM EvidenceItems WHERE EvidenceItemId=@Id AND ResearchProjectId=@ProjectId;";
            cmd.Parameters.AddWithValue("@Id", evidenceId);
            cmd.Parameters.AddWithValue("@ProjectId", claim.ResearchProjectId);
            if (Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new ArgumentException("The linked source does not belong to this research project.", nameof(claim));
        }
    }

    private static ResearchProject ReadProject(SqliteDataReader reader) => new()
    {
        ResearchProjectId = reader.GetInt64(0), WorkId = reader.GetInt32(1), Name = reader.GetString(2),
        Status = Parse(reader.GetString(3), ResearchProjectStatus.Active),
        Notes = reader.IsDBNull(4) ? null : reader.GetString(4),
        CreatedUtc = ParseDate(reader.GetString(5)), UpdatedUtc = ParseDate(reader.GetString(6))
    };

    private static EvidenceItem ReadEvidence(SqliteDataReader reader) => new()
    {
        EvidenceItemId=reader.GetInt64(0), ResearchProjectId=reader.GetInt64(1),
        ResearchQuestionId=reader.IsDBNull(2) ? null : reader.GetInt64(2), Title=reader.GetString(3),
        Type=Parse(reader.GetString(4), EvidenceType.Other), SourceType=reader.IsDBNull(5)?null:reader.GetString(5),
        StableIdentifier=reader.IsDBNull(6)?null:reader.GetString(6), CanonicalReference=reader.IsDBNull(7)?null:reader.GetString(7),
        Provenance=reader.IsDBNull(8)?null:reader.GetString(8), Excerpt=reader.IsDBNull(9)?null:reader.GetString(9),
        Judgment=Parse(reader.GetString(10), EvidenceJudgment.Uncertain),
        Relationship=Parse(reader.GetString(11), EvidenceRelationship.Contextualizes),
        ResearcherNote=reader.IsDBNull(12)?null:reader.GetString(12), SortOrder=reader.GetInt32(13),
        CreatedUtc=ParseDate(reader.GetString(14)), UpdatedUtc=ParseDate(reader.GetString(15)),
        Origin=Parse(reader.GetString(16), EvidenceOrigin.Manual),
        Interpretation=reader.IsDBNull(17)?null:reader.GetString(17),
        InterpretationAuthor=reader.IsDBNull(18)?null:reader.GetString(18),
        GeneratorPrompt=reader.IsDBNull(19)?null:reader.GetString(19),
        GeneratedUtc=reader.IsDBNull(20)?null:ParseDate(reader.GetString(20))
    };

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static string Store<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
    private static T Parse<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
    private static DateTime ParseDate(string value) => DateTime.Parse(value, null,
        System.Globalization.DateTimeStyles.RoundtripKind);

    private static async Task TouchProjectAsync(SqliteConnection conn, long projectId,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE ResearchProjects SET UpdatedUtc=@Now WHERE ResearchProjectId=@Id;";
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Id", projectId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> AppendLogAsync(SqliteConnection conn, ResearchLogEntry entry,
        CancellationToken cancellationToken)
    {
        var created = entry.CreatedUtc == default ? DateTime.UtcNow : entry.CreatedUtc;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO ResearchLogEntries
            (ResearchProjectId, Kind, Summary, Details, ResearchQuestionId, EvidenceItemId, CreatedUtc)
            VALUES (@ProjectId,@Kind,@Summary,@Details,@QuestionId,@EvidenceId,@Created);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@ProjectId", entry.ResearchProjectId);
        cmd.Parameters.AddWithValue("@Kind", Store(entry.Kind));
        cmd.Parameters.AddWithValue("@Summary", entry.Summary.Trim());
        cmd.Parameters.AddWithValue("@Details", Db(entry.Details));
        cmd.Parameters.AddWithValue("@QuestionId", entry.ResearchQuestionId is null ? DBNull.Value : entry.ResearchQuestionId.Value);
        cmd.Parameters.AddWithValue("@EvidenceId", entry.EvidenceItemId is null ? DBNull.Value : entry.EvidenceItemId.Value);
        cmd.Parameters.AddWithValue("@Created", created.ToString("O"));
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        entry.ResearchLogEntryId = id;
        entry.CreatedUtc = created;
        return id;
    }

    private static async Task SaveEvidenceGenerationMetadataAsync(
        SqliteConnection conn, EvidenceItem item, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        var hasMetadata = item.Origin != EvidenceOrigin.Manual
            || !string.IsNullOrWhiteSpace(item.Interpretation)
            || !string.IsNullOrWhiteSpace(item.InterpretationAuthor)
            || !string.IsNullOrWhiteSpace(item.GeneratorPrompt)
            || item.GeneratedUtc != null;
        if (!hasMetadata)
        {
            cmd.CommandText = "DELETE FROM EvidenceGenerationMetadata WHERE EvidenceItemId=@Id;";
            cmd.Parameters.AddWithValue("@Id", item.EvidenceItemId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        cmd.CommandText = @"INSERT INTO EvidenceGenerationMetadata
            (EvidenceItemId,Origin,Interpretation,InterpretationAuthor,GeneratorPrompt,GeneratedUtc)
            VALUES (@Id,@Origin,@Interpretation,@Author,@Prompt,@GeneratedUtc)
            ON CONFLICT(EvidenceItemId) DO UPDATE SET
                Origin=excluded.Origin, Interpretation=excluded.Interpretation,
                InterpretationAuthor=excluded.InterpretationAuthor,
                GeneratorPrompt=excluded.GeneratorPrompt, GeneratedUtc=excluded.GeneratedUtc;";
        cmd.Parameters.AddWithValue("@Id", item.EvidenceItemId);
        cmd.Parameters.AddWithValue("@Origin", Store(item.Origin));
        cmd.Parameters.AddWithValue("@Interpretation", Db(item.Interpretation));
        cmd.Parameters.AddWithValue("@Author", Db(item.InterpretationAuthor));
        cmd.Parameters.AddWithValue("@Prompt", Db(item.GeneratorPrompt));
        cmd.Parameters.AddWithValue("@GeneratedUtc",
            item.GeneratedUtc is null ? DBNull.Value : item.GeneratedUtc.Value.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
