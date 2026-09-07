# Classica Codex 3.6.8

Colour. Twelve places where text was drawn in something the theme had never
been asked about, and one where the theme threw a colour away. If you read in
dark mode, upgrade — several of these are on the screens a new reader sees
first.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Every link in the application was nearly invisible in dark mode

A `LinkLabel` paints its link with `LinkColor`. The theme set `ForeColor`,
which a `LinkLabel` does not use for that — and because `LinkLabel` derives
from `Label`, the theme's `Label` branch matched it, set the wrong property,
and moved on. Nothing warned; the code read as though it worked.

So seven links across four themed windows kept the WinForms default blue,
**1.94:1** on the dark surface — worse than the dark red that 3.6.5 called
unreadable at 1.66:1. A link you had already clicked was worse still, at
1.77:1.

They were not obscure links:

- **Get an API key →** and **Get a free API key →**, in Translation settings.
  The two links a new reader needs in order to set up AI translation at all.
- **See current pricing →**, beside them.
- The two source links in the first-run wizard.
- The voice-installation link in Translate.
- The licence URL in About.

Research Bench was the one window that knew `LinkColor` existed and set it by
hand. Its choice is now the theme's, applied everywhere: **7.59:1** in dark,
7.56:1 in light. Visited links stop turning purple, because being able to read
one matters more here than knowing you have read it before.

## Stylometry's row highlights had never once been drawn

Both tables mark their most interesting rows by colouring them — the works
length does not explain, the samples whose depth doubles between settings.
The themed list painter filled every row with the *list's* colour, so a row
that set its own was painted over. In both themes, since the day that painter
was written. Two of the three highlights were light-mode-only colours besides.

The painter now honours a row's own colour, selection still outranks it, and
all three highlights are theme-aware. This was the window 3.6.5 fixed for
being unable to show its own conclusions; it turns out it still could not
show them.

## Ten more places drawing text you could not read

Six were the quiet halves of pairs 3.6.5 had already fixed — the same
`DimGray` at **3.03:1**, sitting beside a sibling that had been converted, in
Create Translation, Cross-Language Echo, Passage Export and Translate. One of
them is 74 lines above the very line 3.6.7 fixed, on the same label.

Two more the theme could never have reached at all:

- **Menota's ingest plan** flags the row whose title it had to invent — the
  one most likely to be wrong — and drew it at **2.66:1**. A per-row grid
  style outranks every style the theme sets, so no theme pass was ever going
  to touch it.
- **The first-run wizard's readiness line** was three literals, all of them
  failing, and the failing branch is the one that prints a caught exception's
  message on the first screen anybody sees.

And two that were a hair under rather than unreadable: muted text app-wide was
4.46:1 in light mode against a 4.5:1 minimum, and the wizard's "ready" green
was 4.48:1. Both now pass, by an amount nobody will see.

## Corrections

- **README** said "Version 3.6.5", two releases behind.
- **A doc comment in the search code** gave 84 and 42 mentions where the 3.6.6
  notes give 86 and 12. The notes are the measured ones; the comment had
  carried an earlier draft's figures. The notes invite readers to check the
  source, so the two had better agree.
- **The release script now fails where it used to warn** — on a dirty working
  tree, and on a build path surviving into the executable. 3.6.7's notes said
  those faults could no longer happen, which was truer of the two checks that
  threw than of the two that only printed. It also refuses an archive with no
  icons in it.

## Checks

**1,129 tests, zero warnings on a clean build**, ten of them new. They cover
both shapes of this bug: a property the theme never read, and a colour the
theme discarded. One asserts that a `LinkLabel` is still a `Label`, because
the fix depends on matching it *before* the `Label` branch and reordering that
switch would silently bring the whole thing back.

Every contrast figure quoted above is computed with the WCAG formula against
the surface the text actually sits on, and the ones this release introduces
are asserted in the suite rather than checked once by hand.
