# Classica Codex 3.9.0

A bronze dial on the toolbar opens a working Antikythera mechanism.

Nothing here changes your data. **No schema change, no re-ingest, no re-index**,
and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

> If you set up a **Latin** library before 3.7.2 and have not re-run the Latin
> Lemma Data step yet, it is still worth six minutes — see the note in the
> README.

## The machine from the shipwreck

In 1901 sponge divers working a wreck off Antikythera brought up a lump of
corroded bronze. It turned out to be a geared astronomical calculator, built
around 100 BC, and nothing of comparable complexity survives from anywhere for
another thousand years. Cicero describes one twice, in *De Re Publica* and *De
Natura Deorum*. Both passages are in this library, which is the whole reason
this is here.

The last button on the toolbar opens a card about the machine. A button on that
card writes a self-contained web page to your temporary files folder and hands
it to your browser. It opens outside the application, in a separate window, and
it asks the network for nothing — there is not a single URL anywhere in the
page. It works with the network cable out.

Drag the crank and some forty gears turn at the ratios their tooth counts
demand. One turn of the crank is about eleven weeks, because that is the real
223/48 input ratio; the arrow keys step a day at a time if you would rather be
precise, and PageUp and PageDown step a year.

Three views. **Front dial**: the Sun and Moon against the Egyptian calendar and
the zodiac, the Moon's phase shown by the original's own half-silvered rotating
ball, and six rings of planets in the middle. **Back dials**: the nineteen-year
Metonic spiral with its Callippic and Games subsidiaries, and the 223-month
Saros eclipse spiral with its Exeligmos correction. **Gearing**: the trains
themselves, where clicking any wheel tells you its tooth count, which arbor it
sits on, how fast it turns and which way.

The lunar train includes the pin-and-slot: two wheels on slightly offset centres,
one driving the other through a pin running in a slot, which makes the Moon run
fast at perigee and slow at apogee. It is the earliest known mechanism for a
varying rate, and you can watch the coupling breathe as the crank turns.

A side rail reads out where everything is, and one panel grades the machine
against a modern ephemeris while you turn it. That panel is the part I would
point at: freshly aligned, the mechanism holds the Moon to about a fifth of a
degree, and the residual error on the Sun is exactly the equation of centre the
gearing does not model. It is not flattering the machine. It is showing you
where a 2,000-year-old gear train actually stands.

## What it will not pretend to know

Roughly a third of the mechanism survives. The rest is reconstruction, some of
it near-certain and some of it argued over, and a simulation that draws all of
it in the same confident bronze is telling you something false.

So the page grades itself. Lettering that is actually on the fragments is drawn
brighter than the text supplied to fill the gaps — four of the twelve zodiac
names are attested, three of the twelve month names, twenty of the fifty-one
eclipse glyphs. Wheels whose teeth have been counted on surviving bronze are
drawn solid; wheels inferred from the ratios they must produce are drawn
dimmer; wheels that are pure hypothesis are outlines. The two surviving wheels
that nobody has been able to fit into any train are drawn where they are, at the
bottom, connected to nothing.

The crank is one of the reconstructions. No input handle survives at all.

The card lists the eight sources this was built from, five of them as links,
including the 2021 *Scientific Reports* paper for the planetary display and the
2014 eclipse work for the Saros glyphs.

## The show/hide button on the edition dropdown

This one affects everybody, and it has been there far longer than 3.8.3.

Hiding the library gave the reader the column's width back — all of it,
starting at the window's left margin. But one control stays behind when the
library goes: the button that brings it back, which has to, and which sits at
that same margin on the reader's own top row. So the reader slid underneath it,
and the button won, because it was added to the window first and in Windows
Forms the first control added is the one in front.

What that covered was the first thirty-odd pixels of the dropdown naming the
edition you are reading. It was reported as an author's name reading
"ymous (menota)".

The reader now starts clear of that button rather than at the margin. Hiding the
library still gives back nearly all of the column — about 46 pixels less than
before, which is the width of the button and the gap beside it.

## Why 3.8.3 said this could not happen

3.8.3 fixed the same class of bug — the library's author filter box drawn over
the same dropdown — and said the reader's left edge was no longer a number that
happened to sit right of the library, but was worked out from where the library
actually ends, "which stays correct at any scaling and stays correct if that
column is ever made wider".

That was true of the case it was looking at and wrong as a general claim,
because it had a second case it never considered: the library not being there at
all. For that one it kept a special case that started the reader at the window
margin, and it shipped a test asserting exactly that — so the bug being fixed
here was not merely missed, it was written down as the expected answer and
locked in.

The rule now has no special case at all. The reader starts one gap to the right
of whatever is still on screen beside it, and which control that is depends only
on whether the library is showing.

There is a second difference worth naming. 3.8.3's bug was invisible at 100% and
only appeared on a scaled display, which is why it took a laptop to find it.
This one needed no scaled display: it was there at 100%, on every machine, from
the first version that could hide the library.

## Reporting something that breaks

Until now the application named its issue tracker in exactly one place — the
dialog that appears when something has already gone wrong. That is the worst
possible place for it to be the only one. A message box has no link to click and
is gone as soon as you dismiss it, so the address had to be read off the screen
and typed by hand, in the moment you were least inclined to. Anyone who hit a
subtler fault, or who dismissed that dialog and came back to it an hour later,
had the evidence sitting in `errors.log` and nowhere to take it.

Help now carries **Found a problem? Report it on GitHub** along the bottom of
the window. It is on every topic, because somebody who has hit a bug does not
know which topic would have mentioned it. The "When something looks wrong" topic
names that link, says where `errors.log` lives and what to attach, and states
plainly that the application sends nothing anywhere by itself — opening the page
and attaching the log are both yours to do.

## Known, and not fixed here

**Column widths in the detail grids are still not scaled.** The tables in
Compare Saved Runs, Collate Editions, Core Vocabulary and the rest set their
column widths in pixels that Windows Forms does not scale, so at 150% a column
holds about a third less text than it was meant to. 3.8.3's notes said this was
next. It was not — 55 of those literal widths are still there across 12 files,
untouched by anything in this release. It is cosmetic, nothing is drawn over
anything else, and the last column in each grid already stretches to fill. I am
not going to promise it again in a release note; it will be in one when it is
done.

**The two windows whose shape changed here are checked at 100% and nowhere
else.** The Antikythera card is a new window laying out a page of prose from
hand-placed coordinates, and the Help window's two panes were shortened to make
room for the report link. Both were opened by the display-scaling audit, which
now opens 28 windows and reports one caption short of room — the same one it
reported before this release, in a window nothing here touches. But that audit
only runs at whatever scaling the machine it runs on is set to, and this one ran
at 100%. Above that, both windows rest on arithmetic rather than on anything
anybody has looked at, and the card measures its paragraph heights against a
font size it reads before Windows has scaled the window, which is the mistake
that cost it a paragraph's last line at 125% once already. If a line looks cut
off on your display, that is worth telling me about — there is now a link for
it.

**The mechanism's page leaves a copy of itself behind.** It is written to
`%TEMP%\ClassicaCodex\Antikythera.html` each time you open it — about 220 KB,
overwritten rather than added to, so it does not accumulate. It is not cleaned
up when you close the app.

1,418 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  AF1946321EFD7AC26E1047E8BC71048F7A7A56295168E2DD5AE461C45F8F74FD
```
