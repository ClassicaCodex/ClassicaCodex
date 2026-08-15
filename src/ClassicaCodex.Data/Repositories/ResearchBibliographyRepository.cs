using System.Text.Json;
using ClassicaCodex.Core;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

public sealed class ResearchBibliographyRepository
{
    public async Task<List<EvidenceBibliographyMetadata>> GetForProjectAsync(long projectId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<EvidenceBibliographyMetadata>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT e.EvidenceItemId,e.Title,e.StableIdentifier,e.Provenance,
            m.ImportFormat,m.EntryType,m.CiteKey,m.Title,m.AuthorsJson,m.Year,m.ContainerTitle,
            m.Volume,m.Issue,m.Pages,m.Publisher,m.Doi,m.Url,m.Isbn,m.Abstract,m.KeywordsJson,
            m.CreatedUtc,m.UpdatedUtc
            FROM EvidenceItems e LEFT JOIN EvidenceBibliographyMetadata m ON m.EvidenceItemId=e.EvidenceItemId
            WHERE e.ResearchProjectId=@ProjectId AND e.EvidenceType='scholarship'
            ORDER BY e.SortOrder,e.EvidenceItemId;";
        cmd.Parameters.AddWithValue("@ProjectId", projectId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(4)) result.Add(Read(reader));
            else
            {
                var stable = reader.IsDBNull(2) ? null : reader.GetString(2);
                var doi = BibliographyImport.NormalizeDoi(stable);
                if (doi?.StartsWith("10.", StringComparison.Ordinal) != true) doi = null;
                result.Add(new EvidenceBibliographyMetadata
                {
                    EvidenceItemId = reader.GetInt64(0), Title = reader.GetString(1),
                    CiteKey = CiteKeyFromProvenance(reader.IsDBNull(3) ? null : reader.GetString(3)),
                    Doi = doi,
                    Isbn = stable?.StartsWith("isbn:", StringComparison.OrdinalIgnoreCase) == true ? stable[5..].Trim() : null,
                    Url = doi == null && Uri.TryCreate(stable, UriKind.Absolute, out _) ? stable : null
                });
            }
        }
        return result;
    }

    public async Task<EvidenceBibliographyMetadata?> GetAsync(long evidenceItemId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT e.EvidenceItemId,e.Title,e.StableIdentifier,e.Provenance,
            m.ImportFormat,m.EntryType,m.CiteKey,m.Title,m.AuthorsJson,m.Year,m.ContainerTitle,
            m.Volume,m.Issue,m.Pages,m.Publisher,m.Doi,m.Url,m.Isbn,m.Abstract,m.KeywordsJson,
            m.CreatedUtc,m.UpdatedUtc
            FROM EvidenceItems e LEFT JOIN EvidenceBibliographyMetadata m ON m.EvidenceItemId=e.EvidenceItemId
            WHERE e.EvidenceItemId=@Id;";
        cmd.Parameters.AddWithValue("@Id", evidenceItemId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.IsDBNull(4)) return Read(reader);
        var stable=reader.IsDBNull(2)?null:reader.GetString(2);var doi=BibliographyImport.NormalizeDoi(stable);
        if(doi?.StartsWith("10.",StringComparison.Ordinal)!=true)doi=null;
        return new EvidenceBibliographyMetadata { EvidenceItemId=evidenceItemId,Title=reader.GetString(1),
            CiteKey=CiteKeyFromProvenance(reader.IsDBNull(3)?null:reader.GetString(3)),Doi=doi,
            Isbn=stable?.StartsWith("isbn:",StringComparison.OrdinalIgnoreCase)==true?stable[5..].Trim():null,
            Url=doi==null&&Uri.TryCreate(stable,UriKind.Absolute,out _)?stable:null };
    }

    public async Task SaveAsync(EvidenceBibliographyMetadata item,
        CancellationToken cancellationToken = default)
    {
        if (item.EvidenceItemId < 1 || string.IsNullOrWhiteSpace(item.Title))
            throw new ArgumentException("Bibliography metadata needs evidence and a title.", nameof(item));
        var now = DateTime.UtcNow;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO EvidenceBibliographyMetadata
            (EvidenceItemId,ImportFormat,EntryType,CiteKey,Title,AuthorsJson,Year,ContainerTitle,
             Volume,Issue,Pages,Publisher,Doi,Url,Isbn,Abstract,KeywordsJson,CreatedUtc,UpdatedUtc)
            SELECT @Id,@Format,@Type,@Key,@Title,@Authors,@Year,@Container,@Volume,@Issue,@Pages,
                   @Publisher,@Doi,@Url,@Isbn,@Abstract,@Keywords,@Now,@Now
            WHERE EXISTS (SELECT 1 FROM EvidenceItems WHERE EvidenceItemId=@Id)
            ON CONFLICT(EvidenceItemId) DO UPDATE SET ImportFormat=excluded.ImportFormat,
                EntryType=excluded.EntryType,CiteKey=excluded.CiteKey,Title=excluded.Title,
                AuthorsJson=excluded.AuthorsJson,Year=excluded.Year,ContainerTitle=excluded.ContainerTitle,
                Volume=excluded.Volume,Issue=excluded.Issue,Pages=excluded.Pages,Publisher=excluded.Publisher,
                Doi=excluded.Doi,Url=excluded.Url,Isbn=excluded.Isbn,Abstract=excluded.Abstract,
                KeywordsJson=excluded.KeywordsJson,UpdatedUtc=excluded.UpdatedUtc;";
        cmd.Parameters.AddWithValue("@Id", item.EvidenceItemId);
        cmd.Parameters.AddWithValue("@Format", Value(item.ImportFormat) ?? "Manual");
        cmd.Parameters.AddWithValue("@Type", Value(item.EntryType) ?? "MISC");
        cmd.Parameters.AddWithValue("@Key", Db(item.CiteKey)); cmd.Parameters.AddWithValue("@Title", item.Title.Trim());
        cmd.Parameters.AddWithValue("@Authors", JsonSerializer.Serialize(item.Authors));
        cmd.Parameters.AddWithValue("@Year", Db(item.Year)); cmd.Parameters.AddWithValue("@Container", Db(item.ContainerTitle));
        cmd.Parameters.AddWithValue("@Volume", Db(item.Volume)); cmd.Parameters.AddWithValue("@Issue", Db(item.Issue));
        cmd.Parameters.AddWithValue("@Pages", Db(item.Pages)); cmd.Parameters.AddWithValue("@Publisher", Db(item.Publisher));
        cmd.Parameters.AddWithValue("@Doi", Db(BibliographyImport.NormalizeDoi(item.Doi))); cmd.Parameters.AddWithValue("@Url", Db(item.Url));
        cmd.Parameters.AddWithValue("@Isbn", Db(item.Isbn)); cmd.Parameters.AddWithValue("@Abstract", Db(item.Abstract));
        cmd.Parameters.AddWithValue("@Keywords", JsonSerializer.Serialize(item.Keywords)); cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        if (await cmd.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new ArgumentException("The evidence item does not exist.", nameof(item));
        if (item.CreatedUtc == default) item.CreatedUtc = now;
        item.UpdatedUtc = now; item.IsStored = true;
    }

    private static EvidenceBibliographyMetadata Read(SqliteDataReader r) => new()
    {
        EvidenceItemId=r.GetInt64(0),ImportFormat=r.GetString(4),EntryType=r.GetString(5),
        CiteKey=r.IsDBNull(6)?null:r.GetString(6),Title=r.GetString(7),Authors=List(r.GetString(8)),
        Year=Text(r,9),ContainerTitle=Text(r,10),Volume=Text(r,11),Issue=Text(r,12),Pages=Text(r,13),
        Publisher=Text(r,14),Doi=Text(r,15),Url=Text(r,16),Isbn=Text(r,17),Abstract=Text(r,18),
        Keywords=List(r.GetString(19)),CreatedUtc=DateTime.Parse(r.GetString(20),null,System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedUtc=DateTime.Parse(r.GetString(21),null,System.Globalization.DateTimeStyles.RoundtripKind),IsStored=true
    };
    private static List<string> List(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
    private static string? Text(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static string? Value(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static object Db(string? value)=>Value(value) is { } text?text:DBNull.Value;
    private static string? CiteKeyFromProvenance(string? provenance)
    {
        const string marker="; cite key ";
        if(string.IsNullOrWhiteSpace(provenance))return null;
        var start=provenance.IndexOf(marker,StringComparison.OrdinalIgnoreCase);
        if(start<0)return null;start+=marker.Length;
        var end=provenance.EndsWith('.')?provenance.Length-1:provenance.Length;
        return Value(provenance[start..end]);
    }
}
