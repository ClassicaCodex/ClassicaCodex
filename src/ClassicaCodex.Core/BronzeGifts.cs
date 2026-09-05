namespace ClassicaCodex.Core;

public enum BronzeGiftId { Athena, Hermes, Hephaestus, Apollo, Poseidon, Hades }

public sealed record BronzeGift(BronzeGiftId Id, string Name, string Patron, string Effect, string Story, string Source);

/// <summary>Mythic motifs adapted into explicit game rules, not additional quest passages.</summary>
public static class BronzeGifts
{
    public static readonly BronzeGift[] All =
    {
        new(BronzeGiftId.Athena, "The Mirror Shield", "ATHENA",
            "Shift: face and block to reflect enemy bolts. Blocks cost 8 guard instead of 15.",
            "Perseus watches Medusa in a bronze shield while Athena guides his hand. In this arena, the shield turns danger back upon its source.",
            "Apollodorus, Library 2.4.2"),
        new(BronzeGiftId.Hermes, "The Winged Sandals", "HERMES",
            "Move 25% faster. Space: dodge every 0.5 seconds instead of 0.85; dodges cost 12 guard instead of 20.",
            "Hermes crosses sea and land in his golden sandals. Borrow his speed: no monster should choose where you stand.",
            "Homer, Odyssey 5.44–48"),
        new(BronzeGiftId.Hephaestus, "The Living Bronze", "HEPHAESTUS",
            "+35 maximum health. Take 20% less damage after your normal armor protection.",
            "Thetis brings Achilles armor made by Hephaestus. A smith's care can stand between a hero and the end of his story.",
            "Homer, Iliad 18.478–617; 19.1–23"),
        new(BronzeGiftId.Apollo, "The Silver Bow", "APOLLO",
            "L: your magic costs 25 mana instead of 35, and mana returns twice as quickly.",
            "Apollo's silver bow opens the Iliad with terrible power. Here its radiance replenishes your magic rather than bringing plague.",
            "Homer, Iliad 1.43–52"),
        new(BronzeGiftId.Poseidon, "The Earthshaker's Trident", "POSEIDON",
            "K: throw a three-bolt trident volley, each bolt 20% stronger. L remains your separate magic attack.",
            "Poseidon stirs the sea with his trident. In the arena its three points become a wave of bronze.",
            "Homer, Odyssey 5.291–296"),
        new(BronzeGiftId.Hades, "The Unseen Helm", "HADES",
            "Space: dodging conceals you for 0.7 seconds. You cannot be hurt and enemies cannot aim fresh attacks at you.",
            "Perseus escapes the pursuing Gorgons beneath Hades' cap. Your own disappearance lasts only a heartbeat—but a heartbeat can save a life.",
            "Apollodorus, Library 2.4.2–3")
    };

    public static BronzeGift Get(BronzeGiftId id) => All.Single(g => g.Id == id);

    public static BronzeGift[] Offer(IEnumerable<BronzeGiftId> owned, int seed, int chapter)
    {
        var random = new Random(unchecked(seed + chapter * 7919));
        var choices = All.Where(g => !owned.Contains(g.Id)).ToList();
        for (var i = choices.Count - 1; i > 0; i--)
        { var j = random.Next(i + 1); (choices[i], choices[j]) = (choices[j], choices[i]); }
        return choices.Take(3).ToArray();
    }

    public static string Epithet(IReadOnlyCollection<BronzeGiftId> gifts) =>
        gifts.Contains(BronzeGiftId.Athena) && gifts.Contains(BronzeGiftId.Hades) ? "The Gorgon's Last Reflection"
        : gifts.Contains(BronzeGiftId.Hermes) && gifts.Contains(BronzeGiftId.Poseidon) ? "The Storm on Winged Feet"
        : gifts.Contains(BronzeGiftId.Hephaestus) && gifts.Contains(BronzeGiftId.Apollo) ? "The Bronze Dawn"
        : gifts.Contains(BronzeGiftId.Athena) ? "Keeper of the Mirror"
        : gifts.Contains(BronzeGiftId.Hermes) ? "The Uncatchable"
        : gifts.Contains(BronzeGiftId.Hades) ? "The Unseen Reader" : "Keeper of the Lost Verses";
}
