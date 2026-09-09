# Classica Codex 3.6.15

One fix, found by using the program: exporting a passage with its translation
included far more of the translation than you asked for.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Exporting one passage exported the whole translation

Tick "include the translation", export a single passage, and the file came out
with several translated passages in it. The report was generous: both export
layouts took the **entire** counterpart edition, however little had been asked
for.

The combined layout joined every passage in the translation into one block. The
interleaved layout swept up everything standing before the first match, emitted
the match, and then made a final pass over the whole counterpart edition to pick
up "whatever's left". Export one passage of a fifty-passage work and you got its
translation entire — the reader only saw a handful because the work was short.

The behaviour is right, and was written deliberately, **for a whole-work
export**. A translation carries material the original does not divide the same
way, or at all: a translator's introduction, a cast list, a chapter heading over
sections the original numbers one by one. None of that pairs with anything, and
a bilingual edition of a whole work that silently dropped it would be missing
the translation's own front matter. What was missing was the distinction between
that case and every other.

Now: a whole-work export still takes everything. Any narrower scope takes the
passages the exported lines actually resolve to, in reading order, each once,
and nothing else. The status line beneath the preview says which of the two you
are getting rather than asserting the first.

Three things that were already right and stay right. A coarse reference against
a finer translation still gathers the finer passages beneath it — asking for
section 1.2 where the translation has 1.2.1 and 1.2.2 gets both, because that is
the alignment working. A translation passage covering several exported lines
appears once, not once per line. And a line with no counterpart still exports
alone rather than being given something from nearby.

## Checks

**1,160 tests, zero warnings on a clean build**, nine new. The first of them is
the report itself reduced to a single assertion: export one passage, get one
translated passage.

The decision about how much of a translation belongs in an export is now a
separate function so it could be tested at all. Nothing else in that method can
be — it needs a live window.
