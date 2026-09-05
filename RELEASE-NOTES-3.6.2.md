# Classica Codex 3.6.2

If your display is set above 100%, this is the release to take.

Windows scaling has never worked in this application. Not "worked imperfectly"
— the code that was supposed to do it computed a scale factor of exactly 1
every time, in every version, so at 125% the text grew by a quarter and not one
window grew with it. The result was captions cut off mid-sentence, buttons
sitting on top of the boxes above them, and on one screen a **Save** button
pushed off the bottom edge.

At 100% none of that happens, which is why it lasted six versions.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## What was wrong

Every window here positions its controls in absolute pixels measured on a 100%
display. The application is System DPI aware, so at 125% Windows hands it a
larger font — Segoe UI 9pt is 16 pixels tall at 100% and 20 at 125% — and
stretches nothing else. Each window is supposed to scale its own coordinates to
compensate.

It never did. The code set the design size and then the scaling mode, and
assigning the mode discards the design size and re-reads it from the current
font:

```
after setting the design size    {Width=7,  Height=15}
after setting the scaling mode   {Width=8,  Height=20}    at 125%
```

Declared and current were therefore always identical and the factor was always
exactly 1. Measured on a real display: the About window was 720×680 pixels at
both 100% and 125%, with its text a quarter larger in the second.

## What it broke

The first screen a new user ever sees lost the end of both its descriptions —
"The right choice for a first-time setup, or" and then nothing. The AI
translation settings lost four, dropped both **Remove Key** buttons on top of
the text boxes above them, and pushed **Save** and **Cancel** off the bottom
edge of the window. About sliced the descenders off its own title.

## What it does now

Scaling is taken from the display's DPI rather than from font measurements, and
established with layout suspended — which is what stops the re-read that
discarded it. Verified on a real display at three settings, across every window
that opens without arguments:

| window | 100% | 125% | 150% |
|---|---|---|---|
| Tag Categories | 420×480 | 525×600 | 630×720 |
| Setup Wizard | 900×900 | 1125×1125 | 1350×1350 |
| Myth Network | 1184×761 | 1478×941 | 1767×1116 |

Exactly a quarter larger and exactly a half larger, and — the part that
mattered most to check — **byte-identical at 100% to the version before the
fix**. If you read at normal scaling, nothing about this release moves a single
pixel.

Captions without room to draw fell from 51 to 7 at 125%, and those 7 are the
same 7 reported at 100%: an artefact of the measuring tool, not of the
application. Confirmed by photographing them.

## Two more, found by the same pass

**Twelve captions were losing their ampersands.** A caption treats `&` as a
keyboard shortcut marker, so the setup wizard has been offering "Dictionaries
(LSJ + Lewis  Short)" and, since 3.6.0, "Stephanus  Bekker Citations (Plato
Aristotle)". Nothing in this application uses a caption as a shortcut, so they
say what they mean now.

**About was measuring itself in a font it does not draw in.** It sized its text
against Microsoft Sans Serif while rendering in Segoe UI, so every bold line on
the page was a different typeface from the prose beside it, and every paragraph
was measured narrower than it was drawn. Its headings also computed their
height from a point size as though points were pixels — true at 100% and
nowhere above it, which is what cut the bottom off "Classica Codex".

## Checks

Run at 100%, 125% and 150% on an actual display rather than reasoned about —
which is how all of this came to light, and which 3.4.1's notes claimed for
three labels that had only been thought about carefully.

**1,026 tests, zero warnings on a clean build.**
