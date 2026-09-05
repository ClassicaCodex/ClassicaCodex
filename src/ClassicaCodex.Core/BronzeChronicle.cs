using System.Security.Cryptography;
using System.Text;

namespace ClassicaCodex.Core;

public sealed class BronzeRunSave
{
    public Guid RunId { get; set; } = Guid.NewGuid();
    public string ArcKey { get; set; } = "";
    public string StorySignature { get; set; } = "";
    public int Seed { get; set; }
    public int Chapter { get; set; }
    public ArcadeQuestPhase Phase { get; set; }
    public List<ArcadePassage> Found { get; set; } = new();
    public List<BronzeGiftId> Gifts { get; set; } = new();
    public List<BronzeEnemyKind> ChapterFoes { get; set; } = new();
    public int Score { get; set; }
    public int HintLevel { get; set; }

    public static string Signature(QuestArc arc) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        arc.Key + "|" + string.Join("|", arc.Passages.Select(p => p.AuthorKey + ":" + string.Join(",", p.TitleKeys) + ":" + p.CitationRef + ":" + p.Unique)))));

    public ArcadeQuest Restore(ArcadeStory story)
    {
        if (RunId == Guid.Empty || ArcKey != story.Arc.Key || StorySignature != Signature(story.Arc)
            || Gifts == null || Gifts.Any(g => !Enum.IsDefined(g)) || Gifts.Distinct().Count() != Gifts.Count
            || ChapterFoes == null || ChapterFoes.Any(k => !Enum.IsDefined(k)) || Found == null
            || Found.Any(p => p is not { Author: not null, Title: not null, Citation: not null, Text: not null, Language: not null })
            || Score < 0 || HintLevel is < 0 or > 2)
            throw new ArgumentException("This saved adventure does not match the current story.");
        // One gift may be chosen after each nonfinal recovered verse.
        var maximum = Math.Min(Found.Count, story.Arc.Passages.Length - 1);
        var minimum = Phase == ArcadeQuestPhase.Revelation ? maximum - 1 : maximum;
        if (Gifts.Count < Math.Max(0, minimum) || Gifts.Count > maximum)
            throw new ArgumentException("The saved divine gifts do not match this chapter.");
        return ArcadeQuest.Restore(story, Chapter, Phase, Found);
    }
}

public sealed class BronzeDiscovery
{
    public BronzeEnemyKind Kind { get; set; }
    public int Defeats { get; set; }
    public List<BronzeRecoveredVerse> Verses { get; set; } = new();
}

public sealed record BronzeRecoveredVerse(string ArcTitle, string Meaning, ArcadePassage Passage);
public sealed record BronzeTrophy(Guid RunId, string ArcKey, string ArcTitle, string Epithet, int Score,
    DateTimeOffset EarnedAt, string Premise, string Payoff, List<BronzeRecoveredVerse> Verses, List<BronzeGiftId> Gifts);

public sealed class BronzeChronicle
{
    public int Version { get; set; } = 1;
    public BronzeRunSave? Run { get; set; }
    public List<BronzeDiscovery> Bestiary { get; set; } = new();
    public List<BronzeTrophy> Trophies { get; set; } = new();
    public bool Sound { get; set; } = true;
    public bool Scanlines { get; set; } = true;

    public void RecordDefeat(BronzeEnemyKind kind, int count)
    {
        if (count <= 0) return;
        var entry = Bestiary.FirstOrDefault(e => e.Kind == kind);
        if (entry == null) { entry = new BronzeDiscovery { Kind = kind }; Bestiary.Add(entry); }
        entry.Defeats = (int)Math.Min(int.MaxValue, (long)entry.Defeats + count);
    }

    public void RememberVerse(IEnumerable<BronzeEnemyKind> foes, BronzeRecoveredVerse verse)
    {
        foreach (var entry in Bestiary.Where(e => foes.Contains(e.Kind)))
            if (!entry.Verses.Any(v => v.ArcTitle == verse.ArcTitle && v.Passage.Author == verse.Passage.Author
                && v.Passage.Title == verse.Passage.Title && PassageCitation.Display(v.Passage.Citation) == PassageCitation.Display(verse.Passage.Citation)))
                entry.Verses.Add(verse);
    }

    public void Crown(BronzeTrophy trophy)
    {
        if (Trophies.All(t => t.RunId != trophy.RunId)) Trophies.Add(trophy);
    }
}
