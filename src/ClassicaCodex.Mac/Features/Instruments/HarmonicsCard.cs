namespace ClassicaCodex.Mac.Features.Instruments;

/// <summary>
/// The card that stands in front of Greek harmonic science: what the page is,
/// how it is kept honest, and - before anything else - that it makes a noise.
///
/// <b>Why the warning is the first thing on the card.</b> Its two siblings
/// open a diagram; this one opens an instrument. Someone who chose a menu item
/// in a library application has not asked for a sound, and may be somewhere
/// they must not make one. So the caution is above the description rather
/// than below it, the button says what it will do, and the page behind it
/// still plays nothing until a second, separate press on the page itself. Two
/// gates, deliberately: the reader can open the page in a quiet room, read all
/// of it, and never make a sound. It also happens to be what browsers require
/// - an AudioContext will not open without a gesture - so the honest design
/// and the technical one agree.
///
/// The page ships no audio either - every note is synthesised from its exact
/// whole-number ratio at the moment it sounds, which is the only way it can
/// claim the intervals are the ones Ptolemy wrote. A recording would fix the
/// tuning at whatever the recorder believed.
/// </summary>
internal sealed class HarmonicsCard : InstrumentCard
{
    public HarmonicsCard()
        : base(
            "Ptolemy's Harmony",
            "A monochord and twenty tetrachords",
            "harmonics", "Harmonics",
            "Harmonics.html", "Harmonics",
            // The button says what it will do. Someone who has read nothing
            // else on this card has read the button.
            "Open it (it can make sound)",
            "opening Ptolemy's harmony",
            "Couldn't open it: ")
    {
        // ---- the caution, before the description ---------------------------
        Caution("This one makes sound.");

        Paragraph(
            "It is a musical instrument, so hearing it is most of the point - but it will not "
            + "play anything by itself. The page opens silent and stays silent until you press "
            + "a button on it marked \"Turn the sound on\". If you are in a library, or on a "
            + "train, or anywhere a sudden note would be unwelcome, you can open it, read all "
            + "of it and work the monochord without a sound: the ratios, the table and the "
            + "diagram all run in silence, and only the notes are missing. Somewhere you can "
            + "listen, turn it on - a page about music that you cannot hear is half a page.");

        Muted(
            "Headphones are worth it for the enharmonic. Its two smallest steps are about a "
            + "third of a semitone each, and a laptop speaker will blur them into one.");

        // ---- what it is -----------------------------------------------------
        Heading("What it is");
        Paragraph(
            "Greek harmonic science on the instrument it was found on: a monochord, which is "
            + "one string with a bridge you can move. Drag the bridge and the pitch rises as "
            + "the sounding length falls - the octave at half the string, the fifth at two "
            + "thirds, the fourth at three quarters. That inverse is the whole of the "
            + "discovery, and it is why a musical interval is a ratio of two small whole "
            + "numbers rather than a matter of taste.");

        Paragraph(
            "The rest of the page is the argument that followed. A tetrachord spans a perfect "
            + "fourth, exactly 4:3; its outer notes are fixed and its two inner ones move, and "
            + "where you put them is what makes a genus. Twenty divisions of it are here, from "
            + "Archytas in the fourth century BC through Eratosthenes and Didymus to Ptolemy "
            + "at Alexandria around AD 150 - the Harmonics being the companion to the Almagest, "
            + "which is the page next door in the Instruments menu.");

        Heading("What is worth hearing");
        Paragraph(
            "The enharmonic genus. Its lowest two steps are a pair of quarter-tones crowded "
            + "under a wide major third, and it sounds nothing like a scale - Greek writers "
            + "were already complaining in Ptolemy's day that singers could no longer manage "
            + "it. Hearing it settles in two seconds an argument that print cannot make at all.");

        Paragraph(
            "And the beating. Hold the open string, drag slowly through the fifth, and a little "
            + "either side of it the two notes wobble against each other at a rate you can "
            + "count. At exactly 3:2 the wobble stops and the pair locks. That is what a just "
            + "interval is, and it is why small whole numbers were trusted over ears.");

        Paragraph(
            "Then Didymus against Ptolemy. They divide the fourth with the identical three "
            + "intervals and differ only in the order of two of them, which moves the middle "
            + "note by a syntonic comma - a fifth of a semitone. The page plays one, then the "
            + "other, then both together over a held string, where the disagreement becomes a "
            + "slow beat rather than a fact you have to take on trust.");

        // ---- how it is kept honest -------------------------------------------
        Heading("How it is kept honest");
        Paragraph(
            "A wrong ratio in a page like this does not break anything. It sounds - and it "
            + "sounds like music, because almost any ratio near the right one does. Nobody "
            + "alive can hear 46:45 against 45:44. So the arithmetic is checked instead of the "
            + "ear: every division is required to multiply out to exactly 4:3, which is what a "
            + "tetrachord is, and a mistyped ratio almost never still does. Every ratio is also "
            + "typed a second time, independently, in the test file and compared with the "
            + "first copy. 106 checks, and one deliberate skip.");

        Paragraph(
            "The skip is the Pythagorean enharmonic. Its large interval was the ditone 81:64 "
            + "over a pyknon of 256:243, but how that pyknon was divided is simply not "
            + "recorded. It is shown in the table undivided and it cannot be played, because "
            + "inventing a plausible split would have made it the only false line in the page.");

        Paragraph(
            "Two of the labels in the table are computed rather than asserted, so they cannot "
            + "drift from the numbers beside them. \"Pyknon\" means the two lower intervals "
            + "together are smaller than the top one, which is the real structural difference "
            + "between the enharmonic and chromatic genera and the diatonic. \"Epimoric\" means "
            + "every ratio in the division is superparticular, of the form (n+1):n - the form "
            + "Ptolemy insists on. Seven of his eight genera are epimoric throughout. The "
            + "eighth is the Pythagorean tuning he reports rather than endorses, and it is the "
            + "one that is not.");

        // ---- where the numbers came from -------------------------------------
        //
        // The page is worth nothing if its ratios cannot be checked, and it
        // would be worth less than nothing if this card implied a chain of
        // authority the numbers did not actually come through. They came from
        // two reference tables and an arithmetic identity. That is said here
        // plainly, with the standard editions named as what to consult rather
        // than as what was consulted.
        Heading("Where the numbers came from");

        Source("The text",
            "Claudius Ptolemy, Harmonics, Alexandria, c. AD 150 - the genera at I.15-16 and "
            + "II.14, which also preserve the divisions of Archytas, Eratosthenes and Didymus. "
            + "The standard translations are Jon Solomon, Ptolemy Harmonics: Translation and "
            + "Commentary (Brill 2000), and Andrew Barker, Greek Musical Writings II: Harmonic "
            + "and Acoustic Theory (Cambridge 1989).",
            null);

        Source("The standard catalogue",
            "John Chalmers, Divisions of the Tetrachord (Frog Peak, 1993) - chapter 2 for the "
            + "arithmetic tradition, chapter 9 for the catalog itself. Scans of the original "
            + "edition are hosted at Dartmouth.",
            "https://eamusic.dartmouth.edu/~larry/published_articles/divisions_of_the_tetrachord/");

        Source("What was actually read, and what was not",
            "The twenty divisions in this page were taken from two independent modern "
            + "reference tables which agree completely on every ratio, attribution and "
            + "ordering, and each one was then checked to span exactly 4:3. Chalmers and the "
            + "translations above are named as where to settle a disputed number, not as the "
            + "route these numbers travelled - the Chalmers PDF could not be opened when the "
            + "page was built. The research document in the page's own repository tags every "
            + "figure with the source it came through, and says the same thing at more length.",
            null);

        Muted(
            "Ordering matters more than it looks, which is why it is recorded as a sourced "
            + "fact rather than assumed. Didymus's diatonic and Ptolemy's tense diatonic are "
            + "the same three ratios; the order of two of them is the only thing that "
            + "distinguishes the pair, and they are the most confusable in Greek harmonics.");

        Heading("The sound itself");
        Paragraph(
            "No audio is shipped and none is downloaded. Every note is built from its ratio at "
            + "the moment it sounds, as a plucked string with its upper harmonics - a plain "
            + "sine tone would hide the beating, which is the thing worth hearing. The page "
            + "makes no network requests of any kind and carries no fonts, so it works with "
            + "the machine offline, exactly like the mechanism and the planetarium beside it.");
    }
}
