# Classica Codex 3.8.0

One new thing: the argument the first audiences had about a work, laid out like
a group chat you are not in.

Nothing here changes your data. **No schema change, no re-ingest, no re-index**,
and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

> If you set up a **Latin** library before 3.7.2 and have not re-run the Latin
> Lemma Data step yet, it is still worth six minutes — see the note in the
> README. That is unfinished business from the last release, not new here.

## Fictional Ancient Reactions

Right-click a work — in the library tree, or in the text you are reading — and
you get the argument its first audiences might have had about it.

The *Clouds* came third of three at the Dionysia of 423. Four Athenians in a
wine-shop that evening are already sure the judges were wrong, and are arguing
about whether a comedy is allowed to put a charge into the city's mouth that no
court would accept. Twenty-four years later a jury of five hundred votes to
execute the man the comedy made a figure of fun, and the defendant tells them
exactly where the picture in their heads came from.

Thirteen debates ship, covering the *Iliad*, the *Odyssey*, the *Clouds* twice,
the *Frogs*, the *Medea*, *Oedipus Tyrannus*, Herodotus, Thucydides, the
*Republic*, the *Aeneid*, the *Metamorphoses*, and Cicero — who turns up as a
critic in two of the others before being put on trial in his own.

### These are fiction, and the window keeps saying so

Most of the speakers are invented: a charcoal-burner from Acharnae with the
Spartans in his vines, a retired chorus-trainer who watches the drill and not
the words, a rower who came back from Sicily, a freedwoman who was in the room
when Vergil read Book 6 aloud and watched Augustus's sister faint. They are
there to carry the concerns of the people who actually watched and read these
works and left nothing in writing.

A banner says so before anything else on the window is readable. Every
speaker's card says so again. **What am I reading?** says it at length.

### The real ones are handled differently

Where an ancient critic's view on a work survives, they appear under their own
name with a **real person** badge — and every line they are given carries the
ancient reference it is paraphrased from. Where your library has that text, the
reference is a link and you can go and read it.

- **Xenophanes**: Homer and Hesiod gave the gods everything shameful among men.
- **Heraclitus**: Homer deserves to be thrown out of the contests and beaten.
- **Plato**: the finest of the poets, the first of the tragedians, and for that
  reason the most dangerous — plus the one line of the *Odyssey* he wants cut.
- **Aristotle**: the *Medea*'s ending comes from a machine; *Oedipus Tyrannus*
  is how a plot should be built, and here is the one thing wrong with it; the
  *Republic* is taken apart point by point from inside the Academy.
- **Aristarchus**: the *Odyssey* ends at 23.296, whatever your copy says.
- **Cicero**: people who call themselves Thucydideans are a new and unheard-of
  class of the ignorant.
- **Dionysius of Halicarnassus**: summers and winters cut the war into pieces.
- **Horace**, **Propertius**, **Agrippa**, **Quintilian**, **Seneca the Elder**,
  **Longinus**, **Plutarch**, **Tacitus**, **Aelian**, **Zoilus** — and
  **Herodotus** and **Ovid**, each defending himself out of his own text.
- **Augustine**, on what the *Aeneid* did to him at school: he wept for Dido
  and not for himself.

Nobody may speak before the work they are discussing was written, or outside
their own lifetime, and no real person may be given a view without a citation.
Those rules are checked by the program on every build rather than by whoever
wrote the content — which caught three genuine errors in it, including a rower
discussing Thucydides a year before the book existed.

A conversation whose speakers could not all have been in one room is labelled
as centuries of reaction rather than as a scene, with the dates between the
turns. Nobody is made to have heard anybody.

### None of it is evidence

It is a way into the argument. If a turn makes you want to read the passage, or
the source under it, or disagree with both, that is the entire point, and every
link in the window is there to let you.

It is deliberately kept apart from the stylometry and attribution tools, and
nothing in it touches your library.

### Write your own

Anything you drop in a `Reactions` folder beside the program is loaded too —
one JSON file per debate, validated on load, skipped with a reason if it does
not pass. See
[docs/reactions-packs.md](https://github.com/ClassicaCodex/ClassicaCodex/blob/main/docs/reactions-packs.md),
which documents the format, the eleven rules and the one trap worth knowing
about.

## Fixed while building it

**Text vanished when you scrolled the debate.** The window applied its scroll
offset with a GDI+ transform, which every shape it draws honoured and every
word it draws ignored — so the bubbles slid up empty, a speaker's name stranded
itself across the bubble above, and a link's label floated outside its chip. It
survived a great deal of looking at screenshots for the simplest possible
reason: at the top of a list the scroll offset is zero, and every screenshot of
a scrolling list gets taken at the top.

**The footer buttons stopped working when the window was narrowed.** An
invisible label sat in front of them and took their clicks — "What am I
reading?" from about 750 pixels wide, Close from about 620.

**The disclaimer lost its last line when the window was narrowed**, which of
everything in that window is the text that most had to survive it.

**Empty space inside a bubble was a live link**, and clicking it closed the
window and jumped to another work.

**Nothing inside the transcript scaled with the display.** At 150% the words
grew and the portraits, gutters and column width did not; at 200% every chip
and badge turned into a hard-cornered box, because whether it drew a pill came
down to the parity of a measured line of text.

## Under it

1,371 tests, up from 1,302. The new ones include a check that scrolling moves
the whole picture and not only part of it, that the disclaimer is intact at
every window width, that every footer button is clickable at every width, and
that no two speakers in one debate look alike or share a colour.

Each of those was checked by putting the bug back and confirming the test
fails — which is not a formality: the first version of the scroll test passed
against the bug it was written for.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  61B7CFE13510DB69BAAD1AD4A3D0E363BB9314371430737BD75FA02F12EAF2E0
```
