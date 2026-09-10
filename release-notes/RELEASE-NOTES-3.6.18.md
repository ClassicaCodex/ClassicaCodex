> **Historical.** These are the notes for Classica Codex 3.6.18, which is no longer
> available for download. Only the current release is published; get it from
> [the releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).
> Any download link or SHA-256 below refers to the 3.6.18 ZIP, not to the current
> one, so do not check a current download against a checksum printed here.

# Classica Codex 3.6.18

3.6.17 fixed a window that was slow to open, and said it had found the same
fault in five others. It had missed one. This is that one, plus the last small
instances.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Corpus Investigator had all three faults, and was missed

The audit behind 3.6.17 searched for controls created with their type spelled
out — `new ListBox { … }`. Corpus Investigator writes its picker the other way
C# allows, with the type on the left and `new()` on the right, so the search
walked straight past it. It turned out to have every one of the faults 3.6.17
describes, and the largest of them at full size:

- its list of works was filled one row at a time, from the window's own startup
  handler, without the horizontal scrollbar set — so all 4,056 rows were
  measured at once afterwards. Measured on a full library: **307 ms, now 41 ms**;
- typing in its filter refilled all 4,056 rows on every keystroke, undebounced;
- and both of the boxes it shows a passage in were built without the border the
  theme was going to give them, which is the handle-recreation cost 3.6.17
  describes: **176 ms** per box for a 41,475-character passage and **1,643 ms**
  for the longest in the corpus, now **2 ms** and **399 ms**. The window builds
  two of them.

Analyse Parallel Passages builds its passage boxes the same way and is fixed
with it.

## The last two

The author pool in Validation and the work list in the Timeline are filled the
same way. They are small — at most 370 and 223 rows on this library, so around a
tenth of a second each rather than a second — and they were quantified and left
alone in 3.6.17 on the grounds that the release was not worth the churn. Since
this release exists anyway, they are in it.

## What was checked and found to be fine

Two other properties the theme sets could in principle carry the same cost, so
both were measured rather than assumed:

- **`ListView.OwnerDraw`** — the concordance and collation grids — costs
  **nothing** when set over a populated grid. No change needed anywhere.
- **`ComboBox.DrawMode`**, which the theme sets in dark mode only, costs 202 ms
  over a 4,000-item list. No dropdown in the program is filled anywhere near
  that size before the theme reaches it, so again no change.

Both are recorded here because "we checked and it was fine" is worth as much to
the next person as a fix.

## A correction to 3.6.17's figures

3.6.17 said that switching the horizontal scrollbar on over a filled list of
4,056 rows cost **1,320 ms**, and gave a scaling table of 207 ms at 500 rows,
603 ms at 2,000 and 1,320 ms at 4,056. Those numbers were taken on a machine
running a dozen other measurement processes at the same time, and thousands of
text measurements contend badly. Re-measured on a quiet machine, nine
interleaved runs: the same operation is **195 ms** (median 197, max 209), and
the list built with the scrollbar already set is **44 ms** rather than 263 ms.

The ratio 3.6.17 reported is close to right — about 4.4x rather than 5x — and
the fix and its direction are unaffected. The absolute milliseconds were roughly
six times too large. The figures for the text-box border in that release were
re-checked at the same time and stand: 176 ms at 41,475 characters against a
published 164, and 1,643 ms for the longest passage against a published 1,650.

Every figure in *this* release was taken on the quiet machine, and the headline
one was re-measured after the fact to confirm it: 308 ms against the 307 ms
published above, median 309, max 336.

## Checks

**1,203 tests, zero warnings on a clean build.** No new tests: this release adds
no new behaviour, only further uses of the two constants 3.6.17 introduced, and
those constants already have tests pinning them to what the theme sets.

The picker fix was measured directly rather than inferred — a bare checked list
holding the real 4,056 works, interleaved runs, best of five, with the window
handles forced into existence as opening the window really does.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  4AA2D450804E9A6BFD5462F3C9C751E2C59BAF57AAC6771F0D6DCE335DEF72A5
```
