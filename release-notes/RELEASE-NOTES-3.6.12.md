> **Historical.** These are the notes for Classica Codex 3.6.12, which is no longer
> available for download. Only the current release is published; get it from
> [the releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).
> Any download link or SHA-256 below refers to the 3.6.12 ZIP, not to the current
> one, so do not check a current download against a checksum printed here.

# Classica Codex 3.6.12

Translating stops being slow, and translations you write yourself become
searchable. Both are consequences of the same mistake, made twice.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Saving a translation took over a minute a time, and got worse as you went

Create Translation saves after every batch, deliberately, so that a long run
that dies partway doesn't lose everything before it. It saved by clearing the
edition's lines and inserting the new set — the obvious way to make the stored
lines match the current ones, and the way that gives every line a **new
identity** each time.

The word index is keyed on those identities. So every save orphaned all of
this edition's index rows, and clearing the orphans meant searching the index
for rows belonging to lines — which is the one thing it cannot do. `WordIndex`
is keyed on *(word, line)*, so a lookup by line alone makes SQLite examine
every distinct word: on this corpus, **2.2 million probes per line**.

Measured on a full library, 70.8 million index rows:

| | |
|---|---|
| clearing an edition of 3 lines | **3.97 s** |
| clearing an edition of 8,088 lines | **did not finish in 15 minutes** |
| one save of a 100-line translation | **75.1 s** — once per batch |

And it grew as the translation got longer, because every batch re-indexed
everything translated so far. SQLite allows one writer at a time, so the whole
application was locked out of its own library while this ran.

A line now keeps its identity across saves, matched on its citation reference,
and only the lines that actually changed are touched. The same 100-line
translation, saved in batches of twenty against the same 70.8-million-row
index:

| | per batch |
|---|---|
| before | 75.1 s, growing |
| **after** | **4–6 ms, flat** |

Flat because the cost now follows what you just wrote, not how much you have
written.

> **Correction, added in 3.6.14.** Every timing on this page was a single
> measurement, and re-measuring on a byte copy of the same library — replaying
> the exact save sequence rather than its parts — gives different numbers. The
> old path cost **102 s** for one isolated save of 100 lines, and 22, 56, 103,
> 133 and 171 s across five cumulative batches; the new path costs about
> **11 ms** per batch of twenty, with 48 ms on the first while caches warm.
> What the table was claiming — that the cost stops growing with the length of
> the translation — holds, and a re-save with nothing changed is under a
> millisecond. The individual figures were too flattering in both columns.

## Translations you wrote yourself were not searchable

The same fault, quieter, in the hand-written workbench — and worse, because
nothing there updated the word index **at all**.

Whole-word search is the default and it reads that index. So a passage you
translated by hand was saved, appeared correctly in the reader, and could not
be found by searching for any word in it. Editing one made it worse: the line
took a new identity each time and left its previous index rows behind.

Lines written in the workbench now keep their identity when edited, and reach
the index as you write them. Clearing a line still deletes it — that is
deliberate, and it now withdraws the line from the index too.

Both paths keep the existing rule that they will not *create* an index: on a
library that has never built one, whole-word search correctly falls back to
reading the text, and writing a single edition's words into an empty index
would make every later search believe the whole library was indexed when only
this translation was.

## What this does not fix

If you have already written translations on a library with a word index, they
are not in it, and this release does not retroactively add them — it keeps the
index current from here. Rebuilding the word index from the Setup Wizard puts
them in, and also clears the orphaned rows the old save path left behind.

## Checks

**1,145 tests, zero warnings on a clean build**, twelve new. They cover the
part that is easy to get wrong: that a word common to both the old and new
version of a line survives being rewritten, because the withdrawal and the
re-indexing overlap on exactly that word.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  7EF901792A40B7D1CE8AAF6BD82D9DFA3BE33C7E9DF454C4BED7460147BEEB0A
```
