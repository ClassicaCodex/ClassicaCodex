# Classica Codex 3.6.6

One fix. 3.6.5 repaired the places map and broke part of it in the same
change: every pin whose name has a space in it stopped returning anything at
all. If you use the map, upgrade; if you do not, 3.6.5 is fine.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Eight places on the map answered with nothing

Clicking a place used to search for its letters wherever they fell, so Ur
returned five thousand passages of which one mentioned Ur and the rest were
"during", "figure" and "purple". 3.6.5 fixed that by looking the name up in the
word index, which holds whole words.

The word index holds whole *single* words, and normalizing a name throws away
everything that is not a letter — including the space. So a two-word name
arrived as one token that no line can contain: "Euxine sea" became
`euxinesea`. The pin did not return noise, or fewer results. It returned
nothing, silently, as though the place were never mentioned.

Eight of the map's 240 pins were affected, and this is what they lost and have
now got back:

| Place | 3.6.5 | 3.6.6 |
|---|---|---|
| Euxine sea | 0 | 86 |
| Arabian Gulf | 0 | 57 |
| Egyptian Thebes | 0 | 13 |
| lake Moeris | 0 | 12 |
| Hippo Regius | 0 | 9 |
| Colonia Agrippina | 0 | 3 |
| Monte Cassino | 0 | 2 |
| Boeotian Thebes | 0 | 2 |

184 passages, unreachable from the map for one release.

A name of one word is looked up exactly as 3.6.5 looked it up, so the noise
that fix removed stays removed — Ur still returns its 521 mentions and not the
five thousand. A name of more than one word now requires every one of its words
through the index, and then requires the phrase itself to be present, so
"Egyptian Thebes" does not match a line carrying both words a paragraph apart.
Between them those two conditions return exactly the lines a substring search
would have found, without reading two and a third million lines to find them:
the slowest of the twelve names measured took 69ms, the median 5ms.

The map is the only thing that changed. Nothing else calls this path.

## Checks

**1,119 tests, zero warnings on a clean build.** Seventeen new ones, covering
both ways this can be wrong: matching the letters inside other words, and
matching nothing at all. One of them asserts the old and new behaviour side by
side, because a test for the second failure mode passes against the bug unless
it also pins what the first one did.

Every affected pin has a test of its own, named for the place, so a future
change to how words are normalized cannot take them away again quietly.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  D7F3167E756B95945616B811C5A207EF1957038CD22C92C794FAA921A2045631
```
