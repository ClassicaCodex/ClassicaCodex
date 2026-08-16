using ClassicaCodex.Core;

namespace ClassicaCodex.Data.Repositories;

/// <summary>Persistence for passage-first notes that may later become projects.</summary>
public sealed class PassageInquiryRepository
{
    public async Task<PassageInquiry?> GetAsync(
        string editionCtsUrn, string citationRef, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT PassageInquiryId,WorkCtsUrn,EditionCtsUrn,CitationRef,
            AuthorName,WorkTitle,Excerpt,AttentionNote,DraftQuestion,Direction,
            ResearchProjectId,CreatedUtc,UpdatedUtc
            FROM PassageInquiries WHERE EditionCtsUrn=@Edition AND CitationRef=@Citation;";
        cmd.Parameters.AddWithValue("@Edition", editionCtsUrn);
        cmd.Parameters.AddWithValue("@Citation", citationRef);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<long> SaveAsync(
        PassageInquiry inquiry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inquiry.EditionCtsUrn) ||
            string.IsNullOrWhiteSpace(inquiry.CitationRef))
            throw new ArgumentException("An inquiry needs a stable passage identity.", nameof(inquiry));
        if (string.IsNullOrWhiteSpace(inquiry.AttentionNote))
            throw new ArgumentException("Record what caught your attention first.", nameof(inquiry));
        if (string.IsNullOrWhiteSpace(inquiry.DraftQuestion))
            throw new ArgumentException("Draft the question in your own words first.", nameof(inquiry));

        var now = DateTime.UtcNow;
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO PassageInquiries
            (WorkCtsUrn,EditionCtsUrn,CitationRef,AuthorName,WorkTitle,Excerpt,
             AttentionNote,DraftQuestion,Direction,ResearchProjectId,CreatedUtc,UpdatedUtc)
            VALUES (@Work,@Edition,@Citation,@Author,@Title,@Excerpt,@Attention,@Question,
                    @Direction,@Project,@Now,@Now)
            ON CONFLICT(EditionCtsUrn,CitationRef) DO UPDATE SET
                WorkCtsUrn=excluded.WorkCtsUrn,AuthorName=excluded.AuthorName,
                WorkTitle=excluded.WorkTitle,Excerpt=excluded.Excerpt,
                AttentionNote=excluded.AttentionNote,DraftQuestion=excluded.DraftQuestion,
                Direction=excluded.Direction,UpdatedUtc=excluded.UpdatedUtc
            RETURNING PassageInquiryId,CreatedUtc;";
        cmd.Parameters.AddWithValue("@Work", inquiry.WorkCtsUrn);
        cmd.Parameters.AddWithValue("@Edition", inquiry.EditionCtsUrn);
        cmd.Parameters.AddWithValue("@Citation", inquiry.CitationRef);
        cmd.Parameters.AddWithValue("@Author", inquiry.AuthorName);
        cmd.Parameters.AddWithValue("@Title", inquiry.WorkTitle);
        cmd.Parameters.AddWithValue("@Excerpt", inquiry.Excerpt);
        cmd.Parameters.AddWithValue("@Attention", inquiry.AttentionNote.Trim());
        cmd.Parameters.AddWithValue("@Question", inquiry.DraftQuestion.Trim());
        cmd.Parameters.AddWithValue("@Direction", Store(inquiry.Direction));
        cmd.Parameters.AddWithValue("@Project", inquiry.ResearchProjectId is { } id ? id : DBNull.Value);
        cmd.Parameters.AddWithValue("@Now", now.ToString("O"));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The inquiry could not be saved.");
        inquiry.PassageInquiryId = reader.GetInt64(0);
        inquiry.CreatedUtc = DateTime.Parse(reader.GetString(1), null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        inquiry.UpdatedUtc = now;
        return inquiry.PassageInquiryId;
    }

    public async Task LinkProjectAsync(long inquiryId, long projectId,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE PassageInquiries SET ResearchProjectId=@Project,UpdatedUtc=@Now
            WHERE PassageInquiryId=@Inquiry;";
        cmd.Parameters.AddWithValue("@Project", projectId);
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Inquiry", inquiryId);
        if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The passage inquiry no longer exists.");
    }

    private static PassageInquiry Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        PassageInquiryId = reader.GetInt64(0),
        WorkCtsUrn = reader.GetString(1),
        EditionCtsUrn = reader.GetString(2),
        CitationRef = reader.GetString(3),
        AuthorName = reader.GetString(4),
        WorkTitle = reader.GetString(5),
        Excerpt = reader.GetString(6),
        AttentionNote = reader.GetString(7),
        DraftQuestion = reader.GetString(8),
        Direction = ParseDirection(reader.GetString(9)),
        ResearchProjectId = reader.IsDBNull(10) ? null : reader.GetInt64(10),
        CreatedUtc = DateTime.Parse(reader.GetString(11), null,
            System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedUtc = DateTime.Parse(reader.GetString(12), null,
            System.Globalization.DateTimeStyles.RoundtripKind)
    };

    private static string Store(PassageInquiryDirection direction) => direction switch
    {
        PassageInquiryDirection.ReadClosely => "readClosely",
        PassageInquiryDirection.Compare => "compare",
        PassageInquiryDirection.Research => "research",
        _ => "none"
    };

    private static PassageInquiryDirection ParseDirection(string value) => value switch
    {
        "readClosely" => PassageInquiryDirection.ReadClosely,
        "compare" => PassageInquiryDirection.Compare,
        "research" => PassageInquiryDirection.Research,
        _ => PassageInquiryDirection.None
    };
}
