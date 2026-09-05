using System.Text;
using System.Text.RegularExpressions;

namespace ClassicaCodex.Core;

/// <summary>A real row from the installed library. IDs are used only within this run.</summary>
public sealed record ArcadePassage(long NodeId, int WorkId, string Author, string Title,
    string Citation, string Text, string Language);

public sealed record ArcadeStory(QuestArc Arc, IReadOnlyList<IReadOnlyList<ArcadePassage>> Sources);

public enum ArcadeQuestPhase { Introduction, Battle, Hunt, Revelation, Complete }

/// <summary>Gates combat behind actual reading; a victory alone cannot advance the story.</summary>
public sealed class ArcadeQuest
{
    public ArcadeStory Story { get; }
    public int Chapter { get; private set; }
    public ArcadeQuestPhase Phase { get; private set; } = ArcadeQuestPhase.Introduction;
    public List<ArcadePassage> Found { get; } = new();
    public QuestPassage Clue => Story.Arc.Passages[Chapter];

    public ArcadeQuest(ArcadeStory story)
    {
        if (story.Arc.Passages.Length == 0 || story.Sources.Count != story.Arc.Passages.Length
            || story.Sources.Any(s => s.Count == 0))
            throw new ArgumentException("A story must resolve every passage.", nameof(story));
        Story = story;
    }

    public bool BeginBattle()
    {
        if (Phase != ArcadeQuestPhase.Introduction && Phase != ArcadeQuestPhase.Revelation) return false;
        if (Phase == ArcadeQuestPhase.Revelation) Chapter++;
        Phase = ArcadeQuestPhase.Battle;
        return true;
    }

    /// <summary>Replays validated reading progress against a currently available story.</summary>
    public static ArcadeQuest Restore(ArcadeStory story, int chapter, ArcadeQuestPhase phase, IReadOnlyList<ArcadePassage> found)
    {
        if (!Enum.IsDefined(phase) || chapter < 0 || chapter >= story.Arc.Passages.Length)
            throw new ArgumentException("Invalid adventure checkpoint.");
        var expected = phase == ArcadeQuestPhase.Introduction ? 0
            : phase is ArcadeQuestPhase.Revelation or ArcadeQuestPhase.Complete ? chapter + 1 : chapter;
        if (found.Count != expected || (phase == ArcadeQuestPhase.Introduction && chapter != 0))
            throw new ArgumentException("The checkpoint and recovered verses disagree.");
        var quest = new ArcadeQuest(story);
        foreach (var row in found)
        {
            if (!quest.BeginBattle() || !quest.WinBattle() || !quest.Submit(row))
                throw new ArgumentException("A recovered verse no longer matches this story.");
        }
        if (phase is ArcadeQuestPhase.Battle or ArcadeQuestPhase.Hunt)
        {
            quest.BeginBattle();
            if (phase == ArcadeQuestPhase.Hunt) quest.WinBattle();
        }
        if (quest.Chapter != chapter || quest.Phase != phase)
            throw new ArgumentException("Invalid story phase.");
        return quest;
    }

    public bool WinBattle()
    {
        if (Phase != ArcadeQuestPhase.Battle) return false;
        Phase = ArcadeQuestPhase.Hunt;
        return true;
    }

    public bool Submit(ArcadePassage selected)
    {
        if (Phase != ArcadeQuestPhase.Hunt || string.IsNullOrWhiteSpace(selected.Text)) return false;
        var exact = MatchesAddress(Clue, selected);
        // Some poetry repeats verbatim in another work or citation. Accept those
        // locations too, but never infer equivalence from a vaguely similar line.
        var repeated = !Clue.Unique && Story.Sources[Chapter].Any(p =>
            !string.IsNullOrWhiteSpace(p.Text) && Normalize(p.Text) == Normalize(selected.Text));
        if (!exact && !repeated) return false;
        Found.Add(selected);
        Phase = Chapter == Story.Arc.Passages.Length - 1
            ? ArcadeQuestPhase.Complete : ArcadeQuestPhase.Revelation;
        return true;
    }

    public static bool MatchesWork(QuestPassage passage, string author, string title) =>
        author.Contains(passage.AuthorKey, StringComparison.OrdinalIgnoreCase)
        && passage.TitleKeys.Any(k => title.Contains(k, StringComparison.OrdinalIgnoreCase));

    public static bool MatchesAddress(QuestPassage passage, ArcadePassage row) =>
        MatchesWork(passage, row.Author, row.Title)
        && string.Equals(PassageCitation.Display(row.Citation), passage.CitationRef, StringComparison.Ordinal);

    private static string Normalize(string text) =>
        Regex.Replace(text.Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
}
