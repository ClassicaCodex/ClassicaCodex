> **Historical.** These are the notes for Classica Codex 3.6.13, which is no longer
> available for download. Only the current release is published; get it from
> [the releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).
> Any download link or SHA-256 below refers to the 3.6.13 ZIP, not to the current
> one, so do not check a current download against a checksum printed here.

# Classica Codex 3.6.13

**If you have 3.6.12, replace it.** That release could delete passages from a
translation, and 3.6.12 has been withdrawn.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## A citation reference can appear twice, and 3.6.12 assumed it could not

3.6.12 changed how a translation is saved: instead of clearing the edition's
lines and writing them again, it matches the stored lines against the wanted
ones by citation reference and updates them in place. That is what made saving
fast — 75 seconds a batch down to 4 milliseconds — and it rests on one
assumption that is not true.

A citation reference can repeat. The index on `(EditionId, CitationRef)` is
deliberately not unique, and real corpora exercise that: the Menota eddic poems
carry `1.45.10` and `1.45.11` twice each, with different text. 3.6.12 kept one
line per reference and treated every other as a line that should no longer
exist. So a duplicate you already had became a deletion the moment you saved —
and *which* of the two survived was decided by whichever index SQLite chose to
scan, because the read had no ordering.

On a library holding an existing AI translation of those poems, one save would
have taken it from 519 lines to 517, with nothing on screen to say so.

Two more of the same shape:

- **Clearing a line** deleted every stored line for that reference but reported
  only one. The others' index entries were left pointing at a passage that no
  longer existed — and SQLite reuses row identifiers, so in time those words
  would have surfaced in an unrelated passage's search results.
- **Writing a translation** over a reference that had two lines destroyed one
  of them.

Saving now matches the *n*th wanted line against the *n*th stored line for a
reference, in a defined order, so duplicates are preserved on both sides. A
clear reports every line it removes. Writing a translation applies it to every
line carrying that reference rather than deleting one — one translation per
reference, and nothing thrown away to achieve it.

## The one method that deletes passages now checks what it was given

Clearing a line in the workbench deletes a passage. Nothing reachable through
the interface can point that at the wrong edition — the workbench is only ever
handed the translation you own — but the only thing enforcing it was a
substring of an identifier chosen by the caller, and a test harness in this
project once handed it a source edition and deleted three passages of a real
library.

It now reads the edition's kind first and refuses anything that is not a
translation. One extra lookup, and that particular accident cannot happen
again.

## Checks

**1,151 tests, zero warnings on a clean build**, six new. Five of them cover
duplicated references, and I checked they do their job rather than assuming it:
with only the repository fix reverted, four fail and pass again with it
restored. The sixth asserts that writing to an original edition is refused.

Also from this review: a doc comment on the word index's whole-edition delete
still described the behaviour 3.6.12 removed, and was shipping that way in the
built documentation. Corrected.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  8DAC17A18DB076FEE7EA3EF5E91FE75114398A23A53CD01048A10AFDC69AFAF6
```
