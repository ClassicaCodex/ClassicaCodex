# Classica Codex 3.6.0

Plato can be cited.

3.4.1 said this was filed for 3.5, and 3.5 shipped without it. Here it is:
*Republic* 327a where the reader used to say `1.327.1`, and *Nicomachean
Ethics* 1094a1 where it said `1.1.1`.

**Your library does not need rebuilding.** A one-click step reads the
references out of the texts you already have — about twenty seconds — and
writes nothing else. Details below.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## What was wrong

Every article, syllabus and commentary in the field cites Plato by Stephanus
page and Aristotle by Bekker number. Perseus records both — but not as
structure. The `<div>` carries the page, and the part that makes the citation
precise arrives inline, as an empty marker in the middle of the prose:

```xml
<milestone n="327"   unit="page"    resp="Stephanus"/>
<milestone n="327a"  unit="section" resp="Stephanus"/>
<milestone n="1094a" unit="page"    resp="Bekker"/>
<milestone n="5"     unit="line"    resp="Bekker"/>
```

A `<milestone>` carries no text, so the parser skipped it and its number went
with it. What survived was the page from the enclosing division and a paragraph
index this application invented — `2.1` for *Euthyphro* 2a, a reference that
cannot be looked up in any edition, commentary or syllabus anywhere.

| | was | now |
|---|---|---|
| *Euthyphro*, first line | `2.1` | **`2a`** |
| *Republic*, first line | `1.327.1` | **`327a–c`** |
| *Nicomachean Ethics*, first line | `1.1.1` | **`1094a1–15`** |
| *Iliad*, first line | `1.1` | `1.1` — unchanged |

## How the two schemes differ

They divide a page differently, and the difference is in the markers rather
than in anything this application decides.

**A Stephanus section is already whole.** `327a` is page and column together,
so it is taken as it stands.

**A Bekker line is not.** Bekker's own `a` and `b` are *columns* and belong to
the page; the line numbers restart in each one and mean nothing apart from it.
They are composed the way they are written in print — *NE* 1094a1 — and the
line is dropped at a column boundary rather than carried across it, where it
would name a line that exists and is somewhere else entirely.

One rule governs both: **a passage is cited where it begins.** *Euthyphro*'s 2c
opens three words before the end of a speech; that speech is 2b, where the
reader started it, and 2c governs what comes next. This is what a printed
edition means by putting the letter in the margin.

Where a passage covers more than one section it says so, because Perseus
divides the *Republic* a whole Stephanus page to a paragraph and calling that
`329e` would be true of its opening line and of nothing else in it. Ranges are
shortened as they are printed: `328a–e`, `1094a1–15`, `1094a15–b10`, and
`329e–330e` in full because those two share nothing a reader would drop.

## The one thing to do

Existing libraries carry the texts but not the references, because the markers
were discarded on the way in. Rebuilding the passages would take about an hour
and the word index with them, to change one column.

Instead there is a new step in Setup — **"Stephanus & Bekker Citations
(Plato & Aristotle)"**. It downloads nothing. It reads the markers back out of
the texts already on your disk and writes only that column onto the rows
already there:

```
Updated 60,458 passages across 95 editions.
took 17.2s
```

It changes no text, moves no citation, and leaves bookmarks and tags exactly
where they are. It is safe to run twice, and on a library with no Plato it says
so and stops. A library ingested from here on gets the references as it is
built and needs no step at all.

## Where it shows

The reader, and everything that quotes a passage back at you: export — single
passage, range, and the filename an export opens with — search results, the
concordance, Word Study, the apparatus, bookmarks, the tag browser, the myth
network, morphology, reception, cross-language echoes, the translation
workbench, the research bench and its reading queue.

Two details worth having:

**The translations carry it too.** Perseus marks Jowett and Ross as well as the
Greek, so the parallel reading panes now line up on the same Stephanus page
rather than on two different structural references.

**A stored research echo shows it as well** — including one captured months
ago, before any of this existed. The reference is resolved from the passage
when the record is read rather than saved into it, so nothing had to be
recaptured and a later re-ingest cannot leave the value stale.

## What is unchanged

Most of the corpus. Homer is cited by book and line, and the reference this
application already stored says exactly that: 15,687 passages of the *Iliad*
parse to byte-identical citations and no canonical reference at all. Nothing
was added to a text whose source does not record one.

The structural citation is also untouched wherever it was already stored.
Bookmarks, tags, apparatus entries and the pairing between an original and its
translation all resolve through it, and several speeches legitimately share one
Stephanus section — so the reference a reader cites by is kept beside the one
the application resolves through, never instead of it.

## Smaller things

- **The Myth Network help had gone stale.** It said sixty portraits were
  included; there are a hundred and fifteen.
- **The hidden thing added in 3.5.0 no longer throws when its window is closed
  at the wrong moment**, which was a message from GDI+ about a bitmap released
  a fraction of a second earlier.

## Checks

Schema 38 adds one nullable column; the upgrade is instant on a 2.9 GB library
and needs no rebuild. Verified against a copy of a full 2.3-million-line
library: identical passage count, identical distinct citations, bookmarks and
tags untouched, 60,458 passages gaining a reference. A bookmark and a tag
placed on *Euthyphro* 2.3 both come back reading 2b, and all 507 apparatus
entries on Jowett's *Laws* carry a Stephanus page.

The archive was built with correct forward-slash paths — 240 entries, zero
backslashes, all 239 icons inside an `Icons` folder — then extracted and
launched from that extraction to confirm it runs as shipped.

**1,006 tests, zero warnings on a clean build.**
