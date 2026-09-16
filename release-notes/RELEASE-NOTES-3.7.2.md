> **Historical.** These are the notes for Classica Codex 3.7.2, which is no longer
> available for download. Only the current release is published; get it from
> [the releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).
> Any download link or SHA-256 below refers to the 3.7.2 ZIP, not to the current
> one, so do not check a current download against a checksum printed here.

# Classica Codex 3.7.2

What a stranger meets in their first ten minutes, which nobody had ever
actually looked at.

**This one asks something of you, unlike the last few.** It adds a table to the
database — done automatically on first launch, nothing to run — and if you have
a Latin library it is worth re-running one setup step, for the reason under
*Latin words had no grammar* below.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Word Study led with the wrong word

Right-clicking a word shows the headwords it could belong to, and the first one
is selected for you — so whichever led the list was the answer you got, and the
dictionary entry underneath followed it.

The list was ordered alphabetically, which in practice means by character code:
a capital sorts above a lower-case letter, and a rare homograph above the word
actually on the page. Nine of thirty sampled forms led with something the reader
had not clicked on.

Right-click **θεά** in the first line of the *Iliad* and it said θέα, "seeing,
looking at". **ἔχει** led with χέω, "diffuse completely", instead of ἔχω.
**esse** led with "Es", a magistrate who superintended religious exhibitions.
**bello** led with "Bellius"; **regem** with "Rex"; **πόλιν** with a place
called Πόλις.

Three rules now decide the order before anything is selected: the headword
spelled exactly like the word you clicked wins; failing that, a lower-case
headword beats a capitalised one for a lower-case word; failing both, the one
that begins the same way. Checked against the whole library over thirty-three
forms — the wrong leads corrected, every other form unchanged.

## The commonest words in Greek called themselves punctuation

The Greek data marks an unanalysed accent variant with a tag that means
punctuation in the scheme it comes from. Correct there; nonsense here, because
these rows are words. Ten of ἦν's seventeen candidates read "punctuation",
thirteen of οὐ's twenty-seven, eight of μή's ten. Clicking ἦν to find out what
it is told you, nine times, that it is punctuation.

They are dropped now, as long as something analysed survives.

## Latin words had no grammar

Every token in the Latin lemma data carries its full analysis — case, number,
gender, mood, tense, voice, person — in one attribute, and a coarse category in
another. The importer took whichever it found first, which was always the
category. The parse was read out of the file and thrown away on the way past.

So every Latin word in the library was a bare "verb" or "common noun". A Latin
morphology search could never match a case or a tense. And the promise that an
ambiguous form shows all its candidates rather than picking one was true of
Greek only: *nostra*, whose own source file records both readings, showed three
lines that each said "pronoun".

Both halves are kept now. *nostra* gives you ablative feminine singular and
nominative neuter plural, and says which is which.

**If you have a Latin library, re-run the Latin Lemma Data step** from Setup
Wizard — about six minutes. Nothing else needs redoing.

## A cancelled download reported itself finished, for ever

The wizard decided a step was done by asking whether its collection had any
texts at all. One text out of thousands satisfied that.

So an import stopped twenty minutes into ninety — a closed laptop lid, a dropped
connection, a machine you needed back — came up the next day saying **"Already
loaded."** with a tick beside it. You would read for a week on a fifth of a
corpus and find out by searching for a passage you knew was in Perseus and not
finding it.

A step is now recorded as finished only when it finishes, and a step with texts
that never completed says so: *"Partly loaded - the last run didn't finish. Run
it again to complete it."* An existing library is taken as finished, so nothing
you already have asks to be downloaded again.

## Unplugging a drive offered to replace your library

If your library lives on an external disk and the disk is not plugged in, the
app used to go quietly to the setup wizard's welcome screen — and finishing that
wrote the new location over the remembered one, so plugging the disk back in no
longer helped.

It now names the path it cannot see and offers to retry.

## A tag mark was painted over the last word

A tag, bookmark or inquiry mark was drawn at the right-hand edge of a passage's
last line, over whatever was already there. On a line running near the full
width it landed on the final word: measured across the prose authors, between a
fifth and a half of passages, with enough overlap to bury a letter whole —
Ἀγαμέμνων, κούρην and ἄμεινον each came out with the last letter fused into a
blot.

The text itself was never touched and always copied out correctly. It is drawn
after the line's end now, and where there is no room, on cleared ground.

## Running it from inside the ZIP

The README said the app would not start if you ran it from inside the ZIP or
copied the executable out on its own. That was wrong — it starts perfectly well,
and comes up with every toolbar button blank, because the icons live in a folder
beside it. No error, nothing to search for, and the one instruction on screen
pointing at a button that is not drawn.

It says so now, once, and carries on.

## When something goes wrong

Almost everything that fails on a first install is caught and shown in a dialog
— a download that died, an unpack that filled the disk, an import that hit a
locked database — and none of it was written down. Asked for the log afterwards,
you would have sent an empty file.

Those now record what they show. A full disk gets told it is a full disk rather
than "something unexpected went wrong", a locked database says another copy of
the app is probably still importing, and the crash dialog says where to send the
log.

## Notes

The corrections in this release came from reading the app as a stranger meets it
rather than as its author does: the first ten minutes, the accuracy of what it
claims about Greek and Latin, what pixels actually land, and what happens when
things fail. Eight claims in the README were wrong and are now right — including
the first sentence, which said the app runs entirely offline while the Places
Map was fetching photographs from Perseus as you clicked.

Every fix here is pinned by a test that was checked against the change it guards
— applied backwards, to confirm the test fails without it.

1,302 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  F4A61EB299737971E26437A5D543F0B7D8F62854BED8FE9B82DC7F28D011C0B0
```
