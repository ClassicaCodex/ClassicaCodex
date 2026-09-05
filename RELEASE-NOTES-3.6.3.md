# Classica Codex 3.6.3

The reader now says which passage you are on, under each pane.

Small release. Nothing here changes your data: **no schema change, no
re-ingest, no re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The passage you are on

3.6.1 put the reference in the margin, and put it there the way a printed
edition does: at the point a section *starts*, and then nowhere until the next
one. That is what stops it becoming a column of noise, and it is right for
verse and for dialogue.

It leaves one gap. Perseus divides the *Republic* a whole Stephanus page to a
paragraph, so a reader partway down one sees `327a` in the margin above and has
no way to tell the passage runs on to `327c` — short of hovering over the line,
which is where this whole problem started.

So each pane now names its selected passage in full, underneath:

```
in the margin      328a
under the pane     328a–e
```

One strip per pane rather than one for the reader. The two sides are different
editions and cite differently — Jowett's *Republic* and the Greek agree on the
Stephanus page, an *Iliad* and its translation need not agree on anything — and
a single line would have to pick one and be wrong about the other.

The reference alone, without the author and work. Those are already on screen
in the library tree and in the edition list directly above, and looking them up
again on every arrow key would be a database query per keystroke to repeat what
the window is already showing.

A line the editor bracketed as doubtful says so there too, since this is the
string most likely to be copied out.

## Smaller

- **The bestiary's catalogue of ancient witnesses is now checked.** The lookup
  that resolves them against your library was already tested; the table it
  reads was not, so a creature left out of it, or a citation with a typo,
  resolved to nothing and would have been found by somebody playing rather than
  by anybody testing.

## Checks

**1,033 tests, zero warnings on a clean build**, and the reader driven against
a full 2.3-million-line library rather than read — which is how the first
version of this was caught naming a line of the previous work beside the new
one. Opening a different text does not always move the selection, so the strip
was not always told to change, and a reference beside a text it does not belong
to is worse than no reference at all.
