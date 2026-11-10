using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Confirmed plans for Menota's manuscripts that come with the app, so a reader
/// does not have to review all 91 before anything can be imported.
///
/// A plan is a decision a person makes, because nothing in a Menota file links
/// its catalogue entries to the divisions that hold their text (see
/// <see cref="MenotaIngestPlan"/>). Somebody did make those decisions, once,
/// for every manuscript in Menota's archive, and the review then asked every
/// reader to make all of them again. On an iPad that was 91 screens in a row
/// before a single saga could be read.
///
/// The plans are in MenotaPlans/, embedded in this assembly, exactly as the
/// review saved them on 24 October 2026: 889 works, three of them merged by hand
/// and given to Alcuin in AM 619 4to. To refresh them, confirm the plans in the
/// review on a desktop and copy the .plan.json files over the ones there.
///
/// A plan is used only while it still fits the file. It points at divisions by
/// position, and Menota corrects its files, so a plan made against last year's
/// copy could name the wrong text. Anything that does not fit goes to the
/// review as before.
/// </summary>
public static class MenotaBundledPlans
{
    private const string ResourcePrefix = "ClassicaCodex.Ingestion.MenotaPlans.";
    private const string ResourceSuffix = ".plan.json";

    private static readonly XNamespace Tei = MenotaXmlLoader.Tei;

    private static readonly Lazy<Dictionary<string, string>> Resources = new(() =>
        typeof(MenotaBundledPlans).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .ToDictionary(
                n => n[ResourcePrefix.Length..^ResourceSuffix.Length] + ".xml",
                n => n,
                StringComparer.OrdinalIgnoreCase));

    /// <summary>The manuscripts there is a plan for, by file name.</summary>
    public static IReadOnlyCollection<string> FileNames => Resources.Value.Keys;

    /// <summary>
    /// A fresh copy of the plan for this manuscript, or null if none comes
    /// with the app. A copy, because the caller is free to change it.
    /// </summary>
    public static MenotaIngestPlan? Find(string xmlFileName)
    {
        if (!Resources.Value.TryGetValue(xmlFileName, out var name)) return null;

        using var stream = typeof(MenotaBundledPlans).Assembly.GetManifestResourceStream(name);
        if (stream == null) return null;

        using var reader = new StreamReader(stream);
        var plan = JsonSerializer.Deserialize<MenotaIngestPlan>(reader.ReadToEnd());

        return plan != null && string.Equals(plan.FileName, xmlFileName, StringComparison.OrdinalIgnoreCase)
            ? plan
            : null;
    }

    /// <summary>
    /// Saves the plan for this manuscript beside it, if one comes with the app
    /// and still fits the file, and returns it. Returns null, and writes
    /// nothing, otherwise.
    /// </summary>
    public static MenotaIngestPlan? TryUse(string xmlPath, XDocument doc)
    {
        var plan = Find(Path.GetFileName(xmlPath));
        if (plan == null || !Fits(plan, doc)) return null;

        // The language is read from the file each time, as the planner reads
        // it, because Menota does correct it: TuebUB Me II 2 changed its
        // language tag in November 2026 and nothing else.
        var language = doc.Descendants(Tei + "language").FirstOrDefault()?.Attribute("ident")?.Value;
        if (!string.IsNullOrEmpty(language)) plan.Language = language;

        // The works the ingest would number anyway, numbered in the file so
        // it says nothing about them on every import.
        plan.MakeSlugsUnique();

        plan.Notes.Insert(0,
            "This plan came with Classica Codex and was used without asking, because the manuscript still " +
            "matches it: the same reading level, the same divisions, the same number of words in each.");

        plan.Save(MenotaIngestPlan.PlanPathFor(xmlPath));
        return plan;
    }

    /// <summary>
    /// Whether a confirmed plan still describes this file.
    ///
    /// Three things are checked, and the ingest already checks the second
    /// before it imports anything. The reading level must be the one the
    /// planner would choose today, because a manuscript that has gained a
    /// normalised level has been re-edited and is worth looking at again.
    /// Every division a work names must still be there and hold the same
    /// number of words. And the works together must hold every word the
    /// file's divisions do, so a section Menota has added is not left out
    /// without anyone being asked about it.
    /// </summary>
    public static bool Fits(MenotaIngestPlan plan, XDocument doc)
    {
        if (!plan.Confirmed || plan.Works.Count == 0) return false;

        // What the app substitutes for a character it could not read. A plan
        // carrying one was made without the entity table and is wrong for good.
        if (plan.Works.Any(w => w.Title.Contains('�'))) return false;

        var body = doc.Descendants(Tei + "body").FirstOrDefault();
        if (body == null) return false;

        if (MenotaXmlLoader.ChooseReadingLevel(doc, out _, out _) != plan.ReadingLevel) return false;

        var covered = 0;
        foreach (var work in plan.Works)
        {
            if (work.DivPaths.Count == 0) return false;

            var divs = work.DivPaths.Select(p => MenotaIngestService.ResolvePath(body, p)).ToList();
            if (divs.Any(d => d == null)) return false;

            var words = divs.Sum(d => MenotaXmlLoader.DirectWords(d!).Count());
            if (words != work.WordCount) return false;

            covered += words;
        }

        var inFile = MenotaIngestPlanner.LeafDivisions(body).Sum(d => MenotaXmlLoader.DirectWords(d).Count());
        return covered == inFile;
    }
}
