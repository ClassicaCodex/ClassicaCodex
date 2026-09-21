# Classica Codex 3.11.0

Greek harmonic science, on the instrument it was discovered on. A monochord you
can drag a bridge along, and twenty divisions of the tetrachord from Archytas to
Ptolemy — each one playable.

It is the third of a set. The Antikythera mechanism computed the heavens,
Ptolemy's Cosmos explains them, and this is the harmony, by the same author as
the second. It is also the only part of this application that makes a sound, and
that shaped how it is built.

Nothing here changes your data. **No schema change, no re-ingest, no re-index**,
and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

> If you set up a **Latin** library before 3.7.2 and have not re-run the Latin
> Lemma Data step yet, it is still worth six minutes — see the note in the
> README.

## It tells you before it makes a noise

You are reading. You may be in a library, or on a train, or next to someone
asleep. A reading application that suddenly plays a note because you pressed an
unfamiliar button on a toolbar has done something rude, and no amount of good
content afterwards makes up for it.

So there are two gates, and the second is not redundant.

The card that opens first says **"This one makes sound."** above everything
else — above the description, because a warning you meet after you have decided
to press the button is not a warning. The button at the bottom of that card
reads "Open it (it can make sound)", for anyone who read nothing else.

Then the page itself opens **silent**, and stays silent until you press a second
button on it marked "Turn the sound on".

That means you can open it somewhere quiet, read all of it, drag the bridge,
read every ratio in the table, and never make a sound. The ratios, the table and
the diagram all work in silence; only the notes are missing. Somewhere you can
listen, turn it on — a page about music you cannot hear is half a page.

Headphones are worth it for the enharmonic. Its two smallest steps are about a
third of a semitone each, and a laptop speaker will blur them into one.

## What is in it

**A monochord.** One string with a bridge you can drag. The pitch rises as the
sounding length falls — the octave at half the string, the fifth at two thirds,
the fourth at three quarters. That inverse is the whole of the discovery, and it
is why a musical interval is a ratio of two small whole numbers rather than a
matter of taste.

Hold the open string and drag slowly through the fifth. A little either side of
it the two notes beat against each other, a wobble you can count; at exactly
`3:2` the beating stops and the pair locks. That is what a just interval *is*,
and it is why small numbers were trusted over ears.

**Twenty divisions of the tetrachord**, each playable. A tetrachord spans a
perfect fourth, exactly `4:3`; its outer notes are fixed and its two inner ones
move, and where they are put is what makes a genus. Archytas in the fourth
century BC, Eratosthenes, Didymus, and Ptolemy's own eight.

The enharmonic is the one to hear. Its lowest two steps are a pair of
quarter-tones crowded under a wide major third, and it sounds nothing like a
scale — Greek writers were already complaining in Ptolemy's day that singers
could no longer manage it.

**Didymus against Ptolemy.** They divide the fourth with the identical three
intervals and differ only in the order of two of them, which moves the middle
note by a syntonic comma — a fifth of a semitone. The page plays one, then the
other, then both together over a held string, where the disagreement stops being
a fact you take on trust and becomes a slow beat.

Two of the labels in the table are computed rather than asserted, so they cannot
drift from the numbers beside them. **Pyknon** means the two lower intervals
together are smaller than the top one, which is the real structural difference
between the enharmonic and chromatic genera and the diatonic. **Epimoric** means
every ratio in the division is superparticular, of the form `(n+1):n` — the form
Ptolemy insists on. Seven of his eight genera are epimoric throughout; the
eighth is the Pythagorean tuning he reports rather than endorses, and it is the
one that is not.

## Why the arithmetic is checked and the ear is not

A wrong ratio on a page like this does not break anything. It *sounds* — and it
sounds like music, because almost any ratio near the right one does. Nobody
alive can hear `46:45` against `45:44`.

So every division is required to multiply out to exactly `4:3`, which is what a
tetrachord is, and a mistyped ratio almost never still does. Every ratio is also
typed a second time, independently, and compared against the first copy: two
copies of a table disagree loudly, one copy cannot. That is 106 checks before
the page is built, on top of the application's own.

It earned its keep twice while the page was being written. A tense diatonic
recalled as `5/4, 9/8, 16/15` multiplies to `3/2` — a fifth, so not a tetrachord
at all. And the limma and apotome had been filed alongside the commas; at 90.2
and 113.7 cents they are not commas but the two unequal halves the tone divides
into.

**One row cannot be played, and that is deliberate.** The Pythagorean enharmonic
used the ditone `81:64` over a pyknon of `256:243`, but how that pyknon was
divided is not recorded anywhere. It is in the table undivided, marked *not
known*, and there is no button for it. Inventing a plausible split would have
made it the only false line on the page, and it would have been invisible.

## No recordings, no fonts, no network

Every note is synthesised from its exact whole-number ratio at the moment it
sounds, as a plucked string with its upper harmonics — a plain sine tone would
hide the beating, which is the thing worth hearing. There is no audio file in
the download and the build fails if one ever appears, because a recording would
fix the tuning at whatever the person who made it believed, and that is the one
thing this page cannot afford.

Like the mechanism and the planetarium, it is a single self-contained page that
asks the network for nothing and carries no fonts. It works with the machine
offline.

## Also

The Help entry for the Antikythera mechanism had called it "the last button on
the main toolbar" since before Ptolemy's Cosmos was added beside it. Corrected,
and there is now a check that the three companion pages are in the order the
Help text claims.

Ptolemy's Cosmos never got an entry in the README's feature list when it
shipped in 3.10.0. It has one now.

1,470 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  14FCDD3DE9E5C7FE7A0E9021D4B9DF405C6EB6909992B57F390342768795DCAF
```
