namespace ClassicaCodex.Mac.Features.Instruments;

/// <summary>
/// The card that stands beside the Antikythera mechanism: what it is, how the
/// simulation was made to work, and where every number in it came from. See
/// <see cref="InstrumentCard"/> for why the mechanism itself is a page in the
/// reader's browser.
/// </summary>
internal sealed class AntikytheraCard : InstrumentCard
{
    public AntikytheraCard()
        : base(
            "The Antikythera Mechanism",
            "The bronze calculator from the shipwreck",
            "antikythera", "Antikythera",
            "Antikythera.html", "Antikythera",
            "Open the mechanism",
            "opening the Antikythera mechanism",
            "Couldn't open the mechanism: ")
    {
        Paragraph(
            "A Greek astronomical calculator, recovered in 1901 from a wreck off the island of "
            + "Antikythera and built somewhere around 100 BC. Thirty or so bronze gears behind a "
            + "pair of lettered dials. Turn the crank and it tells you where the Sun and Moon "
            + "stand among the stars, what phase the Moon is in, which month of the Greek "
            + "calendar it is, when the Panhellenic games fall, and when to expect an eclipse.");
        Paragraph(
            "This is a working simulation of it. Every wheel turns at the ratio its tooth count "
            + "demands, and every dial reads what the real one read.");
        Muted(
            "Opens in your usual browser. It carries everything it needs and asks the network "
            + "for nothing, so it works offline.");

        Heading("What you can do with it");
        Paragraph(
            "Drag the crank. One turn is about eleven weeks, because the input wheel has "
            + "forty-eight teeth and drives the main wheel's two hundred and twenty-three.");
        Bullet("The front dial",
            "The Sun and Moon against the Egyptian calendar and the zodiac, with the Moon "
            + "speeding up and slowing down as the real one does. In the open centre, six rings "
            + "carrying a little sphere each: Mercury, Venus, the true Sun, Mars, Jupiter, "
            + "Saturn. A sphere with a ring round it is running backwards through the zodiac.");
        Bullet("The back dials",
            "A spiral of two hundred and thirty-five months, which is nineteen years, and "
            + "another of two hundred and twenty-three, which is how long eclipses take to come "
            + "round again. Fifty-one cells of the lower spiral carry a glyph saying an eclipse "
            + "falls in that month, and at what hour.");
        Bullet("The gearing",
            "The wheels themselves, turning, coloured by whether their teeth were counted on a "
            + "surviving fragment, reconstructed, or are hypothesis.");
        Bullet("Align to the sky",
            "Set the pointers the way an owner would and watch two thousand years of "
            + "accumulated drift collapse to what the gearing itself gets wrong.");

        Heading("How it was made to work");
        Paragraph(
            "The whole machine has one input, and every pointer is that number multiplied by an "
            + "exact fraction taken from the tooth counts. So the arithmetic is done in exact "
            + "fractions rather than decimals, and each train is checked against the whole-number "
            + "astronomical fact it exists to embody: two hundred and fifty-four sidereal months "
            + "in nineteen years for the Moon, two hundred and thirty-five synodic months in the "
            + "same nineteen for the calendar spiral. The test page makes a hundred and five "
            + "assertions and allows the gear trains no tolerance at all. One wrong tooth fails "
            + "it rather than quietly moving a pointer.");
        Paragraph(
            "The Moon does not move at a steady rate, and neither does it here. A pin on one "
            + "wheel runs in a slot on another whose axis is about a millimetre away, and that "
            + "millimetre makes the Moon hurry and lag by six and a half degrees either side of "
            + "its mean place. That is not an approximation of the effect. It is the eccentric "
            + "circle of Greek lunar theory built out of bronze, and the simulation uses the "
            + "published closed form of it rather than a sine wave that would look the same.");
        Paragraph(
            "The planets work the same way. Each of the five devices, however it is built, "
            + "produces the direction of a sum of two turning vectors, one going round once a "
            + "year and the other at the planet's own rate, their lengths in the ratio of that "
            + "planet's distance from the Sun to the Earth's. Written out, that is the "
            + "heliocentric vector sum seen from the Earth. Whoever laid it out had a working "
            + "model of the solar system and no idea they had one.");
        Paragraph(
            "To say how good the machine is, the page needs something better to compare it "
            + "against. The Sun and Moon come from Meeus's series, the planets from the "
            + "long-span Keplerian elements NASA publishes for three thousand years either side "
            + "of now. Both are one to three orders of magnitude better than the bronze, which "
            + "is all the comparison needs. The difference between Terrestrial and Universal "
            + "Time is carried too: at 100 BC it is about three hours, and the Moon moves half a "
            + "degree an hour, so leaving it out would have meant grading the machine against a "
            + "broken ruler.");
        Paragraph(
            "Set fresh against the sky, the machine holds the Moon to a fifth of a degree and "
            + "the six bodies of its Cosmos to about seven. Left running since 205 BC it is a "
            + "hundred degrees out on the Moon - and most of the planets' error turns out not to "
            + "be the gearing's fault at all, but precession moving the whole zodiac out from "
            + "under a scale cut in metal.");

        Heading("How much of it is actually there");
        Paragraph(
            "Less than the pictures suggest, and the page is built to say so. Four of the twelve "
            + "zodiac names are on the bronze. Three of the twelve month names. Twenty of the "
            + "fifty-one eclipse glyphs. One gear of the entire front survives, and what it drove "
            + "is disputed. No crank survives either - only the keyway where the drive went in, "
            + "so the handle you turn is the part we are least sure about.");
        Paragraph(
            "So attested lettering is drawn brighter than lettering we supply, reconstructed "
            + "wheels are dimmer than counted ones, purely hypothetical wheels are outlines, and "
            + "the two wheels that belong to no train anyone has worked out sit at the bottom of "
            + "the gearing view connected to nothing. A reconstruction drawn with the same "
            + "confidence as the surviving bronze is a lie told in good faith.");

        Heading("Where the numbers come from");
        Muted(
            "Nothing in the simulation is from memory. Every tooth count, inscription and "
            + "constant was taken from the published literature, and where the literature "
            + "disagrees - as it does, sharply, on several tooth counts - the disagreement is "
            + "recorded in the source rather than smoothed over.");

        Source("The gearing, and the lunar anomaly",
            "Freeth, Bitsakis, Moussas, Seiradakis, Tselikas, Mangou, Zafeiropoulou, Hadland, "
            + "Bate, Ramsey, Allen, Crawley, Hockley, Malzbender, Gelb, Ambrisco & Edmunds, "
            + "“Decoding the ancient Greek astronomical calculator known as the Antikythera "
            + "Mechanism”, Nature 444 (2006), 587–591, with its Supplementary Notes.",
            null);

        Source("The Games dial and the eclipse prediction",
            "Freeth, Jones, Steele & Bitsakis, “Calendars with Olympiad display and eclipse "
            + "prediction on the Antikythera Mechanism”, Nature 454 (2008), 614–617.",
            null);

        Source("The complete gearing tables",
            "Freeth & Jones, “The Cosmos in the Antikythera Mechanism”, ISAW Papers 4 "
            + "(2012). Open access, and the single most useful source here.",
            "https://dlib.nyu.edu/awdl/isaw/isaw-papers/4/");

        Source("The eclipse scheme and its epoch",
            "Freeth, “Eclipse Prediction on the Ancient Greek Astronomical Calculating "
            + "Machine known as the Antikythera Mechanism”, PLOS ONE 9(7): e103275 (2014); "
            + "and Carman & Evans, “On the epoch of the Antikythera mechanism and its "
            + "eclipse predictor”, Archive for History of Exact Sciences 68 (2014).",
            "https://journals.plos.org/plosone/article?id=10.1371/journal.pone.0103275");

        // ISAW Papers live on the library's digital library, not on the
        // institute's own site. Guessing the other pattern gives a 404, which
        // is exactly what shipped on Windows the first time.
        Source("The epoch dates, reconsidered",
            "Jones, “The Epoch Dates of the Antikythera Mechanism (With an Appendix on its "
            + "Authenticity)”, ISAW Papers 17 (2020). Open access.",
            "https://dlib.nyu.edu/awdl/isaw/isaw-papers/17/");

        Source("Every inscription, in Greek",
            "The six papers of “Inscriptions of the Antikythera Mechanism”, Almagest "
            + "7.1 (2016), by Bitsakis, Jones, Anastasiou, Moussas, Tselikas, Zafeiropoulou and "
            + "Steele - the critical edition. The sixth festival on the Games dial is Iversen's "
            + "reading, and it is the reason Rhodes is in the argument at all.",
            null);

        Source("The planets, and the front display",
            "Freeth, Higgon, Dacanalis, MacDonald, Georgakopoulou & Wojcik, “A Model of the "
            + "Cosmos in the ancient Greek Antikythera Mechanism”, Scientific Reports 11: "
            + "5821 (2021). Open access. Almost nothing of this front display survives, and the "
            + "paper says so in its own main text.",
            "https://www.nature.com/articles/s41598-021-84310-w");

        Source("The sky to compare it against",
            "Meeus, Astronomical Algorithms, 2nd edition (Willmann-Bell, 1998), for the Sun, the "
            + "Moon and the Julian day; Standish's Keplerian elements, published by NASA's Jet "
            + "Propulsion Laboratory, for the planets; and the Espenak and Meeus polynomial "
            + "expressions for the difference between Terrestrial and Universal Time.",
            "https://ssd.jpl.nasa.gov/planets/approx_pos.html");
    }
}
