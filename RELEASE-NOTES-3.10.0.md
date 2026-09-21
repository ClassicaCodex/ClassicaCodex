# Classica Codex 3.10.0

A second dial on the toolbar opens a working planetarium of Ptolemy's cosmos.

Nothing here changes your data. **No schema change, no re-ingest, no re-index**,
and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

> If you set up a **Latin** library before 3.7.2 and have not re-run the Latin
> Lemma Data step yet, it is still worth six minutes — see the note in the
> README.

## The theory that explained the heavens

Next to the bronze dial there is now a second one: a night-blue plate with a
small circle riding a large one. It opens a card describing the model of the
heavens Ptolemy set out in the *Almagest* at Alexandria around AD 150, and a
button on that card opens a working planetarium of it in your browser.

The two belong together, and that is the reason this is here at all. One is the
machine that computed the heavens and the other is the theory that explained
them. Cicero draws exactly that distinction in *De re publica* 1.22 — between a
solid star-globe of the old kind, from Thales through Eudoxus, and a sphere with
the motions of the Sun, the Moon and the five wandering stars in it, which the
solid kind could not do. That is the difference between a star-globe and a
planetarium, written down by a Roman in the first century BC, and both passages
are on these shelves.

The Earth sits at the centre. Each planet rides a small circle, the epicycle,
whose centre rides a large one, the deferent. Between them those two circles
produce the thing that broke every earlier model: retrograde motion, where a
planet stops, turns back on itself for weeks, and then goes on. Press **Run** and
watch it happen, or press **Next retrograde** to jump to the moment a planet
turns.

There are three views. **The model** shows one planet at a time with the whole
machine visible — deferent, epicycle, equant, apse line, and the sightline out to
a graduated zodiac ring. **The sky** shows only where each body is on that ring,
with trails, and is where retrograde lives. **The cosmos** shows Ptolemy's
ordering of the spheres.

## The numbers are his

Every epicycle radius, eccentricity, apogee and mean motion comes from Ptolemy's
own tables, to the six sexagesimal places he wrote them in, and the page prints
each one in his notation beside the decimal it became — Mars's epicycle as
`39;30` parts of a deferent radius of `60`, the Sun's daily motion as
`0;59,8,17,13,12,31`.

Nothing is exaggerated to make the loops bigger. Some animations of this model
inflate the epicycle so the retrograde is easier to see, and say so; this one
cannot, because the whole claim being made is that *his* parameters produce the
loop. The trail is a recording of the same arithmetic that is driving the
animation, sample by sample, never a drawn path.

The engine reproduces Ptolemy's own worked example — Toomer's Appendix A,
Example 14, Mars on Nabonassar 886 — at every intermediate step, reaching a true
longitude of `241;34,30` against the `241;35` his tables give and the `241;36` he
recorded observing. His tables are printed to the arcminute, so that is half an
arcminute out: agreement to the precision he wrote down.

Two identities in his tables hold exactly, to the last digit he gives: for
Saturn, Jupiter and Mars, the mean motion in longitude plus the mean motion in
anomaly equals the Sun's, and the epoch longitude plus the epoch anomaly equals
the Sun's epoch longitude of `330;45`. He built the tables that way, because the
sum is the geocentric shadow of a heliocentric fact.

## How wrong it is, and whose fault that is

The page grades the model against modern theory and does not flatter it.

In Ptolemy's own lifetime it places the planets within a degree or two — Mercury,
which he found hardest, is the worst. Today it is out by seven to twelve degrees,
and **most of that is not his geometry**: he has precession at one degree a
century where the truth is nearer 1.38, so after nineteen centuries his whole
tropical frame has slipped about seven degrees and everything slips with it.

A further 1.15 degrees is there before any model error at all. Ptolemy's equinox
observations run about 28 hours late — one of them is 28 hours late on its own —
and that displaces his entire frame. Because every planet in this system is tied
to the Sun, that single observational error reaches all seven bodies. You can see
it: at his own epoch the errors are all the same sign and all about the same
size. There is a checkbox that subtracts it, because the interesting question is
not how wrong Ptolemy was but how wrong his *geometry* was, given the
observations he had.

## The equant

The device that makes the model fit the sky, and the one every critic attacked.
The epicycle's centre rides the deferent, but it does not move uniformly about
that circle's own centre — it moves uniformly as seen from a third point, the
equant, as far beyond the centre as the Earth is on the near side. Uniform
circular motion about a point that is not the centre is not, in the old physics,
uniform circular motion at all, and from the Maragha astronomers to Copernicus
that was the scandal for a thousand years. The card says so, and **The model**
view draws the equant where you can see it.

Mercury gets its own machinery, because Ptolemy's observations convinced him it
came closest to the Earth at two points rather than one. Its deferent centre is
not fixed: it runs backwards round a small circle while the epicycle centre runs
forwards, which gives it two perigees. That is drawn too.

## Where every number came from

The card names its sources rather than gesturing at them: Toomer's translation
over Heiberg's Greek and which books each parameter came from; the standard
commentaries; and the working references the parameters were actually read in
alongside the modern theory they are graded against. Each online source has its
link on its own line, and every one of those links was opened and checked against
the work it claims to be.

## Help

There is a new Help section, **Ptolemy's Cosmos**, next to the one for the
mechanism.

## Known, and not fixed here

**The Moon is not in the planetarium.** Ptolemy's lunar model is his most
interesting and his most awkward — a crank that swings the epicycle in and out to
produce evection, and a third correction that points the epicycle's apsidal line
at a moving point rather than at the Earth. It also has the most famous flaw in
the book: it makes the Moon's distance vary by nearly a factor of two, so its
apparent diameter should visibly double and does not. None of that is
implemented, so the Moon appears in the ordering view and nowhere else, labelled
as not modelled rather than drawn in the wrong place.

**Latitude is not modelled.** Everything here is longitude along the ecliptic.
Ptolemy's Book XIII latitude theory is genuinely messy and adding it badly would
be worse than leaving it out.

**The planetarium's page leaves a copy of itself behind.** It is written to
`%TEMP%\ClassicaCodex\Almagest.html` each time you open it — about 134 KB,
overwritten rather than added to, so it does not accumulate. It is not cleaned up
when you close the app. The mechanism's page does the same.

**The zodiac signs are spelled out rather than shown as symbols.** Not one text
font on a normal Windows install carries a single zodiac sign, and of the seven
planetary symbols only Venus and Mars survive in the common faces — because they
double as the biological signs. A symbol row would have come out with two correct
glyphs and five empty boxes. The planetary symbols on the page are drawn rather
than typed, and the signs are named in words.

## Reporting something that breaks

There is a link on the Help window. If a window looks wrong on your display,
that is worth telling me about — the display-scaling audit only ever tests the
scaling of the machine it runs on.

1,427 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  175AAC7A8DF21513531A53D0493AE6B4BEA5CFAE491298619059569EC6B92E2A
```

That checksum was taken by downloading the published asset back and hashing it,
not from the local build, so it covers the upload too.
