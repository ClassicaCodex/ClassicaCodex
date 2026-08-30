# Classica Codex 3.3.1

A point release that finishes something 3.3.0 started. If you have 3.3.0, this
adds table exports across the result screens and changes nothing else — no
schema change, no migration, and your library file is untouched.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

Coming from 3.0.1 or earlier? Read the [3.3.0
notes](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/v3.3.0)
first — that release carries three versions of work and migrates your database
from schema 14 to 34, and it asks you to back the file up before the first
launch. That advice still stands; this release adds nothing to it.

---

## Every result table can be taken away as a table

3.3.0 gave the collation window CSV, tab-separated and Excel export. It turned
out the rest of the app had a gap in the same shape, and a subtler one than
"some screens cannot export".

Twelve result lists could already **Export All Passages** — plain text, Word or
PDF, citations intact, the format you want when you are going to quote
something. Four screens could export a **table** — rows to sort and chart. The
two had simply never been laid over each other. So bookmarks could be written
out as a document but not as rows, and the one screen showing saved stylometry
results could not be exported at all, even though every validation bench built
to check those results could.

| Screen | Before | Now |
| --- | --- | --- |
| Core vocabulary | nothing | table |
| Saved stylometry runs | nothing | table |
| Concordance | passages | passages **and** table |
| Bookmarks | passages | passages **and** table, with your note and the date |
| Tagged passages | passages | passages **and** table |

The table exports are added to the menu each list already had rather than
replacing it, so nothing that worked before has moved.

## What the screen shows is not always what gets written

Three of those screens round or trim for display, and exporting the visible
cells would have quietly handed over the rounded version:

- **Core vocabulary** shows coverage as `62.4%` in a narrow column. The export
  writes the computed fraction, so a spreadsheet can sort and chart it without
  the percent sign being stripped first.
- **Saved stylometry runs** round the Delta floor to three decimals, purity to a
  whole percent, and z to two. Handing an analyst those and letting them
  recompute is how two slightly different answers to one question get into
  circulation — this project has lost time to exactly that before.
- **The concordance** truncates the left context so the keyword stays aligned
  down the page, which is the whole point of a KWIC view. The export carries the
  full context either side.

Every export carries a short header saying what the table is, what it was
filtered to, and anything that qualifies it — a vocabulary list records how many
running words have no lemma data at all and can never be covered by learning
headwords from it, because a coverage figure read without that promises more
than it can deliver.

Asking to export a table with no rows in it now says so, instead of writing a
file that looks like an answer and contains none.

## Validation

Build on .NET 8, 0 errors. **572 tests passing**, 0 failed, 0 skipped.

**Not verified:** the save dialogs have not been driven end to end on every one
of the five screens.
