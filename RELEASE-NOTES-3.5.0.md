# Classica Codex 3.5.0

There is something hidden in this release. The Help window will tell you how to
find it, in the place where it belongs.

Everything else is where you left it. **No schema change, no re-ingest, no
re-index** — upgrading is extract-and-run, and the library you already have is
the library this works on.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The thing that is hidden

It is a game. That is the whole surprise, so the rest of this section is about
why it is in a reading application rather than whether it should be.

A chapter is won by fighting and finished by **reading**. Beating the arena
gets you an oracle's clue — an author, a work, a description of what happens in
the passage — and nothing advances until you go into the library, find that
exact line, and select it. The game checks the citation against your own
installed editions. It cannot be brute-forced, because the only thing it
accepts is the passage itself.

Six stories, twenty-eight passages, each one resolved against a full library
before it was written down:

| | |
|---|---|
| **The Wrath and the Net** | A king takes a girl from another man and wins his war. Follow him home. |
| **Wrath into Pity** | The *Iliad*'s first word is anger. Follow it to the last thing it becomes. |
| **The Long Way Home** | A man who is very good at lying wants to get home. Follow the lies. |
| **Fire and the Rock** | Someone gave humans fire and was punished for it. Find out what the gift cost. |
| **The Fleece and the Knife** | A hero needs a princess's help to steal a golden fleece. Follow what happens to her. |
| **The God Who Came to Thebes** | A young stranger arrives in the city where his mother died. Find out who he is. |

Two of them need Homer and nothing else, so a library holding only the Greek
core still has something to play.

**A story you cannot finish is never offered.** An arc is only playable when
every passage in it resolves in the library you have. Reaching the last chapter
of a story and finding the verse missing would be worse than never being
offered it, so the check is all-or-nothing and runs before the menu is drawn.

**The monsters have reading of their own.** Each creature you defeat opens its
ancient witness — the Cyclops sends you to Odysseus's borrowed name, and then to
Hesiod, whose Cyclopes forge Zeus's thunderbolt and are not the same creature at
all. A shared name does not make two stories one, which is a thing worth
learning from a bestiary.

**Divine gifts are rules, not flavour.** Athena's mirror shield reflects bolts
because that is what Perseus does with it; the Unseen Helm conceals you because
that is how he escapes the Gorgons. Each one names the passage it comes from.

Progress saves per library, on its own, to its own file — the game never writes
to your corpus. Saves are written atomically with a backup, validated on load,
and version-gated, so a save from a future version refuses to load rather than
being silently misread. Recovered passages are re-resolved against the database
by author, title and citation when you reopen them: the stored row IDs are never
trusted, so re-ingesting your library does not break a journal.

## Fixes

**Closing the game window mid-fight threw an exception.** Windows delivers a
deactivate message to a window that is already being torn down, and the game
answered it by pausing the fight — which repaints the buttons, which asks GDI+
about images the form had just released. The result was `Parameter is not
valid`, which is the least helpful sentence GDI+ knows. The game now ignores
messages that arrive after it starts closing, stops its clock the moment the
close actually goes through, and hands its bitmaps back before freeing them.

**Four windows did not scale with the system text size.** Every other window in
this application derives from a base class that establishes font scaling before
any control is positioned; the new ones derived from `Form` directly, which is
the same bug that clipped three labels at 125% in 3.4.1. They now scale like
everything else. The button icons were already drawn at every size from 16 to
256 pixels, so they stay crisp rather than being stretched into mush.

**The arena stopped redrawing scenery that never changes.** The night sky, the
temple, the floor grid and the CRT scanlines were rebuilt from some three
hundred drawing calls in every one of sixty frames a second. They are rendered
once now and copied thereafter, which is 39% less garbage per frame. It did not
make the game faster — at 2.8 ms a frame against a 16.7 ms budget it was never
short of time, and two thirds of what remains is the unavoidable work of
scaling a 480×300 picture up to the window.

## Still free, still open

The application code is **MIT**, as it has been. The game adds no dependency to
this project and bundles no third-party asset: the sprites are pixel rows
written out in source, the sound effects are square waves synthesised at
startup rather than audio files, and the icons were drawn for it. Nothing in
this release changes the licensing position the README sets out, including the
one noncommercial constraint that comes from the Greek lemma data the setup
wizard fetches — which is unchanged, and documented where it always was.

## Known and deferred

**Plato still cannot be cited properly**, and 3.4.1 said this would be fixed in
3.5. It is not. Perseus ships Stephanus pagination as inline milestone markers
that the parser discards, so *Euthyphro* 2a still displays as `[2.1]`. It needs
a parser change and a full re-ingest of every affected text — which is the sort
of thing that should ship as its own release with its own verification, not
folded in alongside a game. It is
[issue #14](https://github.com/ClassicaCodex/ClassicaCodex/issues/14) and it is
next.

## Checks

**986 tests, zero warnings on a clean build.** The crash was reproduced in a
harness that drives the real form into a real fight and closes it, confirmed
against the stack trace, and the harness is what verified the fix. The game's
scenery cache was checked by rendering both screens and comparing them to what
they drew before.
