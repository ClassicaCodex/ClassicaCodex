# Classica Codex 3.7.1

Corrections to 3.7.0, found by reading it rather than by running into it.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The previous work could come back over the one you opened

Cutting a long work into rows takes seconds, so more than one fill can be in
flight at once and only the newest is wanted. The reader arbitrated that by
asking whoever started a fill whether it was still wanted — but the pane also
starts fills of its own, after a resize or a change of reading size, and those
had nobody to ask. They simply landed, seconds later, on whatever the pane had
become.

So opening a long work and clicking a second one within the next few seconds
could put the first one back, and the pane was then stuck: what was on screen
was not what it thought it held, so nothing could correct it. Copy, Tag,
Bookmark, Translate and Export would act on text from a work you were not
looking at, while the edition name above the pane said otherwise.

The pane decides now, rather than trusting whoever asked. Anything that replaces
what is on screen takes the next turn, and a fill that comes back out of turn
abandons its rows.

## Opening a work was doing the work twice

The reader read its width, spent seconds cutting a work against it, then checked
the width again to see whether the cuts were still good. Filling the pane is
exactly what brings the scrollbar out, and the scrollbar takes seventeen pixels
off the width — so the two readings disagreed on essentially every work long
enough to scroll, and each one quietly queued a complete re-cut of itself.

Measured at six hundred passages: 493ms to cut, then 394ms to cut again. Both
panes, every time, on the commonest thing anyone does here.

It now cuts for the width the pane is going to have. A row is at least one line
tall, so when that floor already overflows the pane the scrollbar is certain
rather than likely, and where it cannot be certain the old behaviour stands.

## The empty pane was dragging the one you were reading

Most works have no translation, so the right-hand pane ordinarily holds a line
of explanation rather than text — and Windows sends the mouse wheel to whatever
the pointer is over. One notch over that half of the window sent the work you
were reading back to its first line.

The reverse was commoner still. Where the two editions have different numbers of
passages — **667 of the 896 works here**, and the Iliad is 15,687 against 425 in
its translation — scrolling past the end of the shorter one pinned it to its last
paragraph and printed that paragraph's reference underneath, which read as the
app agreeing with itself while showing you the wrong place.

Both halves of the sync answered with a row number where they meant "there isn't
one here". They say so now, and both panes leave each other alone.

This was not new in 3.7.0; 3.6.19 did the same thing from the same two gestures.

## One passage could switch off the whole feature for a work

The row splitter asked for a negative length and threw, whenever nothing fitted —
an unbreakable run too tall for a row. The reader catches everything there and
falls back to one row per passage **for the entire work**, so a single passage of
that shape silently took 3.7.0's headline feature down with it for the edition
containing it, and said nothing at all.

One edition in this library reaches it: Optatianus Porfyrius, whose grid poems
are an unbroken run of 1,170 characters. The visible loss there is small, because
those poems cannot be split anyway — but it was a property of the splitter rather
than of that edition, and one prose passage away from being total.

Verified rather than assumed this time: 12,883 passages at three pane widths,
five of them throwing before and returning now, and **not one differing in any
other way**.

## A remembered layout could hide a line

3.7.0 learned to remember where a large work's rows fell, so that reopening
Pliny did not cost twenty seconds again. An entry was accepted if every passage's
pieces still added up to the length that passage has now — which stops it slicing
text that is not there, and is not enough, because the file also holds the height
of each row.

A passage replaced by the same number of different characters passes that check.
Every row is then given a height measured for text it no longer holds, and the
surplus line is drawn outside its row and clipped away with no marker, because a
row under the ceiling does not count as truncated. That is the exact defect 3.7.0
exists to end, coming back through the thing that makes it fast.

An entry now carries a fingerprint of each passage's text, and the key carries
the font's real line height in pixels — the old key recorded the point size,
while everything in the file is pixels, so the display scaling stood between
them and a layout measured at 100% could be accepted at 125%.

Entries in the older format are swept rather than left to sit. Changing the key
changed the filename, so they were never read again and still counted against
the forty the folder keeps; two thirds of the entries in this library's cache
folder were that.

Also fixed, smaller: a placeholder message kept the height it was first shown at,
so narrowing the pane cut off the half of the sentence that says what to do about
it. A single-passage work in a very short pane was cut for a width it did not
have. And the reader's fallback path measured its rows against whatever width
happened to be current mid-insert, which clipped a line off each of them — the
same defect again, by a third route.

## Notes

Three rounds of adversarial review went into this, each one reading the fix the
previous round produced. The second found four defects in the first round's fix,
including one whose improvement never fired in the app at all; the third found
none that a reader could see. Where a fix could not be pinned by a test, that is
said so in the test rather than left implied.

1,284 tests, zero warnings.
