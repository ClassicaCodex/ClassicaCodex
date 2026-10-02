# Classica Codex 3.15.0

The Pyramid Texts.

Spells cut into the walls of six burial chambers at Saqqara between about 2350
and 2200 BC, so that a dead king might eat, breathe, rise and cross the sky.
They are the oldest religious literature that survives anywhere, and the
pyramid of Unas is the first building in the world with writing inside it.

519 spells in 3,089 passages, from Unas through Teti, Pepi I, Merenre and Pepi
II to Queen Neith. Each pyramid arrives twice — once in hieroglyphs and once in
the transliteration Egyptologists read them in — and every word is parsed and
carries its dictionary headword.

Nothing here changes your data. **No schema change to anything you already
have, no re-ingest, no re-index**, and nothing to run afterwards. One new
optional step appears in the Setup Wizard; leave it alone and your library is
exactly as it was.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
More info, then Run anyway.

## Installing it

Setup Wizard → **The Pyramid Texts (Old Egyptian)**. It is a 1 MB download and
it finishes in about a second, which makes it the smallest and fastest step in
the wizard by a wide margin.

Six works arrive, one per pyramid, ordered as they were built rather than
alphabetically:

| | passages | spells |
|---|---|---|
| Pyramid Texts I: Unas | 1,240 | 227 |
| Pyramid Texts II: Teti | 744 | 158 |
| Pyramid Texts III: Pepi I | 829 | 113 |
| Pyramid Texts IV: Merenre | 6 | 4 |
| Pyramid Texts V: Pepi II | 230 | 78 |
| Pyramid Texts VI: Queen Neith | 40 | 14 |

Merenre's six passages are not a mistake. His pyramid was badly robbed and
little of its text is legible; the treebank has what there is. Neith is not a
king — she is Pepi II's queen, and the only woman whose pyramid carries these
spells.

## Two scripts, not two spellings

Every pyramid is installed twice, and the edition dropdown switches between
**Egyptian (hieroglyphs)** and **Egyptian (transliteration)**.

This is a different thing from the second reading in the French, German and
Nordic collections. Those are two *spellings* of one script — what the scribe
wrote against what an editor prints. These are two *scripts*. The hieroglyphs
are the signs on the wall; the transliteration is the Latin-letter convention
Egyptology reads them in, with its own brackets for what is restored and its
own parentheses for what the grammar requires and the wall does not show.
Neither is a normalisation of the other, and you want both: the signs cannot be
searched for and the transliteration cannot be looked at.

The signs draw in **Segoe UI Historic**, which ships with Windows 10 and 11 and
carries 1,071 of the 1,072 hieroglyphs in the original Unicode block. Nothing
has to be installed. This is the same machinery that fixed the cuneiform in
3.13.1, which now knows two scripts instead of one and looks for both in a
single pass.

## Citations, and the two men who numbered them

Passages are cited by spell and section — **Pyr. 23.16a** — which is how these
have been cited since Sethe. The spells Sethe did not number carry Allen's. A
section usually holds one sentence and sometimes four, so where there is more
than one a position is added: 23.16b.1 and 23.16b.2 are the two halves of §16b.

Getting the order right turned on a detail. Sethe's section numbers run
continuously through his edition, so for his 2,817 sentences it makes no
difference whether you sort by spell or by section. Allen's 272 count from one
again inside each spell — so sorting by section throws spell 502C's passages to
the very front of Pepi II, among the opening offering formulae, hundreds of
places from where they belong. Spell is the unit both editions agree on.

## Word Study

Every word is parsed: a headword, a part of speech, and full morphology —
verb class, voice, mood, gender, number. 4,945 distinct word forms are filed
under their headwords, from 34,140 annotated words. Word Study answers on these
the moment the step finishes, with no second download, because the annotation
arrives in the same file as the text.

## There is no translation

The treebank carries the signs, the transliteration and the grammar, and no
rendering into any modern language. Faulkner's translation and Allen's are both
in copyright and there is no free one to pair with this.

So if you do not read Egyptian, this gives you the apparatus and not the sense.
It is material to study rather than to read through — closer to the Oracc
tablets than to the Commedia. The wizard says so before anything is downloaded,
and so does the Help.

## Boxes where a sign should be

About **one passage in five** shows a small empty box. Unicode added four
thousand more hieroglyphs in version 15.1 in 2023, 1.35% of the signs here are
among them, and no font that ships with Windows has caught up. The signs are
scattered rather than clustered, which is why so small a share of the signs
touches so large a share of the passages.

Nothing is wrong with the text, and the signs are deliberately kept rather than
dropped — a line that quietly omitted them would look complete and be wrong.
Installing a font with wider coverage, such as Aegyptus or NewGardiner, fills
them in; this application ships no fonts of its own and will not start.

The square sign-clusters hieroglyphic is really set in are not reproduced
either. Unicode has controls for that layout and Windows does not apply them:
tested both ways, the drawing stack either ignores them or draws them as
visible placeholder rings. The signs are shown in a line, which is what most
digital Egyptian shows.

## Not offered to Stylometry

Neither reading goes into word-frequency comparison. The six pyramids share
their spells — §16a is in Unas and in Teti and in Pepi — so a distance between
two of them would measure how much of the corpus they have in common and report
it as a distance between authors. A string of signs is not word-comparable in
the first place.

## Also

The edition dropdown now names Old French, Italian and Egyptian rather than
showing `fro`, `ita` and `egy`.

The release archive turned out to contain a file that is not the corpus: the
editors' working copy, under a folder named `not-to-release`. Reading every
treebank file under the download folder — which is exactly what the Dante step
does, correctly, because that treebank has no such folder — gives 5,271
sentences instead of 3,089, with duplicates in them and no error anywhere to
say so.

1,707 tests, zero warnings.
