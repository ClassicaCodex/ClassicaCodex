> **Historical.** These are the notes for Classica Codex 3.8.3, which is no longer
> available for download. Only the current release is published; get it from
> [the releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).
> Any download link or SHA-256 below refers to the 3.8.3 ZIP, not to the current
> one, so do not check a current download against a checksum printed here.

# Classica Codex 3.8.3

On any display above 100% scaling, the library's author filter box was drawn on
top of the reader — including the dropdown that names the edition you are
reading.

Nothing here changes your data. **No schema change, no re-ingest, no re-index**,
and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

> If you set up a **Latin** library before 3.7.2 and have not re-run the Latin
> Lemma Data step yet, it is still worth six minutes — see the note in the
> README.

## The filter box over the work name

Reported from a laptop as the author filter box *sometimes* covering the
dropdown with the name of the work in it. "Sometimes" turned out to be the
whole diagnosis.

The main window puts the reader to the right of the library, and it works that
position out again every time the window is resized. The number it used was
320 pixels — measured, like every other coordinate in this application, on a
100% display. But that particular calculation runs *after* Windows has scaled
the window, so the number stays 320 while the library column beside it grows:

| display scaling | library column ends at | reader started at | overlap |
| --- | --- | --- | --- |
| 100% | 310 | 320 | none |
| 125% | 387 | 320 | 67 pixels |
| 150% | 465 | 320 | 145 pixels |
| 200% | 620 | 320 | 300 pixels |

Which one wins where they overlap is decided by the order the two were added to
the window, and the filter box was added first — so the box was painted over the
reader rather than the other way round.

At 125% that is six pixels of the dropdown's leading edge under the filter box,
and a further 52-pixel band under the favourites star just inside it: enough to
notice, not enough to look deliberate, and it was reported in exactly those
terms. At 150% the library column runs 145 pixels into the reader — 134 of them
actually covering the dropdown — and the tree covers a matching strip of the
reading pane below it.

The reader's left edge is no longer a number that happens to sit to the right of
the library. It is worked out from where the library actually ends, which stays
correct at any scaling and stays correct if that column is ever made wider. At
100% it produces the same 320 as before, to the pixel, so nothing moves for
anyone who was not seeing this.

Three smaller versions of the same mistake went with it: the window's right and
bottom margins, the spacing between the seven buttons in the top-right corner,
and the smallest size the reader is allowed to shrink to were all fixed pixel
counts that never grew. And one line that claimed to shrink the library toggle
button was doing nothing except undoing that button's scaling, so it is gone.

## Why 3.8.1 said this could not happen

3.8.1 fixed the same class of bug in the setup wizard, and its notes said a
sweep of every other window had found no other coordinate assigned outside a
constructor — so that was the only place it could happen.

The sweep was real and its result was accurate. Its *method* had a hole. This
calculation does live inside the main window's constructor — it is written there
as a named block of work — but it is not *run* there. It runs on every resize,
which is long after the window has been scaled, and a sweep looking for
assignments outside a constructor will not find it. Stated as a general claim,
it was wrong, and this is what it missed.

The tests added here would have caught it either way, because they measure the
result rather than inspect where the code sits: the reader's left edge is
checked against the library's right edge at 100%, 125%, 150% and 200%, with the
window scaled the same way a high-DPI display scales it. Each of those was
confirmed by putting the old number back and watching three of the four
scalings fail while 100% passed — which is precisely the shape that let this
ship in the first place.

There is also a check that this calculation contains no bare pixel counts at
all, so the next change to it cannot quietly reintroduce one.

## Known, and not fixed here

Column widths in the detail grids — the tables in Compare Saved Runs, Collate
Editions, Core Vocabulary and six other windows — are not scaled by Windows
Forms at all, whatever the rest of the window does. This was measured rather
than assumed: a column set to 200 pixels is still 200 pixels at 150%, while the
text inside it is half again as large, so roughly a third less of each entry
fits. It is cosmetic, nothing is drawn over anything else, and the last column
in each grid already stretches to fill. It is next.

1,406 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  79FE6C8ED03A4E52F2132AEC38774A51C6748C4DBCA758185860F96F46C227CE
```
