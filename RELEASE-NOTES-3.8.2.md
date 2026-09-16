# Classica Codex 3.8.2

Importing Medieval Nordic manuscripts appeared to stall. It was not stalled —
it was going to finish, several hours later.

Nothing here changes your data. **No schema change, no re-ingest, no re-index**,
and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The Menota import stopped at the same file every time

Reported as "stalls on 44 of 91", and that is exactly what it looked like: the
file name stopped changing and nothing else happened.

Every Menota manuscript is one file containing many separate works, and the
importer rewrites one edition per work. Before rewriting an edition it removes
that edition's entries from the word index — and the word index is stored as
(word, passage) pairs, in an arrangement that is half the size it would
otherwise be but offers no way to look an entry up by passage alone. Removing
entries that way makes SQLite walk the index once for every distinct word in
your library: 2.2 million probes, whatever the size of the edition.

Measured on a full library: removing the entries for an edition with **no lines
in it at all** took **12.6 seconds**. `Holm-A-80.xml` — the 44th of the 91 files
— declares **275 works**, so that one file was about to spend the better part of
an hour before moving on, and there were 47 files behind it.

The entries are now removed by naming them, which is a key lookup apiece. The
same operations on the same library now take between 0.00 and 0.11 seconds. The
import reports each file as it goes and finishes in the time it takes to read
the manuscripts.

## What that cost, stated plainly

Naming the rows means the cleanup removes the rows a passage's text can
produce. If the index has already drifted from the text — something none of the
application's own paths do — a stale entry is left behind rather than removed.

Such an entry is invisible: search asks the index for passages and joins them
back, and one pointing at a passage that no longer exists drops out. **Rebuild
Word Index** clears any that accumulate. The alternative was keeping a guarantee
that only matters in a state the program does not create, at the cost of making
a shipped feature unusable, and there is a note in the code saying what to do if
that judgement ever turns out wrong.

## Also

The tokenizer that decides what counts as an indexable word now lives in one
place. It was private to the code that *writes* the index, and the code that
*removes* entries could not reach it — which is the sort of arrangement where
two copies appear and quietly disagree, and a disagreement here leaves rows
behind without saying so. A test now checks that what the builder writes is
exactly what the cleaner offers to remove.

1,391 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  D9708DDB35B42A53AD3E7A653F46C3FFD1A29096F7401C7BBC00BBF4AD459EEA
```
