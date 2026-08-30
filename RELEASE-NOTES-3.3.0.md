# Classica Codex 3.3.0

The last release was 3.0.1. This one carries everything since: the stylometry
validation bench from 3.1.0, four new collections and collection filtering from
3.2.0, and collation — comparing two editions of one work and saying where their
editors disagreed — which is what having overlapping collections was for.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`, and a setup wizard does the rest.
Windows will show a blue "Windows protected your PC" box on first run because the
app isn't code-signed; click **More info**, then **Run anyway**.

> **Back up your library file before first launch.** This upgrade takes the
> database from schema 14 to 34 — twenty migrations, run automatically on the
> first start. They are designed to be replayable and are tested against real
> pre-existing libraries, but a copy of `classicacodex.db` costs nothing and is
> the only thing standing between you and a bad afternoon. It lives in
> `%LocalAppData%\ClassicaCodex\` unless you moved it.

---

## Collate two editions of one work

Holding seven collections means holding some works twice, and two independent
printings of one text are the raw material of a collation. Where the library has
a work twice — from two collections, or as two editions inside one — the
**Collate** button shows what the editors disagreed about.

The whole difficulty is that comparing two editions character by character
reports almost every line as different, which is worse than no comparison
because it looks like evidence. So differences are graded, and each passage is
filed under the first thing that explains it:

- **punctuation** — spacing, case, brackets, the elision mark. Editorial
  brackets count here deliberately: one editor bracketing a word another prints
  plainly disagrees about the word's standing, not about whether the word is
  there
- **spelling** — Greek accents and breathings, final sigma, Latin u/v and i/j,
  the ae digraph however it is written
- **line division** — the same words broken across two lines at a different
  point, usually one edition hyphenating at a line end
- **THE WORDS** — the editions print something different. The only one that is a
  reading, and the view opens on it

The counts above the list say whether a pairing is worth reading before you read
any of it. Some pairs cannot be collated at all, and the window says so rather
than inventing a result: two editions that divide a work differently still
collide on plain numbers, so they look aligned and then disagree at every one.

Nothing here says which reading is right. That is not a question the app can
answer and it does not pretend to.

Every collation exports to CSV, tab-separated text or Excel, carrying both
editions with their CTS identifiers, the counts, the filter applied and any
caution shown — because four columns of Greek with no record of which editions
produced them is a file that gets misread later.

## Getting the fold right

The grading is the feature, so it was measured against a real library rather
than reasoned about. The first run said 57.7% of 16,445 shared passages differed
substantively, which cannot be true — no two editions of Eusebius differ on
every line. Three specific faults, each found by looking at what was being
flagged:

- **One character was most of it.** Perseus writes the elision mark as U+1FBD, a
  Unicode *symbol*; First1KGreek writes it as U+02BC, a modifier **letter**.
  Only the first fell out of the punctuation test, so every elided word in seven
  plays of Aeschylus read as a textual variant — 2,714 passages.
- **Hyphenation across a lyric line break.** `Ἀχαι-` then `ῶν` against `Ἀχαιῶν`
  makes two adjacent lines differ where the text does not, always in pairs.
- **Sharing citation references is not alignment.** Several CSEL and Patrologia
  Latina pairings reported every shared passage as a variant, because their "1"
  and the other's "1" were not the same passage.

That took the rate to **23.8%**, and the Aeschylus pairings to 12–21% per play.
Spot-checking those finds real readings: πευθοῖ against πειθοῖ, ξύμφρονε ταγώ
against ξύμφρονα ταγάν, λήμασιν ἴσους against λήμασι δισσοὺς.

One hypothesis was checked and dropped rather than shipped. Sophocles' *Ajax*
comes out at 89%, and the first guess was OCR damage — Latin letters inside
Greek words. That edition has 11 such lines out of 1,698, which explains
nothing, and the editions scoring highest on the measure turned out to be
scholia quoting manuscript sigla, which are perfectly sound. The 89% is honest:
it is simply a different, older edition.

## Also in this release, from 3.1.0 and 3.2.0

**Four more collections.** CSEL — the critical editions of the Latin Church
Fathers. The Patrologia Latina — Migne's collection, Tertullian to the twelfth
century, and much the largest thing the app can install. Bodin's *Six Books of
the Commonwealth* in French, his own Latin, and Knolles's English. Seven
collections in all, from three.

**Filtering by collection.** With several installed, "search only the Church
Fathers" is a question the language filter cannot answer, since they and the
classical Latin texts are both Latin. Search, the library tree and the
recent-search list all narrow by collection, and a default collection settles
which edition a work opens on when two carry it.

**A validation bench for the stylometry** — leave-one-out validation, a
parameter-stability grid, and controlled perturbation with synthetic
contamination. It reports how much contamination the method could detect at all,
which is what a null result needs to mean anything.

**Marks in the margin of the line.** A `?`, `#` or `★` at the end of a passage
says an inquiry has been started from it, that it carries a tag, or that it is
bookmarked. Drawn rather than stored, so copying or exporting still gives you
only the text.

## Fixes

- **Three thousand lines that could be searched and not opened.** First1KGreek
  ships the notes published alongside the Septuagint Isaiah as a separate
  edition, and a gap in how CTS version identifiers were read left it classified
  as neither original nor translation — so the reader, which sorts editions into
  its two panes on that question, showed it in neither. Ingested, indexed,
  returned by searches, and impossible to open.
- **Fourteen passage lists could crash on the text they exist to show.** Asking
  for a horizontal scrollbar makes WinForms measure every item with GDI+, and
  that measurement throws on characters the list font cannot resolve — which the
  Menota transcriptions carry throughout. Reported against the places map; it
  was the same fault in the Myth Network, the Timeline, the tag browser,
  bookmarks, echoes, the reception tracker, compare, auto-tag, the apparatus,
  Word Study and morphology.
- **Dark mode reached the last menus that were missing it.** The reader's
  right-click **Show** submenu is rebuilt each time the menu opens, so the pass
  that themes every context menu reached an item with no drop-down and returned
  before touching it — a pale strip down the icon margin, and entries in dark
  ink on a dark surface. The same shape turned out to affect the export menu on
  the parameter grid, perturbation and validation benches, which had never been
  themed at all.
- **Search's "translations only" filter disagreed with the reader** about what
  counts as a translation, so an edition the importer could not classify was
  findable with the filter off and invisible with it on.
- **An import no longer renames authors already in the library.** Installing a
  collection could rename authors someone already knew how to find.
- **Hypothesis assessments keep when they were first recorded**, rather than
  being restamped as today's every time the matrix was saved.

## Under the hood

No schema change in 3.3.0 itself — the migrations in this download come from
3.2.0 and earlier. Four misattached documentation blocks were found and moved
back onto the functions they describe, including two long explanations of the
stylometry that had been describing the wrong method. Nine compiler warning
types down to three.

**572 tests passing**, 0 failed, 0 skipped.
