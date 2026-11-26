using System.Text.RegularExpressions;
using ClassicaCodex.Core.Lexicon;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>A passage a dictionary citation opens, as this library holds it.</summary>
/// <param name="How">Which of <see cref="PassageIndex"/>'s steps found it.</param>
public sealed record CitedPassage(
    Edition Edition, long TextNodeId, string CitationRef, string? Milestone, string Text,
    string AuthorName, string WorkTitle, CitationMatch How);

/// <summary>A dictionary entry that cites a passage, with what the window needs to show it.</summary>
/// <param name="Ordinal">Which of the entry's citations it is, to find it in <paramref name="Entry"/>.</param>
/// <param name="Sense">The sense it was printed under, "A.II.2", or null.</param>
public sealed record CitingEntry(
    long CitationId, long DefinitionId, int Ordinal, string? Sense, string Headword, string Language, string? Source, string Entry);

/// <summary>
/// Opens the dictionaries' citations: finds the passage each names, choosing
/// among the editions of a work the way the reader does, and remembers each
/// edition's passages for the life of the resolver - a long entry cites the
/// same work hundreds of times.
///
/// <b>Which edition.</b> A work is often held more than once - Diodorus in
/// three Perseus editions that divide the books between them, Aeschylus in
/// Perseus and in First1K - and the dictionary usually names an edition the
/// library does not hold (LSJ was keyed to perseus-grc1; the library has
/// grc2). Originals only, never a translation. The edition the reader would
/// open the work in comes first, the preferred collection honoured; then the
/// edition the dictionary names, where it is held; then the rest in the
/// reader's order. The first that holds the reference is the one opened, so
/// a work split across editions still finds the book it needs.
///
/// The order is the caller's to give, because the reader's order is a matter
/// of the reader's labels and settings, which this layer does not see.
/// </summary>
public sealed class LexiconCitationResolver
{
    private static readonly Regex UrnHead = new(@"^urn:cts:[^:]+:", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Func<IReadOnlyList<Edition>, IReadOnlyList<Edition>> _arrange;
    private readonly Dictionary<string, IReadOnlyList<Edition>> _editionsByWork = new(StringComparer.Ordinal);
    private readonly Dictionary<int, PassageIndex> _indexes = new();
    private readonly Dictionary<int, (string Author, string Title)> _names = new();

    // One resolution at a time: the caches are plain dictionaries, and Word
    // Study starts a new one each time the reader picks another headword.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="arrange">
    /// The originals of a work in the order the reader offers them, the one it
    /// opens by default first. Without one, by identifier.
    /// </param>
    public LexiconCitationResolver(Func<IReadOnlyList<Edition>, IReadOnlyList<Edition>>? arrange = null)
    {
        _arrange = arrange ?? (editions => editions.OrderBy(e => e.CtsUrn, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>An identifier without its "urn:cts:namespace:" head, as the library compares them.</summary>
    public static string Key(string urn) => UrnHead.Replace(urn.Trim(), string.Empty);

    /// <summary>The passage each target names, in the same order; null where the library holds none.</summary>
    public async Task<IReadOnlyList<CitedPassage?>> ResolveAsync(
        IReadOnlyList<LexiconCitationTarget?> targets, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ResolveOnceAsync(targets, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<CitedPassage?>> ResolveOnceAsync(
        IReadOnlyList<LexiconCitationTarget?> targets, CancellationToken cancellationToken)
    {
        var found = new (Edition Edition, long Id, CitationMatch How)?[targets.Count];
        if (targets.All(t => t == null)) return new CitedPassage?[targets.Count];

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        for (var i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (targets[i] is not { } target) continue;

            foreach (var edition in await CandidatesAsync(conn, target, cancellationToken))
            {
                var index = await IndexAsync(conn, edition, target.WorkUrn, cancellationToken);
                if (index.Find(target.Passage, CitationConventions.MayStopAtChapter(target.WorkUrn, target.Passage)) is { } hit)
                {
                    found[i] = (edition, hit.TextNodeId, hit.How);
                    break;
                }
            }
        }

        var passages = await PassagesAsync(conn, found.Where(f => f != null).Select(f => f!.Value.Id).Distinct().ToList(), cancellationToken);

        var result = new CitedPassage?[targets.Count];
        for (var i = 0; i < targets.Count; i++)
        {
            if (found[i] is not { } f || !passages.TryGetValue(f.Id, out var p)) continue;
            var (author, title) = await NamesAsync(conn, f.Edition.WorkId, cancellationToken);
            result[i] = new CitedPassage(f.Edition, f.Id, p.CitationRef, p.Milestone, p.Text, author, title, f.How);
        }

        return result;
    }

    /// <summary>The editions to try for a target, in the order they are tried.</summary>
    internal async Task<IReadOnlyList<Edition>> CandidatesAsync(
        SqliteConnection conn, LexiconCitationTarget target, CancellationToken cancellationToken)
    {
        if (!_editionsByWork.TryGetValue(target.WorkUrn, out var arranged))
        {
            var originals = new List<Edition>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT e.EditionId, e.WorkId, e.CtsUrn, e.Kind, e.Language, e.Translator, e.SourcePath, e.Orthography, e.Collection
                      FROM Editions e
                      JOIN Works w ON w.WorkId = e.WorkId
                     WHERE (TRIM(w.CtsUrn) = @Work OR TRIM(w.CtsUrn) LIKE 'urn:cts:%:' || @Work)
                       AND e.Kind = 'Original';";
                cmd.Parameters.AddWithValue("@Work", target.WorkUrn);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) originals.Add(EditionRepository.Read(reader));
            }

            arranged = originals.Count == 0 ? originals : _arrange(originals);
            _editionsByWork[target.WorkUrn] = arranged;
        }

        if (target.EditionUrn == null || arranged.Count < 3) return arranged;

        // The edition the dictionary names goes second, after the reader's own.
        var named = arranged.ToList().FindIndex(e => string.Equals(Key(e.CtsUrn), target.EditionUrn, StringComparison.OrdinalIgnoreCase));
        if (named <= 1) return arranged;

        var order = arranged.ToList();
        var edition = order[named];
        order.RemoveAt(named);
        order.Insert(1, edition);
        return order;
    }

    private async Task<PassageIndex> IndexAsync(SqliteConnection conn, Edition edition, string workUrn, CancellationToken cancellationToken)
    {
        var editionId = edition.EditionId;
        if (_indexes.TryGetValue(editionId, out var known)) return known;

        var passages = new List<(long, string, string?)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT TextNodeId, CitationRef, Milestone
                  FROM TextNodes
                 WHERE EditionId = @EditionId
                 ORDER BY SortOrder, TextNodeId;";
            cmd.Parameters.AddWithValue("@EditionId", editionId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                passages.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        var index = new PassageIndex(passages, CitationConventions.SectionsRunThrough(workUrn));
        _indexes[editionId] = index;
        return index;
    }

    private static async Task<Dictionary<long, (string CitationRef, string? Milestone, string Text)>> PassagesAsync(
        SqliteConnection conn, IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, (string, string?, string)>();
        for (var offset = 0; offset < ids.Count; offset += 500)
        {
            var batch = ids.Skip(offset).Take(500).ToList();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT TextNodeId, CitationRef, Milestone, Text FROM TextNodes WHERE TextNodeId IN ({string.Join(',', batch)});";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                result[reader.GetInt64(0)] = (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3));
        }

        return result;
    }

    private async Task<(string Author, string Title)> NamesAsync(SqliteConnection conn, int workId, CancellationToken cancellationToken)
    {
        if (_names.TryGetValue(workId, out var known)) return known;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT a.Name, w.Title FROM Works w JOIN Authors a ON a.AuthorId = w.AuthorId WHERE w.WorkId = @WorkId;";
        cmd.Parameters.AddWithValue("@WorkId", workId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var names = await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetString(1)) : (string.Empty, string.Empty);
        _names[workId] = names;
        return names;
    }
}

/// <summary>
/// The citations table, from the passage's side: which lines of an edition
/// the dictionaries cite, and the entries that cite each one.
/// </summary>
public sealed class LexiconCitationRepository
{
    /// <summary>
    /// The citations each passage of an edition receives, by node id - every
    /// citation of the edition's work whose reference names a passage of this
    /// edition, found the way Word Study finds it (<see cref="PassageIndex"/>).
    ///
    /// Asked of the edition the reader has open rather than of the one Word
    /// Study would open, so that a reader in the First1K Agamemnon sees the
    /// lines LSJ cites there too. Translations have none: a dictionary cites
    /// the Greek and Latin, and a translation numbered the same way would
    /// otherwise be marked as if it were cited.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, IReadOnlyList<long>>> CitationsByPassageAsync(
        Edition edition, IEnumerable<(long TextNodeId, string CitationRef, string? Milestone)> passages,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<long, IReadOnlyList<long>>();
        if (edition.Kind != EditionKind.Original) return result;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);

        string? workUrn;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT CtsUrn FROM Works WHERE WorkId = @WorkId;";
            cmd.Parameters.AddWithValue("@WorkId", edition.WorkId);
            workUrn = await cmd.ExecuteScalarAsync(cancellationToken) as string;
        }

        if (string.IsNullOrWhiteSpace(workUrn)) return result;

        var byPassage = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT CitationId, Passage FROM LexiconCitations WHERE WorkUrn = @Work;";
            cmd.Parameters.AddWithValue("@Work", LexiconCitationResolver.Key(workUrn));
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var passage = reader.GetString(1);
                if (!byPassage.TryGetValue(passage, out var ids)) byPassage[passage] = ids = new List<long>();
                ids.Add(reader.GetInt64(0));
            }
        }

        if (byPassage.Count == 0) return result;

        var work = LexiconCitationResolver.Key(workUrn);
        var index = new PassageIndex(passages, CitationConventions.SectionsRunThrough(work));
        var byNode = new Dictionary<long, List<long>>();
        foreach (var (passage, ids) in byPassage)
        {
            var reference = passage.Split('.');
            if (index.Find(reference, CitationConventions.MayStopAtChapter(work, reference)) is not { } hit) continue;
            if (!byNode.TryGetValue(hit.TextNodeId, out var list)) byNode[hit.TextNodeId] = list = new List<long>();
            list.AddRange(ids);
        }

        foreach (var (node, ids) in byNode) result[node] = ids;
        return result;
    }

    /// <summary>The entries these citations are in, with the entry text to show each in context.</summary>
    public async Task<IReadOnlyList<CitingEntry>> GetCitingEntriesAsync(
        IReadOnlyCollection<long> citationIds, CancellationToken cancellationToken = default)
    {
        var result = new List<CitingEntry>();
        if (citationIds.Count == 0) return result;

        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        foreach (var batch in citationIds.Distinct().Chunk(500))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT c.CitationId, c.DefinitionId, c.Ordinal, c.Sense, d.Headword, d.Language, d.Source, d.Entry
                  FROM LexiconCitations c
                  JOIN Definitions d ON d.DefinitionId = c.DefinitionId
                 WHERE c.CitationId IN ({string.Join(',', batch)});";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new CitingEntry(
                    reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7)));
            }
        }

        return result;
    }

    /// <summary>
    /// Whether the dictionaries in this library carry their citations - false
    /// until the dictionary step has been run with a version that keeps them.
    /// </summary>
    public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM LexiconCitations LIMIT 1);";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0L) != 0;
    }
}
