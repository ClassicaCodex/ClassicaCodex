using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public sealed class ResearchHypothesisRepository
{
    public async Task<List<ResearchHypothesis>> GetHypothesesAsync(long projectId, CancellationToken ct = default)
    {
        var result = new List<ResearchHypothesis>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchHypothesisId,ResearchProjectId,Title,Statement,Status,Origin,
            ResearcherNote,AiModel,AiPrompt,AiGeneratedUtc,SortOrder,CreatedUtc,UpdatedUtc
            FROM ResearchHypotheses WHERE ResearchProjectId=@Project ORDER BY SortOrder,ResearchHypothesisId;";
        cmd.Parameters.AddWithValue("@Project", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new ResearchHypothesis
        {
            ResearchHypothesisId = reader.GetInt64(0), ResearchProjectId = reader.GetInt64(1),
            Title = reader.GetString(2), Statement = reader.GetString(3),
            Status = Parse(reader.GetString(4), ResearchHypothesisStatus.Active),
            Origin = Parse(reader.GetString(5), EvidenceOrigin.Manual), ResearcherNote = Text(reader, 6),
            AiModel = Text(reader, 7), AiPrompt = Text(reader, 8), AiGeneratedUtc = Date(reader, 9),
            SortOrder = reader.GetInt32(10), CreatedUtc = RequiredDate(reader, 11), UpdatedUtc = RequiredDate(reader, 12)
        });
        return result;
    }

    public async Task<long> SaveHypothesisAsync(ResearchHypothesis hypothesis, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(hypothesis.Title) || string.IsNullOrWhiteSpace(hypothesis.Statement))
            throw new ArgumentException("A hypothesis needs a title and a testable statement.", nameof(hypothesis));
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(ct);
        await RequireProjectAsync(conn, hypothesis.ResearchProjectId, ct);
        var isNew = hypothesis.ResearchHypothesisId == 0; var now = DateTime.UtcNow;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = isNew
            ? @"INSERT INTO ResearchHypotheses (ResearchProjectId,Title,Statement,Status,Origin,ResearcherNote,
                AiModel,AiPrompt,AiGeneratedUtc,SortOrder,CreatedUtc,UpdatedUtc)
                VALUES (@Project,@Title,@Statement,@Status,@Origin,@Note,@Model,@Prompt,@Generated,@Sort,@Now,@Now);
                SELECT last_insert_rowid();"
            : @"UPDATE ResearchHypotheses SET Title=@Title,Statement=@Statement,Status=@Status,Origin=@Origin,
                ResearcherNote=@Note,AiModel=@Model,AiPrompt=@Prompt,AiGeneratedUtc=@Generated,SortOrder=@Sort,
                UpdatedUtc=@Now WHERE ResearchHypothesisId=@Id AND ResearchProjectId=@Project; SELECT @Id;";
        if (!isNew) cmd.Parameters.AddWithValue("@Id", hypothesis.ResearchHypothesisId);
        cmd.Parameters.AddWithValue("@Project", hypothesis.ResearchProjectId); cmd.Parameters.AddWithValue("@Title", hypothesis.Title.Trim());
        cmd.Parameters.AddWithValue("@Statement", hypothesis.Statement.Trim()); cmd.Parameters.AddWithValue("@Status", Store(hypothesis.Status));
        cmd.Parameters.AddWithValue("@Origin", Store(hypothesis.Origin)); cmd.Parameters.AddWithValue("@Note", Db(hypothesis.ResearcherNote));
        cmd.Parameters.AddWithValue("@Model", Db(hypothesis.AiModel)); cmd.Parameters.AddWithValue("@Prompt", Db(hypothesis.AiPrompt));
        cmd.Parameters.AddWithValue("@Generated", hypothesis.AiGeneratedUtc is null ? DBNull.Value : hypothesis.AiGeneratedUtc.Value.ToString("O"));
        cmd.Parameters.AddWithValue("@Sort", hypothesis.SortOrder); cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        hypothesis.ResearchHypothesisId = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        if (hypothesis.CreatedUtc == default) hypothesis.CreatedUtc = now; hypothesis.UpdatedUtc = now;
        await LogAsync(hypothesis.ResearchProjectId, isNew ? ResearchLogEntryKind.HypothesisAdded : ResearchLogEntryKind.HypothesisUpdated,
            $"{(isNew ? "Added" : "Updated")} hypothesis: {hypothesis.Title}", hypothesis.Status.ToString(), ct);
        return hypothesis.ResearchHypothesisId;
    }

    public async Task DeleteHypothesisAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(ct);
        await using var read = conn.CreateCommand(); read.CommandText = "SELECT ResearchProjectId,Title FROM ResearchHypotheses WHERE ResearchHypothesisId=@Id;";
        read.Parameters.AddWithValue("@Id", id); await using var reader = await read.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return; var projectId = reader.GetInt64(0); var title = reader.GetString(1); await reader.DisposeAsync();
        await using var cmd = conn.CreateCommand(); cmd.CommandText = "DELETE FROM ResearchHypotheses WHERE ResearchHypothesisId=@Id;";
        cmd.Parameters.AddWithValue("@Id", id); await cmd.ExecuteNonQueryAsync(ct);
        await SortOrderCompaction.RenumberAsync(conn, "ResearchHypotheses", "ResearchHypothesisId", projectId, ct);
        await LogAsync(projectId, ResearchLogEntryKind.HypothesisRemoved, $"Removed hypothesis: {title}", null, ct);
    }

    public async Task<List<HypothesisSource>> GetSourcesAsync(long projectId, CancellationToken ct = default)
    {
        var result = new List<HypothesisSource>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT Kind,Id,Title,Detail,ReviewState FROM (
            SELECT 'evidence' Kind,EvidenceItemId Id,Title,
              COALESCE(CanonicalReference,StableIdentifier,Excerpt,'') Detail,Judgment ReviewState,SortOrder Sort
              FROM EvidenceItems WHERE ResearchProjectId=@Project
            UNION ALL SELECT 'finding',ResearchFindingId,Title,Statement,Status,SortOrder
              FROM ResearchFindings WHERE ResearchProjectId=@Project
            UNION ALL SELECT 'scholarlyclaim',ScholarlyClaimId,Claimant,ClaimText,Judgment,SortOrder
              FROM ScholarlyClaims WHERE ResearchProjectId=@Project
            UNION ALL SELECT 'echoresult',r.ResearchEchoResultId,
              r.TargetAuthorName || ', ' || r.TargetWorkTitle || ' ' || r.TargetCitationRef,
              i.SourceCitationRef || ' ↔ ' || r.TargetText || COALESCE(' — ' || r.Rationale,''),
              r.Disposition,r.SortOrder
              FROM ResearchEchoResults r JOIN ResearchEchoInvestigations i
                ON i.ResearchEchoInvestigationId=r.ResearchEchoInvestigationId
              WHERE i.ResearchProjectId=@Project
            ) ORDER BY Kind,Sort,Id;";
        cmd.Parameters.AddWithValue("@Project", projectId); await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(new HypothesisSource(Parse(reader.GetString(0), HypothesisSourceKind.Evidence),
            reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return result;
    }

    public async Task<List<ResearchHypothesisAssessment>> GetAssessmentsAsync(long hypothesisId, CancellationToken ct = default)
    {
        var result = new List<ResearchHypothesisAssessment>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(ct); await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchHypothesisAssessmentId,ResearchHypothesisId,SourceKind,SourceId,
            Relationship,Strength,ResearcherNote,CreatedUtc,UpdatedUtc FROM ResearchHypothesisAssessments
            WHERE ResearchHypothesisId=@Id ORDER BY SourceKind,SourceId;"; cmd.Parameters.AddWithValue("@Id", hypothesisId);
        await using var reader = await cmd.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) result.Add(new ResearchHypothesisAssessment
        {
            ResearchHypothesisAssessmentId=reader.GetInt64(0),ResearchHypothesisId=reader.GetInt64(1),
            SourceKind=Parse(reader.GetString(2),HypothesisSourceKind.Evidence),SourceId=reader.GetInt64(3),
            Relationship=Parse(reader.GetString(4),HypothesisRelationship.Contextualizes),Strength=Parse(reader.GetString(5),HypothesisStrength.Moderate),
            ResearcherNote=Text(reader,6),CreatedUtc=RequiredDate(reader,7),UpdatedUtc=RequiredDate(reader,8)
        }); return result;
    }

    public async Task SaveAssessmentsAsync(long hypothesisId, IReadOnlyCollection<ResearchHypothesisAssessment> assessments, CancellationToken ct = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(ct); var projectId = await HypothesisProjectAsync(conn,hypothesisId,ct);
        var valid = (await GetSourcesAsync(projectId,ct)).Select(s=>(s.Kind,s.Id)).ToHashSet();
        if (assessments.Any(a=>!valid.Contains((a.SourceKind,a.SourceId)))) throw new ArgumentException("Every assessed source must belong to this project.",nameof(assessments));
        await using var tx = await conn.BeginTransactionAsync(ct); await using(var del=conn.CreateCommand())
        { del.Transaction=(SqliteTransaction)tx;del.CommandText="DELETE FROM ResearchHypothesisAssessments WHERE ResearchHypothesisId=@Id;";del.Parameters.AddWithValue("@Id",hypothesisId);await del.ExecuteNonQueryAsync(ct); }
        var now=DateTime.UtcNow.ToString("O"); foreach(var a in assessments){await using var ins=conn.CreateCommand();ins.Transaction=(SqliteTransaction)tx;
            ins.CommandText=@"INSERT INTO ResearchHypothesisAssessments (ResearchHypothesisId,SourceKind,SourceId,Relationship,Strength,ResearcherNote,CreatedUtc,UpdatedUtc)
                VALUES (@Hypothesis,@Kind,@Source,@Relationship,@Strength,@Note,@Now,@Now);";
            ins.Parameters.AddWithValue("@Hypothesis",hypothesisId);ins.Parameters.AddWithValue("@Kind",Store(a.SourceKind));ins.Parameters.AddWithValue("@Source",a.SourceId);
            ins.Parameters.AddWithValue("@Relationship",Store(a.Relationship));ins.Parameters.AddWithValue("@Strength",Store(a.Strength));ins.Parameters.AddWithValue("@Note",Db(a.ResearcherNote));ins.Parameters.AddWithValue("@Now",now);await ins.ExecuteNonQueryAsync(ct);}
        await tx.CommitAsync(ct); await LogAsync(projectId,ResearchLogEntryKind.HypothesisAssessmentsChanged,$"Updated hypothesis source assessments ({assessments.Count})",null,ct);
    }

    public async Task<List<ResearchExperiment>> GetExperimentsAsync(long projectId,CancellationToken ct=default)
    {
        var result=new List<ResearchExperiment>();await using var conn=await DbConnectionFactory.OpenConnectionAsync(ct);await using var cmd=conn.CreateCommand();
        cmd.CommandText=@"SELECT ResearchExperimentId,ResearchProjectId,ResearchHypothesisId,Title,Method,Status,PredictedOutcome,FalsificationCriterion,
            ResearcherNote,Origin,AiModel,AiPrompt,AiGeneratedUtc,SortOrder,CreatedUtc,UpdatedUtc FROM ResearchExperiments WHERE ResearchProjectId=@Project ORDER BY SortOrder,ResearchExperimentId;";
        cmd.Parameters.AddWithValue("@Project",projectId);await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result.Add(new ResearchExperiment{
            ResearchExperimentId=r.GetInt64(0),ResearchProjectId=r.GetInt64(1),ResearchHypothesisId=r.IsDBNull(2)?null:r.GetInt64(2),Title=r.GetString(3),
            Method=Parse(r.GetString(4),ResearchExperimentMethod.Manual),Status=Parse(r.GetString(5),ResearchExperimentStatus.Planned),PredictedOutcome=Text(r,6),
            FalsificationCriterion=Text(r,7),ResearcherNote=Text(r,8),Origin=Parse(r.GetString(9),EvidenceOrigin.Manual),AiModel=Text(r,10),AiPrompt=Text(r,11),
            AiGeneratedUtc=Date(r,12),SortOrder=r.GetInt32(13),CreatedUtc=RequiredDate(r,14),UpdatedUtc=RequiredDate(r,15)});return result;
    }

    public async Task<long> SaveExperimentAsync(ResearchExperiment experiment,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(experiment.Title))throw new ArgumentException("An experiment needs a title.",nameof(experiment));
        await using var conn=await DbConnectionFactory.OpenConnectionAsync(ct);await RequireProjectAsync(conn,experiment.ResearchProjectId,ct);
        if(experiment.ResearchHypothesisId is long hid && await HypothesisProjectAsync(conn,hid,ct)!=experiment.ResearchProjectId)throw new ArgumentException("Hypothesis does not belong to this project.");
        var isNew=experiment.ResearchExperimentId==0;var now=DateTime.UtcNow;await using var cmd=conn.CreateCommand();cmd.CommandText=isNew
            ?@"INSERT INTO ResearchExperiments (ResearchProjectId,ResearchHypothesisId,Title,Method,Status,PredictedOutcome,FalsificationCriterion,ResearcherNote,Origin,AiModel,AiPrompt,AiGeneratedUtc,SortOrder,CreatedUtc,UpdatedUtc)
               VALUES (@Project,@Hypothesis,@Title,@Method,@Status,@Predicted,@Falsification,@Note,@Origin,@Model,@Prompt,@Generated,@Sort,@Now,@Now);SELECT last_insert_rowid();"
            :@"UPDATE ResearchExperiments SET ResearchHypothesisId=@Hypothesis,Title=@Title,Method=@Method,Status=@Status,PredictedOutcome=@Predicted,FalsificationCriterion=@Falsification,
               ResearcherNote=@Note,Origin=@Origin,AiModel=@Model,AiPrompt=@Prompt,AiGeneratedUtc=@Generated,SortOrder=@Sort,UpdatedUtc=@Now WHERE ResearchExperimentId=@Id AND ResearchProjectId=@Project;SELECT @Id;";
        if(!isNew)cmd.Parameters.AddWithValue("@Id",experiment.ResearchExperimentId);cmd.Parameters.AddWithValue("@Project",experiment.ResearchProjectId);cmd.Parameters.AddWithValue("@Hypothesis",Db(experiment.ResearchHypothesisId));
        cmd.Parameters.AddWithValue("@Title",experiment.Title.Trim());cmd.Parameters.AddWithValue("@Method",Store(experiment.Method));cmd.Parameters.AddWithValue("@Status",Store(experiment.Status));
        cmd.Parameters.AddWithValue("@Predicted",Db(experiment.PredictedOutcome));cmd.Parameters.AddWithValue("@Falsification",Db(experiment.FalsificationCriterion));cmd.Parameters.AddWithValue("@Note",Db(experiment.ResearcherNote));
        cmd.Parameters.AddWithValue("@Origin",Store(experiment.Origin));cmd.Parameters.AddWithValue("@Model",Db(experiment.AiModel));cmd.Parameters.AddWithValue("@Prompt",Db(experiment.AiPrompt));
        cmd.Parameters.AddWithValue("@Generated",experiment.AiGeneratedUtc is null?DBNull.Value:experiment.AiGeneratedUtc.Value.ToString("O"));cmd.Parameters.AddWithValue("@Sort",experiment.SortOrder);cmd.Parameters.AddWithValue("@Now",now.ToString("O"));
        experiment.ResearchExperimentId=Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));await LogAsync(experiment.ResearchProjectId,isNew?ResearchLogEntryKind.ExperimentAdded:ResearchLogEntryKind.ExperimentUpdated,$"{(isNew?"Added":"Updated")} experiment: {experiment.Title}",experiment.Status.ToString(),ct);return experiment.ResearchExperimentId;
    }

    public async Task DeleteExperimentAsync(long id,CancellationToken ct=default){await using var conn=await DbConnectionFactory.OpenConnectionAsync(ct);await using var read=conn.CreateCommand();read.CommandText="SELECT ResearchProjectId,Title FROM ResearchExperiments WHERE ResearchExperimentId=@Id;";read.Parameters.AddWithValue("@Id",id);await using var r=await read.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))return;var project=r.GetInt64(0);var title=r.GetString(1);await r.DisposeAsync();await using var cmd=conn.CreateCommand();cmd.CommandText="DELETE FROM ResearchExperiments WHERE ResearchExperimentId=@Id;";cmd.Parameters.AddWithValue("@Id",id);await cmd.ExecuteNonQueryAsync(ct);await SortOrderCompaction.RenumberAsync(conn,"ResearchExperiments","ResearchExperimentId",project,ct);await LogAsync(project,ResearchLogEntryKind.ExperimentRemoved,$"Removed experiment: {title}",null,ct);}

    private static async Task RequireProjectAsync(SqliteConnection conn,long id,CancellationToken ct){await using var cmd=conn.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM ResearchProjects WHERE ResearchProjectId=@Id;";cmd.Parameters.AddWithValue("@Id",id);if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))!=1)throw new ArgumentException("Research project does not exist.");}
    private static async Task<long> HypothesisProjectAsync(SqliteConnection conn,long id,CancellationToken ct){await using var cmd=conn.CreateCommand();cmd.CommandText="SELECT ResearchProjectId FROM ResearchHypotheses WHERE ResearchHypothesisId=@Id;";cmd.Parameters.AddWithValue("@Id",id);var value=await cmd.ExecuteScalarAsync(ct);if(value is null or DBNull)throw new ArgumentException("Hypothesis does not exist.");return Convert.ToInt64(value);}
    private static string? Text(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);private static DateTime? Date(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:DateTime.Parse(r.GetString(i),null,System.Globalization.DateTimeStyles.RoundtripKind);private static DateTime RequiredDate(SqliteDataReader r,int i)=>Date(r,i)!.Value;
    private static string Store<T>(T value) where T:struct,Enum=>value.ToString().ToLowerInvariant();private static T Parse<T>(string value,T fallback)where T:struct,Enum=>Enum.TryParse<T>(value,true,out var parsed)?parsed:fallback;
    private static object Db(string? value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value.Trim();private static object Db(long? value)=>value.HasValue?value.Value:DBNull.Value;
    private static Task LogAsync(long projectId,ResearchLogEntryKind kind,string summary,string? details,CancellationToken ct)=>new ResearchRepository().AddSystemResearchLogEntryAsync(new ResearchLogEntry{ResearchProjectId=projectId,Kind=kind,Summary=summary,Details=details},ct);
}
