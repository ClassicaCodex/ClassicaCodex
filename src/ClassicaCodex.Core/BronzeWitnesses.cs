namespace ClassicaCodex.Core;

/// <summary>Optional primary-source reading, separate from the adventure's passage gates.</summary>
public sealed record BronzeWitnessSpec(BronzeEnemyKind Creature, string Title, string Note,
    string AuthorKey, string[] TitleKeys, string Citation, bool Section = false);
public sealed record BronzeWitness(BronzeWitnessSpec Witness, IReadOnlyList<ArcadePassage> Editions);

public static class BronzeWitnesses
{
    public static readonly BronzeWitnessSpec[] All =
    {
        new(BronzeEnemyKind.Serpent, "The serpent at Delphi", "Read the account behind the serpent's mythic echo.",
            "Apollodorus", new[] { "Library", "Bibliotheca" }, "1.4.1", Section: true),
        new(BronzeEnemyKind.Harpy, "A stolen meal", "Follow the Harpies into the story of Phineus and the Argonauts.",
            "Apollodorus", new[] { "Library", "Bibliotheca" }, "1.9.21", Section: true),
        new(BronzeEnemyKind.Boar, "Bring it back alive", "The Erymanthian boar belongs to a labour that calls for capture.",
            "Apollodorus", new[] { "Library", "Bibliotheca" }, "2.5.4", Section: true),
        new(BronzeEnemyKind.Cyclops, "A hero called Nobody", "Start at Odysseus's borrowed name, then follow what the Cyclops makes of it. Open the reader to continue beyond this line.",
            "Homer", new[] { "Odyssey", "Odyssea" }, "9.366"),
        new(BronzeEnemyKind.Cyclops, "The makers of thunder", "Hesiod's Cyclopes are makers of Zeus's thunderbolt. Compare their craft with Homer's cave-dweller: a shared name does not make every story the same.",
            "Hesiod", new[] { "Theogony", "Theogonia" }, "139"),
        new(BronzeEnemyKind.Gorgon, "Look into the bronze", "Visit Perseus, the Gorgons, and the borrowed equipment behind your divine gifts.",
            "Apollodorus", new[] { "Library", "Bibliotheca" }, "2.4.2", Section: true),
        new(BronzeEnemyKind.Hydra, "The labour of two heroes", "Read the Hydra's story with Iolaus beside Heracles. Open the reader to follow the whole labour.",
            "Apollodorus", new[] { "Library", "Bibliotheca" }, "2.5.2", Section: true)
    };
}
