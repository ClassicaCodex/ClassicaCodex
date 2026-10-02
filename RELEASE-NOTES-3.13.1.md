# Classica Codex 3.13.1

Cuneiform draws as cuneiform.

An Oracc tablet opened in the reader came out as rows of small circles — one
for every sign. That was font substitution: the reader draws every edition in
its reading font, Palatino Linotype on the source side, and no ordinary reading
font has a glyph above U+FFFF. Cuneiform starts at U+12000.

Nothing was wrong with your library. The text, the word index and search were
all correct the whole time; only the drawing was. **No schema change, no
re-ingest, no re-index** — install this and the tablets are legible.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
More info, then Run anyway.

## What changed

A passage that contains cuneiform is now read in **Segoe UI Historic**, which
ships with Windows 10 and 11 and carries the whole block. Nothing has to be
installed. If a machine somehow lacks it, Noto Sans Cuneiform, Akkadian,
CuneiformComposite and Santakku are tried in turn, and an installation with none
of them keeps the reading font rather than failing.

**The text decides, not the language.** Oracc publishes each work twice, as a
transliteration and as cuneiform, and records both as `akk` and `Original` — the
language is identical and the script is not, so asking the language would give
the same answer for both and be wrong for one. The font is chosen by looking for
cuneiform codepoints in the passages themselves.

The choice is made once per edition rather than once per row. Every row height
in the reader is measured against the pane's font, and a row measured in one
font and drawn in another is text in the wrong place rather than merely the
wrong shape.

## Still substituting

The reader is fixed. The lists that show passages gathered from many works at
once — search results, Word Study, Bookmarks, Auto-Tag, Compare Translations —
still draw in the interface font, so a cuneiform line bookmarked and read back
there will still show circles. Those need a font chosen per row rather than per
list, since each row can come from a different work, and that is a wider change
than a fix for a shipped bug should carry.

In practice the reader is where this material is read. Cuneiform is also not
reachable by search in the first place: the word index keeps letters, and every
cuneiform codepoint arrives as a surrogate pair that is not one — which is why
the transliteration edition is the one to search, as it is in print.

## Also

Twelve tests cover the detection, and the one that matters checks a surrogate
pair. Every cuneiform codepoint is above U+FFFF, so each sign is two chars in
the D800 range; a detector written as a loop over chars compares those halves
against 0x12000, finds nothing, and reports a tablet as needing no special font.
That is exactly the state that shipped.

1,655 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  C83F0B950E0ACBB5D4DF4A3DC53057A373ED8DA11F3415024F6194A428FE99A8
```
