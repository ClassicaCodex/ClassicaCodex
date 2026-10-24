using ClassicaCodex.Core.Reactions;

namespace ClassicaCodex.Mac.Features.Reactions;

/// <summary>
/// One turn of a debate, with everything the window needs to draw it already
/// worked out.
///
/// Built before anything is drawn, because whether a citation resolves
/// depends on which corpora this particular reader installed, and that
/// question costs a query - one per citation, asked once per debate, never
/// while the transcript is being laid out.
/// </summary>
internal sealed record TurnView(
    DebateTurn Turn,
    AncientCritic Critic,
    bool ShowSpeaker,
    string? DividerBefore,
    string? PassageLabel,
    bool PassageResolved,
    string? SourceLabel,
    bool SourceResolved);

/// <summary>
/// What the Windows ReactionsForm works out about a debate before it draws
/// it: which turns carry a face and a name, where the date dividers go, how
/// each citation chip reads, and the one-line summary of how much of what is
/// on screen was invented. Kept apart from the window so it can be checked
/// without one.
/// </summary>
internal static class DebateTranscript
{
    public static IReadOnlyList<TurnView> BuildViews(
        ReactionDebate debate,
        IReadOnlyDictionary<int, string> resolvedPassages,
        IReadOnlyDictionary<int, string> resolvedSources)
    {
        var views = new List<TurnView>();

        string? previousSpeaker = null;
        string? previousDivider = null;

        foreach (var turn in debate.Turns)
        {
            // A pack that validated has every speaker declared, so this cannot
            // happen from shipped content - but a turn is not worth dropping
            // silently either, so it gets a placeholder that says what is
            // wrong rather than disappearing.
            var critic = ReactionLibrary.Critic(turn.CriticId)
                         ?? new AncientCritic(turn.CriticId, turn.CriticId, CriticKind.Composite,
                             "unknown", 0, 0, null, "speaker not found in any pack", null, null, null);

            // The date pill, and only when the date changes - otherwise every
            // turn of a five-turn year carries its own label and the thing
            // that matters (the centuries opening up) stops standing out.
            string? divider = null;
            if (debate.Kind == DebateKind.AcrossTheCenturies && turn.Year.HasValue)
            {
                var label = AncientCritic.Year(turn.Year.Value);
                if (label != previousDivider)
                {
                    divider = label;
                    previousDivider = label;
                }
            }

            // Consecutive turns by one speaker drop the name and the portrait
            // and tuck up under the one before, which is what every messaging
            // application does and what makes a nine-turn argument read as
            // five people rather than as nine paragraphs. A date between them
            // starts a new block, because centuries have passed.
            var showSpeaker = divider != null || previousSpeaker != turn.CriticId;
            previousSpeaker = turn.CriticId;

            var passageResolved = resolvedPassages.ContainsKey(turn.Seq);
            var sourceResolved = resolvedSources.ContainsKey(turn.Seq);

            views.Add(new TurnView(
                turn,
                critic,
                showSpeaker,
                divider,
                PassageLabel(turn, passageResolved),
                passageResolved,
                SourceLabel(turn, sourceResolved),
                sourceResolved));
        }

        return views;
    }

    /// <summary>
    /// The citation alone, not the work and the citation.
    ///
    /// It used to name the work, which is fine for the Iliad and absurd for
    /// Thucydides: "History of the Peloponnesian War 7.87.5" is a chip wider
    /// than some of the sentences above it, three times on one screen. The
    /// work is in the title bar, in the header, and is the thing the reader
    /// right-clicked to get here.
    /// </summary>
    public static string? PassageLabel(DebateTurn turn, bool resolved)
    {
        if (string.IsNullOrWhiteSpace(turn.PassageRef)) return null;

        return resolved
            ? $"›  read {turn.PassageRef}"
            : $"{turn.PassageRef} – not in this library";
    }

    public static string? SourceLabel(DebateTurn turn, bool resolved)
    {
        if (string.IsNullOrWhiteSpace(turn.SourceCitation)) return null;

        return resolved
            ? "Source: " + turn.SourceCitation + "  – open it"
            : "Source: " + turn.SourceCitation;
    }

    /// <summary>
    /// Counts rather than names. Listing the real speakers ran off the end of
    /// the line on the Iliad debate, which has five of them - and a sentence
    /// cut off mid-word is worse than a shorter one, particularly this
    /// sentence, whose job is to say how much of what is above was invented.
    /// </summary>
    public static string Summarise(ReactionDebate debate, IReadOnlyList<TurnView> views)
    {
        var critics = views.Select(v => v.Critic).DistinctBy(c => c.Id).ToList();

        var real = critics.Count(c => c.Kind == CriticKind.Historical);
        var invented = critics.Count - real;

        var people = real == 0
            ? $"{invented} invented speakers"
            : $"{invented} invented, {real} real (click a face for which is which)";

        return $"{debate.Turns.Count} turns · {people}.";
    }

    /// <summary>The header's second line: what kind of thing this is, and when and where.</summary>
    public static string SettingLine(ReactionDebate debate) =>
        debate.Kind == DebateKind.AcrossTheCenturies
            ? $"Not a conversation · {debate.SettingLabel}"
            : $"A scene · {debate.SettingLabel}";

    /// <summary>The line under a speaker's name: their trade, their world, their years.</summary>
    public static string SpeakerMeta(AncientCritic critic)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(critic.Role)) parts.Add(critic.Role!);
        if (!string.IsNullOrWhiteSpace(critic.Era)) parts.Add(critic.Era);
        parts.Add(critic.FloruitLabel);
        return string.Join("  ·  ", parts);
    }

    /// <summary>The fiction label - the sentence that has to be readable before anything else is.</summary>
    public const string FictionLabel =
        "These conversations were written for this window - nobody said these words. "
        + "Speakers marked “real person” are paraphrased from something they wrote, and their turns carry the reference.";

    /// <summary>
    /// "What am I reading?", in full: what is invented, what is guaranteed,
    /// and the rules the program holds the content to.
    /// </summary>
    public static IReadOnlyList<string> Explanation()
    {
        var paragraphs = new List<string>
        {
            "Nobody in antiquity had these conversations. The words in the bubbles were "
            + "written for this window, and most of the speakers are invented people - a "
            + "farmer from Acharnae, a rhapsode, a schoolmaster - who stand in for the "
            + "audiences and readers who really did argue about these works and left nothing "
            + "in writing.",

            "Some speakers are real, and those are handled differently. A speaker marked "
            + "“real person” may only voice a view that survives in an ancient text, "
            + "and every one of their turns carries the reference for it. Where your library "
            + "has that text, the reference is a link and you can go and read the passage. "
            + "The wording is still a paraphrase written for this window; what is guaranteed "
            + "is the position and the citation, not the sentence.",

            "The invented speakers are kept honest too. Nobody may speak before the work "
            + "they are discussing was written, or outside their own lifetime, and a "
            + "conversation whose speakers could not all have been in one room is labelled "
            + "as centuries of reaction rather than as a scene. Those rules are checked by "
            + "the program on every build, not by whoever wrote the content.",

            "None of this is evidence, and none of it should be cited. What it is for is "
            + "the thing a work like this is hardest to get at on your own: that it landed in "
            + "a particular room, in front of people with their own quarrels about morality, "
            + "religion, decorum and craft, and that the argument about it started at once and "
            + "has never stopped. If a turn makes you want to go and read the passage, or the "
            + "source under it, or disagree with both - that is the entire point, and every "
            + "link in this window is there to let you.",

            $"Debates available: {ReactionLibrary.Packs.Count}. You can add your own as JSON "
            + $"files in:\n{ReactionLibrary.UserPackDirectory}"
        };

        var problems = ReactionLibrary.Problems;
        if (problems.Count > 0)
        {
            paragraphs.Add("Some packs could not be loaded:\n  " + string.Join("\n  ", problems.Take(6)));
        }

        return paragraphs;
    }
}
