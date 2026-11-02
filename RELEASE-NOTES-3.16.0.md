# Classica Codex 3.16.0

Cuneiform, the Egyptian texts, what ancient magicians wrote down, and English
for passages that had none.

Windows catches up with the two Mac previews. Setup now offers every
collection they added: the cuneiform of Oracc, the Egyptian texts of the
Thesaurus Linguae Aegyptiae, curse tablets and magical papyri, the Hebrew
Bible, the Quran, Aquinas and the rest. It also offers one collection that
neither Mac preview has: the Classica Codex AI translations, English for about
15,900 lines that had no published English. The reader has learned to show
the new scripts.

All of it is optional. A library you already have keeps working as it is:
**no re-ingest, no re-index**, unless you add something (see the end of these
notes).

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
More info, then Run anyway.

## The AI translations

English for passages that have no published English: about 15,900 lines from
446 works in twelve languages. They are the passages that the
[Atlas of Ancient Myth](https://skirnirstudios.com/atlas-of-ancient-myth/),
[Magic in the Ancient Texts](https://skirnirstudios.com/magic-in-the-ancient-texts/)
and a study of demons and angels cite, in Greek and Latin, Old Norse, Middle
High German, Egyptian, Old French, Dante's Italian, Hebrew and Aramaic.

- **They are AI translations, and labelled so.** Claude made each one from the
  original, line for line, without a published translation to lean on. A
  second AI, Codex, checked every passage against the original, and the
  objections that held up were corrected. **No specialist has reviewed any of
  it.** The edition list calls each one "Claude (AI-generated)", its Work
  Details say so, and an exported passage carries the label with it.
- **Each is filed beside the text it translates**, under the same references,
  as another translation of that work. Only the cited passages were translated,
  so most cover part of a work, and the edition list marks them INCOMPLETE
  with how many lines they have. That is by design, not a download that stopped.
- **The texts come first.** A translation arrives only if the text it
  translates is already in your library. One whose text is not there is
  skipped and noted, and the step stays unticked. Install that collection and
  run the step again.
- **Corrections reach you.** The translations live in a public repository,
  [ClassicaCodex/ai-translations](https://github.com/ClassicaCodex/ai-translations),
  with the translators' briefs and the review of every passage. Running the
  step again brings in corrections and new passages, and changes only the
  lines that changed.

About 8 MB. Each translation carries its source's licence, CC BY-SA 4.0 or
CC BY 4.0, and the credit that licence asks for.

## New in setup

- **Cuneiform from Oracc.** Akkadian and Sumerian from twelve Oracc
  projects, about 94 MB in all:
  - the royal inscriptions of Tiglath-pileser III, Sennacherib and
    Esarhaddon, and the letters written to Sargon II
  - scholarly commentaries and bilinguals, and the glassmaking texts
  - the magic: *Maqlû*, the night-long ceremony for burning a witch, in
    Abusch's edition, the three volumes of the anti-witchcraft rituals, and
    the scholars' libraries of Sultantepe, Nimrud and Uruk, with *Šurpu*
    among them

  Each text comes in the signs and in transliteration, with the dictionary
  meaning of every word under the line. The magical texts also have their
  editors' English translation, line by line. Open data, under CC0 or
  CC BY-SA 3.0 by project.
- **Egyptian texts from the Thesaurus Linguae Aegyptiae**, about 3,500 texts
  in 62,000 passages:
  - the Book of the Dead from 24 papyri
  - the medical papyri, Ebers and Edwin Smith among them
  - Sinuhe, the Shipwrecked Sailor and the love songs
  - the late funerary and temple books
  - letters, archives, stelae and rock inscriptions

  Each passage is in transliteration, with the TLA's German translation beside
  it, and the literary, medical and funerary papyri come in hieroglyphs too.
  Word Study looks each word up in the TLA's dictionary, in German. A 75 MB
  download, under CC BY-SA 4.0.
- **Curse tablets**, about 840 of them: all 486 found in Attica, each with an
  English and a German translation; the Greek tablets of Sicily; and the Latin
  tablets of the Roman provinces, from Bath and Uley to Mainz and Spain. Under
  CC BY 4.0 and CC BY-SA 4.0.
- **Magical papyri:** about 150 Greek papyri from papyri.info, PGM I and most
  of the *Supplementum Magicum* among them, without translations; and about
  350 Coptic spells from the Kyprianos database, each with its English. Under
  CC BY 3.0 and ODbL.
- **The Hebrew Bible**: the Westminster Leningrad Codex, every word parsed and
  keyed to Strong's dictionary, with the 1917 JPS translation beside it.
- **The Quran**, in Arabic and in Pickthall's English.
- **Aquinas**: the First Part of the Summa Theologiae, in English.
- **Pseudo-Dionysius**: the Heavenly Hierarchy and the other works.
- **Pseudepigrapha**: 1 Enoch, the Testament of Solomon, Jubilees and others.
- **Rabbinic texts** on angels and demons: five tractates of the Talmud,
  Pirkei de-Rabbi Eliezer and the midrashim.
- **Early English books** on devils, spirits and angels, twenty-one of them,
  from *Daemonologie* to *Paradise Lost*.
- **The Goetia** of Mathers and Crowley, from Project Gutenberg.
- **The Life of Antony, the Latin Life of Adam and Eve and the Arabian Nights.**

Help has a topic for each, with the terms it comes under.

## The reader

- **The signs are drawn.** Cuneiform and hieroglyphs used to draw as one empty
  box per sign, because no reading font has them. A pane holding them now
  switches to Segoe UI Historic, which comes with Windows and draws 922 of the
  1,024 cuneiform signs and the hieroglyphs. Text that read correctly before
  is not affected. Some hieroglyphs that Unicode added in 2023 are in no
  Windows font yet, and still show as boxes.
- **Search can find them.** The word index used to leave out every letter
  outside Unicode's first 65,536 characters without saying so: an Egyptian or
  cuneiform text was indexed, reported success, and held nothing. It now
  indexes them.
- **The second pane takes the other original.** Where a work has two
  originals and no translation, as an Oracc tablet does, the second one now
  opens opposite the first instead of leaving that pane empty. The label over
  it says ORIGINAL, not TRANSLATION.
- **The right-click menu offers only what the text can answer.** Word Study is
  hidden for a language the library has no word data for, and Editor's Notes
  for an edition with no apparatus.
- **View on oracc.org...** opens the page a text came from, for any edition
  that records one. For cuneiform it is the way to Oracc's published English,
  which is on its site but not in its open data.

## Adding them to a library you already have

Open **Setup**, choose **Advanced Setup**, and run the step for each
collection you want. Run the AI translations last, since they install only
beside texts already in the library. Then click **Word Index...** and rebuild
it, or the new texts won't turn up in Search.

A rebuild also picks up the Pyramid Texts' hieroglyphs if you installed them
before this release; until then Search cannot see them.

1,989 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  8EDFA92365C61B536C3397A9110485F210BC9A9110AD5B29F80FFA3AEA4622DE
```
