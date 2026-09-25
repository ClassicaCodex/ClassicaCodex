> **Historical.** These are the notes for Classica Codex 3.11.1, which is no longer
available for download. Only the current release is published; get it from
[the releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).
> Any download link or SHA-256 below refers to the 3.11.1 ZIP, not to the current
> one, so do not check a current download against a checksum printed here.

# Classica Codex 3.11.1

Some passages had a gap in the middle of a word. Around 86,000 of them, which
is about one in twenty-seven.

```
... ließ sich unter den Lakedämoniern also ver nehmen:
... die Athener und ihre Bundes genossen haben Frieden geschlossen
... Nach ihm nahm Euphemos, der Ge sandte der Athener, das Wort
```

Those are not typographic accidents in the source. They are words broken across
a printed line by a soft hyphen — U+00AD, a character with **no glyph** — which
is why it never looked like a stray hyphen and always looked like a space.

**This one wants a re-ingest**, unlike the last few. See *What to do about an
existing library* below; there is no schema change and nothing is destroyed if
you leave it.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## How much of the library

Measured across a full install — 86,188 of 2,340,260 passages:

| collection | | affected | of | |
|---|---|---:|---:|---:|
| patrologia-latina | Original | 85,026 | 286,531 | 29.7% |
| perseus-greek | Translation | 1,036 | 223,602 | 0.5% |
| csel | Original | 120 | 92,147 | 0.1% |
| first1k-greek, perseus-latin | | 6 | | |

Nearly thirty per cent of the Latin Fathers, and the whole of that second row is
the nineteenth-century German Thucydides, which is where the examples above come
from.

## It was not only a display problem

A word split in two is **two words** to the index, and that is the half with
teeth. Run over every affected line, the repair removes 878,000 index rows
across 89,705 distinct fragments and adds 389,776 rows across 130,856 whole
words.

What leaves the index: `con` (7,425), `tur` (7,115), `rum` (6,819), `re`, `tem`,
`di`, `que`, `bus`, `mus`, `tum` — Latin prefixes and endings, sitting in the
word list as though they were words.

What arrives: `dominus` (754), `secundum` (617), `dominum` (559), `omnibus`,
`autem`, `quoniam`, `quibus`, `dicitur` — the actual vocabulary of the texts.

So searching for `gratiam` did not find a line that reads `gratiam`, because the
line stored `gra` and `tiam`. Word Study offered `gra` and `tiam` as two
separate entries, neither of which has a lemma behind it. The translation
workbench asked the lemma data about both and got nothing for either. And the
intertextual echo search counted shared words it should not have counted, and
missed ones it should have.

Nine places in the code read the stored text this way. The first pass caught
five; this release catches the rest.

**One of them needed two fixes rather than one**, and it is worth recording why.
The search path confirms a whole-word match by splitting the line on whitespace,
which threw away a real hit — but repairing only that changed nothing, because
the database prefilter never returned the row in the first place: `gratiam` is
not a substring of `gra`+`tiam`. A test written for the first half passed the
confirmation and still failed overall, which is what surfaced the second. The
prefilter now also tests the text with the breaks closed up.

## What to do about an existing library

**Nothing is required.** No schema change, nothing is deleted, and an untouched
library keeps working exactly as it does now.

But the fix changes what *ingest writes*, so it reaches your existing texts
only if you re-run one:

- **Re-ingest the affected collections** (Setup Wizard → the collection's step)
  and both halves are repaired: the reader stops showing the gap, and the index
  is rebuilt from corrected text. This is the complete answer. In practice that
  means `patrologia-latina` if you have it, which is the only collection where
  this is widespread.
- **Rebuild the word index alone** (Setup Wizard → Tools → Word Index, about
  fifteen minutes) and **search is fixed but the reader still shows the gap** —
  the index is rebuilt from the text already stored, and that stored text is
  what has the break in it.

If you do neither, you keep today's behaviour, which is what you have had all
along.

## Also

The repair happens as the text is read, in the parser's whitespace handling,
before the whitespace is reduced. That ordering is the whole trick: afterwards
the soft hyphen and its following space are indistinguishable from a word that
simply ends there, and the two halves are two words for good. Every collection
that carries these goes through that one method, including the Renaissance
ingest, which reuses the parser.

The survey published with the first attempt at this was incomplete — it counted
Original editions only and so reported 120 affected lines in `csel` and almost
nothing elsewhere, missing 85,026 in the Latin Fathers and the German Thucydides
entirely. The table above is the corrected count.

1,511 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  A57C3964B94B7C0D7751102D83FCD59C007152900067499F44AF292B10C03531
```
