# Classica Codex 3.19.0

The dictionaries' citations open the passages they cite, and the rabbinic texts
grow to the Mishnah, the Tosefta, both Talmuds and the Targumim.

**[Download for Windows](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
`win-x64` for most PCs, `win-arm64` for Windows on Arm (not yet tried on an Arm
PC). Extract all of it and run `Classica Codex.exe`. Windows will show a blue
"Windows protected your PC" box on first run because the app isn't
code-signed; click More info, then Run anyway.

## Dictionary citations

LSJ and Lewis & Short back almost every sense with a quotation, and readers
trust them because each one says where it comes from. The app used to throw
those references away when it read the dictionaries. It keeps them now, and
about half of them open the passage they cite.

- **In Word Study.** Each citation is back where the dictionary printed it, in
  a smaller, fainter face so the definition still reads first. A citation in
  the accent colour opens a passage in your library: hover to see the line,
  click to go there. The window stays open, and Back in the reader returns. A
  citation the library cannot open (a work you do not have, a papyrus, a
  numbering no edition follows) is plain grey text.
- **In the reader.** A line that LSJ or Lewis & Short cites ends with a §,
  after Quoted By's “. Right-click it and choose **Cited in the
  Dictionaries…** for the entries that cite it: the headword, the sense, the
  words the entry prints before the citation (usually the phrase it quotes and
  its gloss), and the whole entry. Translations are never marked.
- **Run "Dictionaries (LSJ + Lewis & Short)" in Setup again.** The entries
  already in your library were read without their citations, so neither of the
  above appears until you do. It takes a few minutes and replaces the two
  dictionaries rather than adding a second copy. Until then, Word Study says
  how to get the citations.

**How many open, and how well.** Of LSJ's 579,070 citations, 298,457 (51.5%)
open a passage in the library they were measured on; of Lewis & Short's
380,656, 186,946 (49.1%). The rest carry no reference the app can follow, name
a work that library does not hold, or cite it by a numbering no edition in it
follows. Where a dictionary cites a whole poem, letter or chapter, its first
line is the one opened and marked. That library has 128,241 lines marked from
LSJ, in 882 editions, and 77,784 from Lewis & Short, in 165.

An AI (Claude) read 40 citations chosen at random: 37 opened the passage the
dictionary quotes, and the other three landed one section or chapter away,
where the edition divides the text differently. No second reader has checked
a sample yet.

## Rabbinic texts

"Rabbinic texts: Mishnah, Talmud, Targum and midrash" grows from 33 downloads to
about 630: 266 works and 626 editions, about 140 MB.

- **The Mishnah,** all 63 tractates, in the printed text and, for 61, Codex
  Kaufmann, with Joshua Kulp's English and, for 61, Lazarus Goldschmidt's
  German.
- **The Tosefta,** 61 tractates, in the Vilna text and, for 33, Lieberman's
  edition of Codex Vienna.
- **The Babylonian Talmud,** now all 37 tractates, with Goldschmidt's German
  beside every one. English is only beside Berakhot, Hagigah, Sukkah, Megillah
  and Makkot, because the complete modern English is non-commercial.
- **The Jerusalem Talmud,** all 39 tractates, in Heinrich Guggenheimer's English.
- **The 15 minor tractates,** Avot de-Rabbi Natan among them, in A. Cohen's
  English.
- **The Targumim:** Onkelos, Jonathan on the Prophets, Pseudo-Jonathan, the
  Jerusalem Targum and the Targumim to the Writings, with English where a free
  one exists.

Every version is in the public domain or under CC0, CC BY or CC BY-SA, and each
file's licence is checked again as it is read. Midrash Rabbah, Sifra, the
Mekhilta and the Zohar are not here: Sefaria gives no licence for most of their
Hebrew and Aramaic.

**If you installed this step before,** it now shows as unfinished. Run it again
to add the rest. Nothing you already have moves: its references, your bookmarks
and the AI translations filed against them all stay where they were.

## Fixed

- **Footnotes in the Sefaria texts come out whole.** A note with italics in it
  used to stop at the first closing tag and leave the rest of itself in the
  text. And a pattern meant for footnote markers also matched Goldschmidt's
  Mishnah numbers, so it deleted the opening words of the mishnah after them;
  about 19,100 words of his German are back. Run the step again to get the
  corrected text.

## Known

- Tosefta Terumot 5.8 loses one sentence, because the source closes a note in
  the wrong place.
- In Goldschmidt's German, 409 Mishnah numbers run into the next word
  ("iiiWER"). This was already the case before this release.
- Lewis & Short is in the library twice, in its original form and its 2023
  revision. The new Cited in the Dictionaries window lists each entry once.

## On the Mac

The Mac preview is still 3.16.3. It shares this app, so these changes reach it
with its next release.

1,889 tests, zero warnings.

The downloads are not code-signed, so if you would rather check one than trust it:

```
SHA-256  d74db29c118ae0ce1ca80dd4a5a4622d5ceacba7e24c3f9e78f7e881f43f9a1e  ClassicaCodex-v3.19.0-win-x64.zip
SHA-256  d476060864a52a771a95a97ee501a0ecb2888cb99f1ff1ef6b761402e98e2e1b  ClassicaCodex-v3.19.0-win-arm64.zip
```
