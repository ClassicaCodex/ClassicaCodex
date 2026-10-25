# Classica Codex for Mac 3.16.0 (preview)

Cuneiform, the Egyptian texts, and what ancient magicians wrote down.

Four new collections in setup: the cuneiform of Oracc, the Egyptian texts of
the Thesaurus Linguae Aegyptiae, the Greek and Latin curse tablets, and the
Greek and Coptic magical papyri. The reader has learned to show them. A
cuneiform tablet comes in its signs and its transliteration, often with no
translation, so both of them now go side by side. It is still a preview: the
Mac app has had far less use than the Windows one, and the Intel build has
not yet been opened on an Intel Mac.

**[Download the disk image for your Mac](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.16.0)** —
`arm64` for Apple silicon, `x64` for Intel. **Apple menu → About This Mac**
says which you have: a Chip line means Apple silicon, a Processor line means
Intel. Open the disk image and drag Classica Codex into Applications. It needs
macOS 12 or later, and brings its own copy of .NET, so there is nothing else to
install. If you have 3.15.1, let it replace that one. Your library and
settings stay where they are.

The app isn't notarised by Apple — that needs a paid developer account — so
macOS refuses to open a downloaded copy the first time, and may do so again
for this new one. Click **Done**, open **System Settings → Privacy &
Security**, scroll down to the message about Classica Codex, and click
**Open Anyway**.

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

  Each text comes in the signs, in transliteration, and with the dictionary
  meaning of every word. The magical texts also have their editors' English
  translation, line by line. Open data, under CC0 or CC BY-SA 3.0 by project.
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
- **Curse tablets**, about 840 of them:
  - all 486 tablets found in Attica, each with an English and a German
    translation
  - the Greek tablets of Sicily
  - the Latin tablets of the Roman provinces, from Bath and Uley to Mainz and
    Spain

  Under CC BY 4.0 and CC BY-SA 4.0.
- **Magical papyri:**
  - about 150 Greek papyri from papyri.info, PGM I and most of the
    *Supplementum Magicum* among them, without translations
  - about 350 Coptic spells from the Kyprianos database, each with its English

  Under CC BY 3.0 and ODbL.

## The reader

- **The second pane takes the other original.** Where a work has two
  originals and no translation, as an Oracc tablet does, the second one now
  opens opposite the first instead of leaving that pane empty. The label over
  it says ORIGINAL, not TRANSLATION.
- **The right-click menu offers only what the text can answer.** Word Study is
  hidden for a language the library has no word data for, and Editor's Notes
  for an edition with no apparatus.
- **View on oracc.org…** opens the page a text came from. For cuneiform it is
  the way to Oracc's published translations, which are on its site but not in
  its open data.
- **The signs.** Cuneiform and hieroglyphs are drawn in the Noto fonts that
  come with macOS, so there is nothing to install. The word index and Search
  now see them, too: before, any character outside Unicode's first 65,536 was
  silently left out.

## Help and About

- Help has topics for the Egyptian texts, the curse tablets, the magical
  papyri, the Life of Antony and the Arabian Nights, with the terms each is
  under. About credits the cuneiform.
- Three topics were behind:
  - **The Goetia** described the old Wikisource text and its request for
    non-commercial use. It now describes the Project Gutenberg text that setup
    installs.
  - **Pseudo-Dionysius** gave the old terms.
  - **Pseudepigrapha** left out the Latin Life of Adam and Eve.

## Adding them to a library you already have

Open **Setup**, choose **Advanced Setup**, and run the step for each
collection you want. Then click **Word Index…** and rebuild it, or the new
texts won't turn up in Search.

If your library already has the Egyptian texts, from a build of the Windows
app newer than 3.15.1, run that step again. It adds 125 texts that were
missing, most of them Middle Kingdom letters, and removes two of the TLA's
trial documents that had been installed as texts.

## Ahead of Windows 3.15.1

Like the last Mac release, this one is built from newer shared code than
[Windows 3.15.1](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/v3.15.1).
Setup offers the four collections above and the ones listed in the
[Mac 3.15.1 notes](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.15.1)
— the Hebrew Bible, the Quran, Aquinas and the rest — none of which Windows
3.15.1 has yet.

## If something goes wrong

An error in one window puts up a message and leaves the rest of the app
running. The details go to `~/Library/Application Support/ClassicaCodex/errors.log`;
please [report it](https://github.com/ClassicaCodex/ClassicaCodex/issues) with
that file attached.

The Mac app builds with zero warnings, and 1,721 tests pass on macOS: the Mac
app's 125 and the shared libraries' 1,596.

The downloads are signed ad hoc but not notarised, so if you would rather check
them than trust them:

```
SHA-256  93C5376337BA510EA6201F7F7243333FC051587A7AED149CD3850866649AF21E  ClassicaCodex-3.16.0-mac-arm64.dmg
SHA-256  9AE6239B145E58D180C1349F1131E60776AA6F946AEC39ECAA9BB50F16D9B9A3  ClassicaCodex-3.16.0-mac-x64.dmg
```
