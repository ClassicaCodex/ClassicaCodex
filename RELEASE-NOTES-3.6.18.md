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
  describes — up to **570 ms** on a long passage, now none.

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

## Checks

**1,203 tests, zero warnings on a clean build.** No new tests: this release adds
no new behaviour, only further uses of the two constants 3.6.17 introduced, and
those constants already have tests pinning them to what the theme sets.

The picker fix was measured directly rather than inferred — a bare checked list
holding the real 4,056 works, interleaved runs, best of five, with the window
handles forced into existence as opening the window really does.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  (recorded below once the release is built)
```
