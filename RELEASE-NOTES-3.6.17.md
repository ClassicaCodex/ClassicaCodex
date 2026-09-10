# Classica Codex 3.6.17

Find Cross-Language Echo took about five seconds to open. It now takes about
four tenths of one.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The window that was slow to open

Reported from use: every other window had got quicker, and this one still took a
noticeable moment before it appeared. It did — measured on a full library, about
**4.7 seconds**, and up to **8.6** if the passage you had right-clicked was a
long one.

Three separate causes, none of them the thing that looked most likely. The
database query behind the window returns 4,056 works in 7 milliseconds and was
never the problem.

**A list measured all at once instead of as it filled.** Every list in the
program shows a horizontal scrollbar, so that a row wider than the list is not
silently cut off. Switching that on makes Windows measure every row the list is
holding — and this window filled its list of works in its own startup handler,
which runs *before* the theme is applied. So four thousand rows went in, and then
the scrollbar was switched on over all of them at once. Measured: **1,320 ms**
that way, against **263 ms** if the list is built with the scrollbar already on,
where the measuring is spread across the insertions. It scales with the library:
207 ms at 500 works, 603 ms at 2,000, 1,320 ms at 4,056.

**Rows added one at a time.** The same list was filled with one insertion per
row and no batching around it, so the control recalculated its extent on each of
four thousand insertions rather than once at the end.

**The passage you right-clicked was re-inserted.** The window shows that passage
in a read-only box, and the theme sets every box's border afterwards. Windows
recreates a text box's window handle when its border changes, and recreating the
handle re-inserts everything the box holds — so the cost was proportional to the
length of whatever you had right-clicked: 3 ms for a normal paragraph, 164 ms at
41,475 characters, **1,650 ms** for the longest passage in the corpus. That box
is now built with the border it is going to end up with, which makes the theme's
later assignment the no-op Windows makes of an unchanged value.

Where that leaves it, measured the same way:

| passage you right-clicked | before | after |
|---|---|---|
| a line of verse | 4,688 ms | **377 ms** |
| a paragraph of prose | 4,639 ms | **393 ms** |
| a very long passage (41,475 chars) | 6,399 ms | **467 ms** |
| the longest passage in the library | 8,575 ms | **1,145 ms** |

## The same trap, found in five other windows

The border and the scrollbar are both set by the theme *after* a window has
built itself, so any window that fills a list, or puts a passage in a box, before
the theme reaches it pays the same price. Three were filling a picker that way —
Compare Selected, Compare Translations, and Stylometric Fingerprint — and two
were putting a passage into a box that way: Translate, and the window behind
"Start inquiry from this passage". All five now build the control with what the
theme is going to want.

None of those five was reported as slow, and their lists are smaller than this
one's four thousand, so the saving there is tenths of a second rather than
seconds. They are changed because it is the same defect, not because anyone
noticed them.

Both values are named constants now, with the measurements written beside them,
so the next window to be added gets it right rather than rediscovering this.

## Also

Typing in that window's work filter refilled all 4,056 rows on every keystroke.
It now waits for the typing to settle, the same as the library filter does.

## Checks

**1,203 tests, zero warnings on a clean build**, five new. They pin the two
constants to what the theme actually sets — if those ever drift apart, every
window silently goes back to paying for a handle recreation, and nothing else
would catch it.

One measurement caveat worth recording, because it nearly sent this the wrong
way: a control that has never been shown has no window handle, and the theme
skips a control whose handle does not exist. Measuring against a form that was
never displayed therefore reported all of these costs as zero. Every figure above
was taken with the handles forced into existence, as opening the window really
does.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  5B9CD1258A1F410C14703912970AAF45B478FAA60E58198ABF2DBF527359A589
```
