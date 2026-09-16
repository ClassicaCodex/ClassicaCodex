using System.Text.RegularExpressions;

namespace ClassicaCodex.Core.Reactions;

/// <summary>
/// The rules the authored content has to keep, checked in code rather than by
/// whoever wrote it remembering.
///
/// <b>Why a validator and not just care.</b> Three of these rules are things a
/// classicist would catch on a good day and miss on a tired one, and all three
/// are invisible once shipped. A speaker who died before the play opened reads
/// perfectly. A turn numbered 4, 5, 7 displays in order and silently drops
/// nothing a reader can see. A historical critic given an opinion with no
/// citation is exactly as fluent as one with a real one, and is the single
/// thing this feature must never do - see <see cref="CriticKind"/>.
///
/// So the rules run on every pack at load, in a unit test per rule against a
/// deliberately broken fixture, and over the shipped packs as a whole. The
/// shipped-content test is the one that matters: it is what turns "the content
/// was checked once" into "the content is checked on every build".
///
/// <b>What it cannot check.</b> Whether an invented Athenian sounds like an
/// Athenian, and whether an attested stance is really attested. Those are
/// editorial, and no amount of code substitutes for the citation being right.
/// What code can do is insist the citation is THERE.
/// </summary>
public static class ReactionPackValidator
{
    /// <summary>
    /// The shape a citation into the corpus is allowed to take: digits, dots,
    /// and the occasional letter that a real reference carries ("327a",
    /// "5b", "pr.1"). Anything with a space in it is prose rather than a
    /// reference, and would resolve to nothing while looking like a link.
    /// </summary>
    private static readonly Regex CitationShape =
        new(@"^[A-Za-z0-9]+(\.[A-Za-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>
    /// A debate has to be long enough to be an argument and short enough to
    /// read. Both ends are from the kickoff spec; the lower one is the one
    /// doing real work, since two turns is a remark and a reply, not a debate.
    /// </summary>
    public const int MinimumTurns = 6;

    public const int MaximumTurns = 14;

    /// <summary>
    /// Everything wrong with one pack, in the order the rules are listed.
    /// Empty means the pack is fit to ship.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        ReactionPack pack, IReadOnlyDictionary<string, AncientCritic>? sharedCritics = null)
    {
        var problems = new List<string>();
        var debate = pack.Debate;

        // Critics this pack can see: its own, plus any another pack already
        // introduced. Shared by id is the point - the same chorus-trainer
        // turns up in more than one argument.
        var known = new Dictionary<string, AncientCritic>(StringComparer.Ordinal);
        if (sharedCritics != null)
            foreach (var pair in sharedCritics) known[pair.Key] = pair.Value;

        foreach (var critic in pack.Critics)
        {
            if (known.ContainsKey(critic.Id) && !ReferenceEquals(known[critic.Id], critic)
                && known[critic.Id] != critic)
            {
                problems.Add($"{pack.Name}: critic \"{critic.Id}\" is declared differently in "
                             + "another pack. Critics are shared by id, so the two declarations "
                             + "have to agree.");
            }

            if (string.IsNullOrWhiteSpace(critic.Name))
                problems.Add($"{pack.Name}: critic \"{critic.Id}\" has no name.");

            if (critic.FloruitStart > critic.FloruitEnd)
            {
                problems.Add($"{pack.Name}: critic \"{critic.Id}\" has a floruit that ends "
                             + $"({critic.FloruitEnd}) before it starts ({critic.FloruitStart}).");
            }

            known[critic.Id] = critic;
        }

        ValidateTurnStructure(pack, problems);
        ValidateSpeakers(pack, known, problems);
        ValidateChronology(pack, known, problems);
        ValidateCitations(pack, known, problems);

        if (string.IsNullOrWhiteSpace(debate.Title))
            problems.Add($"{pack.Name}: debate \"{debate.Id}\" has no title.");

        return problems;
    }

    private static void ValidateTurnStructure(ReactionPack pack, List<string> problems)
    {
        var turns = pack.Debate.Turns;

        if (turns.Count < MinimumTurns || turns.Count > MaximumTurns)
        {
            problems.Add($"{pack.Name}: debate \"{pack.Debate.Id}\" has {turns.Count} turns; "
                         + $"expected between {MinimumTurns} and {MaximumTurns}.");
        }

        for (var i = 0; i < turns.Count; i++)
        {
            if (turns[i].Seq != i + 1)
            {
                problems.Add($"{pack.Name}: turn {i + 1} is numbered {turns[i].Seq}. "
                             + "Sequence numbers start at 1 and have no gaps.");
                break;
            }
        }

        foreach (var turn in turns)
        {
            if (string.IsNullOrWhiteSpace(turn.Text))
                problems.Add($"{pack.Name}: turn {turn.Seq} says nothing.");
        }
    }

    private static void ValidateSpeakers(
        ReactionPack pack, IReadOnlyDictionary<string, AncientCritic> known, List<string> problems)
    {
        foreach (var turn in pack.Debate.Turns)
        {
            if (!known.ContainsKey(turn.CriticId))
            {
                problems.Add($"{pack.Name}: turn {turn.Seq} is spoken by \"{turn.CriticId}\", "
                             + "who is not declared in this pack or any other.");
            }
        }

        var speakers = pack.Debate.SpeakerIds.Count();
        if (speakers < 2)
        {
            problems.Add($"{pack.Name}: debate \"{pack.Debate.Id}\" has {speakers} speaker(s). "
                         + "A debate needs at least two.");
        }
    }

    /// <summary>
    /// The anachronism rules. Both directions matter: a critic cannot speak
    /// before the work exists, and cannot speak outside their own lifetime.
    /// </summary>
    private static void ValidateChronology(
        ReactionPack pack, IReadOnlyDictionary<string, AncientCritic> known, List<string> problems)
    {
        var debate = pack.Debate;

        if (debate.Kind == DebateKind.Scene)
        {
            if (debate.SettingYear == null)
            {
                problems.Add($"{pack.Name}: debate \"{debate.Id}\" is a Scene and needs a settingYear.");
                return;
            }

            var year = debate.SettingYear.Value;

            if (year < debate.CompositionYear)
            {
                problems.Add($"{pack.Name}: debate \"{debate.Id}\" is set in "
                             + $"{AncientCritic.Year(year)}, before the work was written "
                             + $"({AncientCritic.Year(debate.CompositionYear)}).");
            }

            foreach (var id in debate.SpeakerIds)
            {
                if (!known.TryGetValue(id, out var critic)) continue;
                if (critic.WasActiveIn(year)) continue;

                problems.Add($"{pack.Name}: {critic.Name} ({critic.FloruitLabel}) speaks in a debate "
                             + $"set in {AncientCritic.Year(year)}, outside their floruit.");
            }

            foreach (var turn in debate.Turns)
            {
                if (turn.Year != null)
                {
                    problems.Add($"{pack.Name}: turn {turn.Seq} carries its own year, but this is a "
                                 + "Scene - every speaker is in the room at settingYear.");
                }
            }

            return;
        }

        // AcrossTheCenturies: each turn is its own moment, and the moments
        // only move forward. Out of order, this stops being centuries of
        // reception and becomes a muddle that quietly implies the later
        // speaker was answered by the earlier one.
        var previous = int.MinValue;

        foreach (var turn in debate.Turns)
        {
            if (turn.Year == null)
            {
                problems.Add($"{pack.Name}: turn {turn.Seq} has no year, and this debate spans "
                             + "centuries, so there is no settingYear to fall back on.");
                continue;
            }

            var year = turn.Year.Value;

            if (year < debate.CompositionYear)
            {
                problems.Add($"{pack.Name}: turn {turn.Seq} is dated {AncientCritic.Year(year)}, "
                             + $"before the work was written ({AncientCritic.Year(debate.CompositionYear)}).");
            }

            if (year < previous)
            {
                problems.Add($"{pack.Name}: turn {turn.Seq} is dated {AncientCritic.Year(year)}, "
                             + "earlier than the turn before it.");
            }

            previous = year;

            if (known.TryGetValue(turn.CriticId, out var critic) && !critic.WasActiveIn(year))
            {
                problems.Add($"{pack.Name}: turn {turn.Seq} has {critic.Name} "
                             + $"({critic.FloruitLabel}) speaking in {AncientCritic.Year(year)}, "
                             + "outside their floruit.");
            }
        }
    }

    /// <summary>
    /// The rule this feature exists or fails on: a real person may only be
    /// given a view somebody can go and check.
    /// </summary>
    private static void ValidateCitations(
        ReactionPack pack, IReadOnlyDictionary<string, AncientCritic> known, List<string> problems)
    {
        foreach (var turn in pack.Debate.Turns)
        {
            if (known.TryGetValue(turn.CriticId, out var critic)
                && critic.Kind == CriticKind.Historical
                && string.IsNullOrWhiteSpace(turn.SourceCitation))
            {
                problems.Add($"{pack.Name}: turn {turn.Seq} puts words in the mouth of "
                             + $"{critic.Name}, who is a real person, with no source citation. "
                             + "Every turn of a Historical critic needs the ancient reference "
                             + "for the view it is voicing.");
            }

            if (turn.PassageRef != null && !CitationShape.IsMatch(turn.PassageRef))
            {
                problems.Add($"{pack.Name}: turn {turn.Seq} cites passage \"{turn.PassageRef}\", "
                             + "which is not the shape of a citation reference.");
            }

            if (turn.SourceWork?.CitationRef is { } sourceRef && !CitationShape.IsMatch(sourceRef))
            {
                problems.Add($"{pack.Name}: turn {turn.Seq} points at source passage "
                             + $"\"{sourceRef}\", which is not the shape of a citation reference.");
            }

            if (turn.SourceWork != null && string.IsNullOrWhiteSpace(turn.SourceCitation))
            {
                problems.Add($"{pack.Name}: turn {turn.Seq} links to a source work but has no "
                             + "sourceCitation to print, so the link would have no label.");
            }
        }
    }
}
