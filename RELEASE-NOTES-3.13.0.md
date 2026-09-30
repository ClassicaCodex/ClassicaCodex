# Classica Codex 3.13.0

Three medieval collections and a window for looking at handwriting.

The Old French chansons de geste and Dante's *Commedia* install as one download
each and join the library you read. **Medieval Hands** is something else: 313
manuscripts from CATMuS-Medieval, every line photographed beside a transcription
of it, in its own window — and for 121 of them, the actual leaves from the
library that holds the book.

Nothing here changes your data. **No schema change to anything you already have,
no re-ingest, no re-index**, and nothing to run afterwards. Four new optional
steps appear in the Setup Wizard; leave them alone and your library is exactly
as it was.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
More info, then Run anyway.

## Old French: the chansons de geste

Floovant, Otinel in five manuscripts, Garin le Lorrain in ten, Aspremont,
Fierabras, Huon de Bordeaux, Girart de Vienne — 33 texts in Old French,
Anglo-Norman and Walloon, transcribed from the manuscripts at the École
nationale des chartes. One 6 MB download.

Nineteen of them arrive twice: once letter for letter as the scribe wrote it,
abbreviations and all, and once in the reading a modern edition prints. The
edition dropdown switches between them, the way it does for Menota and Middle
High German.

Every word carries its dictionary headword and its grammar, so Word Study works
the moment the download finishes. There is no second download for it — the
corpus annotated its own text.

## Italian: Dante's Commedia

All 14,233 lines, every one of the 122,000 words parsed and mapped to its
headword. 2.4 MB. Cited by canto and verse, as Dante has been cited for seven
centuries, so Inf. 5.142 here is Inf. 5.142 in any printed edition.

Be clear about what this is: Petrocchi's critical edition, not a transcription
of a manuscript. There is no scribe's spelling and no second reading to switch
to, unlike the Norse, German and French collections. It is one poem rather than
a corpus because the large Old Italian corpora — OVI, Corpus Taurinense —
cannot be downloaded at all.

## Medieval Hands

Every other collection in this library arrives already transcribed, with the
step where somebody looked at parchment and decided what it said left out. This
window is that step: **194,808 lines from 313 manuscripts**, seventh to
sixteenth century, each line photographed and set beside a diplomatic
transcription of it. Latin, French, Castilian, Middle Dutch, Italian, Catalan,
Occitan and four more; Caroline, Textualis, Cursiva, Hybrida, Semitextualis,
Praegothica, Humanistica, Uncial and four others. A lens over a line of script
on the toolbar opens it.

The transcriptions are diplomatic, so a line reads `ꝯcessisse` and not
*concessisse*, `nr̃e` and not *nostre*. That is the point of having it.

**It is not part of the reading library, and that is deliberate.** CATMuS
shuffles its lines and records no page or line number, so the lines of a
manuscript cannot be put back into the order they were written in. Loading them
as an edition would produce a work whose every line was genuine and whose order
was invented — and it would then be read, searched, cited and bookmarked as
though the order meant something. Here they are specimens of a hand, which is
what they are.

### Two downloads, because the text is 0.1% of the dataset

The transcriptions come from the Setup Wizard: about **eight megabytes** for the
whole corpus, five to ten minutes, and nothing saved to your download folder.
The dataset they are read out of is 24.7 GB, almost all of it photographs; only
the parts holding text are fetched.

The line photographs are the other 99.9%, so they are fetched one manuscript at
a time from inside the window, which states each one's size before you agree to
it — a median of 30 MB, the smallest under one and the largest 1.2 GB. Once
fetched they are on your computer and the window needs no connection at all.

### Searching

Type a word and the search folds it the way the rest of the app folds words, so
`nre` finds `nr̃e` and `stet` finds `ſtet`. Without that, a diplomatic
transcription is unreachable from a keyboard. Tick **Exactly as written** to
search the transcription character for character instead, which is how to ask
where a particular abbreviation sign appears — the Tironian et, ꝯ for *con-*,
ꝑ for *per-*.

### See the pages

CATMuS holds lines, not pages, and no pictures beyond them — every image in it
is a strip of writing. The pages belong to the library that holds the book, and
where that library publishes them, **See the pages** shows them: the ruling, the
columns, the rubrics, the decorated initials, the marginalia, and whatever
illumination the manuscript has. Vat. Reg.lat. 1616 opens on a historiated
initial; Arsenal 3516 on a calendar of blue and red KL monograms.

Nothing is downloaded. Each leaf comes from the library's own server as you look
at it and is kept only while it is on screen — the same rule the Places Map
follows for Perseus's photographs. The credit and the conditions of use shown
under the page are read from the library's own record, not assumed, because they
differ: the Vatican's own wording is "Images Copyright Biblioteca Apostolica
Vaticana", e-codices is CC-BY-NC, and Gallica links its own conditions page.

It works for **121 of the 313**, and the panel under the manuscript list says
which. That is a limit of what can be looked up rather than of the idea. The
Bibliothèque nationale de France holds half of CATMuS and has no index that
answers a shelfmark, so the only route is its full-text search — which, asked
for "Espagnol 480", offers a nineteenth-century bibliography of books about
hunting. Every Gallica record carries its own shelfmark, so each answer is
confirmed against the shelfmark that was asked for and discarded otherwise;
what ships is only what verified. Munich, Oxford, the British Library, KBR, the
Escorial, Vienna and Toronto all publish their manuscripts too, and none of them
can be asked for one by shelfmark yet.

### The licence is mixed, and the window says so

CATMuS is labelled CC-BY-4.0 and part of it is not: the manuscripts from the
Towards General Castilian HTR project are CC-BY-NC-SA-4.0. The photographs are a
third case again, staying under the terms of the library that holds the
manuscript. The window names the transcription licence beside whichever
manuscript is selected and the image terms in the confirmation before a
download. None of it is bundled — the catalogue of what exists ships, the text
does not.

## Also

A long s now folds to s everywhere, so a search for *stet* reaches `ſtet`. This
went in with Middle High German and matters again here: the Old French
transcriptions write what the scribe wrote.

The toolbar gained an eighteenth icon and still fits its window.

1,643 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  PENDING
```
