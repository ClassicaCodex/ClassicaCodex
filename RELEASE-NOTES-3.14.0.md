# Classica Codex 3.14.0

Five hundred texts of medieval French.

The *Chanson de Roland*, all four of Chrétien de Troyes's romances, *Aucassin et
Nicolette*, the *Queste del saint Graal*, the *Roman de Renart*, Marie de
France, Rutebeuf, Villon, and 281 fabliaux — French as it was written between
the ninth century and the fifteenth, from the **Base de Français Médiéval** at
the ENS de Lyon. One new step in the Setup Wizard.

Most of them arrive twice: once letter for letter as the scribe wrote them, and
once in a modern editor's reading. That is the point of the corpus, and it is
what the rest of these notes are mostly about.

Nothing here changes your data. **No schema change to anything you already have,
no re-ingest, no re-index**, and nothing to run afterwards. One new optional step
appears in the Setup Wizard; leave it alone and your library is exactly as it
was.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
More info, then Run anyway.

## Installing it

Setup Wizard → **Medieval French Texts (BFM)**. 500 texts, 222.5 MB, and it will
take something like ten minutes on a reasonable connection — the corpus is
published as 500 separate deposits, each with a DOI of its own, so this is 500
small downloads one after another rather than one archive, and the time goes on
making 500 requests rather than on the volume. The step names each text as it
arrives.

Files already fetched are kept and skipped on a second run, so stopping it part
way and starting it again resumes rather than starts over.

494 texts install, in 552,883 passages, with 385,828 word forms filed under
their headword and part of speech. The six that do not are explained below.

They appear in the library under their own authors where the corpus names one —
96 named authors across 206 texts, among them Chrétien de Troyes, Marie de
France, Jean Bodel, Rutebeuf, Jean Froissart and Villon — and under *Anonymous*
where it does not, which is the rest. The other 294 give their author as the
French word *anonyme*; that is an adjective rather than a person, and it is not
filed as one.

## Two readings of the same line

Where a text carries both, you get two editions of it and a dropdown to choose
between them, labelled **diplomatic** and **normalised** — the same arrangement
the Middle High German corpus already uses.

The difference is not spelling modernisation. It is the difference between what
is on the parchment and what the editor concluded it says:

- **Abbreviations.** A scribe writing *sempres* writes `S emp` then a mark then
  `s`. The diplomatic reading gives you `Semps`, which is what is there; the
  normalised reading gives you `sempres`, which is what it means. The corpus
  marks the expansion 96,872 times.
- **Word division.** These scribes do not space words the way print does. Where
  the manuscript runs *le flabel* together, the diplomatic reading runs it
  together too — `Leflabeld'Aloul` — and the normalised reading separates it.
  30,680 words are marked this way.
- **Corrections.** Where an editor emended the manuscript, the diplomatic
  reading keeps the manuscript's reading and the normalised one keeps the
  emendation.

The grammar that Word Study reads is taken from the editor's reading, which is
the one a search term will look like: *sempres* is a word somebody might type
and `Semps` is not. Both editions are stored and both are read, and both go into
the word index when you build it, so in practice a search lands on the
normalised edition and the diplomatic one is there to be looked at beside it.

Where the two readings are identical — a text with no abbreviations and no
irregular division — you get one edition rather than two copies of it.

## Word Study on 23 of them

Twenty-three texts carry their own word-by-word annotation — a headword and a
part of speech on each word, done by the corpus. Those arrive ready to query,
and Word Study answers on them with nothing further to fetch: there is no Old
French lemma list to download anywhere, and these texts do not need one. The
other 471 are text without grammar, as the corpus publishes them.

## Two licences, and the oldest texts are in the restricted set

This corpus is not under one licence, and the difference matters if you plan to
do anything with the text beyond reading it:

- **211 texts** are **Licence Ouverte / Open Licence 2.0** — attribution only.
- **281 fabliaux and 8 others** are **CC BY-NC-SA** — noncommercial, and
  share-alike.

Among those 8 are the **Serments de Strasbourg** (842) and the **Séquence de
sainte Eulalie** — the two oldest surviving texts in French. So "the BFM is
Licence Ouverte" is wrong about 289 of its 500 texts, including the two anyone
would most want to quote.

The Setup Wizard says both before you download, the sources table in the README
says which is which, and every text carries its own licence rather than
inheriting a corpus-wide one.

## Six texts are not included

The corpus marks six of its files as not redistributable, inside the files
themselves. They are downloaded, read, found to be restricted, and dropped
before anything is stored; the step reports the count. They are JAntInv,
JAntRect, bodelnic, Elucidaireiii, mestiersparis and RegleSBenCotton.

This is read from each file rather than from the catalogue, because that is
where the corpus puts it — a text could become restricted in a later release of
the corpus without the catalogue changing, and this way it would still be
skipped.

## Also

The corpus is TEI throughout but not in one encoding — there are four shapes in
it, and 78 of the 500 files are plain prose with no word tokenisation at all, so
a reader that only understands the tokenised shape returns nothing for them. One
document-order walk reads all four.

Everything awkward about it was found by running the loader over all 500 files
and reading what came out, not from documentation. The one worth naming: an
unnumbered line break is layout and a numbered one is a verse. Treating them
alike cut the Seigneur d'Anglure's prose account of his journey to Jerusalem
into 2,652 numbered "verses", each a fragment of a sentence. Twenty-two tests
cover the parsing, each one a case taken from a real file, and seven more check
that the shipped list of what the corpus contains has not arrived truncated —
which would leave the step with nothing to download and no error to show.

1,684 tests, zero warnings.
