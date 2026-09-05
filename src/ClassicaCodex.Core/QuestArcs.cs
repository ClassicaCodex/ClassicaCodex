namespace ClassicaCodex.Core;

/// <summary>
/// One passage a player is sent to find, and what it is for.
///
/// Identified the way <see cref="StartingPoints"/> identifies a work - by
/// author and title text rather than by URN - because the same work carries
/// different identifiers across the corpora this application can install, and
/// a quest keyed on one corpus's URNs would be unplayable on another library.
/// The citation reference is exact, since that part is stable across editions:
/// Iliad 1.1 is Iliad 1.1 wherever it is printed.
/// </summary>
/// <param name="Award">
/// What the game says when it hands the passage over. Never the line itself -
/// the player is given the sense and has to find the words.
/// </param>
/// <param name="Reveal">
/// What the passage turns out to mean once found, and how it hooks the next
/// one. Read in order these are the story the arc is telling.
/// </param>
/// <param name="Unique">
/// False where this exact text sits at more than one citation in the library,
/// so the game must accept any of its locations rather than one. Measured
/// rather than assumed: Greek poetry repeats whole formulaic lines on purpose,
/// Aeschylus has Agamemnon cry out twice in identical words, and Euripides ends
/// four different plays with the same three lines - which is the point of the
/// last passage below.
/// </param>
public sealed record QuestPassage(
    string AuthorKey,
    string[] TitleKeys,
    string CitationRef,
    string Award,
    string Reveal,
    bool Unique = true);

/// <summary>
/// A run of passages that add up to something, with the connective sense
/// stated rather than left for a player to guess.
/// </summary>
/// <param name="Premise">Shown when the arc begins - what the player is chasing.</param>
/// <param name="Payoff">Shown when the last passage is found - what it added up to.</param>
public sealed record QuestArc(
    string Key,
    string Title,
    string Premise,
    string Payoff,
    QuestPassage[] Passages);

/// <summary>
/// The passage-hunts a player can be sent on.
///
/// Each arc is a real reading of the texts rather than a list of famous lines:
/// the passages are in narrative order, each one sets up the next, and the last
/// is chosen so that arriving at it changes what the first one meant. That is
/// the whole point of the exercise - somebody who finishes an arc should come
/// away knowing something about the mythology they did not know going in, and
/// should have read five real passages in the original to get there.
///
/// Every citation here was resolved against a full library before being
/// written down, and QuestArcTests keeps them honest. An arc whose texts a
/// particular library does not have is simply not offered, which is why there
/// are several and why two of them need only Homer.
/// </summary>
public static class QuestArcs
{
    public static readonly QuestArc[] All =
    {
        // ------------------------------------------------------------------
        new QuestArc(
            "agamemnon-net",
            "The Wrath and the Net",
            "A king takes a girl from another man and wins his war. Follow him home.",
            "The quarrel that opens the Iliad is over a captured woman, and nobody in "
            + "it is thinking about justice. Four texts later the same family's blood "
            + "feud is stopped by a jury of Athenian citizens voting in the open - the "
            + "first court in the world, invented on stage to end a story that began "
            + "with a soldier refusing to give a girl back. Aeschylus is answering "
            + "Homer, and the answer is that vengeance had to become law or it would "
            + "never have stopped.",
            new[]
            {
                new QuestPassage("Homer", new[]{"iliad"}, "1.106",
                    "A king turns on the prophet who told him the truth. Find the insult.",
                    "Agamemnon calls Calchas a prophet of evil who never yet told him "
                    + "anything good. He has just been told to give back a girl. Watch "
                    + "what he takes instead."),
                new QuestPassage("Homer", new[]{"iliad"}, "1.185",
                    "He gives one girl back and takes another from the best man in the army.",
                    "He takes Briseis from Achilles, and the whole Iliad follows from "
                    + "it. Ten years later he sails home in triumph."),
                new QuestPassage("Homer", new[]{"odyssey"}, "11.409",
                    "In the land of the dead, a shade tells Odysseus how he died.",
                    "Agamemnon's ghost says Aegisthus contrived his death - killed at "
                    + "dinner, he says, like an ox at the manger. Homer gives the murder "
                    + "to the lover. Two hundred years later a playwright gives it to "
                    + "the wife."),
                new QuestPassage("Aeschylus", new[]{"agamemnon"}, "1343",
                    "A cry from inside the house. The king is finished.",
                    "The most famous death in Greek tragedy happens offstage - and he "
                    + "cries out twice in identical words, so either line is the one you "
                    + "were sent for. Clytemnestra did it, not Aegisthus, and she did it "
                    + "for the daughter he killed at Aulis to get his fair wind.",
                    Unique: false),
                new QuestPassage("Aeschylus", new[]{"eumenides"}, "752",
                    "The son who avenged him stands trial. Find the verdict.",
                    "Orestes is acquitted by a jury, and the Furies are argued into "
                    + "becoming the Kindly Ones. The blood feud does not end because "
                    + "someone finally wins it. It ends because a court is invented.")
            }),

        // ------------------------------------------------------------------
        new QuestArc(
            "achilles-pity",
            "Wrath into Pity",
            "The Iliad's first word is anger. Follow it to the last thing it becomes.",
            "The Iliad is not a war poem that happens to end with a funeral. It is a "
            + "poem about a man's rage, and it ends when the rage runs out - not in "
            + "victory but in two enemies crying together about their fathers. Homer "
            + "buries Hector, not Troy. The city never falls inside this poem.",
            new[]
            {
                new QuestPassage("Homer", new[]{"iliad"}, "1.1",
                    "The first word of European literature. It is not a name.",
                    "Menin - wrath. The poem announces its subject before it names its "
                    + "hero, and the subject is an emotion."),
                new QuestPassage("Homer", new[]{"iliad"}, "16.855",
                    "His friend goes out wearing his armour and does not come back.",
                    "Patroclus dies. Read the two lines that carry his soul off, and "
                    + "keep them in mind - one of them is going to come back."),
                new QuestPassage("Homer", new[]{"iliad"}, "22.363",
                    "Achilles kills the man who killed Patroclus. Find the line that carries Hector off.",
                    "Word for word the line that carried Patroclus off at 16.857 - his "
                    + "soul going to Hades, bewailing its fate, leaving manhood and youth. "
                    + "Homer gives the avenger and the avenged the same death formula. The "
                    + "revenge changes nothing, and the poem says so in its own repetitions.",
                    Unique: false),
                new QuestPassage("Homer", new[]{"iliad"}, "24.478",
                    "An old man crosses the battlefield alone at night to beg.",
                    "Priam kisses the hands that killed his son. It is the most "
                    + "shocking gesture in the poem, and Achilles lets him."),
                new QuestPassage("Homer", new[]{"iliad"}, "24.507",
                    "The two of them weep. Find who each is weeping for.",
                    "Priam weeps for Hector, Achilles for his own father - whom he "
                    + "knows he will never see again. The wrath of the first line ends "
                    + "here, in a tent, over an old man neither of them can save.")
            }),

        // ------------------------------------------------------------------
        new QuestArc(
            "odysseus-home",
            "The Long Way Home",
            "A man who is very good at lying wants to get home. Follow the lies.",
            "Odysseus survives by being nobody - a false name, a borrowed shape, a "
            + "story for every host. The Odyssey is about how much of yourself you can "
            + "give away and still be recognised at the end, and the recognition when "
            + "it comes is not by his face or his scar but by a bed only two people "
            + "know the secret of.",
            new[]
            {
                new QuestPassage("Homer", new[]{"odyssey"}, "1.1",
                    "The poem's first line asks for a man. Find the word it uses for him.",
                    "Polytropon - of many turns, many ways, many tricks. The first thing "
                    + "we learn about him is that he is hard to pin down."),
                new QuestPassage("Homer", new[]{"odyssey"}, "9.366",
                    "Trapped in a cave by something enormous, he gives a false name.",
                    "'Nobody is my name.' The joke costs the Cyclops his eye and costs "
                    + "Odysseus ten years, because he cannot resist shouting his real "
                    + "name from the boat."),
                new QuestPassage("Homer", new[]{"odyssey"}, "10.239",
                    "A goddess turns his crew into animals. Find what is left of them.",
                    "They have the heads and voices and bristles of pigs - but their "
                    + "minds stay their own. Homer's monsters are rarely about the body."),
                new QuestPassage("Homer", new[]{"odyssey"}, "12.184",
                    "Tied to the mast, he hears the one song no one survives.",
                    "The Sirens do not sing about pleasure. They sing that they know "
                    + "everything that happened at Troy - they offer him his own story. "
                    + "The temptation is to stop travelling and start being sung about."),
                new QuestPassage("Homer", new[]{"odyssey"}, "23.296",
                    "Home at last, his wife will not believe him. Find what convinces her.",
                    "Not the scar, not the bow. The bed he built himself from a living "
                    + "olive tree, which cannot be moved - and only she and he know it. "
                    + "The man of many turns is finally identified by the one thing he "
                    + "made that will not turn.")
            }),

        // ------------------------------------------------------------------
        new QuestArc(
            "prometheus-fire",
            "Fire and the Rock",
            "Someone gave humans fire and was punished for it. Find out what the gift cost.",
            "Hesiod tells the story twice and blames Prometheus both times: the trick "
            + "at Mecone gets fire taken away, the theft gets it back, and the price is "
            + "Pandora and the whole misery of human life. Then Aeschylus puts the same "
            + "figure on stage and makes him the one who gave us everything - fire, "
            + "number, writing, medicine - and Zeus the tyrant. The same myth, told by "
            + "a farmer and by a democrat, comes out as two different arguments about "
            + "power.",
            new[]
            {
                new QuestPassage("Hesiod", new[]{"theogony"}, "535",
                    "Gods and men divide an ox at Mecone. Find who is doing the carving.",
                    "Prometheus splits the sacrifice so that men keep the meat and the "
                    + "gods get bones wrapped in fat. Every Greek sacrifice afterwards "
                    + "follows the portions set here."),
                new QuestPassage("Hesiod", new[]{"theogony"}, "561",
                    "Zeus notices. Find what he takes away.",
                    "He hides fire from men. Hesiod's Zeus is not fooled by the trick - "
                    + "he sees it and lets it stand, then punishes anyway."),
                new QuestPassage("Hesiod", new[]{"works"}, "47",
                    "The same story again, in a poem about farming. Find what it explains.",
                    "Hesiod tells it a second time to explain why work is hard. Fire "
                    + "hidden, fire stolen, and then Pandora - the myth is an answer to "
                    + "the question of why living is difficult."),
                new QuestPassage("Aeschylus", new[]{"prometheus"}, "88",
                    "Now he is nailed to a rock at the edge of the world. Find what he calls out to.",
                    "Bright air, swift winds, rivers, the sea's countless laughter, "
                    + "earth the mother of all - and the all-seeing circle of the sun. "
                    + "He addresses the world he furnished, and asks it to see what a "
                    + "god suffers at the hands of gods.",
                    Unique: false),
                new QuestPassage("Aeschylus", new[]{"prometheus"}, "907",
                    "Chained and tortured, he says one thing that frightens Zeus. Find it.",
                    "That Zeus will make a marriage that throws him from power, and only "
                    + "Prometheus knows which. The tyrant needs the prisoner. Hesiod's "
                    + "trickster has become the one figure in the universe that absolute "
                    + "power cannot simply crush.")
            }),

        // ------------------------------------------------------------------
        new QuestArc(
            "medea-knife",
            "The Fleece and the Knife",
            "A hero needs a princess's help to steal a golden fleece. Follow what happens to her.",
            "Apollonius wrote the most sympathetic portrait of falling in love in ancient "
            + "literature, and Euripides wrote what the same woman did fifteen years "
            + "later. Read in order they are one story: the girl who betrayed her father "
            + "for a man, in a country where a foreign wife has no standing at all, and "
            + "what she does when he sets her aside for a better marriage. Medea is not "
            + "a monster who appears in Act One. She is made.",
            new[]
            {
                new QuestPassage("Apollonius", new[]{"argonautica"}, "3.275",
                    "A god slips into the palace unseen and shoots. Find who he hits.",
                    "Eros crouches at Jason's feet and shoots Medea. Apollonius stages "
                    + "falling in love as an ambush - she has no say in it, which matters "
                    + "for everything that follows."),
                new QuestPassage("Apollonius", new[]{"argonautica"}, "4.1",
                    "Book four opens by asking the goddess for help. Find what the poet says he cannot tell.",
                    "He asks the Muse for the girl's anguish and her thoughts, and says "
                    + "his own mind is helpless. The poem knows it is handling something "
                    + "it cannot fully explain."),
                new QuestPassage("Euripides", new[]{"medea"}, "1",
                    "Years later, in Corinth, a nurse wishes one thing had never happened.",
                    "'Would that the Argo had never winged its way.' The play opens by "
                    + "wishing away the voyage the previous poem celebrated."),
                new QuestPassage("Euripides", new[]{"medea"}, "1078",
                    "She knows exactly what she is about to do. Find her saying so.",
                    "'I understand what evil I am about to do, but anger is stronger than "
                    + "my deliberations.' One of the most argued-over lines in Greek - it "
                    + "is a person watching herself decide."),
                new QuestPassage("Euripides", new[]{"medea"}, "1236",
                    "Find the moment the decision stops being a debate.",
                    "'Friends, the deed is resolved.' What began as an ambush by a god in "
                    + "a Hellenistic love poem ends with a woman in a tragedy choosing, "
                    + "in full knowledge, the thing that will destroy the man who "
                    + "discarded her - and her own children with him.")
            }),

        // ------------------------------------------------------------------
        new QuestArc(
            "dionysus-thebes",
            "The God Who Came to Thebes",
            "A young stranger arrives in the city where his mother died. Find out who he is.",
            "Pentheus spends the play insisting that Dionysus is not a god and that the "
            + "women on the mountain are drunk. He is wrong about both, and Euripides "
            + "makes the audience watch a rationalist be dismantled by exactly the thing "
            + "he refused to believe in. The last word of the play is that the gods do "
            + "what nobody expects - which is either a consolation or a threat, and "
            + "Euripides does not say which.",
            new[]
            {
                new QuestPassage("Euripides", new[]{"bacchae"}, "1",
                    "The play opens with someone announcing himself. Find whose son he says he is.",
                    "'I am come, the son of Zeus, to this land of the Thebans.' The god "
                    + "speaks the prologue in his own person, so the audience knows from "
                    + "line one what Pentheus will spend the play denying."),
                new QuestPassage("Euripides", new[]{"bacchae"}, "1118",
                    "On the mountain, a man tries to make his mother recognise him.",
                    "'Mother, it is I, your son Pentheus.' She does not see him. The god "
                    + "has taken her sight, and she is about to tear him apart believing "
                    + "he is a lion."),
                new QuestPassage("Euripides", new[]{"bacchae"}, "1388",
                    "The chorus closes the play. Find what they say about the gods.",
                    "Many are the forms of the divine, and the gods bring to pass much "
                    + "that was unlooked for. Search for it and you will find it four "
                    + "times over - Euripides ends Alcestis, Andromache, Helen and this "
                    + "play with the same lines. A tag he kept reaching for, and never "
                    + "more exactly than after a god has torn a city's king apart.",
                    Unique: false)
            })
    };

    /// <summary>
    /// The arcs a library can actually run, given a way to ask whether one of
    /// its passages is present.
    ///
    /// All or nothing per arc: an arc missing its last passage is worse than
    /// an arc not offered, because a player would reach the end of a story
    /// that cannot finish. Which is also why several arcs here need only
    /// Homer - the smallest useful install can still play.
    /// </summary>
    public static List<QuestArc> PlayableIn(Func<QuestPassage, bool> passageExists) =>
        All.Where(arc => arc.Passages.All(passageExists)).ToList();
}
