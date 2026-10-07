# Classica Codex 3.15.1

The research area, put right.

Everything behind **Research…** — the Research Bench and the windows it opens —
was driven end to end against a full library, its code read through, and every
window checked at its default size and at its smallest. The Bench itself held
up. What went wrong was around it, and three of those things could misfile or
hide your work, which is why this is a release now rather than a line in the
next one.

One small repair runs the first time you start it: a research project left
pointing at a work that is no longer in the library is moved back to that work
by its URN. Most libraries have none, and in those it changes nothing. **No
re-ingest, no re-index**, and nothing to run afterwards.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
More info, then Run anyway.

## Things that could cost you work

**A reordered list could undo itself.** Move a research question up, remove a
different one, and the two you had reordered could come back tied, in their old
order. Every list in the research area did this — questions, evidence, scholarly
claims, findings, hypotheses, experiments and the reading queue. A list that was
already affected keeps the order it shows now; nothing else will move it.

**The scholarly claims matrix could save over the wrong claim.** Opened from
Project audit → Open item, or after saving any claim but the first, it
highlighted one claim and put another in the editor — and Save wrote to the one
in the editor. If you have used Open item on a claim, look over the first claim
in that project. The matrix's fields also ran off the right of the window, with
their drop-down arrows; they now fit at any size.

**A research project could vanish after an upgrade.** Version 3.4.0 merged a
handful of Patrologia Latina works that the library had listed twice — Paulinus
of Nola's *Carmina* among them. A project on the copy that was removed kept
pointing at it, and the Bench never listed it again. The repair above finds it
and puts it back.

## AI and bibliography

- **AI evidence is checked against what was actually sent.** A long work is cut
  to fit before it goes to Gemini, and a passage cited from beyond the cut —
  which the model never saw — used to be saved as verified against your
  edition. It is now rejected.
- **AI replies are read more forgivingly.** A reply that gave a list where the
  question asked for text, or put a sentence before its answer, was rejected
  whole, as if it were not JSON at all. Now only what is missing is lost.
- **Importing RIS or BibTeX:** two articles from the same journal, or two
  chapters of one edited volume, are no longer refused as duplicates of each
  other; a quotation mark inside braces (`M"uller`) no longer drops the rest of
  the file; author lists wrapped onto a second line, and names in braces such as
  `{Barnes and Noble}`, split where BibTeX splits them; LaTeX accents such as
  `M{\"u}ller` arrive as the letters they stand for; `@string` abbreviations are
  expanded; and DOIs written `https://dx.doi.org/…` are recognised.
- **Exporting:** a book chapter stays a chapter, with its book's title, instead
  of going out as `@misc`; `& % # _ $` are escaped so LaTeX will build with them;
  and a page range comes out as `12--15` however it went in.

## The windows

- **Corpus snapshots** no longer freeze the window while they run. Cancel works,
  and the result stays on screen instead of being overwritten by the last
  progress line.
- **Open source** in a reading queue or Parallel Studio opened from the
  Hypothesis Lab now goes to the passage.
- Evidence promoted from **Echo investigations** shows in the Bench as soon as
  the window closes.
- Saving a search to the **archived project** it was started from works.
- A dossier export that cannot write its file says so, rather than putting up
  the crash dialog; **Draft from linked evidence** stops if the finding could
  not be saved, instead of sending Gemini the old version.
- The **project audit** flags a question whose evidence has all been rejected.
- Bench menu items that need a project say so instead of doing nothing; the
  Bench's header buttons stay on screen when the window is narrowed; no research
  window can be shrunk until its buttons disappear; and double-clicking a column
  header no longer opens the Parallel Studio.
- Crossref leads with no publication date no longer read "Author ()".

1,734 tests, zero warnings.
