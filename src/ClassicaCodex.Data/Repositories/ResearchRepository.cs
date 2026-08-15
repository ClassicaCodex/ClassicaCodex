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

        if (project.ResearchProjectId == 0)
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
        if (question.ResearchQuestionId == 0)
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
        return id;
    }

    public async Task DeleteQuestionAsync(long questionId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ResearchQuestions WHERE ResearchQuestionId=@Id;";
        cmd.Parameters.AddWithValue("@Id", questionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReorderQuestionsAsync(IReadOnlyList<long> ids,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
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
    }

    public async Task<List<EvidenceItem>> GetEvidenceAsync(
        long projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<EvidenceItem>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT EvidenceItemId, ResearchProjectId, ResearchQuestionId, Title,
            EvidenceType, SourceType, StableIdentifier, CanonicalReference, Provenance, Excerpt,
            Judgment, Relationship, ResearcherNote, SortOrder, CreatedUtc, UpdatedUtc
            FROM EvidenceItems WHERE ResearchProjectId=@ProjectId ORDER BY SortOrder, EvidenceItemId;";
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
        if (item.EvidenceItemId == 0)
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
        await TouchProjectAsync(conn, item.ResearchProjectId, cancellationToken);
        return id;
    }

    public async Task DeleteEvidenceAsync(long evidenceId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM EvidenceItems WHERE EvidenceItemId=@Id;";
        cmd.Parameters.AddWithValue("@Id", evidenceId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
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
        CreatedUtc=ParseDate(reader.GetString(14)), UpdatedUtc=ParseDate(reader.GetString(15))
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
}
