using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public sealed class ResearchSourceRepository
{
    public async Task<List<EvidenceAttachment>> GetAttachmentsAsync(long evidenceId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<EvidenceAttachment>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT EvidenceAttachmentId,EvidenceItemId,FilePath,FileName,MediaType,
            Sha256,FileSize,FileModifiedUtc,CreatedUtc,UpdatedUtc FROM EvidenceAttachments
            WHERE EvidenceItemId=@EvidenceId ORDER BY FileName,EvidenceAttachmentId;";
        cmd.Parameters.AddWithValue("@EvidenceId", evidenceId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadAttachment(reader));
        return result;
    }

    public async Task<long> SaveAttachmentAsync(EvidenceAttachment item,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.FilePath) || string.IsNullOrWhiteSpace(item.Sha256))
            throw new ArgumentException("A source attachment needs a path and fingerprint.", nameof(item));
        var now = DateTime.UtcNow;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        var context = await EvidenceContextAsync(conn, item.EvidenceItemId, cancellationToken);
        await using var cmd = conn.CreateCommand();
        var isNew = item.EvidenceAttachmentId == 0;
        if (!isNew)
        {
            var existing = await AttachmentContextAsync(conn, item.EvidenceAttachmentId, cancellationToken);
            if (existing == null || existing.Value.EvidenceId != item.EvidenceItemId)
                throw new ArgumentException("The source attachment does not belong to this evidence item.", nameof(item));
        }
        if (isNew)
            cmd.CommandText = @"INSERT INTO EvidenceAttachments
                (EvidenceItemId,FilePath,FileName,MediaType,Sha256,FileSize,FileModifiedUtc,CreatedUtc,UpdatedUtc)
                VALUES (@EvidenceId,@Path,@Name,@Media,@Hash,@Size,@Modified,@Now,@Now); SELECT last_insert_rowid();";
        else
        {
            cmd.CommandText = @"UPDATE EvidenceAttachments SET FilePath=@Path,FileName=@Name,MediaType=@Media,
                Sha256=@Hash,FileSize=@Size,FileModifiedUtc=@Modified,UpdatedUtc=@Now
                WHERE EvidenceAttachmentId=@Id AND EvidenceItemId=@EvidenceId; SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", item.EvidenceAttachmentId);
        }
        cmd.Parameters.AddWithValue("@EvidenceId", item.EvidenceItemId);
        cmd.Parameters.AddWithValue("@Path", item.FilePath);
        cmd.Parameters.AddWithValue("@Name", item.FileName);
        cmd.Parameters.AddWithValue("@Media", item.MediaType);
        cmd.Parameters.AddWithValue("@Hash", item.Sha256);
        cmd.Parameters.AddWithValue("@Size", item.FileSize);
        cmd.Parameters.AddWithValue("@Modified", item.FileModifiedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        item.EvidenceAttachmentId = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        if (item.CreatedUtc == default) item.CreatedUtc = now;
        item.UpdatedUtc = now;
        await LogAsync(context.ProjectId, item.EvidenceItemId, ResearchLogEntryKind.SourceAttached,
            $"Attached local source: {item.FileName}", $"SHA-256 {item.Sha256}; {item.FileSize:N0} bytes", cancellationToken);
        return item.EvidenceAttachmentId;
    }

    public async Task DeleteAttachmentAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        var found = await AttachmentContextAsync(conn, id, cancellationToken);
        if (found == null) return;
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM EvidenceAttachments WHERE EvidenceAttachmentId=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LogAsync(found.Value.ProjectId, found.Value.EvidenceId, ResearchLogEntryKind.SourceRemoved,
            $"Removed local source: {found.Value.FileName}", null, cancellationToken);
    }

    public async Task<List<EvidencePageAnnotation>> GetAnnotationsAsync(long attachmentId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<EvidencePageAnnotation>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT EvidencePageAnnotationId,EvidenceAttachmentId,PageNumber,QuotedText,
            Note,Judgment,CreatedUtc,UpdatedUtc FROM EvidencePageAnnotations
            WHERE EvidenceAttachmentId=@Id ORDER BY PageNumber,EvidencePageAnnotationId;";
        cmd.Parameters.AddWithValue("@Id", attachmentId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new EvidencePageAnnotation
        {
            EvidencePageAnnotationId=reader.GetInt64(0), EvidenceAttachmentId=reader.GetInt64(1),
            PageNumber=reader.GetInt32(2), QuotedText=reader.IsDBNull(3)?null:reader.GetString(3),
            Note=reader.IsDBNull(4)?null:reader.GetString(4),
            Judgment=Enum.TryParse<EvidenceJudgment>(reader.GetString(5),true,out var j)?j:EvidenceJudgment.Uncertain,
            CreatedUtc=DateTime.Parse(reader.GetString(6),null,System.Globalization.DateTimeStyles.RoundtripKind),
            UpdatedUtc=DateTime.Parse(reader.GetString(7),null,System.Globalization.DateTimeStyles.RoundtripKind)
        });
        return result;
    }

    public async Task<long> SaveAnnotationAsync(EvidencePageAnnotation item,
        CancellationToken cancellationToken = default)
    {
        if (item.PageNumber < 1 || (string.IsNullOrWhiteSpace(item.QuotedText) && string.IsNullOrWhiteSpace(item.Note)))
            throw new ArgumentException("A page annotation needs a page and quotation or note.", nameof(item));
        var now = DateTime.UtcNow;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        var context = await AttachmentContextAsync(conn, item.EvidenceAttachmentId, cancellationToken)
            ?? throw new ArgumentException("The source attachment does not exist.", nameof(item));
        await using var cmd = conn.CreateCommand();
        var isNew = item.EvidencePageAnnotationId == 0;
        if (!isNew && await AnnotationAttachmentIdAsync(conn, item.EvidencePageAnnotationId, cancellationToken)
            != item.EvidenceAttachmentId)
            throw new ArgumentException("The page annotation does not belong to this source attachment.", nameof(item));
        if (isNew)
            cmd.CommandText = @"INSERT INTO EvidencePageAnnotations
                (EvidenceAttachmentId,PageNumber,QuotedText,Note,Judgment,CreatedUtc,UpdatedUtc)
                VALUES (@AttachmentId,@Page,@Quote,@Note,@Judgment,@Now,@Now); SELECT last_insert_rowid();";
        else
        {
            cmd.CommandText = @"UPDATE EvidencePageAnnotations SET PageNumber=@Page,QuotedText=@Quote,
                Note=@Note,Judgment=@Judgment,UpdatedUtc=@Now
                WHERE EvidencePageAnnotationId=@Id AND EvidenceAttachmentId=@AttachmentId; SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", item.EvidencePageAnnotationId);
        }
        cmd.Parameters.AddWithValue("@AttachmentId", item.EvidenceAttachmentId);
        cmd.Parameters.AddWithValue("@Page", item.PageNumber);
        cmd.Parameters.AddWithValue("@Quote", Db(item.QuotedText));
        cmd.Parameters.AddWithValue("@Note", Db(item.Note));
        cmd.Parameters.AddWithValue("@Judgment", item.Judgment.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        item.EvidencePageAnnotationId = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        await LogAsync(context.ProjectId, context.EvidenceId,
            isNew ? ResearchLogEntryKind.PageAnnotationAdded : ResearchLogEntryKind.PageAnnotationUpdated,
            $"{(isNew ? "Added" : "Updated")} page {item.PageNumber} note in {context.FileName}",
            item.Note, cancellationToken);
        return item.EvidencePageAnnotationId;
    }

    public async Task DeleteAnnotationAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var read = conn.CreateCommand();
        read.CommandText = @"SELECT e.ResearchProjectId,a.EvidenceItemId,a.FileName,n.PageNumber
            FROM EvidencePageAnnotations n JOIN EvidenceAttachments a ON a.EvidenceAttachmentId=n.EvidenceAttachmentId
            JOIN EvidenceItems e ON e.EvidenceItemId=a.EvidenceItemId WHERE n.EvidencePageAnnotationId=@Id;";
        read.Parameters.AddWithValue("@Id", id);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return;
        var context = (ProjectId:reader.GetInt64(0), EvidenceId:reader.GetInt64(1), FileName:reader.GetString(2), Page:reader.GetInt32(3));
        await reader.DisposeAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM EvidencePageAnnotations WHERE EvidencePageAnnotationId=@Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await LogAsync(context.ProjectId, context.EvidenceId, ResearchLogEntryKind.PageAnnotationRemoved,
            $"Removed page {context.Page} note from {context.FileName}", null, cancellationToken);
    }

    private static EvidenceAttachment ReadAttachment(SqliteDataReader r) => new()
    {
        EvidenceAttachmentId=r.GetInt64(0),EvidenceItemId=r.GetInt64(1),FilePath=r.GetString(2),FileName=r.GetString(3),
        MediaType=r.GetString(4),Sha256=r.GetString(5),FileSize=r.GetInt64(6),
        FileModifiedUtc=DateTime.Parse(r.GetString(7),null,System.Globalization.DateTimeStyles.RoundtripKind),
        CreatedUtc=DateTime.Parse(r.GetString(8),null,System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedUtc=DateTime.Parse(r.GetString(9),null,System.Globalization.DateTimeStyles.RoundtripKind)
    };
    private static async Task<(long ProjectId,string Title)> EvidenceContextAsync(SqliteConnection c,long id,CancellationToken ct)
    {
        await using var cmd=c.CreateCommand();cmd.CommandText="SELECT ResearchProjectId,Title FROM EvidenceItems WHERE EvidenceItemId=@Id;";cmd.Parameters.AddWithValue("@Id",id);
        await using var r=await cmd.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new ArgumentException("Evidence does not exist.");return(r.GetInt64(0),r.GetString(1));
    }
    private static async Task<(long ProjectId,long EvidenceId,string FileName)?> AttachmentContextAsync(SqliteConnection c,long id,CancellationToken ct)
    {
        await using var cmd=c.CreateCommand();cmd.CommandText=@"SELECT e.ResearchProjectId,a.EvidenceItemId,a.FileName FROM EvidenceAttachments a JOIN EvidenceItems e ON e.EvidenceItemId=a.EvidenceItemId WHERE a.EvidenceAttachmentId=@Id;";cmd.Parameters.AddWithValue("@Id",id);
        await using var r=await cmd.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?(r.GetInt64(0),r.GetInt64(1),r.GetString(2)):null;
    }
    private static async Task<long?> AnnotationAttachmentIdAsync(SqliteConnection c,long id,CancellationToken ct)
    {
        await using var cmd=c.CreateCommand();cmd.CommandText="SELECT EvidenceAttachmentId FROM EvidencePageAnnotations WHERE EvidencePageAnnotationId=@Id;";cmd.Parameters.AddWithValue("@Id",id);
        var value=await cmd.ExecuteScalarAsync(ct);return value is null or DBNull?null:Convert.ToInt64(value);
    }
    private static Task LogAsync(long projectId,long evidenceId,ResearchLogEntryKind kind,string summary,string? details,CancellationToken ct) =>
        new ResearchRepository().AddSystemResearchLogEntryAsync(new ResearchLogEntry{ResearchProjectId=projectId,EvidenceItemId=evidenceId,Kind=kind,Summary=summary,Details=details},ct);
    private static object Db(string? value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value.Trim();
}
