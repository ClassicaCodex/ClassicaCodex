namespace ClassicaCodex.Mac.Features.Tags;

/// <summary>
/// One gathered passage in a result list, in the shape every tag query
/// returns it.
///
/// A class rather than the repositories' tuples or a record, because a list
/// selects by equality: two rows with the same fields - the same line
/// reached through two tags on a network edge, say - must still be two rows.
/// </summary>
internal sealed class PassageItem
{
    public PassageItem(
        int workId, long textNodeId, string authorName, string workTitle,
        string citationRef, string text, string? milestone, string? tagName = null)
    {
        WorkId = workId;
        TextNodeId = textNodeId;
        AuthorName = authorName;
        WorkTitle = workTitle;
        CitationRef = citationRef;
        Text = text;
        Milestone = milestone;
        TagName = tagName;
    }

    public static PassageItem From(
        (int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text, string? Milestone) row) =>
        new(row.WorkId, row.TextNodeId, row.AuthorName, row.WorkTitle, row.CitationRef, row.Text, row.Milestone);

    public int WorkId { get; }
    public long TextNodeId { get; }
    public string AuthorName { get; }
    public string WorkTitle { get; }
    public string CitationRef { get; }
    public string Text { get; }
    public string? Milestone { get; }

    /// <summary>Which of two tags this row came from, on a list of an edge's passages.</summary>
    public string? TagName { get; }

    public string Source => $"{AuthorName}, {WorkTitle}";

    public string CopyText => PassageResults.CopyLine(AuthorName, WorkTitle, CitationRef, Milestone, Text);
}
