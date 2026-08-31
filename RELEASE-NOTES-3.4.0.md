# Classica Codex 3.4.0

The result of an audit rather than a plan. The application was read end to
end and, more usefully, run — its own TEI parser over all 2,299 Greek and
Latin files on disk, its own ingest into a throwaway database, its own word
index over a full library — and the findings measured before and after being
fixed.

**The headline is that a large part of the Latin corpus was never being
ingested and nothing said so.** If you have a Latin library, re-run the Ancient
Latin Texts step: it will find roughly 40% more editions than it did last time,
including six authors that were not in your library at all.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

**This release migrates your database to schema 36 and clears the word index.**
Search still works without it, but until you rebuild it from the Setup Wizard,
whole-word search falls back to matching the spelling as typed and loses its
accent-insensitivity. Back the file up before the first launch, as always.

Coming from 3.0.1 or earlier? Read the [3.3.0
notes](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/v3.3.0)
first — that release migrates from schema 14 and carries three versions of work.

---

## 197 Latin editions were being dropped in silence

Perseus ships a `__cts__.xml` catalogue in each author and work folder, naming
what is inside it. The ingest read that catalogue to learn the author's name and
the work's title, and where there was no catalogue it moved on — without
recording anything, because the two lines that did the moving on were `continue`
statements and nothing else.

`canonical-greekLit` catalogues every folder, so this never showed. It ships
1,612 files and ingests 1,612. `canonical-latinLit` leaves 65 of its 399 work
folders and six of its author folders with no catalogue at all, and those
folders hold 197 edition files — 36.8 MB of text that went nowhere while setup
reported "Done — ready."

What was missing entirely, in no edition:

| | |
| --- | --- |
| **Bede** | *Historia ecclesiastica gentis Anglorum*, Latin and English |
| **Cato the Elder** | *De agri cultura* |
| **Apicius** | *De re coquinaria* |
| **Sidonius Apollinaris** | *Carmina* and *Epistulae* |
| **Augustine** | *Select Letters* — two Latin editions and an English |
| **Appendix Vergiliana** | all eleven poems: *Dirae*, *Lydia*, *Culex*, *Copa*, *Catalepton*, *Moretum* and the rest |
| **Petronius** | *Fragmenta* and *Poemata* |
| **Livy** | the *Periochae*, the fragments, and four of the six editions Perseus carries — the library held one Latin text and one English where six exist |

A folder without a catalogue is now reconstructed from the files in it. The
work's identifier comes from the edition filenames, which in Perseus *are* the
CTS identifier; the title and the author come from the TEI header, which states
both. Where the files name no author — the Appendix Vergiliana names none, and
correctly, since it is *carmina minora Vergilio adtributa*, poems merely
attributed to Virgil — the printed collection they were digitised from is used
instead, which is the next most specific thing the files actually say. Nothing
is invented: a folder that cannot be named from its own contents is skipped and
reported, rather than skipped.

The Latin corpus now ingests **687 of 687 files**. Three are still refused, and
those three are genuinely malformed XML in the Perseus source — they are named
in the setup report now instead of vanishing.

This had to be done without breaking the opposite case. A missing catalogue is
also what keeps First1KGreek's `save/`, `split/` and `volume_xml/` working
directories out of the corpus, and those hold the same texts as the textgroups
they were derived from. Recovering them the same way would have ingested a good
part of that corpus twice over, and duplicate texts do not make a Burrows's
Delta run fail — they make it confident and wrong. The guard is the folder's own
name: a real textgroup folder is named for the identifier its files carry, and a
working directory is not.

## Petronius was missing a fifth of the Satyricon

The parser recovers 99.92% of the reading text across the 194.8 million letters
of the Greek and Latin corpora, which is where it should be. One file was well
below that.

The *Satyricon* encodes each section as a block of prose with its verse quoted
inline. The parser saw the quoted verse, decided the block was a container
worth descending into, emitted the poems and dropped everything around them —
34,097 letters, the narrative. What survived was the verse, which is the one
part of the Satyricon nobody reads it for.

This is the third instance of one mistake. A block that holds nothing reachable
must be emitted whole or its words are lost — that rule recovered King Lear's
cast list and 42,448 speakers. A block that holds something reachable must be
descended into. A block that holds *both* needs both, and it was getting
neither. It now walks its own contents in order, so the prose comes back where
Petronius put it rather than gathered ahead of the verse.

Measured across all 2,296 parseable files, reading text lost to this is now
**zero letters**, and no edition gained a character it did not have.

## The word index is 55% smaller

The `WordIndex` table was an ordinary table with a covering index over both of
its columns. Every query used the index and the base table was never read, so
the whole index was stored twice and every build paid to fill both.

Making the pair the table's primary key stores it once. Over a full library —
26,723,817 entries from 1,085,843 lines:

| | on disk | build |
| --- | --- | --- |
| before | 1,054 MB | 263.7 s |
| after | 474 MB | 223.0 s |

The word index was the largest single object in a finished library, larger than
every text, dictionary and apparatus entry put together. A complete install goes
from about 1,692 MB to 1,107 MB.

## Metre, in Word Study

The hexameter scanner added in 3.3.1's development had no way in. It does now,
and it answers a question no dictionary can.

Latin editions print no macrons, so the letters of a word frequently do not say
which word it is. `cano` is the first word of the *Aeneid*; its final o is long,
which makes it the first person of a verb. `puella` and `puellā` are nominative
and ablative and differ in nothing an edition prints. The metre settles both.

Word Study now carries a row under the source line showing the line's feet and
how far its reading is settled, and — when you pick a word — what the metre
makes of that word's syllables. Measured over 33,114 verse lines of Virgil,
Ovid, Lucretius and Juvenal, the metre settles **75.1% of the syllables the
spelling leaves open**, and 74.8% of words in a scanned line come out with every
syllable determined.

The marks are on syllables, not vowels, and the row says so: `arma` opens with a
long syllable containing a short a — long by position, before `rm` — and a
macron over that vowel would teach a reader something false about the word.
Where the surviving readings disagree, or where a syllable is the last of its
line and free by convention, the mark says the metre does not know rather than
picking the likelier answer.

The row appears only for Latin lines the markup calls verse, which leaves Greek,
the translations and all prose exactly as they were. Horace's Odes are Latin
verse and are not hexameter; they report that, on 98% of their lines.

## Citations are citations again

Perseus puts the full CTS identifier in the markup for most of the corpus, so
the reference stored against a passage reads
`urn:cts:greekLit:tlg0012.tlg002.perseus-grc2.1.1` rather than `1.1`. Storing
that is right — it is the durable key that tags, bookmarks and inquiries hang
on, and it survives a re-ingest. Showing it was not.

It had reached the tooltip on every line, search results, the bookmark list, the
concordance, tag and echo browsers, export headers, the filename an export opens
with, and the citation sent to Claude or Gemini in a translation prompt. An
exported PDF arrived called `Homer - Odyssey urn_cts_greekLit_tlg0012.tlg002.
perseus-grc2.1.1.pdf`, and that file is the one that ends up in somebody's
essay. It now says *Od.* 1.1, in all sixty places, and the stored form is
untouched.

## Smaller things

- **An AI translation could be cut off and presented as finished.** The request
  capped the reply at about 4,000 characters of English and never checked
  whether the model had reached that cap. 1,050 passages in a full library are
  longer than that and Apuleius *Metamorphoses* 5.2 is 41,475 characters — a
  reader asking for that got the first twelfth of it, ending mid-sentence, with
  nothing to say so. The cap is now a backstop rather than a budget, and a reply
  that hits it is reported as incomplete.

- **1,679 editor's notes could not be reached.** A note sitting between the
  divisions of a text was given a citation of its own that no passage answered
  to, so the Editor's Notes pane — which looks its entries up by the line you
  are standing on — could never show them. Across 116 editions, including the
  French chapter summaries of Thucydides. They are now carried to the next
  passage, the way notes inside a line already were.

- **A malformed catalogue file could abandon an ingest.** One unparseable
  `__cts__.xml` would have thrown out of a run that has no handler above it
  until the setup step, losing every author sorting after it alphabetically.
  None of the 1,314 catalogues in the Perseus repositories is malformed today,
  which is the kind of fact that holds until it doesn't.

- **The Myth Network and Timeline leaked a font handle on every repaint** while
  the pointer moved over them. The map canvas had always got this right; those
  two were the copies that hadn't.

- **The Guided Setup said the download was "a few hundred megabytes and a few
  minutes."** It is about nine gigabytes and the better part of an hour, mostly
  unattended, which is what it says now and what the README always said.

## Under the hood

- 41 new tests, 667 passing.
- Schema 36. The migration drops and recreates the word index rather than
  copying 26 million rows into a new table, which would need room for both at
  once. It is derived data and the Setup Wizard already reports when it needs
  building.
- Setup now distinguishes files it *skipped* from folders it *named from their
  texts* — nothing was lost in the second case, but a title came from the file
  rather than the catalogue and may not be the canonical one.
