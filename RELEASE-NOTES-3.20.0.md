# Classica Codex 3.20.0

A citation copied from the Atlas of Ancient Myth or the reports built from this
library now opens in the app: **Go ▸ Go to Citation…**, or Ctrl+G.

**[Download for Windows](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
`win-x64` for most PCs, `win-arm64` for Windows on Arm (not yet tried on an Arm
PC). Extract all of it and run `Classica Codex.exe`. Windows will show a blue
"Windows protected your PC" box on first run because the app isn't
code-signed; click More info, then Run anyway.

## Go to Citation

Click a reference on the Atlas of Ancient Myth or in the Magic in the Ancient
Texts report and the page copies a citation: the edition's identifier, a space
and the reference, such as `tlg0020.tlg001.perseus-grc2 457`. The app had no
way to take one until now.

- **Ctrl+G opens a small window.** If the clipboard holds a citation, it is
  already filled in. The line it names is shown underneath as you type: the
  author, the work, the reference, the edition and the text itself.
- **Return opens it in the reader,** with the line selected. Back returns to
  where you were.
- **Other shapes work too:** a work's identifier in place of an edition's
  (`tlg0020.tlg001 457`), a whole CTS URN
  (`urn:cts:greekLit:tlg0020.tlg001.perseus-grc2:457`), quotation marks around
  it, and identifiers with accented letters, such as Menota's. A range opens at
  its first line.
- **An edition your library doesn't have** (`perseus-grc1` where it holds
  `grc2`) is looked up in the same work's other editions, in the same language
  first. The window says that it has done this, because two editions can number
  the text differently.
- **A reference the edition doesn't have** is named as missing, and the button
  becomes **Open at the Start**, which opens the edition at its first passage.
- **A museum object the Atlas cites,** such as `artifact:London E 163`, opens
  its photographs in the Art and Archaeology browser.

**Checked against a real library.** Every distinct citation the Atlas and the
Magic report copy, 5,670 in all, was tried against a library with every Setup
step installed. 5,647 opened at exactly the line copied and all 14 museum
objects were found. The other 9 are Medieval Hands lines (see below).

## Known

- **Medieval Hands lines are not opened.** The Atlas copies some manuscript
  lines as `CatmusLines – Paris, BnF, lat. 9768 #120078`. The window names the
  manuscript and says to choose it in Medieval Hands, on the Tools menu.

## On the Mac and Linux

The Mac preview is still 3.16.3. It shares this app, so Go to Citation reaches
it with its next release. Its next download, and the Linux one, will also leave
out PDFsharp's sample fonts, which are Microsoft's and not ours to ship. The
Windows download already left them out.

1,919 tests, zero warnings.

The downloads are not code-signed, so if you would rather check one than trust it:

```
SHA-256  4b2e33fddb86df0b930a82efd0dbb9aaf3db96da167ccb8c5e70ee1f8d733bb2  ClassicaCodex-v3.20.0-win-x64.zip
SHA-256  67db9b828252328edbb8eca249e46c79519229f57346dda049ced402f0668a9e  ClassicaCodex-v3.20.0-win-arm64.zip
```
