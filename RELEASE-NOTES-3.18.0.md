# Classica Codex 3.18.0

Quoted By: who quoted a line, and what they made of it.

Quotations in later authors are the indirect tradition, the testimonia a
critical edition prints and the evidence for who read what. This release puts
them in the reader, line by line. A line a later author quotes carries a “ at
its end; right-click it for the passages that quote it, each with the quoter's
wording set against the edition's. In the quoting passage, the quoted words are
underlined and lead back to the line.

**[Download for Windows](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
`win-x64` for most PCs, `win-arm64` for Windows on Arm (not yet tried on an Arm
PC). Extract all of it and run `Classica Codex.exe`. Windows will show a blue
"Windows protected your PC" box on first run because the app isn't
code-signed; click More info, then Run anyway.

## Quoted By

- **The mark.** A line that a later author quotes ends with a “, after the
  ?, # and ★ you already know. It is the one mark you did not make.
- **The window.** Right-click the line and choose **Quoted By…**. It lists the
  quoting passages, earliest author first, each with:
  - its author, work, reference and dates;
  - which search found it, and how strongly;
  - the quoter's words above the edition's, with the words that differ marked.
    Accents, case, punctuation, u/v and i/j, and the lunate sigma are set
    aside, so what is marked is what the quoter changed, added or left out.

  **Go to Passage** reads the quotation where it stands, and Back returns to
  the line.
- **The link back.** In the quoting passage the quoted words are underlined.
  Click them, or use **Go to Quoted Passage** on the right-click menu, to go
  to the line they quote.

**The links were found by machine, and nobody has checked them.** They come
from two searches run over the library:

- **Testimonia** looks for an author named beside a word of citing (*ut ait
  Vergilius*, ὡς Πλάτων φησίν), then for the named author's passage that
  shares at least two four-word runs with the words around the name. Greek and
  Latin; mostly the Fathers quoting scripture, then Homer, Plato and the rest.
  In each of two development samples of 40 of its citations, 37 were genuine.
- **Afterlives** matches Latin quotations of the classics in the Patrologia and
  CSEL by shared rare words and runs of words, named or not. Only the
  quotations it rated high are included; a second AI read 40 of them and
  judged 35 genuine.

So expect roughly one link in ten to be wrong. The window opens by saying so.
It also finds far from everything: Testimonia sees a quotation only where its
author is named, and Afterlives only in Latin.

Together the links mark 10,341 lines in 640 editions, quoted from 20,466
passages, wherever your library holds both ends. Nothing is downloaded and your
library is not changed: the app carries a table of references and positions,
about 600 KB, with no text in it, and every word it shows comes from your own
library.

## Fixed

- **Back returns to the line you were on.** After a jump from Quoted By, a
  search result, a bookmark or any other window, Back went to the top of the
  work you had been reading, or to wherever an earlier jump had landed in it.
  It now goes to the line you were on.
- **LSJ entries in the translation workbench are in Greek letters.** Clicking
  a Greek word printed its dictionary entry in raw Beta Code, broken across
  lines (`polu/s ,` `Att.` `pollh/`). It now reads as Word Study shows it.
- **Stylometry counts samples and works apart.** With sampling on, the status
  line said "against 6981 other grc works" when it meant 6,981 samples. It now
  says how many samples from how many works, and saved runs store their pool in
  works. Runs you saved with sampling on are recounted from their own results
  the first time this version opens your library; nothing else in the library
  changes, and earlier versions still open it.

## Also

- The README's screenshots now show the app as it is on Windows, and there is a
  new one of Quoted By.

## On the Mac

The Mac preview is still 3.16.3. It shares this app, so Quoted By and these
fixes reach it with its next release.

1,836 tests, zero warnings.

The downloads are not code-signed, so if you would rather check one than trust it:

```
SHA-256  62cc0e1ed58c5d046be7ce9c54675ce7a1439f940fe8ced8e9440639f4709d65  ClassicaCodex-v3.18.0-win-x64.zip
SHA-256  033f27d49fd62acd42dfec353cf781e3e562fa3dd2f6ee3404bd52176a869f3b  ClassicaCodex-v3.18.0-win-arm64.zip
```
