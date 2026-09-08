# Classica Codex 3.0

Version 1 was a reader. Version 2 made it a library. Version 3 adds manuscripts —
and with them, for the first time, text where the evidence is visible rather than
settled.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`, and a setup wizard does the rest.
Windows will show a blue "Windows protected your PC" box on first run because the
app isn't code-signed; click **More info**, then **Run anyway**.

---

## Medieval Nordic manuscripts

Old Norse, Icelandic, Swedish and Danish texts from the
[Medieval Nordic Text Archive](https://www.menota.org) — Heimskringla, Laxdœla
saga, the Codex Wormianus, the Old Norwegian homily book, Vǫluspá in the Codex
Regius. These aren't printed editions. They're transcriptions of particular
manuscripts, made word by word from the parchment, and they behave differently
from everything else in the library.

**Reading levels.** Menota transcribes at up to three: facsimile follows the page
letter for letter, diplomatic expands abbreviations but keeps the scribe's
spelling, normalised regularises to standard Old Norse orthography. The app reads
one level per manuscript, chosen for coverage, and records which with the edition.
Mixing them would produce a text belonging to no scribe and no dictionary.

This matters most for Stylometry: comparing a diplomatic text against a normalized
one measures spelling habits, not authorship. The import surveys every manuscript
and reports its level before anything is written.

**Editor's Notes.** The apparatus, kept beside the text rather than read as part of
it. Manuscript variants carry the adopted reading, the alternative, and the witness
it came from — AM 63 fol collates Heimskringla against AM 18 fol throughout, 4,157
variants, every one naming that witness. Editorial notes carry ligatures, scribal
corrections, worn passages, missing leaves.

A variant collated from another manuscript is not a word of this one. Reading the
two together would quietly corrupt every word count, search result and frequency
measure built on the text.

**Curating what a manuscript contains.** A manuscript is a physical object holding
whatever was bound into it, not a book with one author. The import shows what it
found in each file and lets you merge divisions into a single work, split them,
retitle them or leave them out before anything is written. Those decisions are
saved beside the manuscript and reused.

## Verification

The apparatus counts were checked against the source files rather than assumed.
AM 63 fol yields exactly 4,157 entries and AM 619 4to exactly 410 — the count of
`<note>` elements each file contains, with nothing lost and nothing duplicated.

Getting there meant fixing things that were silently wrong:

- **73,172 phantom entries.** Möðruvallabók puts a `<note type="location">` inside
  every word holding its position on the page — `114ra410`. All of them were being
  stored as editorial notes. Two manuscripts had an apparatus that was pure noise.
- **Notes describing absences were being deleted for describing absences.** A note
  about a lacuna sits where there is no text to sit on, and the code discarded
  pending notes on any line without text. Lost: the missing leaf between 62v and
  63r, the inserted quire 69r–72v, the worn page, the blank line.
- **Editorial pages cited as manuscript leaves.** Menota marks printed editions'
  pages with `<pb>` alongside the manuscript's own, distinguished by `@ed`. Taken
  indiscriminately, a citation could read `161.11` — page 161 of a book published
  in 1931 — sitting beside `69r.2`, a real folio, in identical format. Holm perg 4
  fol carried 665 edition pages against 258 manuscript ones.
- **Heading apparatus dropped with its heading**, and a container division reading
  its own chapters twice over.

A wrong count is visible. A citation that looks followable and isn't gets written
into someone's notes.

## Drama, in Greek and in English

Cast lists and stage directions were being lost from every play in the library —
Shakespeare and Perseus alike. King Lear's dramatis personae showed two group
labels and not one character; Hecuba's 48 stage directions, which carry the entire
staging, weren't there at all. Both now read.

Where a translator supplied a cast list of their own — Coleridge's for Hecuba —
it goes to Editor's Notes under his name, because that is what it is.

## AI features that show their working

**Cross-Language Echo** now checks its own evidence. When a suggested echo quotes a
word that isn't in the line it cites, the candidate is marked. It's flagged rather
than dropped: a rationale may legitimately reach for nearby context, so the mark
means *check this*, not *this is wrong*.

**Translation** no longer answers from memory. Asked to translate a two-word section
heading, it used to supply a famous fragment by the same poet instead — fluent,
real, and not a translation of anything on screen. The prompt now says the
attribution is context only.

These are mitigations, not guarantees. Every AI rendering in this app carries the
line *a rendering to weigh against yours, not a correct answer*, and it is meant
literally.

## Also in 3.0

- **Compare Translations** wraps text instead of scrolling sideways, timestamps
  multiple AI renderings of the same work so they can be told apart, and exports a
  whole translation from the right-click menu
- **Places Map** shows every place on one footing, and marks passages you've tagged
  where they turn up in the results
- **PDF export** renders elided Greek correctly — `ὅ τ'`, `νῦν δ'` — instead of
  dropping the mark to an empty box

## Upgrading

Existing databases upgrade in place on first launch. Annotations, bookmarks and
tags are carried forward.

If you already have Menota manuscripts ingested, re-import them — the counts above
only apply to a corpus read by this version. Möðruvallabók's apparatus dropping to
zero is the expected result, not a regression.

## Known gaps

- Windows only. It's WinForms; there's no Mac or Linux build.
- The Menota manuscripts have to be downloaded by hand — Menota publishes one file
  per manuscript through a catalogue rather than as an archive, so that setup step
  opens the catalogue and points at a folder.
- The Codex Wormianus import currently covers 11 of its divisions and misses the
  rest, so part of that manuscript isn't reachable yet.
- Diplomatic manuscripts carry a small number of MUFI characters in the Private Use
  Area, which render as boxes without a font like Junicode installed. Under half a
  percent of words in the affected manuscripts, and none in the normalized texts.

---

Built for my own reading, and shared in case it's useful to someone else doing the
same thing. Bug reports and corrections welcome — particularly from anyone who
knows these texts better than I do.

