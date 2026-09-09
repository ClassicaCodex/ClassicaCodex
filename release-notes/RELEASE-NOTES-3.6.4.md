> **Historical.** These are the notes for Classica Codex 3.6.4, which is no longer
> available for download. Only the current release is published; get it from
> [the releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).
> Any download link or SHA-256 below refers to the 3.6.4 ZIP, not to the current
> one, so do not check a current download against a checksum printed here.

# Classica Codex 3.6.4

Two crashes, and four more things found by chasing what caused them.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The crash in every result list

Auto-Tag, and any other list showing search results, could fail outright with
*"A generic error occurred in GDI+"* — no window, no results, nothing to act
on. This is **not new in 3.6.x**; it reproduces the same way in earlier
releases and has been reachable the whole time.

It needed two conditions at once, which is why it had survived being looked
for. Lists are given a horizontal scrollbar, so Windows measures every row as
it is added. GDI+ measures text two ways: a simple path with no practical
length limit, and a shaping path — used when the text contains a format
character, a combining mark, a control character, or a right-to-left script —
that fails above roughly 32,000 characters in one go.

Neither alone does anything. A very long plain passage measures fine; a short
awkward one measures fine. It takes both, and this library has passages that
are both: Migne and CSEL works ingested as whole sections rather than lines,
carrying soft hyphens left at the line breaks of the printed page by whoever
scanned them. Facundus of Hermiane is 32,132 characters with 195 of those, and
in a search for "Athena" it was row 871 of four thousand.

Result rows are now cut to a readable length. Twenty-two passages in a
full library meet both conditions; eighteen of them crashed a list before, and
all twenty-two are fine now.

**The reader is deliberately exempt** and still shows a passage entire, however
long — its panes are never measured, which is what makes that safe. The longest
in the corpus is 77,659 characters and it renders whole.

## Morphology search

Every morphology search that matched anything failed with an internal error.

Unlike the one above, this was self-inflicted, and recently: it arrived in
**3.6.0** and has been broken in every release since — 3.6.0, 3.6.1, 3.6.2 and
3.6.3. That release added the canonical reference to these results and the
query that fetches them was never updated to select it, so the code read a
column that was not there.

The same slip is fixed in two more places in the research views, where the
Stephanus and Bekker references were being fetched and then never displayed —
a part of 3.6.0 that has therefore never worked at all.

## Also fixed

- **Exported passage sets lost their canonical references.** Exporting a set
  of search results wrote the raw reference where the list beside it showed
  the Stephanus or Bekker one — the export knew how to print it and was never
  given it, at any of thirteen places that build one. Exporting a run of lines
  from the reader was never affected.

- **Auto-Tag now shows you the match.** Rows are cut to a readable length, and
  a whole section can put the word you searched for thousands of characters
  past the cut — in the one screen that asks you to confirm each match before
  it writes tags. Of 2,203 rows for "Athena", 1,574 showed the matched word;
  now 2,202 do. Each row is a window positioned on the first match rather than
  the opening of the passage.

- **Six captions were cut off**, all of them since the first release. The worst
  was the Myth Network legend, which stopped at "click one to browse its" and
  took the right-click-for-artifacts hint with it.

## Checks

**1,050 tests, zero warnings on a clean build.** The count went up because
there is now somewhere to put tests for the interface itself; the existing
project cannot reference it.

Every read query in the application — 102 of them, across all 28 repositories
— was run against a full 2.3-million-passage library, which is the only thing
that would have caught the morphology fault. A first pass reported no
failures while a third of those queries had matched nothing and therefore
proved nothing; each was re-run with a value drawn from the table it reads.

The display audit that checks captions had been passing the ones listed above.
It measured how tall the text is, when what matters is how tall the caption
needs to be to hold it, and those differ by up to a line. It now measures the
rendering, and errs toward reporting. Some of these captions were only found
because somebody looked at a screenshot after the tool had said everything was
fine, which is the right order to trust those two things in.
