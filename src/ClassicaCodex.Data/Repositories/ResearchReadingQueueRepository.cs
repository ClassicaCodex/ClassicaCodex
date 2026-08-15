using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public sealed class ResearchReadingQueueRepository
{
    public async Task<List<ResearchReadingItem>> GetAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var result = new List<ResearchReadingItem>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ResearchReadingItemId,ResearchProjectId,ResearchQuestionId,
            Kind,Status,Priority,Title,Purpose,WorkCtsUrn,EditionCtsUrn,CitationRef,LinkedEvidenceItemId,
            StableIdentifier,Locator,Quotation,Notes,PromotedEvidenceItemId,SortOrder,CreatedUtc,UpdatedUtc
            FROM ResearchReadingItems WHERE ResearchProjectId=@ProjectId
            ORDER BY CASE Priority WHEN 'high' THEN 0 WHEN 'normal' THEN 1 ELSE 2 END,
                     SortOrder,ResearchReadingItemId;";
        cmd.Parameters.AddWithValue("@ProjectId", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader));
        return result;
    }

    public async Task<long> SaveAsync(ResearchReadingItem item, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
            throw new ArgumentException("A reading item needs a title.", nameof(item));
        if (item.Kind == ResearchReadingKind.CorpusPassage &&
            (string.IsNullOrWhiteSpace(item.WorkCtsUrn) || string.IsNullOrWhiteSpace(item.CitationRef)))
            throw new ArgumentException("A corpus passage needs a work CTS URN and citation.", nameof(item));

        var now = DateTime.UtcNow;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await ValidateLinksAsync(conn, item, cancellationToken);
        var isNew = item.ResearchReadingItemId == 0;
        await using var cmd = conn.CreateCommand();
        if (isNew)
        {
            cmd.CommandText = @"INSERT INTO ResearchReadingItems
                (ResearchProjectId,ResearchQuestionId,Kind,Status,Priority,Title,Purpose,WorkCtsUrn,EditionCtsUrn,
                 CitationRef,LinkedEvidenceItemId,StableIdentifier,Locator,Quotation,Notes,PromotedEvidenceItemId,
                 SortOrder,CreatedUtc,UpdatedUtc)
                VALUES (@Project,@Question,@Kind,@Status,@Priority,@Title,@Purpose,@WorkUrn,@EditionUrn,@Citation,
                        @LinkedEvidence,@StableId,@Locator,@Quotation,@Notes,@PromotedEvidence,@Sort,@Now,@Now);
                SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = @"UPDATE ResearchReadingItems SET
                ResearchQuestionId=@Question,Kind=@Kind,Status=@Status,Priority=@Priority,Title=@Title,
                Purpose=@Purpose,WorkCtsUrn=@WorkUrn,EditionCtsUrn=@EditionUrn,CitationRef=@Citation,
                LinkedEvidenceItemId=@LinkedEvidence,StableIdentifier=@StableId,Locator=@Locator,
                Quotation=@Quotation,Notes=@Notes,PromotedEvidenceItemId=@PromotedEvidence,
                SortOrder=@Sort,UpdatedUtc=@Now
                WHERE ResearchReadingItemId=@Id AND ResearchProjectId=@Project;
                SELECT @Id;";
            cmd.Parameters.AddWithValue("@Id", item.ResearchReadingItemId);
        }

        cmd.Parameters.AddWithValue("@Project", item.ResearchProjectId);
        cmd.Parameters.AddWithValue("@Question", Db(item.ResearchQuestionId));
        cmd.Parameters.AddWithValue("@Kind", Store(item.Kind));
        cmd.Parameters.AddWithValue("@Status", Store(item.Status));
        cmd.Parameters.AddWithValue("@Priority", Store(item.Priority));
        cmd.Parameters.AddWithValue("@Title", item.Title.Trim());
        cmd.Parameters.AddWithValue("@Purpose", Db(item.Purpose));
        cmd.Parameters.AddWithValue("@WorkUrn", Db(item.WorkCtsUrn));
        cmd.Parameters.AddWithValue("@EditionUrn", Db(item.EditionCtsUrn));
        cmd.Parameters.AddWithValue("@Citation", Db(item.CitationRef));
        cmd.Parameters.AddWithValue("@LinkedEvidence", Db(item.LinkedEvidenceItemId));
        cmd.Parameters.AddWithValue("@StableId", Db(item.StableIdentifier));
        cmd.Parameters.AddWithValue("@Locator", Db(item.Locator));
        cmd.Parameters.AddWithValue("@Quotation", Db(item.Quotation));
        cmd.Parameters.AddWithValue("@Notes", Db(item.Notes));
        cmd.Parameters.AddWithValue("@PromotedEvidence", Db(item.PromotedEvidenceItemId));
        cmd.Parameters.AddWithValue("@Sort", item.SortOrder);
        cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        item.ResearchReadingItemId = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        if (item.CreatedUtc == default) item.CreatedUtc = now;
        item.UpdatedUtc = now;
        await LogAsync(item.ResearchProjectId,
            isNew ? ResearchLogEntryKind.ReadingItemAdded : ResearchLogEntryKind.ReadingItemUpdated,
            $"{(isNew ? "Queued" : "Updated")} reading: {item.Title.Trim()}",
            $"{item.Kind}; {item.Status}; {item.Priority}", cancellationToken);
        return item.ResearchReadingItemId;
    }

    public async Task MarkPromotedAsync(long itemId, long evidenceId, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        string title;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = @"SELECT q.ResearchProjectId,q.Title FROM ResearchReadingItems q
                JOIN EvidenceItems e ON e.EvidenceItemId=@Evidence AND e.ResearchProjectId=q.ResearchProjectId
                WHERE q.ResearchReadingItemId=@Id;";
            read.Parameters.AddWithValue("@Id", itemId);
            read.Parameters.AddWithValue("@Evidence", evidenceId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new ArgumentException("Reading item and evidence must belong to the same project.");
            projectId = reader.GetInt64(0);
            title = reader.GetString(1);
        }
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"UPDATE ResearchReadingItems
                SET PromotedEvidenceItemId=@Evidence,Status='reviewed',UpdatedUtc=@Now
                WHERE ResearchReadingItemId=@Id;";
            cmd.Parameters.AddWithValue("@Evidence", evidenceId);
            cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@Id", itemId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await LogAsync(projectId, ResearchLogEntryKind.ReadingItemPromoted,
            $"Promoted reading to evidence: {title}", null, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        long projectId;
        string title;
        await using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT ResearchProjectId,Title FROM ResearchReadingItems WHERE ResearchReadingItemId=@Id;";
            read.Parameters.AddWithValue("@Id", id);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return;
            projectId = reader.GetInt64(0);
            title = reader.GetString(1);
        }
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM ResearchReadingItems WHERE ResearchReadingItemId=@Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await LogAsync(projectId, ResearchLogEntryKind.ReadingItemRemoved,
            $"Removed reading item: {title}", null, cancellationToken);
    }

    private static async Task ValidateLinksAsync(SqliteConnection conn, ResearchReadingItem item, CancellationToken ct)
    {
        if (!await BelongsToProjectAsync(conn, "ResearchProjects", "ResearchProjectId",
                item.ResearchProjectId, item.ResearchProjectId, ct))
            throw new ArgumentException("Research project does not exist.", nameof(item));
        if (item.ResearchReadingItemId > 0 &&
            !await BelongsToProjectAsync(conn, "ResearchReadingItems", "ResearchReadingItemId",
                item.ResearchReadingItemId, item.ResearchProjectId, ct))
            throw new ArgumentException("Reading item does not belong to this project.", nameof(item));
        if (item.ResearchQuestionId is long question &&
            !await BelongsToProjectAsync(conn, "ResearchQuestions", "ResearchQuestionId",
                question, item.ResearchProjectId, ct))
            throw new ArgumentException("Question does not belong to this project.", nameof(item));
        foreach (var evidenceId in new[] { item.LinkedEvidenceItemId, item.PromotedEvidenceItemId }
                     .Where(id => id.HasValue).Select(id => id!.Value))
            if (!await BelongsToProjectAsync(conn, "EvidenceItems", "EvidenceItemId",
                    evidenceId, item.ResearchProjectId, ct))
                throw new ArgumentException("Linked evidence does not belong to this project.", nameof(item));
    }

    private static async Task<bool> BelongsToProjectAsync(SqliteConnection conn, string table,
        string idColumn, long id, long projectId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = table == "ResearchProjects"
            ? "SELECT COUNT(*) FROM ResearchProjects WHERE ResearchProjectId=@Id AND ResearchProjectId=@Project;"
            : $"SELECT COUNT(*) FROM {table} WHERE {idColumn}=@Id AND ResearchProjectId=@Project;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Project", projectId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    private static ResearchReadingItem Read(SqliteDataReader r) => new()
    {
        ResearchReadingItemId = r.GetInt64(0), ResearchProjectId = r.GetInt64(1),
        ResearchQuestionId = r.IsDBNull(2) ? null : r.GetInt64(2),
        Kind = Parse(r.GetString(3), ResearchReadingKind.ExternalSource),
        Status = Parse(r.GetString(4), ResearchReadingStatus.Queued),
        Priority = Parse(r.GetString(5), ResearchReadingPriority.Normal), Title = r.GetString(6),
        Purpose = Text(r, 7), WorkCtsUrn = Text(r, 8), EditionCtsUrn = Text(r, 9), CitationRef = Text(r, 10),
        LinkedEvidenceItemId = r.IsDBNull(11) ? null : r.GetInt64(11), StableIdentifier = Text(r, 12),
        Locator = Text(r, 13), Quotation = Text(r, 14), Notes = Text(r, 15),
        PromotedEvidenceItemId = r.IsDBNull(16) ? null : r.GetInt64(16), SortOrder = r.GetInt32(17),
        CreatedUtc = DateTime.Parse(r.GetString(18), null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedUtc = DateTime.Parse(r.GetString(19), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static string Store<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
    private static T Parse<T>(string value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static object Db(long? value) => value.HasValue ? value.Value : DBNull.Value;
    private static Task LogAsync(long projectId, ResearchLogEntryKind kind, string summary,
        string? details, CancellationToken ct) =>
        new ResearchRepository().AddSystemResearchLogEntryAsync(new ResearchLogEntry
        {
            ResearchProjectId = projectId, Kind = kind, Summary = summary, Details = details
        }, ct);
}
