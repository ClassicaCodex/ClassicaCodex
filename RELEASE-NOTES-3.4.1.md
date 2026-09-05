# Classica Codex 3.4.1

Search was returning a fraction of the evidence and saying nothing about it.

This release is all repair, and almost all of it is in one place: what a search
matches, what it counts, and what it shows you it matched. Nothing here changes
your data — **no schema change, no re-ingest, no re-index.** Upgrading is
extract-and-run, and every fix below applies to the library you already have.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

**The archive itself is fixed too.** Every path inside 3.4.0's ZIP used a
backslash separator, which the ZIP format does not allow. Windows Explorer and
7-Zip are forgiving about it and most people saw nothing wrong — but a stricter
extractor reads `Icons\About.png` as a filename rather than a folder, and you
end up with 184 loose files where the `Icons` folder should be and an
application with text-only buttons. This one is written correctly, so if your
3.4.0 copy has no icons on its toolbar, that was why.

## A Latin search was hiding about a third of the evidence

`u` and `v` were one letter in antiquity, and so were `i` and `j`. Which glyph
an edition prints is the editor's decision, editors disagree, and the same word
therefore sits in this corpus under two spellings. A search reached one of them.

Measured against a full library, counting lines in Latin editions:

| typed | found | really there | hidden |
|---|---|---|---|
| `adiuvare` | 58 | **293** | 80% |
| `eiusdem` | 1,953 | **5,867** | 67% |
| `iudex` | 602 | **1,765** | 66% |
| `iustitia` | 1,425 | **4,172** | 66% |
| `iam` | 24,046 | **43,149** | 44% |
| `vel` | 33,214 | **44,117** | 25% |

Across twenty-two ordinary query words, **31.8% of the evidence was hidden** —
and it fell the wrong way round. `iustitia` and `iudicium` are the spellings of
every modern critical edition and every textbook, so the reader typing what
they were taught got the smaller half.

Queries now expand into their `u/v` and `i/j` spellings. `virtus` and `uirtus`
return the same 5,634 lines.

## Greek had two of its own

**The default match mode was wrong for it.** The window opened on "Anywhere in
the line", a pattern compared against the raw text — and Greek is written with
diacritics that editions disagree about and nobody types. Searching `μηνιν`
returned **8** lines where the word is in **316**. Whole words is now the
default, and it goes through an index that folds accents and breathings.

**Lunate sigma is sigma.** 87 editions in this corpus are set in the rounded
sigma that papyri use — the Suda, Herodian, Apollonius Dyscolus, Philodemus,
Porphyry, Galen — and nothing folded it, so none of them could be reached by
anyone typing an ordinary sigma. That stranded **349,421 index entries across
84,799 distinct words**, and 22% of every line containing `πόλις`.

## Typing more than one word now means all of them

Whole-word matching ORed the words together, which is the same as ANDing them
while queries are one word long and badly wrong the moment they are not.
Searching `gallia est omnis divisa` returned 5,000+ lines led by an argumentum
to a letter of Cyprian, because nearly every line in the corpus contains `est`.
It now returns the one line that opens the *Gallic War*, in 37 ms.

Pasting a line straight out of the reader works, which is the most natural
thing to do with a search box.

## "One row per document" was a distribution that wasn't one

A search stops at 5,000 rows **ordered by author name**, so the cap does not
sample the matches — it truncates the alphabet. The document view grouped
whatever survived and presented it as "which works use this word".

Searching `vel`: 44,457 lines across 1,623 works, of which that view showed
5,000 lines across 168. **1,455 works — 90% of those containing it — showed
nothing**, and Augustine, who has more of them than anyone, was credited with
184 because the cap landed in the middle of him. For `λόγος` the real top of
the distribution is the Homeric scholia; the view led with Aesop, who was there
because of the A.

Counts are now computed across the whole library, not across the page. The
status line says how many matches there are, in how many works, by how many
authors — where it used to say `5000+`, which is all a capped row query can
honestly report and useless to anyone asking how often a word occurs.

## The concordance could not do Greek at all

It was still running the old substring search, so it inherited every problem
above: concordancing `μηνιν` found **8** lines where the word is in **316**.
Worse, of those 316 the keyword column could be filled on **one** — the rest
printed as "(stemmed match)", which is a concordance with no keyword column.

It now matches whole words through the index, fills the keyword column on every
row, and shows the word **as that edition prints it**, so searching `uirtus`
lines up `virtus`, `uirtus` and `Virtus` in one column. It is also about a
hundred times faster: 13 ms against 2,564 ms.

## The era filter was putting a tenth of the library in the wrong century

Authors are dated from a lookup table, and the era filter includes or excludes
a work on the strength of it while saying nothing on screen about how it
decided. Matching was a plain substring test, which produced exactly the
failures that shape suggests:

| author | was dated | why |
|---|---|---|
| **Anonymous** — 269,429 lines | 560–580 CE | *"Anonymous pilgrim of Piacenza"* contains it |
| **Scholia in Homerum** — 37,374 lines | 750–650 BCE | dated to Homer himself |
| **Elias Neoplatonicus** | 428–348 BCE | "plato" inside Neo·**plato**·nicus |
| **Appendix Vergiliana** | 70–19 BCE | the collection defined by *not* being Vergil |

and the same for the lives of Homer and Aesop, the *Certamen*, the scholia on
Pindar, Euripides and Euclid, Solon's pseudonymous letters, and Pseudo-Arrianus.
More than 300,000 lines in the wrong century.

Matching is now by whole word, a name that describes a work *about* someone
never inherits that person's dates, and a generic anonym has to match exactly.
Then the other half of the problem: 444 of 748 authors had no dates at all, so
fourteen entries were added for the largest of them — **Silius Italicus** and
**Valerius Flaccus** among them, which is not a good look for a classics tool
to have been missing.

Lines the era filter can place: **1,499,158 → 1,687,790**, with the wrong ones
gone as well. What remains undated is undated correctly.

## Cite a passage without retyping it

Export Passage now writes **BibTeX** and **RIS** beside text, Word and PDF, so
a passage you find here goes into Zotero as a reference rather than by hand.

```bibtex
@incollection{Augustine-Saint:Epistulae:1.1,
  author = {Augustine, Saint},
  title = {Epistulae},
  booktitle = {Latin Church Fathers (CSEL)},
  pages = {1.1},
  url = {https://scaife.perseus.org/reader/urn:cts:latinLit:stoa0040.stoa001.opp-lat1},
  abstract = {urn:cts:latinLit:stoa0040.stoa001.opp-lat1; author 354 CE-430 CE; …}
}
```

One entry for the run on screen rather than one per line. It does not invent a
publication year an ancient work does not have — the author's floruit goes in
the note instead — and it only writes a URL where one resolves, so CTS URNs get
a Scaife link and Menota's identifiers get none rather than a link that goes
nowhere.

## Smaller things

- **Word Study led with a headword the dictionary could not answer.** Only
  43,507 of 139,190 Latin lemma headwords have a Lewis & Short entry, and a
  capital letter sorts first — so clicking `regere` offered "Reger" (0 entries)
  with `rego` sitting below it. Answerable headwords now sort first.
- **"Where should I start?" sent beginners to Pseudo-Caesar.** The screen that
  exists so a beginner doesn't land on the wrong text opened *De Bello Africo*
  for "Caesar, Gallic War", and Pseudo-Lucian's *Amores* for "Lucian,
  Dialogues". It now prefers the real author, and offers nothing rather than
  substituting a similar name.
- **An exported passage now says which edition it came from** — this library
  deliberately holds the same work in CSEL and in Migne, and the export used to
  name neither.
- **Create Translation could not finish a prose work.** Batches were 25 lines,
  and a line is a verse line in Homer and a whole section in Julian: the same
  constant sent 1,000 characters of one and 22,812 of the other, and the second
  timed out. Batches are now bounded by text as well as lines, the timeout
  accounts for what the model has to write rather than only what it reads, and
  one slow request no longer ends the whole run.
- **Results are highlighted again.** The search got better at finding lines the
  literal query does not appear in, and the highlighter could not mark them —
  299 of 300 rows for `μηνιν` came back with nothing lit up.
- **A passage queued by the research bench is titled by its citation**, not by
  the full CTS URN — "Misopogon 2.1" rather than
  "Misopogon urn:cts:greekLit:tlg2003.tlg012.perseus-grc2.2.1".
- **Three labels had room for their text at 100% and nowhere else.** Measured
  at 125% and 150%, where two lines of the concordance's status wanted 50px in
  a 34px box.

## Known and deferred

**Plato cannot be cited properly.** Perseus ships Stephanus pagination as
inline milestone markers, which the parser discards, so *Euthyphro* 2a displays
as `[2.1]`. Capturing it needs a parser change and a re-ingest, so it is filed
for 3.5 rather than rushed into a point release.

## Checks

Verified against a full 2.3-million-line library, and by building one from
empty with this code: 14 setup steps, 30 minutes, all ten integrity checks
clean, 68,871,775 index entries.

The search window, the concordance, the translation dialog and the research
bench were driven as the real forms rather than read, against that library and
against the live API. **905 tests, zero warnings on a from-scratch build.**
