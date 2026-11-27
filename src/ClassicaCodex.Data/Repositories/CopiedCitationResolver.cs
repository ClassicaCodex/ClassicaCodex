using System.Text.RegularExpressions;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Citations;
using ClassicaCodex.Core.Lexicon;
using ClassicaCodex.Core.Models;
using Microsoft.Data.Sqlite;

namespace ClassicaCodex.Data.Repositories;

/// <summary>What looking up a copied citation came to.</summary>
public enum CitationLookupOutcome
{
    /// <summary>The passage is in the library.</summary>
    Found,

    /// <summary>The edition or work is in the library, but no passage of it has the reference.</summary>
    ReferenceNotFound,

    /// <summary>Nothing in the library has the identifier.</summary>
    NotInLibrary
}

/// <summary>A copied citation, looked up.</summary>
/// <param name="Edition">The edition the passage is in; for <see cref="CitationLookupOutcome.ReferenceNotFound"/>, the edition that was searched first.</param>
/// <param name="TextNodeId">The passage; for <see cref="CitationLookupOutcome.ReferenceNotFound"/>, the edition's first passage, to open it at the start.</param>
/// <param name="Substituted">
/// Whether the citation names an edition this library does not hold, and
/// another edition of the same work was searched in its place.
/// </param>
public sealed record CitationLookupResult(
    CitationLookupOutcome Outcome,
    Edition? Edition,
    long? TextNodeId,
    string? CitationRef,
    string? Milestone,
    string? Text,
    string AuthorName,
    string WorkTitle,
    bool Substituted)
{
    public static CitationLookupResult NotInLibrary { get; } =
        new(CitationLookupOutcome.NotInLibrary, null, null, null, null, null, string.Empty, string.Empty, false);
}

/// <summary>A museum object a citation names, as the Art and Archaeology step stored it.</summary>
public sealed record CitedArtifact(string ArtifactId, string Name, string? Type, string? Location, string? Description);

/// <summary>A reference found in a footnote, and what looking it up came to.</summary>
/// <param name="Reference">The reference as the footnote writes it.</param>
/// <param name="Result">What looking it up came to; null where its names match no work at all.</param>
/// <param name="Work">The work it was looked up in.</param>
/// <param name="Written">The reference as written, from the first of its names that named the work: "Thuc. 2.40.1" out of "See Thuc. 2.40.1".</param>
public sealed record FootnoteLookup(FootnoteReference Reference, CitationLookupResult? Result, string? Work, string Written)
{
    /// <summary>Whether its names were read as a work, whether or not the library has it.</summary>
    public bool Recognised => Result != null;
}

/// <summary>
/// Finds the passage a copied citation names (<see cref="CopiedCitation"/>).
///
/// <b>Which edition.</b> The identifier is matched against every edition
/// in the library as the library stores it, with or without a
/// "urn:cts:namespace:" head and a leading "urn:", since the pages copy
/// Perseus identifiers without the head and the library stores most of them
/// that way too. Failing an edition, it is matched against the works, and a
/// work's originals are searched first, in the reader's order, then its
/// translations. A CTS edition the library does not hold - "perseus-grc1"
/// where it has "perseus-grc2" - falls back to its work, with the editions in
/// the language the identifier names first, so a citation of one English
/// translation opens another rather than the Greek.
///
/// <b>Which passage.</b> The reference is looked up exactly in every
/// candidate edition before any looser match is tried in any of them, the
/// looser matches being the dictionaries' (<see cref="PassageIndex"/>): with a
/// paragraph number added to prose, the first section of a chapter, a
/// Stephanus or Bekker reference within a page's range. A copied reference is
/// the library's own and is almost always exact; the rest is for a reference
/// typed from a printed book.
///
/// <b>Footnotes.</b> <see cref="LookUpFootnoteAsync"/> takes text that cites
/// the way books do - "Thuc. 2.40.1; cf. Pl. Resp. 514a" - and looks up each
/// reference in it: <see cref="FootnoteReader"/> finds them, and
/// <see cref="WorkAbbreviations"/> says which work each one's abbreviation
/// names. Only the works that match its names best are tried, so a reference
/// to a work the library lacks says so rather than opening a near miss.
/// </summary>
public sealed class CopiedCitationResolver
{
    private static readonly Regex UrnHead = new(@"^urn:cts:[^:]+:", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // tlg0020.tlg001.perseus-grc2: a CTS work and a version of it.
    private static readonly Regex CtsEdition = new(@"^(?<work>[^.:/]+\.[^.:/]+)\.(?<version>[^.:/]+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The language a version names: perseus-eng2, 1st1K-grc1, opp-lat5.
    private static readonly Regex VersionLanguage = new(@"-(?<language>[a-z]{3})\d*$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly Func<IReadOnlyList<Edition>, IReadOnlyList<Edition>> _arrange;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, EditionPassages> _passages = new();
    private List<Edition>? _editions;
    private List<(int WorkId, string Key)>? _works;
    private WorkAbbreviations? _abbreviations;

    /// <param name="arrange">
    /// The editions of a work in the order the reader offers them, the one it
    /// opens by default first. Without one, by identifier.
    /// </param>
    public CopiedCitationResolver(Func<IReadOnlyList<Edition>, IReadOnlyList<Edition>>? arrange = null)
    {
        _arrange = arrange ?? (editions => editions.OrderBy(e => e.CtsUrn, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>An identifier as this compares them: without a CTS head or a leading "urn:", in lower case.</summary>
    public static string Key(string identifier)
    {
        var key = UrnHead.Replace(identifier.Trim(), string.Empty);
        if (key.StartsWith("urn:", StringComparison.OrdinalIgnoreCase)) key = key[4..];
        return key.ToLowerInvariant();
    }

    public async Task<CitationLookupResult> LookUpAsync(CopiedCitation citation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
            return await LookUpOnceAsync(conn, citation, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Every reference in a footnote, looked up, in the order written. Empty
    /// where the text holds none. A reference whose names match no work comes
    /// back without a result, so the window can say which it could not read.
    /// </summary>
    public async Task<IReadOnlyList<FootnoteLookup>> LookUpFootnoteAsync(string? text, CancellationToken cancellationToken = default)
    {
        var references = FootnoteReader.Read(text);
        if (references.Count == 0) return Array.Empty<FootnoteLookup>();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
            var abbreviations = await AbbreviationsAsync(conn, cancellationToken);
            var lookups = new List<FootnoteLookup>();
            string? previous = null;

            foreach (var reference in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidates = reference.Kind switch
                {
                    FootnoteReferenceKind.Named => abbreviations.Match(reference.Names, reference.Passage),
                    FootnoteReferenceKind.SameAuthor when previous != null => abbreviations.MatchByAuthorOf(previous, reference.Names, reference.Passage),
                    FootnoteReferenceKind.SameWork when previous != null => abbreviations.Continue(previous, reference.Passage),
                    _ => Array.Empty<WorkCandidate>()
                };

                var lookup = await LookUpCandidatesAsync(conn, text!, reference, candidates, cancellationToken);
                lookups.Add(lookup);
                if (lookup.Work != null) previous = lookup.Work;
            }

            return lookups;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The reference looked up in the works its names match best - only those
    /// that match as well as the first, so a work the library lacks is said to
    /// be missing rather than replaced by a weaker match that it holds.
    /// </summary>
    private async Task<FootnoteLookup> LookUpCandidatesAsync(
        SqliteConnection conn, string text, FootnoteReference reference, IReadOnlyList<WorkCandidate> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return new FootnoteLookup(reference, null, null, reference.Written);

        var written = reference.WrittenFrom(text, candidates[0].NamesUsed);
        CitationLookupResult? fallback = null;
        string? fallbackWork = null;
        foreach (var candidate in candidates.Where(c => c.Score == candidates[0].Score).Take(3))
        {
            var result = await LookUpOnceAsync(conn, new CopiedCitation(candidate.Work, string.Join('.', candidate.Passage)), cancellationToken, originalsFirst: true);

            // "Plin. 2, 47, 48, § 128": the book and the section, where the
            // edition numbers by section.
            if (result.Outcome == CitationLookupOutcome.ReferenceNotFound && reference.Section is { } section && candidate.Passage.Count > 0)
            {
                var bySection = await LookUpOnceAsync(conn, new CopiedCitation(candidate.Work, $"{candidate.Passage[0]}.{section}"), cancellationToken, originalsFirst: true);
                if (bySection.Outcome == CitationLookupOutcome.Found) result = bySection;
            }

            if (result.Outcome == CitationLookupOutcome.Found) return new FootnoteLookup(reference, result, candidate.Work, written);
            if (fallback == null || (fallback.Outcome == CitationLookupOutcome.NotInLibrary && result.Outcome == CitationLookupOutcome.ReferenceNotFound))
            {
                fallback = result;
                fallbackWork = candidate.Work;
            }
        }

        return new FootnoteLookup(reference, fallback, fallbackWork, written);
    }

    /// <summary>The works of the library as the abbreviations are matched against them, read once.</summary>
    private async Task<WorkAbbreviations> AbbreviationsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        if (_abbreviations != null) return _abbreviations;

        var works = new List<LibraryWork>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT w.CtsUrn, a.Name, w.Title FROM Works w JOIN Authors a ON a.AuthorId = w.AuthorId WHERE w.CtsUrn IS NOT NULL;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            works.Add(new LibraryWork(Key(reader.GetString(0)), reader.IsDBNull(1) ? string.Empty : reader.GetString(1), reader.IsDBNull(2) ? string.Empty : reader.GetString(2)));
        return _abbreviations = new WorkAbbreviations(works);
    }

    /// <summary>The museum object with this name, or null where the library has none - or no Art and Archaeology at all.</summary>
    public static async Task<CitedArtifact?> LookUpArtifactAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var conn = await DbConnectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ArtifactId, Name, Type, Location, Description FROM Artifacts WHERE Name = @Name COLLATE NOCASE LIMIT 1;";
        cmd.Parameters.AddWithValue("@Name", name.Trim());

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            string? Text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            return new CitedArtifact(reader.GetString(0), reader.GetString(1), Text(2), Text(3), Text(4));
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
    }

    /// <param name="originalsFirst">
    /// Search a work's originals, exactly and then loosely, before any
    /// translation. A footnote cites the Greek or the Latin, and a
    /// translation's own numbering can match exactly where the original's
    /// needs the looser match - Thucydides' Greek numbers 2.40.1 as 2.40.1.1,
    /// a French translation as 2.40.1. A copied citation names its edition,
    /// and is searched as one list.
    /// </param>
    private async Task<CitationLookupResult> LookUpOnceAsync(
        SqliteConnection conn, CopiedCitation citation, CancellationToken cancellationToken, bool originalsFirst = false)
    {
        var (candidates, substituted) = await CandidatesAsync(conn, Key(citation.Identifier), cancellationToken);
        if (candidates.Count == 0) return CitationLookupResult.NotInLibrary;

        var references = citation.References();
        if (references.Count == 0)
        {
            // An identifier alone: the edition from its start.
            var first = candidates[0];
            var start = (await PassagesAsync(conn, first, cancellationToken)).First;
            return start is { } id
                ? await FoundAsync(conn, first, id, substituted, cancellationToken)
                : await NotFoundAsync(conn, first, substituted, cancellationToken);
        }

        var groups = originalsFirst
            ? new[] { candidates.Where(e => e.Kind == EditionKind.Original).ToList(), candidates.Where(e => e.Kind != EditionKind.Original).ToList() }
            : new[] { candidates.ToList() };

        foreach (var group in groups)
        {
            foreach (var edition in group)
            {
                var passages = await PassagesAsync(conn, edition, cancellationToken);
                foreach (var reference in references)
                    if (passages.Exact.TryGetValue(reference, out var id)) return await FoundAsync(conn, edition, id, substituted, cancellationToken);
            }

            foreach (var edition in group)
            {
                var passages = await PassagesAsync(conn, edition, cancellationToken);
                foreach (var reference in references)
                    if (passages.Index.Find(reference.Split('.')) is { } hit)
                        return await FoundAsync(conn, edition, hit.TextNodeId, substituted, cancellationToken);
            }
        }

        return await NotFoundAsync(conn, candidates[0], substituted, cancellationToken);
    }

    /// <summary>The editions to search, in order, and whether they stand in for one the library lacks.</summary>
    private async Task<(IReadOnlyList<Edition> Editions, bool Substituted)> CandidatesAsync(
        SqliteConnection conn, string key, CancellationToken cancellationToken)
    {
        var editions = await EditionsAsync(conn, cancellationToken);

        var named = editions.Where(e => Key(e.CtsUrn) == key).ToList();
        if (named.Count > 0) return (named.Count == 1 ? named : _arrange(named), false);

        var works = await WorksAsync(conn, cancellationToken);
        var work = works.Where(w => w.Key == key).Select(w => w.WorkId).ToHashSet();
        if (work.Count > 0) return (OfWorks(editions, work, language: null), false);

        // An edition of a work the library holds in another edition.
        if (CtsEdition.Match(key) is { Success: true } version)
        {
            var workKey = version.Groups["work"].Value;
            work = works.Where(w => w.Key == workKey).Select(w => w.WorkId).ToHashSet();
            var language = VersionLanguage.Match(version.Groups["version"].Value) is { Success: true } l ? l.Groups["language"].Value : null;
            if (work.Count > 0) return (OfWorks(editions, work, language), true);
        }

        return (Array.Empty<Edition>(), false);
    }

    /// <summary>The editions of some works: those in the language asked for, then originals, then translations, each in the reader's order.</summary>
    private List<Edition> OfWorks(IReadOnlyList<Edition> editions, IReadOnlySet<int> works, string? language)
    {
        var all = editions.Where(e => works.Contains(e.WorkId)).ToList();
        var inLanguage = language == null
            ? new List<Edition>()
            : all.Where(e => string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase)).ToList();
        var rest = all.Except(inLanguage).ToList();

        var ordered = new List<Edition>();
        if (inLanguage.Count > 0) ordered.AddRange(_arrange(inLanguage));
        var originals = rest.Where(e => e.Kind == EditionKind.Original).ToList();
        if (originals.Count > 0) ordered.AddRange(_arrange(originals));
        var others = rest.Where(e => e.Kind != EditionKind.Original).ToList();
        if (others.Count > 0) ordered.AddRange(_arrange(others));
        return ordered;
    }

    private async Task<IReadOnlyList<Edition>> EditionsAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        if (_editions != null) return _editions;

        var editions = new List<Edition>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EditionId, WorkId, CtsUrn, Kind, Language, Translator, SourcePath, Orthography, Collection FROM Editions;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) editions.Add(EditionRepository.Read(reader));
        return _editions = editions;
    }

    private async Task<IReadOnlyList<(int WorkId, string Key)>> WorksAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        if (_works != null) return _works;

        var works = new List<(int, string)>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT WorkId, CtsUrn FROM Works WHERE CtsUrn IS NOT NULL;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) works.Add((reader.GetInt32(0), Key(reader.GetString(1))));
        return _works = works;
    }

    private async Task<EditionPassages> PassagesAsync(SqliteConnection conn, Edition edition, CancellationToken cancellationToken)
    {
        if (_passages.TryGetValue(edition.EditionId, out var known)) return known;

        var passages = new List<(long, string, string?)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT TextNodeId, CitationRef, Milestone
                  FROM TextNodes
                 WHERE EditionId = @EditionId
                 ORDER BY SortOrder, TextNodeId;";
            cmd.Parameters.AddWithValue("@EditionId", edition.EditionId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                passages.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        var exact = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (id, citationRef, _) in passages)
        {
            exact.TryAdd(citationRef, id);
            exact.TryAdd(PassageAligner.ExtractPassageRef(citationRef), id);
        }

        var work = CtsEdition.Match(Key(edition.CtsUrn)) is { Success: true } m ? m.Groups["work"].Value : Key(edition.CtsUrn);
        var found = new EditionPassages(
            exact,
            new PassageIndex(passages, CitationConventions.SectionsRunThrough(work)),
            passages.Count > 0 ? passages[0].Item1 : null);

        // A few editions' passages are plenty: the window asks again as the
        // reader types, nearly always of the same edition.
        if (_passages.Count > 8) _passages.Clear();
        _passages[edition.EditionId] = found;
        return found;
    }

    private static async Task<CitationLookupResult> FoundAsync(
        SqliteConnection conn, Edition edition, long textNodeId, bool substituted, CancellationToken cancellationToken)
    {
        var (author, title) = await NamesAsync(conn, edition.WorkId, cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CitationRef, Milestone, Text FROM TextNodes WHERE TextNodeId = @Id;";
        cmd.Parameters.AddWithValue("@Id", textNodeId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return CitationLookupResult.NotInLibrary;

        return new CitationLookupResult(
            CitationLookupOutcome.Found, edition, textNodeId,
            reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2),
            author, title, substituted);
    }

    private async Task<CitationLookupResult> NotFoundAsync(
        SqliteConnection conn, Edition edition, bool substituted, CancellationToken cancellationToken)
    {
        var (author, title) = await NamesAsync(conn, edition.WorkId, cancellationToken);
        var start = (await PassagesAsync(conn, edition, cancellationToken)).First;
        return new CitationLookupResult(CitationLookupOutcome.ReferenceNotFound, edition, start, null, null, null, author, title, substituted);
    }

    private static async Task<(string Author, string Title)> NamesAsync(SqliteConnection conn, int workId, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT a.Name, w.Title FROM Works w JOIN Authors a ON a.AuthorId = w.AuthorId WHERE w.WorkId = @WorkId;";
        cmd.Parameters.AddWithValue("@WorkId", workId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetString(1)) : (string.Empty, string.Empty);
    }

    private sealed record EditionPassages(IReadOnlyDictionary<string, long> Exact, PassageIndex Index, long? First);
}
