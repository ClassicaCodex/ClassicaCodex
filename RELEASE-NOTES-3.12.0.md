# Classica Codex 3.12.0

Middle High German. The Reference Corpus of Middle High German (ReM) — 406
texts written between 1050 and 1350 and transcribed from the manuscripts
themselves, the Nibelungenlied, Wolfram's Parzival, Hartmann's Iwein and
Gottfried's Tristan among them, alongside sermons, charters, charms and saints'
lives — installs as one download, and Word Study works on it.

It is the first collection in this library that brings its own lemma data. Greek,
Latin and English each need a separate download from another project; ReM
annotated the same 406 texts the reader is reading, so the mappings cover them
completely rather than approximately.

Nothing here changes your data. **No schema change, no re-ingest, no re-index**,
and nothing to run afterwards. Two new optional steps appear in the Setup
Wizard; leave them alone and the library is exactly as it was.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The texts

Setup Wizard → **Middle High German Texts (ReM)**. One 27 MB archive from
Zenodo, CC BY-SA 4.0, no login and no form. Unlike the Medieval Nordic step next
to it, there is nothing to fetch by hand.

Every text arrives twice: once spelled as the scribe wrote it, and once in the
normalised form a printed edition would use. They go in as two editions of one
work, so the edition dropdown switches between the reading text and the
manuscript behind it — and the dropdown now says which is which:

```
Middle High German (manuscript spelling)
Middle High German (normalised)
```

Medieval Nordic editions carry the same distinction and gain the same label. It
was always in the data and was never shown; two originals in one language used
to arrive as two identical entries.

Citations are the manuscript's own folio and line, or the editor's line numbers
for the 163 texts ReM cites that way. Most of the texts are anonymous and are
filed that way.

It is pinned to ReM's v2.1 rather than following "latest". ReM renumbers texts
between versions — 2.0 merged two pairs, split another into four and removed
one — and bookmarks are kept against edition and citation, so a version bump
underneath them would detach a reader's marks silently. Moving will be a
deliberate edit with the renumbering understood.

## Word Study

Setup Wizard → **Middle High German Lemma Data**. 158 MB, read straight from
the archive; install the texts first, since on their own the mappings have
nothing to attach to.

Measured against the real corpus: 2,293,069 annotated tokens across 406 files
become 334,427 word-form mappings in about a minute. Click a word and you get
its headword, every attested spelling of it, its part of speech and grammar, and
everywhere else it occurs — 158 places for *stân*, 91 for *wër(e)lt*.

What this does **not** bring is meanings. ReM carries no glosses, and there is
no openly licensed machine-readable Middle High German dictionary of the kind
LSJ and Lewis & Short are for Greek and Latin. The definition pane will be
empty; everything else on the form will not.

Two things had to be got right for this to be right, and both would have looked
fine if they had been got wrong:

- **ReM's TEI has an attribute called `lemma`, and it is not the lemma.** It
  holds the normalised spelling; the headword is only in ReM's separate
  annotation files. Taking the TEI's would have filled Word Study with inflected
  words grouped under normalised spellings, looking entirely convincing while
  doing it.
- **131,665 tokens have no headword and looked as though they did.** ReM marks
  a word it could not lemmatise as `[!!]` or `[!]`, 5.7% of the corpus. Kept,
  those would have been the two commonest "headwords" in Middle High German, and
  a reader clicking a word would have been told its dictionary form was `[!!]`.

## Long s

The tall ſ that manuscripts and early print use everywhere except at the end of
a word is the letter s in a different shape, not a different letter, and it is
now folded to s wherever words are compared. It is the whole difference between
*iſt* and *ist*, and so between a manuscript reading being findable by someone
typing an ordinary s and not being findable at all. Across the Middle High
German corpus that is 431,099 occurrences.

This is right for the rest of the library too: where an OCR'd printed text
carries a long s, it means s there. If you have such texts and want searches to
reach them, **Setup Wizard → Tools → Word Index** rebuilds the index with the
fold applied, about fifteen minutes. Nothing requires it.

## A bug this made live, fixed before it shipped

Word Study decided which dictionary to consult by looking at the script of the
headword: Greek letters meant Greek, anything else meant Latin. That was true
for two languages. With a third written in Latin letters, every Middle High
German headword would have gone to Lewis & Short — and *an*, *in*, *a* and
*her* are all real Latin headwords, so the pane would not have come back empty.
It would have come back confidently wrong. It now asks the edition its language,
as the rest of the form always did.

## Also

Direct downloads now send a User-Agent. .NET sends none by default and Zenodo
answers 403 to a headerless request, which surfaced as a permissions error
nobody could act on. Every direct-download source gains it.

1,565 tests, zero warnings.
