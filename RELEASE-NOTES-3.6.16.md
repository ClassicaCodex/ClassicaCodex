# Classica Codex 3.6.16

A responsiveness release, and one thing that turned out not to be about speed
at all: about 169,000 passages in a full library were showing an arbitrary
fraction of themselves.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Long passages were cut off at a height chosen by accident

A list box on Windows stores each row's height in a single byte. Ask it for
more than 255 pixels and it keeps the remainder: ask for 300 and it stores 44,
ask for 21,861 and it stores 101. The reader was asking for whatever the text
needed — up to 75,636 pixels for the longest passage in the corpus — and
getting back a number with no relation to it.

So a paragraph needing twelve lines showed two, while the longer one beneath it
showed all fifteen and looked perfectly fine. That is why this reads as random
rather than as a limit, and it is why it survived so long: 511 pixels happens to
store as 255, so some of the worst cases looked the best.

Measured on a full library at a typical pane width, this affected **7.2% of
source passages and 7.5% of translated ones** — around 169,000 passages, most
of them in exactly the prose you would read at length: Josephus, Augustine's
letters, Pliny, Holinshed. At a narrow pane it is nearer 11%.

Row heights are now clamped to what the control can hold, so a long passage
shows as much as there is room for instead of a slice chosen by modular
arithmetic. **This is an improvement, not a cure.** A passage needing more than
255 pixels still shows only its first ten to fifteen lines, because that is the
control's ceiling. What has changed is that it now shows the *most* it can, and
says so: a row with more text than it can display is marked with an ellipsis in
the corner, and its tooltip points at Copy to Clipboard, which has always taken
the whole passage.

Showing such a passage entire means splitting it across several rows, and the
two panes currently stay in step by row number — a Greek passage becoming three
rows against an English one becoming five would break the synchronised
scrolling that the reader is built around. That is a real piece of work rather
than a patch, and it is written down rather than done.

## Opening a book no longer freezes the window

Because the reader's rows vary in height, the control demands the height of
every row the instant the rows are added — it needs the total before it can size
a scrollbar. So opening a work ran a word-wrap layout per paragraph, all of it
on the thread that draws the window, and the window was dead for the duration.

Measured on a full library at 13pt with the citation margin on, this is the time
the window spent unresponsive when a work was opened, before and after:

| | before | after |
|---|---|---|
| Herodotus, *Histories* | 2,614 ms | **142 ms** |
| Augustine, *Epistulae* | 3,658 ms | **89 ms** |
| Homer, *Iliad* | 1,612 ms | **574 ms** |

And both panes are filled per work, so those are halves.

The measuring now happens on a background thread before the rows are added, and
the fill afterwards is dictionary lookups. Total time is much the same; what
changed is that the window stays alive and repainting through it. Because a row
whose height is missing is still measured the old way, a prewarm that is
skipped, abandoned or interrupted costs the saved seconds and nothing else — it
cannot produce a wrong layout.

Measuring text off the drawing thread is not something Windows Forms documents
as supported, so it was checked rather than assumed: across 6,233 passages
including the two hundred longest in the library, at two pane widths in both
reader fonts, every height computed on a worker thread was identical to the one
computed on the drawing thread — and four threads sharing one font at once
produced no disagreement in 24,132 comparisons.

## Morphology search was a sixty-seven second freeze

Choosing a part of speech and clicking Search could hang the window for over a
minute, long enough for Windows to offer to close the program.

Two separate faults on one line. The search was awaited on the drawing thread —
SQLite's asynchronous methods here run synchronously, so "Searching…" never even
painted — and the query sorted every matching row before discarding all but two
thousand of them. "Every Greek verb" matches 114,458 distinct forms, which fan
out to six million rows, and all six million went through a temporary sort to
return two thousand.

Measured: **67,061 ms before, 203 ms now.**

The sort now happens after the limit rather than before it. For any search that
does not hit the two-thousand cap — which is every narrow or text-scoped search
— the result is identical, because the whole result set comes back either way.
For a search that does hit the cap, you now get a spread across the corpus
rather than the first two authors alphabetically: the same query used to return
2,000 rows from "AA VV" and Achilles Tatius and nothing else, and now returns
rows from 188 authors between Achilles Tatius and Zosimus. The status line says
plainly when there are more matches than were shown, instead of reporting the
cap as though it were the count.

## Smaller things

**The Setup wizard no longer opens broken.** It drew all three of its panels on
top of each other, unreadable, for as long as it took to count the word index —
half a minute on a full library — before rearranging itself into a wizard. It
laid itself out after asking the database instead of before. It now renders
first, then fills in the tick marks when the counts arrive, and the panels start
hidden so no future ordering slip can stack them again.

**Clicking a place on the map no longer stalls.** The search was moved off the
drawing thread some releases ago; the tag lookup beside it was not, and it was
the more expensive of the two. It also asked the question in a way that got
worse the more tags you had — one lookup per tag per result — so it would have
grown into a hang. It now reads the tagged passages once and matches them up in
memory: measured flat at 3–4 ms regardless of how many results the pin has,
against 44–220 ms and rising before.

**Typing in the library filter no longer stutters.** Every keystroke cleared and
rebuilt all 4,769 nodes of the library tree, 149–231 ms each, so a nine-letter
author name queued nine rebuilds. It now waits for the typing to settle. Where
the program clears the filter itself to jump to a work, the rebuild is forced
through immediately — without that the jump would land on a tree that had not
been rebuilt yet, losing both the filter and the destination silently.

## Checks

**1,198 tests, zero warnings on a clean build**, thirty-eight new.

The row-height limit is now arithmetic in a separate file so it could be tested
at all, and its tests include the measured table of what the control used to
store for each height asked — so that 255 is not mistaken later for an arbitrary
number someone picked.

The early exit that skips measuring a passage too long to fit was checked
against the corpus rather than argued: over 44,152 passages at five pane widths
in both fonts, every passage it ruled out did need more room than a row has. The
one passage where it ran ahead of the measurement is Optatianus Porfyrius's grid
poem, whose longest unbroken run is 334 characters — a layout 2,997 pixels wide
in a 380-pixel pane, which Windows declines to break and draws clipped. It holds
more text than it shows either way.

The tag lookup's rewrite was diffed against the old query on the real library at
three result sizes: identical answers, every time.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  1BC8A1EB21EB0C225886665DA8050AB76C9F8C35E47D3711F3C76F02CFE455A2
```
