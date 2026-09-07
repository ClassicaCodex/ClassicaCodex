# Classica Codex 3.6.5

Twenty-two fixes from a full sweep of the application. Two of them lose data
if you hit them, and one crashes a search on any library that has not built
its word index yet — which is every library between a first ingest and a first
index build.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Read this one first, if you re-ingest

**A re-ingest whose source file had gone bad destroyed the text it was
replacing.**

Ingesting clears an edition's passages and then re-parses the file to put them
back, and the two were not in one transaction — nor in the right order. If the
parse failed, the clear had already happened: the run recorded a "failed file",
carried on, and the text that had been in your library was gone. Re-running
ingest could not bring it back, because the file was still bad.

The catch block said the edition kept the passages it had. It did not.

Found by reading the ingest path, not by hitting it. An earlier version of
these notes said this bug had emptied a work in a real 2.3-million-passage
library. That was a misattribution: the work was emptied by the test harness
auditing the application, which opened every window against a live library, and
no ingest ran at all. The paragraph is withdrawn.

The bug itself is not in doubt, and there is a test for it now: it truncates a
source file mid-run and requires the edition's passage count to be unchanged.
It fails against the old ordering and passes against the new one.

Fixed by parsing before anything is cleared, so a bad file now fails with the
library untouched. The same ordering is applied to the Renaissance ingest,
which had it the same way round.

## Search crashed on a library with no word index

Auto-Tag's "Search Corpus" threw on every attempt: an error dialog, the status
stuck on "Searching...", and no results, forever. Word Study's occurrence
search failed the same way.

One character. A clause was built as `ESCAPE '\'` inside an interpolated
string, where `\'` is an escaped apostrophe, so SQLite received an empty escape
expression and refused the query before it ran.

It only affected the path taken when the word index is empty — and no ingest
builds that index. Every library is in that state between its first ingest and
its first index build, and the setup wizard invites skipping that step. So it
was new installations, on the feature the myth network exists for.

## Wrong answers

- **The places map returned mostly noise.** Clicking a place searched for its
  letters rather than its name, so clicking Ur returned 5,000 passages of which
  exactly one mentioned Ur — the rest being "during", "figure", "purple".
  Across all 240 places on the map, 57 returned more noise than mentions:
  86,727 passages that had nothing to do with the place clicked. Now 603.

  This build carried a regression, fixed in 3.6.6: a name with a space in it
  was folded into a single token — "Euxine sea" became "euxinesea" — which
  matches nothing, so eight of the 240 pins returned no results at all rather
  than too many. Single-word names, the other 232, were unaffected. Upgrade to
  3.6.6 if you use the map.

- **Half of some search results showed the wrong part of the passage.** Rows
  are cut to a readable length and were cut from the start, so a hit thousands
  of characters into an ingested section showed the section's opening and no
  sign of the word. For "Carthage", 579 of 1,074 rows showed nothing of what
  matched. Rows now sit on the match.

- **The Timeline put three authors on another man's dates.** Pliny the Younger
  was plotted on his uncle's, ending sixteen years before the nephew was born;
  Seneca the Elder on his son's; and Xenophon of Ephesus, a novelist of the
  second century AD, seven hundred years early among the historians he was
  imitating.

- **Ticking every collection hid your own translations.** A translation you
  wrote belongs to no ingested corpus, and the filter excluded anything with no
  collection — so the more thorough-looking of the two ways to say "everything"
  quietly dropped 622 of your own passages.

- **Spreadsheet exports turned references into numbers.** "1.10" is book 1,
  section 10; as a number it is 1.1, and so is "1.100". Two different passages
  became the same value, and sorting that column put section 10 between
  sections 1 and 2.

- **Spreadsheet exports containing a long passage would not open at all.**
  Excel refuses a cell over 32,767 characters and calls the file corrupt. This
  corpus has 78 passages past that limit, the longest 468,865 characters.

## Things that were there and could not be seen

- **Nine in ten artifact photographs were unreachable.** Perseus photographs
  most objects several times over — 45,688 images across 4,754 objects here —
  and only the first was ever shown. Clicking the photo now steps through them.

- **The Stylometry comparison window drew none of its own conclusions.** Every
  control on all three tabs was laid out off the page: the tables 1,920px wide
  inside a 1,068px page, and both summary labels 316px below the bottom, never
  drawn at any window size. On a fresh library it opened as three empty tabs
  that said nothing at all.

- **Research Bench's question buttons were unreachable.** Add, Edit, Remove and
  the reorder arrows sat 518px below the panel at every window size, and they
  are the only way to add a research question. Its two lists were 226px wider
  than the panel, cutting every name off mid-word.

- **Word Study opened on nothing.** Double-clicking a word in the reader should
  select it; the code looked for it in a list that had not been filled yet, so
  it selected nothing, every time.

- **The source-passage header vanished on the longest passages.** Find Echoes
  and Reception Tracker name the passage you are working on; above 65,535
  characters the caption was refused outright and the strip drew nothing.

- **Comparing more than three translations made all of them unreadable.**
  Thucydides carries twelve here; at an equal share of the panel each column
  was 74 pixels, about one word wide. Columns now hold a readable width and the
  panel scrolls.

- **Twenty-two controls, across nine windows, were laid out outside the window
  they live in** — the same cause each time, now none.

## Crashes on ordinary actions

- Dragging a tag on a narrow Myth Network window.
- Opening the Translate dialog when its settings folder is unwritable.
- Exporting a bibliography to a read-only folder, or to a file already open in
  a reference manager.

## Also

- Three dialogs — Database Location, Ingest Corpus, Load Lemmas — never
  followed the theme and opened as bright white windows out of a dark
  application.
- Error messages in six windows were drawn in a red that is unreadable on the
  dark surface, at about 1.7:1 contrast.
- Clicking an author's name on the Timeline did nothing; only the bar answered.
- The Myth Network hid every edge after a reload with lighter connections.
- Auto-Tag's category box was discarded in silence for a tag that already
  existed. It still keeps the existing category — the box carries a default,
  and overwriting silently would be worse — but it now says so.
- A new library's main window says "No texts yet" rather than showing three
  blank panels.

## Checks

**1,102 tests, zero warnings on a clean build.** The count is up by 52: the
crash above is covered by tests that leave the word index empty on purpose,
because every existing test built one first, and the ingest fix is covered by a
test that truncates a source file and requires the passage count to be
unchanged.

All 80 windows now open with real arguments from a full library — including the
53 that need a work, a passage or a project and had never been opened by
anything — and none of them lays a control outside itself.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  736F90BF85DDA8F689D95505753FE510749186A7176585FED3BB50B7BFCE70F4
```
