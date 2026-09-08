# Classica Codex 3.6.10

Four fixes from an adversarial review of 3.6.9, three of them defects the
previous two releases introduced while fixing something else.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The places map was missing real mentions

3.6.6 stopped the map matching letters instead of names — clicking Ur used to
return "during", "figure" and "purple". It did that by requiring every word of
the name to appear in the word index as a whole word, which is what removes the
noise: "Le Mans" no longer returns "nob**le mans**ions" or "il**le mans**uetudine",
and none of those thirty passages was ever the city.

But a phrase does not always sit on whole-word boundaries. "the Persian and
**Arabian Gulfs**" is a mention of the Arabian Gulf, and the index holds
"gulfs", not "gulf". Strabo's "to **Egyptian Thebes**.”And of its power" has no
space after the quotation mark, so the token is "thebesand". Both were rejected.

Words of four letters or more are now matched by prefix, which recovers them
without letting any noise back in:

| Place | 3.6.9 | 3.6.10 |
|---|---|---|
| Arabian Gulf | 57 | **60** |
| Egyptian Thebes | 13 | **14** |
| Le Mans (not in this corpus) | 0 | 0 |

Four letters, not three or five: "le" as a prefix matches "less", "left",
"legions" — some word in almost any passage — so the conjunct would stop
excluding anything and Le Mans would answer with twenty-two passages about
mansions. At five, "gulf" falls below the threshold and those three mentions are
lost again. Every other pin is unchanged, and the slowest query measured 91ms.

One mention still escapes: Strabo also spells it "Aegyptian Thebes", which no
prefix of "egyptian" reaches. And a phrase typed without its accents will not
match text that has them, though a single-word search will — the index holds
normalized words, the phrase match does not. Both are now written down in the
code rather than believed not to exist; fixing the second needs a normalized
copy of the text to match against, which is a schema change.

## Muted text went black on the second pass

3.6.8 darkened the light-mode muted grey slightly, to clear the 4.5:1 minimum.
It did not tell the theme that the new value counted as muted — so the next
time the theme was applied, it no longer recognised what it had itself written
and promoted the label to full-strength body text.

The theme is applied more than once: at construction, when a window is shown,
and again on every press of the light/dark button. So the reading pane's two
reference strips lost their muting immediately, and every hint label on the
main window and Search lost it the first time you switched themes.

## Links went white when you pressed them

Also 3.6.8. A link's pressed colour was set to the theme's selection
foreground, which in light mode is the system highlight colour — pure white.
A link paints on the page, not on a selection, so pressing one turned it white
on parchment: **1.23:1**, and 1.07:1 on the lighter surfaces. The WinForms
default it replaced was red at 3.25:1, so that change made the pressed state
worse than doing nothing.

Pressed links now use a colour meant for text on a page — 10.21:1 dark, 5.79:1
light. Research Bench, which had kept its own hand-written copy of these
colours since before the theme knew links existed, now uses the theme's.

## A dangling reference in the download

3.6.9 removed `PdfSharp.WPFonts.dll` and its six proprietary Microsoft
typefaces, and `PdfSharp.Snippets.dll` with it. It missed `PdfSharp.Quality.dll`,
which also references WPFonts — so 3.6.9 shipped an assembly pointing at
something no longer in the bundle. Nothing reaches it, and no code path could
have thrown. It is gone anyway: "unreachable" is what was said about two checks
in 3.6.9 that turned out not to work.

## Checks

**1,133 tests, zero warnings on a clean build**, four new. They pin the plural
and the missing-space cases that were being lost, and — because the four-letter
threshold looks arbitrary until you see what three would cost — the "noble
mansions" case that sets it.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  F9A54DE5F141DED9D08EF1A7A387910017F2E242C943767A6C5DEA7AE78007D5
```
