# Classica Codex 3.7.0

Long passages are shown whole. They never were before.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## A paragraph no longer stops where the row runs out

A row of a Windows list cannot be taller than 255 pixels. The control keeps each
row's height in a single byte, and that is not a setting — it is the ceiling.

About **seven per cent** of the passages in a full library want more than that:
roughly **169,000** of them at a typical pane width, and nearer eleven per cent
if you read in a narrow pane. Josephus, Augustine's letters, Pliny, Holinshed —
the prose you would sit down with.

Until 3.6.16 those passages were given a height of `height % 256`, so a
paragraph wanting 300 pixels was drawn in 44 of them and showed two lines of
twelve. 3.6.16 clamped that to the tallest a row can be and marked the shortfall
with an ellipsis, which was honest but still showed you ten lines of a
twenty-line paragraph.

Now a passage too tall for one row is drawn across as many rows as it needs, and
the ellipsis is gone.

The rows of a passage put back together are that passage, character for
character — that invariant is what the whole change rests on, and it is pinned
by tests. Cuts fall only at spaces, so no word is ever broken across a row. A
single unbroken run longer than a row — Optatianus Porfyrius's grid poem has one
of 334 characters — is left whole rather than chopped, because inventing a line
break the edition does not have would be worse.

Verified against a full library: every passage intact, and not one row over the
ceiling, read back from the control's own stored height rather than from what we
asked it for.

Verse is unaffected — a line of Homer was always one row and still is. Prose
works grow by about a quarter to seven tenths in row count; the worst single
passage in the corpus becomes nine rows.

## Reading a long work got much faster on the way

Dividing passages meant measuring more of them, and measuring text costs about a
millisecond a row whatever the row says. Pliny's *Natural History* is about
17,900 rows across its two panes, so the first attempt at this took **sixty
seconds**. That was not shippable, and fixing it turned up two things that had
been costing time all along.

The layout of a large work is now remembered between launches — where its
passages were cut, and how tall each row came out. Only works that actually
proved slow are kept, and the program works that out by timing them rather than
guessing from length: fifteen thousand lines of verse cost less than three
thousand paragraphs of prose. Most of the library never touches it.

And the reader had been handing the list control a copy of every row's full
text, which it keeps and charges more than linearly for — 42 milliseconds per
thousand rows at ten characters a row, 1,300 at twelve hundred. Nothing you see
came from that copy; the panes are drawn from the text directly.

Reopening Pliny the Elder, both panes:

| | |
|---|---|
| before this release | — (it could not show the text at all) |
| first attempt at splitting | ~60 s |
| after remembering the layout | ~4 s |
| now | **~0.4 s** |

## Everything that had to change with it

Dividing a passage breaks anything that assumed one row meant one passage, and
most of this release is that:

- **The panes stay in step by passage now, not by row number.** Row number was
  standing in for passage number and stopped meaning it. Worth saying plainly:
  the old row-number linking was already wrong for **671 of the 897** works that
  fill both panes, because two editions rarely divide a text the same way. That
  is a separate fault and is *not* fixed here.
- **Tags and bookmarks** are drawn after a passage's last row rather than folded
  into its text, so marking a passage still works while it is on screen.
- **The reference in the margin** is printed against the row a passage starts
  on. It would otherwise have printed four times down a passage divided four
  ways, telling you it was four passages.
- **Resizing the window or changing the reading size re-cuts the text**, because
  where the cuts fall depends on both, and your place is kept by passage rather
  than by row number — the row count itself changes.
- Clicking, tagging, translating or jumping to a passage works from any of its
  rows.

## Also

**A work that has no text now says why.** Augustus's *Res Gestae* is named in the
catalogue and no text for it was ever published in the data; the reader used to
say "no edition ingested", which sounds like something failed. Two works in the
corpus are like this, the other being Cicero's *De Legibus*.

## Checks

**1,267 tests, zero warnings on a clean build**, forty-five new since 3.6.19.

The reader's own tests run on a thread with a real message loop, because a
control on a test runner's thread has no window handle and measures nothing —
it would pass without testing anything.

A caveat worth stating rather than burying: a screen reader reading a row now
gets its first 120 characters rather than the whole row. That is what bought the
speed above, and it can be given back properly; say so if you want it.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  (recorded below once the release is built)
```
