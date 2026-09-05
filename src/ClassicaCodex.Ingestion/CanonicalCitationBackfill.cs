using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Fills in Stephanus and Bekker pagination on a library that already has the
/// texts.
///
/// The markers were being discarded, so every library built before this
/// carries Plato and Aristotle without the references anybody cites them by.
/// The obvious remedy is to ingest those texts again, and it works - but it
/// rebuilds the passages from nothing, which on a full library means an hour
/// and the word index with it, to change one column.
///
/// This reads the same files the ingest read and writes only that column, onto
/// the rows already there. Nothing else is touched: not the passage text, not
/// the citation a bookmark resolves through, not the word index. A run that
/// finds nothing to do costs a few seconds and says so.
///
/// It is safe to run twice, and safe to run on a library that has no Plato.
/// </summary>
public sealed class CanonicalCitationBackfill
{
    private readonly string _corpusRoot;

    /// <param name="corpusRoot">
    /// The folder the setup wizard downloads into - the one holding
    /// greek-texts, first1k-greek and the rest. Each edition is found under it
    /// by its own CTS URN, which is how Perseus names both the folders and the
    /// file.
    /// </param>
    public CanonicalCitationBackfill(string corpusRoot) => _corpusRoot = corpusRoot;

    public sealed record Report(int EditionsExamined, int EditionsUpdated, int PassagesUpdated, int FilesMissing)
    {
        public bool FoundNothing => EditionsUpdated == 0;
    }

    /// <summary>
    /// Where a Perseus file sits, derived from the edition's URN:
    /// urn:cts:greekLit:tlg0059.tlg001.perseus-grc1 is
    /// data/tlg0059/tlg001/tlg0059.tlg001.perseus-grc1.xml, under whichever
    /// collection folder holds it.
    ///
    /// Returns null for a URN this shape does not fit - a Menota manuscript or
    /// a translation the user made - rather than guessing at a path.
    /// </summary>
    internal static string? RelativePathFor(string? ctsUrn)
    {
        if (string.IsNullOrWhiteSpace(ctsUrn)) return null;

        var identifier = ctsUrn.Trim();
        var lastColon = identifier.LastIndexOf(':');
        if (lastColon >= 0) identifier = identifier[(lastColon + 1)..];

        var parts = identifier.Split('.');
        if (parts.Length < 3) return null;
        if (!parts[0].StartsWith("tlg", StringComparison.OrdinalIgnoreCase)
            && !parts[0].StartsWith("phi", StringComparison.OrdinalIgnoreCase)
            && !parts[0].StartsWith("stoa", StringComparison.OrdinalIgnoreCase)) return null;

        return Path.Combine("data", parts[0], parts[1], identifier + ".xml");
    }

    public async Task<Report> RunAsync(
        IProgress<string>? progress, CancellationToken cancellationToken = default)
    {
        var collections = Directory.Exists(_corpusRoot)
            ? Directory.GetDirectories(_corpusRoot)
            : Array.Empty<string>();

        if (collections.Length == 0)
        {
            progress?.Report($"No downloaded texts found under {_corpusRoot}.");
            return new Report(0, 0, 0, 0);
        }

        var editions = await LoadEditionsAsync(cancellationToken);
        progress?.Report($"Checking {editions.Count:N0} editions for Stephanus and Bekker pagination…");

        var parser = new TeiParser();
        int examined = 0, updated = 0, passages = 0, missing = 0;

        foreach (var (editionId, urn) in editions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = RelativePathFor(urn);
            if (relative == null) continue;

            var file = collections.Select(c => Path.Combine(c, relative)).FirstOrDefault(File.Exists);
            if (file == null) { missing++; continue; }

            // Cheaper than parsing: the overwhelming majority of the corpus has
            // no such marker, and reading the file as text to find that out
            // costs a fraction of building a document from it.
            var raw = await File.ReadAllTextAsync(file, cancellationToken);
            if (!raw.Contains("Stephanus", StringComparison.Ordinal)
                && !raw.Contains("Bekker", StringComparison.Ordinal)) continue;

            examined++;

            var parsed = parser.ParseXml(raw)
                .Where(n => !string.IsNullOrEmpty(n.Milestone))
                .GroupBy(n => n.CitationRef)
                .ToDictionary(g => g.Key, g => g.First().Milestone!, StringComparer.Ordinal);

            if (parsed.Count == 0) continue;

            var written = await WriteAsync(editionId, parsed, cancellationToken);
            if (written == 0) continue;

            updated++;
            passages += written;
            progress?.Report($"  {urn} — {written:N0} passages");
        }

        progress?.Report(updated == 0
            ? "Nothing to update: no installed text carries Stephanus or Bekker pagination."
            : $"Updated {passages:N0} passages across {updated:N0} editions.");

        return new Report(examined, updated, passages, missing);
    }

    private static async Task<List<(int EditionId, string Urn)>> LoadEditionsAsync(
        CancellationToken cancellationToken)
    {
        var results = new List<(int, string)>();
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EditionId, CtsUrn FROM Editions WHERE CtsUrn IS NOT NULL AND CtsUrn <> '';";

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add((reader.GetInt32(0), reader.GetString(1)));

        return results;
    }

    /// <summary>
    /// Matched on the citation rather than on position, so a passage that
    /// moved since the ingest keeps its own reference instead of inheriting a
    /// neighbour's. A row the file no longer has is left exactly as it is.
    /// </summary>
    private static async Task<int> WriteAsync(
        int editionId, Dictionary<string, string> milestones, CancellationToken cancellationToken)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (SqliteTransaction)transaction;
        cmd.CommandText = @"UPDATE TextNodes SET Milestone = @m
                            WHERE EditionId = @e AND CitationRef = @c
                              AND (Milestone IS NULL OR Milestone <> @m);";
        var milestone = cmd.Parameters.Add("@m", SqliteType.Text);
        var edition = cmd.Parameters.Add("@e", SqliteType.Integer);
        var citation = cmd.Parameters.Add("@c", SqliteType.Text);
        edition.Value = editionId;

        var written = 0;
        foreach (var (reference, value) in milestones)
        {
            cancellationToken.ThrowIfCancellationRequested();
            citation.Value = reference;
            milestone.Value = value;
            written += await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return written;
    }
}
