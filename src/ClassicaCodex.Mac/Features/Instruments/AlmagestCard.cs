namespace ClassicaCodex.Mac.Features.Instruments;

/// <summary>
/// The card that stands beside Ptolemy's planetarium: what the model is, what
/// it gets right, what it gets wrong, and where every number came from - the
/// companion to the mechanism: the machine that computed the heavens, and the
/// theory that explained them. See <see cref="InstrumentCard"/> for why the
/// planetarium itself is a page in the reader's browser.
/// </summary>
internal sealed class AlmagestCard : InstrumentCard
{
    public AlmagestCard()
        : base(
            "Ptolemy's Cosmos",
            "The heavens of the Almagest, turning",
            "almagest", "Almagest",
            "Almagest.html", "Almagest",
            "Open the planetarium",
            "opening Ptolemy's cosmos",
            "Couldn't open the planetarium: ")
    {
        Paragraph(
            "The model of the heavens set out in the Almagest at Alexandria around AD 150, "
            + "turning at the rates Ptolemy's own tables give. The Earth is at the centre; each "
            + "planet rides a small circle, the epicycle, whose centre rides a large one, the "
            + "deferent. Between them those two circles produce the thing that broke every "
            + "earlier model: retrograde motion, where a planet stops, turns back on itself for "
            + "weeks, and then goes on.");

        Paragraph(
            "Nothing here is exaggerated to make the loops bigger. The epicycle radii, the "
            + "eccentricities, the apogees, the mean motions to six sexagesimal places and the "
            + "epoch positions are all his, and the page prints each one in the notation he "
            + "wrote it in beside the decimal it became.");

        Heading("The equant, and why it was the scandal");
        Paragraph(
            "The epicycle's centre rides the deferent, but it does not move uniformly about the "
            + "deferent's centre. It moves uniformly as seen from a third point, the equant, as "
            + "far beyond the centre as the Earth is on the near side. That single device is "
            + "what makes the model fit the sky as well as it does, and it is what astronomers "
            + "from the Maragha school to Copernicus objected to for a thousand years: uniform "
            + "circular motion about a point that is not the centre is not, in the old physics, "
            + "uniform circular motion at all.");

        Heading("How wrong is it");
        Paragraph(
            "Against modern theory, in Ptolemy's own lifetime, the model places the planets "
            + "within about a degree or two - Mercury, which he found hardest, is the worst. "
            + "Today it is out by seven to twelve degrees, and most of that is not the geometry: "
            + "he has precession at one degree a century where the truth is nearer 1.38, so "
            + "after nineteen centuries his whole frame has slipped.");

        Paragraph(
            "A further 1.15 degrees is there before any model error at all. Ptolemy's equinox "
            + "observations run about 28 hours late, which displaces his entire tropical frame - "
            + "and because every planet in the system is tied to the Sun, that one observational "
            + "error reaches all of them. The page will subtract it for you, because the "
            + "interesting question is not how wrong Ptolemy was but how wrong his geometry was, "
            + "given the observations he had.");

        Heading("In this library");
        Paragraph(
            "Cicero draws exactly this distinction in De re publica 1.22 - between a solid "
            + "star-globe of the old kind, from Thales through Eudoxus, and a sphere with the "
            + "motions of the Sun, the Moon and the five wandering stars in it, which the solid "
            + "kind could not do. That is the difference between a star-globe and a "
            + "planetarium, drawn by a Roman in the first century BC. The Somnium Scipionis at "
            + "6.17 gives the nine spheres. Both passages are on these shelves.");

        // Every number in the page came from one of these, and the page is
        // worth nothing if that cannot be checked. The primary text is first,
        // then the working sources the parameters were actually read in, then
        // the modern theory the model is graded against.
        Heading("Where every number came from");

        Source("The text",
            "Claudius Ptolemy, Mathematike Syntaxis (the Almagest), Alexandria, c. AD 150. "
            + "Quoted throughout from G. J. Toomer's translation, Ptolemy's Almagest "
            + "(Duckworth 1984; Princeton 1998), with Heiberg's Greek text underlying it. "
            + "The parameters used here are from Books III (the Sun), IV-V (the Moon) and "
            + "IX-XI (the five planets).",
            null);

        Source("The standard commentaries",
            "O. Neugebauer, A History of Ancient Mathematical Astronomy (Springer 1975); "
            + "Olaf Pedersen, A Survey of the Almagest (Odense 1974; rev. Jones, Springer 2011); "
            + "James Evans, The History and Practice of Ancient Astronomy (OUP 1998).",
            null);

        Source("The parameter set, cross-checked line by line",
            "R. H. van Gent, Almagest Ephemeris Calculator (Universiteit Utrecht) - and its "
            + "JavaScript source, which was read directly rather than summarised. Every "
            + "eccentricity, epicycle radius, apogee, mean motion and epoch value in this page "
            + "was confirmed against it.",
            "https://webspace.science.uu.nl/~gent0113/astro/almagestephemeris_main.htm");

        Source("The equant, and Mercury's crank",
            "Dennis W. Duke (Florida State University), An Interesting Property of the Equant; "
            + "Ptolemy's Treatment of the Outer Planets; Almagest Planetary Model Animations. "
            + "The closed form used here for the equation of centre was derived independently "
            + "and then checked against Duke's animation source and against Fitzpatrick.",
            "https://people.sc.fsu.edu/~dduke/models.htm");

        Source("An independent derivation",
            "Richard Fitzpatrick, A Modern Almagest: An Updated Version of Ptolemy's Model of "
            + "the Solar System (University of Texas at Austin), sections 4.2-4.4.",
            "https://farside.ph.utexas.edu/Books/Syntaxis/Almagest.pdf");

        Source("The acceptance test",
            "Toomer's Appendix A, Example 14 - Mars at Nabonassar 886, Epiphi 15/16, 9 pm - "
            + "worked through against the printed tables by Jon Voisey, Following Kepler. "
            + "Ptolemy's tables give the true longitude as 241;35 degrees and his own "
            + "observation as 241;36. This engine returns 241;34,31, agreeing with his tables "
            + "to 0.8 seconds of arc at every intermediate step.",
            "https://jonvoisey.net/blog/2024/09/almagest-book-xi-calculating-planetary-longitude/");

        Source("The real sky, for grading",
            "Jet Propulsion Laboratory, Approximate Positions of the Planets - Table 2a, the "
            + "fit for 3000 BC to 3000 AD, with the Table 2b terms for Jupiter and Saturn. "
            + "Stated accuracy over that span is 20 arcseconds for Mercury and about 600 for "
            + "Saturn, against Ptolemaic errors measured in degrees - so the ruler is two "
            + "orders of magnitude finer than the thing being measured.",
            "https://ssd.jpl.nasa.gov/planets/approx_pos.html");

        Source("Delta T",
            "Fred Espenak and Jean Meeus, Polynomial Expressions for Delta T, after Morrison & "
            + "Stephenson (2004), published by NASA. Terrestrial time ran about 2 hours 34 "
            + "minutes ahead of universal time at AD 137; ignoring that would displace the Moon "
            + "by more than Ptolemy's own lunar error and make his good longitudes look bad.",
            "https://eclipse.gsfc.nasa.gov/SEcat5/deltatpoly.html");

        Source("Cicero",
            "De re publica 1.21-22 and 6.17, from the Latin Library text. Section numbering of "
            + "the Somnium Scipionis varies between editions; some cite 6.17 as 6.16.",
            "https://thelatinlibrary.com/cicero/repub1.shtml");

        Muted(
            "The full research behind the page - eight documents, every figure carrying a "
            + "provenance tag and a source - lives with its own repository rather than here. "
            + "Where sources disagree, the page follows Toomer and says so.");
    }
}
