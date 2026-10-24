using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Mac.Features.Research.Bench;

/// <summary>
/// One resolved candidate, held in memory only until the researcher accepts it. Every
/// field has already been checked against the local edition - the citation resolved, and
/// the excerpt is the corpus text rather than anything the model wrote - so accepting is
/// a decision about relevance, not about whether the passage exists.
/// </summary>
internal sealed record EvidenceCandidatePreview(
    string Title, string Citation, string StableId, string Excerpt,
    EvidenceRelationship Relationship, long? QuestionId, string Confidence, string Rationale);

/// <summary>
/// The parts of "AI: Find relevant corpus passages" that are plain logic: what
/// is sent, and how what comes back is checked against it. Kept apart from the
/// window so the check can be tested without a key or a network.
/// </summary>
internal static class EvidenceGathering
{
    /// <summary>
    /// The edition as tagged lines - "[1.23] text" - up to the character budget,
    /// with the passages actually included and the last reference before the cut.
    /// </summary>
    internal static (string Text, string? TruncatedAtRef, List<TextNode> Sent) BuildTaggedCorpus(
        IEnumerable<TextNode> nodes, int maxCharacters = 220_000)
    {
        var builder = new System.Text.StringBuilder();
        var sent = new List<TextNode>();
        string? lastRef = null;
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Text) || string.IsNullOrWhiteSpace(node.CitationRef)) continue;

            // Stored form - the model's candidates are resolved against real
            // passages by this reference. See CrossLanguageEchoForm.
            var line = $"[{node.CitationRef}] {node.Text}\n";
            if (builder.Length + line.Length > maxCharacters)
                return (builder.ToString(), lastRef, sent);

            builder.Append(line);
            sent.Add(node);
            lastRef = node.CitationRef;
        }

        return (builder.ToString(), null, sent);
    }

    /// <summary>
    /// Checks the model's candidates against the passages that were sent, and
    /// returns those worth offering with the number whose citation did not resolve.
    /// </summary>
    internal static (List<EvidenceCandidatePreview> Offered, int Unresolved) Resolve(
        IEnumerable<ResearchEvidenceCandidate> candidates,
        IEnumerable<TextNode> sent,
        string editionCtsUrn,
        IEnumerable<EvidenceItem> existing,
        IReadOnlyList<ResearchQuestion> questions,
        bool challengeTheory)
    {
        // Only the passages that were sent. The edition is cut off at the
        // character budget, and a citation past the cut resolved against the
        // full edition all the same - a line the model never saw, recalled from
        // training, saved as "verified against local edition" beside a note
        // saying the search had stopped before it.
        var textByRef = sent
            .Where(n => !string.IsNullOrWhiteSpace(n.CitationRef))
            .GroupBy(n => n.CitationRef, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(n => n.Text)), StringComparer.OrdinalIgnoreCase);
        var knownIds = existing.Select(e => e.StableIdentifier)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offered = new List<EvidenceCandidatePreview>();
        var unresolved = 0;
        foreach (var candidate in candidates)
        {
            var citation = candidate.CitationRef.Trim().TrimStart('[').TrimEnd(']');
            if (!textByRef.TryGetValue(citation, out var corpusText))
            {
                unresolved++;
                continue;
            }

            var stableId = $"{editionCtsUrn}:{citation}";
            if (!knownIds.Add(stableId)) continue;

            var relationship = Enum.TryParse<EvidenceRelationship>(candidate.Relationship, true, out var parsed)
                ? parsed
                : challengeTheory ? EvidenceRelationship.Contradicts : EvidenceRelationship.Contextualizes;
            long? questionId = candidate.QuestionIndex is > 0 && candidate.QuestionIndex <= questions.Count
                ? questions[candidate.QuestionIndex.Value - 1].ResearchQuestionId
                : null;
            offered.Add(new EvidenceCandidatePreview(candidate.Title, citation, stableId, corpusText,
                relationship, questionId, candidate.Confidence, candidate.Rationale));
        }

        return (offered, unresolved);
    }
}

/// <summary>Small readings of stored identifiers that more than one research window needs.</summary>
internal static class ResearchLinks
{
    private const string StylometryRunPrefix = "classicacodex:stylometry-run:";

    /// <summary>The stable identifier an attached stylometry run is filed under.</summary>
    public static string StylometryRunId(long runId) => StylometryRunPrefix + runId;

    /// <summary>The saved run an evidence item was copied from, if it was.</summary>
    public static long? GetStylometryRunId(EvidenceItem? item)
    {
        if (item?.Origin != EvidenceOrigin.ClassicaCodexAnalysis ||
            item.StableIdentifier?.StartsWith(StylometryRunPrefix, StringComparison.OrdinalIgnoreCase) != true)
            return null;

        return long.TryParse(item.StableIdentifier[StylometryRunPrefix.Length..], out var runId) ? runId : null;
    }

    /// <summary>
    /// A web address to open for a reading: an http(s) identifier or locator,
    /// or a "doi:" identifier sent to doi.org.
    /// </summary>
    public static bool TryGetOpenUri(string? identifier, string? locator, out Uri uri)
    {
        foreach (var candidate in new[] { identifier, locator })
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            {
                uri = parsed;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(identifier) &&
            identifier.StartsWith("doi:", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate("https://doi.org/" + identifier[4..].Trim(), UriKind.Absolute, out var doi))
        {
            uri = doi;
            return true;
        }

        uri = null!;
        return false;
    }
}
