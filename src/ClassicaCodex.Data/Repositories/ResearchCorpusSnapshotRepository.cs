using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public sealed class ResearchCorpusSnapshotRepository
{
    public async Task<List<ResearchCorpusSnapshot>> GetSnapshotsAsync(long projectId,
        CancellationToken cancellationToken = default)
    {
        var result=new List<ResearchCorpusSnapshot>();await using var conn=await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd=conn.CreateCommand();cmd.CommandText=@"SELECT ResearchCorpusSnapshotId,ResearchProjectId,Name,Scope,
            AppVersion,Notes,WorkCount,EditionCount,TextNodeCount,CreatedUtc FROM ResearchCorpusSnapshots
            WHERE ResearchProjectId=@ProjectId ORDER BY CreatedUtc DESC,ResearchCorpusSnapshotId DESC;";
        cmd.Parameters.AddWithValue("@ProjectId",projectId);await using var reader=await cmd.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))result.Add(ReadSnapshot(reader));return result;
    }

    public async Task<List<ResearchCorpusSnapshotEntry>> GetEntriesAsync(long snapshotId,
        CancellationToken cancellationToken = default)
    {
        await using var conn=await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        return await ReadEntriesAsync(conn,snapshotId,cancellationToken);
    }

    public async Task<ResearchCorpusSnapshot> CaptureAsync(long projectId,string name,
        CorpusSnapshotScope scope,string appVersion,string? notes=null,
        IProgress<CorpusSnapshotProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("A corpus snapshot needs a name.",nameof(name));
        await using var conn=await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        var workId=await ProjectWorkIdAsync(conn,projectId,cancellationToken);
        var entries=await CaptureEntriesAsync(conn,workId,scope,progress,cancellationToken);
        var now=DateTime.UtcNow;var snapshot=new ResearchCorpusSnapshot{ResearchProjectId=projectId,Name=name.Trim(),Scope=scope,
            AppVersion=string.IsNullOrWhiteSpace(appVersion)?"unknown":appVersion.Trim(),Notes=Clean(notes),
            WorkCount=entries.Select(e=>e.WorkCtsUrn).Distinct(StringComparer.Ordinal).Count(),
            EditionCount=entries.Count(e=>e.EditionCtsUrn!=null),TextNodeCount=entries.Sum(e=>e.TextNodeCount),CreatedUtc=now};
        await using var transaction=await conn.BeginTransactionAsync(cancellationToken);
        await using(var cmd=conn.CreateCommand())
        {
            cmd.Transaction=(SqliteTransaction)transaction;cmd.CommandText=@"INSERT INTO ResearchCorpusSnapshots
                (ResearchProjectId,Name,Scope,AppVersion,Notes,WorkCount,EditionCount,TextNodeCount,CreatedUtc)
                VALUES (@ProjectId,@Name,@Scope,@Version,@Notes,@Works,@Editions,@Nodes,@Created);SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@ProjectId",projectId);cmd.Parameters.AddWithValue("@Name",snapshot.Name);
            cmd.Parameters.AddWithValue("@Scope",scope.ToString());cmd.Parameters.AddWithValue("@Version",snapshot.AppVersion);
            cmd.Parameters.AddWithValue("@Notes",Db(snapshot.Notes));cmd.Parameters.AddWithValue("@Works",snapshot.WorkCount);
            cmd.Parameters.AddWithValue("@Editions",snapshot.EditionCount);cmd.Parameters.AddWithValue("@Nodes",snapshot.TextNodeCount);
            cmd.Parameters.AddWithValue("@Created",now.ToString("O"));snapshot.ResearchCorpusSnapshotId=Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        }
        foreach(var entry in entries)await InsertEntryAsync(conn,(SqliteTransaction)transaction,snapshot.ResearchCorpusSnapshotId,entry,cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await LogAsync(projectId,ResearchLogEntryKind.CorpusSnapshotCaptured,$"Captured corpus snapshot: {snapshot.Name}",
            $"{scope}; {snapshot.WorkCount} works; {snapshot.EditionCount} editions; {snapshot.TextNodeCount:N0} text nodes; app {snapshot.AppVersion}",cancellationToken);
        return snapshot;
    }

    public async Task<CorpusSnapshotComparison> CompareAsync(ResearchCorpusSnapshot snapshot,
        IProgress<CorpusSnapshotProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        await using var conn=await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        var workId=await ProjectWorkIdAsync(conn,snapshot.ResearchProjectId,cancellationToken);
        var saved=await ReadEntriesAsync(conn,snapshot.ResearchCorpusSnapshotId,cancellationToken);
        var current=await CaptureEntriesAsync(conn,workId,snapshot.Scope,progress,cancellationToken);
        var old=saved.ToDictionary(Key,StringComparer.Ordinal);var live=current.ToDictionary(Key,StringComparer.Ordinal);
        var differences=new List<CorpusSnapshotDifference>();int unchanged=0,changed=0,added=0,missing=0;
        foreach(var key in old.Keys.Union(live.Keys,StringComparer.Ordinal).OrderBy(k=>k,StringComparer.Ordinal))
        {
            if(!old.TryGetValue(key,out var before)){var after=live[key];added++;differences.Add(Difference("Added",after,"Present now but absent from the snapshot."));continue;}
            if(!live.TryGetValue(key,out var afterEntry)){missing++;differences.Add(Difference("Missing",before,"Present in the snapshot but absent now."));continue;}
            var details=Changes(before,afterEntry);if(details.Count==0){unchanged++;continue;}
            changed++;differences.Add(Difference("Changed",afterEntry,string.Join("; ",details)));
        }
        return new CorpusSnapshotComparison(unchanged,changed,added,missing,differences);
    }

    public async Task DeleteAsync(long snapshotId,CancellationToken cancellationToken=default)
    {
        await using var conn=await DbConnectionFactory.OpenConnectionAsync(cancellationToken);long projectId;string name;
        await using(var read=conn.CreateCommand()){read.CommandText="SELECT ResearchProjectId,Name FROM ResearchCorpusSnapshots WHERE ResearchCorpusSnapshotId=@Id;";read.Parameters.AddWithValue("@Id",snapshotId);await using var r=await read.ExecuteReaderAsync(cancellationToken);if(!await r.ReadAsync(cancellationToken))return;projectId=r.GetInt64(0);name=r.GetString(1);}
        await using(var cmd=conn.CreateCommand()){cmd.CommandText="DELETE FROM ResearchCorpusSnapshots WHERE ResearchCorpusSnapshotId=@Id;";cmd.Parameters.AddWithValue("@Id",snapshotId);await cmd.ExecuteNonQueryAsync(cancellationToken);}
        await LogAsync(projectId,ResearchLogEntryKind.CorpusSnapshotRemoved,$"Removed corpus snapshot: {name}",null,cancellationToken);
    }

    private static async Task<List<ResearchCorpusSnapshotEntry>> CaptureEntriesAsync(SqliteConnection conn,int workId,
        CorpusSnapshotScope scope,IProgress<CorpusSnapshotProgress>? progress,CancellationToken ct)
    {
        var entries=new List<(ResearchCorpusSnapshotEntry Entry,long? EditionId)>();await using(var cmd=conn.CreateCommand())
        {
            cmd.CommandText=@"SELECT a.CtsUrn,a.Name,w.CtsUrn,w.Title,w.AttributionStatus,w.AttributionNote,
                w.AttributionSetByUser,e.EditionId,e.CtsUrn,e.Kind,e.Language,e.Translator,e.Orthography,
                w.CitationScheme,e.SourcePath
                FROM Works w JOIN Authors a ON a.AuthorId=w.AuthorId LEFT JOIN Editions e ON e.WorkId=w.WorkId
                WHERE (@Scope='EntireCorpus') OR (@Scope='ProjectWork' AND w.WorkId=@WorkId)
                   OR (@Scope='SameAuthor' AND w.AuthorId=(SELECT AuthorId FROM Works WHERE WorkId=@WorkId))
                ORDER BY a.CtsUrn,w.CtsUrn,e.CtsUrn;";
            cmd.Parameters.AddWithValue("@Scope",scope.ToString());cmd.Parameters.AddWithValue("@WorkId",workId);
            await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))entries.Add((new ResearchCorpusSnapshotEntry{
                AuthorCtsUrn=r.GetString(0),AuthorName=r.GetString(1),WorkCtsUrn=r.GetString(2),WorkTitle=r.GetString(3),
                AttributionStatus=r.GetString(4),AttributionNote=r.IsDBNull(5)?null:r.GetString(5),AttributionSetByUser=r.GetInt32(6)!=0,
                EditionCtsUrn=r.IsDBNull(8)?null:r.GetString(8),EditionKind=r.IsDBNull(9)?null:r.GetString(9),Language=r.IsDBNull(10)?null:r.GetString(10),
                Translator=r.IsDBNull(11)?null:r.GetString(11),Orthography=r.IsDBNull(12)?null:r.GetString(12),CitationScheme=r.IsDBNull(13)?null:r.GetString(13),
                SourcePath=r.IsDBNull(14)?null:r.GetString(14)},r.IsDBNull(7)?null:r.GetInt64(7)));
        }
        for(var i=0;i<entries.Count;i++)
        {
            ct.ThrowIfCancellationRequested();var pair=entries[i];progress?.Report(new CorpusSnapshotProgress(i,entries.Count,pair.Entry.WorkTitle));
            if(pair.EditionId is long editionId)(pair.Entry.TextNodeCount,pair.Entry.ContentSha256)=await FingerprintEditionAsync(conn,editionId,ct);
        }
        progress?.Report(new CorpusSnapshotProgress(entries.Count,entries.Count,"Complete"));return entries.Select(x=>x.Entry).ToList();
    }

    private static async Task<(long Count,string Hash)> FingerprintEditionAsync(SqliteConnection conn,long editionId,CancellationToken ct)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long count=0;await using var cmd=conn.CreateCommand();
        cmd.CommandText="SELECT CitationRef,SortOrder,NodeKind,IsAthetized,Text FROM TextNodes WHERE EditionId=@Id ORDER BY SortOrder,TextNodeId;";cmd.Parameters.AddWithValue("@Id",editionId);
        await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){Append(hash,r.GetString(0));Append(hash,r.GetInt32(1).ToString(System.Globalization.CultureInfo.InvariantCulture));Append(hash,r.GetString(2));Append(hash,r.GetInt32(3).ToString());Append(hash,r.GetString(4));count++;}
        return(count,Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
    private static void Append(IncrementalHash hash,string value){var bytes=Encoding.UTF8.GetBytes(value);Span<byte> size=stackalloc byte[4];BinaryPrimitives.WriteInt32LittleEndian(size,bytes.Length);hash.AppendData(size);hash.AppendData(bytes);}

    private static async Task InsertEntryAsync(SqliteConnection conn,SqliteTransaction tx,long snapshotId,ResearchCorpusSnapshotEntry e,CancellationToken ct)
    {
        await using var cmd=conn.CreateCommand();cmd.Transaction=tx;cmd.CommandText=@"INSERT INTO ResearchCorpusSnapshotEntries
            (ResearchCorpusSnapshotId,AuthorCtsUrn,AuthorName,WorkCtsUrn,WorkTitle,CitationScheme,AttributionStatus,AttributionNote,
             AttributionSetByUser,EditionCtsUrn,EditionKind,Language,Translator,SourcePath,Orthography,TextNodeCount,ContentSha256)
            VALUES (@Snapshot,@AuthorUrn,@Author,@WorkUrn,@Work,@CitationScheme,@Attribution,@AttributionNote,@User,@EditionUrn,
                    @Kind,@Language,@Translator,@SourcePath,@Orthography,@Nodes,@Hash);";
        cmd.Parameters.AddWithValue("@Snapshot",snapshotId);cmd.Parameters.AddWithValue("@AuthorUrn",e.AuthorCtsUrn);cmd.Parameters.AddWithValue("@Author",e.AuthorName);
        cmd.Parameters.AddWithValue("@WorkUrn",e.WorkCtsUrn);cmd.Parameters.AddWithValue("@Work",e.WorkTitle);cmd.Parameters.AddWithValue("@CitationScheme",Db(e.CitationScheme));cmd.Parameters.AddWithValue("@Attribution",e.AttributionStatus);
        cmd.Parameters.AddWithValue("@AttributionNote",Db(e.AttributionNote));cmd.Parameters.AddWithValue("@User",e.AttributionSetByUser?1:0);cmd.Parameters.AddWithValue("@EditionUrn",Db(e.EditionCtsUrn));
        cmd.Parameters.AddWithValue("@Kind",Db(e.EditionKind));cmd.Parameters.AddWithValue("@Language",Db(e.Language));cmd.Parameters.AddWithValue("@Translator",Db(e.Translator));cmd.Parameters.AddWithValue("@SourcePath",Db(e.SourcePath));
        cmd.Parameters.AddWithValue("@Orthography",Db(e.Orthography));cmd.Parameters.AddWithValue("@Nodes",e.TextNodeCount);cmd.Parameters.AddWithValue("@Hash",Db(e.ContentSha256));await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<ResearchCorpusSnapshotEntry>> ReadEntriesAsync(SqliteConnection conn,long id,CancellationToken ct)
    {
        var result=new List<ResearchCorpusSnapshotEntry>();await using var cmd=conn.CreateCommand();cmd.CommandText=@"SELECT ResearchCorpusSnapshotEntryId,ResearchCorpusSnapshotId,
            AuthorCtsUrn,AuthorName,WorkCtsUrn,WorkTitle,AttributionStatus,AttributionNote,AttributionSetByUser,
            EditionCtsUrn,EditionKind,Language,Translator,Orthography,TextNodeCount,ContentSha256,CitationScheme,SourcePath
            FROM ResearchCorpusSnapshotEntries WHERE ResearchCorpusSnapshotId=@Id ORDER BY WorkCtsUrn,EditionCtsUrn;";cmd.Parameters.AddWithValue("@Id",id);
        await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))result.Add(new ResearchCorpusSnapshotEntry{
            ResearchCorpusSnapshotEntryId=r.GetInt64(0),ResearchCorpusSnapshotId=r.GetInt64(1),AuthorCtsUrn=r.GetString(2),AuthorName=r.GetString(3),
            WorkCtsUrn=r.GetString(4),WorkTitle=r.GetString(5),AttributionStatus=r.GetString(6),AttributionNote=r.IsDBNull(7)?null:r.GetString(7),AttributionSetByUser=r.GetInt32(8)!=0,
            EditionCtsUrn=r.IsDBNull(9)?null:r.GetString(9),EditionKind=r.IsDBNull(10)?null:r.GetString(10),Language=r.IsDBNull(11)?null:r.GetString(11),Translator=r.IsDBNull(12)?null:r.GetString(12),
            Orthography=r.IsDBNull(13)?null:r.GetString(13),TextNodeCount=r.GetInt64(14),ContentSha256=r.IsDBNull(15)?null:r.GetString(15),
            CitationScheme=r.IsDBNull(16)?null:r.GetString(16),SourcePath=r.IsDBNull(17)?null:r.GetString(17)});return result;
    }
    private static ResearchCorpusSnapshot ReadSnapshot(SqliteDataReader r)=>new(){ResearchCorpusSnapshotId=r.GetInt64(0),ResearchProjectId=r.GetInt64(1),Name=r.GetString(2),
        Scope=Enum.TryParse<CorpusSnapshotScope>(r.GetString(3),true,out var scope)?scope:CorpusSnapshotScope.ProjectWork,AppVersion=r.GetString(4),Notes=r.IsDBNull(5)?null:r.GetString(5),
        WorkCount=r.GetInt32(6),EditionCount=r.GetInt32(7),TextNodeCount=r.GetInt64(8),CreatedUtc=DateTime.Parse(r.GetString(9),null,System.Globalization.DateTimeStyles.RoundtripKind)};
    private static async Task<int> ProjectWorkIdAsync(SqliteConnection conn,long projectId,CancellationToken ct){await using var cmd=conn.CreateCommand();cmd.CommandText="SELECT WorkId FROM ResearchProjects WHERE ResearchProjectId=@Id;";cmd.Parameters.AddWithValue("@Id",projectId);var value=await cmd.ExecuteScalarAsync(ct);if(value is null or DBNull)throw new ArgumentException("Research project does not exist.");return Convert.ToInt32(value);}
    private static string Key(ResearchCorpusSnapshotEntry e)=>e.EditionCtsUrn??"work:"+e.WorkCtsUrn;
    private static CorpusSnapshotDifference Difference(string status,ResearchCorpusSnapshotEntry e,string details)=>new(status,$"{e.AuthorName} — {e.WorkTitle}",e.EditionCtsUrn??"(no edition)",details);
    private static List<string> Changes(ResearchCorpusSnapshotEntry a,ResearchCorpusSnapshotEntry b)
    {
        var c=new List<string>();if(a.AuthorName!=b.AuthorName||a.WorkTitle!=b.WorkTitle)c.Add("catalog label changed");if(a.CitationScheme!=b.CitationScheme)c.Add("citation scheme changed");if(a.AttributionStatus!=b.AttributionStatus||a.AttributionNote!=b.AttributionNote||a.AttributionSetByUser!=b.AttributionSetByUser)c.Add($"attribution {a.AttributionStatus} → {b.AttributionStatus}");
        if(a.EditionKind!=b.EditionKind||a.Language!=b.Language||a.Translator!=b.Translator||a.SourcePath!=b.SourcePath||a.Orthography!=b.Orthography)c.Add("edition metadata changed");if(a.TextNodeCount!=b.TextNodeCount)c.Add($"text nodes {a.TextNodeCount:N0} → {b.TextNodeCount:N0}");if(a.ContentSha256!=b.ContentSha256)c.Add("ordered text fingerprint changed");return c;
    }
    private static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();private static object Db(string? value)=>Clean(value) is { } s?s:DBNull.Value;
    private static Task LogAsync(long projectId,ResearchLogEntryKind kind,string summary,string? details,CancellationToken ct)=>new ResearchRepository().AddSystemResearchLogEntryAsync(new ResearchLogEntry{ResearchProjectId=projectId,Kind=kind,Summary=summary,Details=details},ct);
}
