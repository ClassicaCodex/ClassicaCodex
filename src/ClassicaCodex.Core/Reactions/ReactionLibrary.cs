using System.Reflection;

namespace ClassicaCodex.Core.Reactions;

/// <summary>
/// Every debate this application knows, loaded once.
///
/// <b>Why the content is not in SQLite.</b> The kickoff spec put these in
/// three tables with a migration and an importer, and the rest of this
/// codebase says not to. Everything else hand-curated here - StartingPoints,
/// DisputedWorkData, PlaceData, AuthorEraData, BronzeWitnesses - ships as
/// data inside the executable, because it is content rather than the reader's
/// own material. Putting it in the database would mean a schema migration
/// against a file that is 2.9 GB and irreplaceable on a real installation, a
/// re-seed path to run on every version that edits a line of dialogue, and a
/// question nobody wants to answer about what happens to rows for a debate a
/// later release withdrew. The packs are 30 KB. They can simply be in the
/// program.
///
/// What IS kept from the spec is the authoring format: one JSON file per
/// debate, validated on load, because JSON is what a human can edit and a test
/// can break on purpose.
///
/// <b>The folder.</b> Anything in a "Reactions" folder beside the executable
/// is loaded too. That is the part of the spec's importer that was worth
/// having - somebody who wants to write their own argument about the Georgics
/// can, without a build - and it costs one directory scan. A file in there
/// that does not validate is skipped and reported, never thrown: this runs
/// during the first paint of a context menu, and a stray comma in a stranger's
/// file is not this application's crash to have.
/// </summary>
public static class ReactionLibrary
{
    private static readonly object Gate = new();
    private static Loaded? _loaded;

    private sealed record Loaded(
        IReadOnlyList<ReactionPack> Packs,
        IReadOnlyDictionary<string, AncientCritic> Critics,
        IReadOnlyList<string> Problems);

    /// <summary>
    /// Where a reader's own packs go. Beside the executable rather than in the
    /// data folder, because these are part of the installation rather than
    /// part of the library - they survive re-ingesting a corpus and they are
    /// not worth backing up with 9 GB of texts.
    /// </summary>
    public static string UserPackDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Reactions");

    /// <summary>Every debate, shipped and user-supplied, in load order.</summary>
    public static IReadOnlyList<ReactionPack> Packs => Load().Packs;

    /// <summary>
    /// What was wrong with the packs that failed to load. Empty in a healthy
    /// installation; shown in the window's own "where these come from" panel
    /// rather than in a dialog, because a broken user pack is worth mentioning
    /// and not worth interrupting anyone over.
    /// </summary>
    public static IReadOnlyList<string> Problems => Load().Problems;

    public static AncientCritic? Critic(string id) =>
        Load().Critics.TryGetValue(id, out var critic) ? critic : null;

    /// <summary>
    /// The debates about a given work, matched the way StartingPoints matches
    /// its recommendations - on author and title text, because CTS URNs differ
    /// between the corpora and which one a reader installed varies.
    /// </summary>
    public static IReadOnlyList<ReactionDebate> DebatesFor(string authorName, string workTitle)
    {
        if (string.IsNullOrWhiteSpace(authorName) && string.IsNullOrWhiteSpace(workTitle))
            return Array.Empty<ReactionDebate>();

        return Load().Packs
            .Select(p => p.Debate)
            .Where(d => d.Work.Matches(authorName ?? string.Empty, workTitle ?? string.Empty))
            .ToList();
    }

    /// <summary>
    /// Whether anything at all is available for this work - what the context
    /// menu asks, on every right-click, so it does no more than the lookup.
    /// </summary>
    public static bool HasDebateFor(string authorName, string workTitle) =>
        DebatesFor(authorName, workTitle).Count > 0;

    /// <summary>Drops the cache. For tests that write pack files.</summary>
    public static void Reset()
    {
        lock (Gate) _loaded = null;
    }

    private static Loaded Load()
    {
        lock (Gate)
        {
            if (_loaded != null) return _loaded;

            var packs = new List<ReactionPack>();
            var critics = new Dictionary<string, AncientCritic>(StringComparer.Ordinal);
            var problems = new List<string>();

            foreach (var (name, json) in ReadShippedPacks())
                Accept(name, json, packs, critics, problems);

            foreach (var (name, json) in ReadUserPacks(problems))
                Accept(name, json, packs, critics, problems);

            _loaded = new Loaded(packs, critics, problems);
            return _loaded;
        }
    }

    /// <summary>
    /// Parse, then validate, then keep - and on any problem, keep nothing.
    ///
    /// Half a debate is worse than none: the turns that survived would read as
    /// the whole argument, and the one dropped could be the one carrying the
    /// citation that makes a historical critic honest.
    /// </summary>
    private static void Accept(
        string name, string json,
        List<ReactionPack> packs, Dictionary<string, AncientCritic> critics, List<string> problems)
    {
        var result = ReactionPackReader.Read(json, name);

        if (result.Pack == null)
        {
            problems.AddRange(result.Problems);
            return;
        }

        var found = result.Problems.Concat(ReactionPackValidator.Validate(result.Pack, critics)).ToList();

        if (found.Count > 0)
        {
            problems.AddRange(found);
            return;
        }

        foreach (var critic in result.Pack.Critics) critics[critic.Id] = critic;
        packs.Add(result.Pack);
    }

    private static IEnumerable<(string Name, string Json)> ReadShippedPacks()
    {
        var assembly = typeof(ReactionLibrary).Assembly;

        // Ordered, so that a critic shared between packs is introduced by the
        // same pack on every run and a validation message never depends on
        // whatever order the manifest happened to list.
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".Reactions.Packs.", StringComparison.Ordinal)
                                 && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            yield return (ShortName(resource), reader.ReadToEnd());
        }
    }

    private static IEnumerable<(string Name, string Json)> ReadUserPacks(List<string> problems)
    {
        string[] files;

        try
        {
            if (!Directory.Exists(UserPackDirectory)) yield break;
            files = Directory.GetFiles(UserPackDirectory, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"Could not read {UserPackDirectory}: {ex.Message}");
            yield break;
        }

        foreach (var file in files)
        {
            string json;

            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Could not read {Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            yield return (Path.GetFileName(file), json);
        }
    }

    /// <summary>
    /// "ClassicaCodex.Core.Reactions.Packs.clouds.json" becomes "clouds.json",
    /// which is what a problem message should name.
    /// </summary>
    private static string ShortName(string resourceName)
    {
        var marker = ".Reactions.Packs.";
        var at = resourceName.IndexOf(marker, StringComparison.Ordinal);
        return at < 0 ? resourceName : resourceName[(at + marker.Length)..];
    }
}
