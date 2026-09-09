# Classica Codex 3.6.14

Mostly documentation, and one real defect: **adding a collection after building
the word index left it out of every search, and the setup wizard reported the
step finished anyway.**

Nothing here changes your data: **no schema change and no re-ingest.** If you
have added a collection since building the word index, rebuild it — see below.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## A collection added later was invisible to search

Ingesting a corpus does not touch the word index — none of the three ingest
services mentions it. So the lines from a collection added after the index was
built have no index entries, and a re-ingest also gives every line a new
identity, which abandons the entries the old ones had.

That would only cost speed if search noticed. It doesn't: search decides whether
to use the index by asking whether it has *any* rows at all, so it consults a
half-populated index and the missing lines are simply absent from the results.
Not slower to find — **not found.**

And Guided Setup asked the same has-any-rows question, so it showed the word
index step **complete, "Already built."** while a whole collection was missing
from it. The README's advice that anything skipped "can be added later" led
straight into this.

The step now asks whether every line is indexed, and says what it finds:

> **Out of date** — 2,100,000 of 2,339,233 lines indexed. 239,233 added since
> the last build will not turn up in searches until this is rebuilt.

Three states rather than two, and the middle one is the common one. The honest
check already existed — the Word Index window has used it since it was written,
and its own comment records that the naive one "stayed true the whole time
Shakespeare's lines sat unindexed after a source was added post-build." It just
was not asked on the screen a newcomer sees. It runs off the UI thread, because
counting distinct indexed lines reads all seventy million index rows.

**If you have added a collection since building your index, rebuild it now** —
Setup Wizard, or the Word Index window. That is the only way those texts become
searchable.

## The README was wrong about what setup costs

Four figures a newcomer would plan around:

| | said | is |
|---|---|---|
| full setup | about an hour | **two to three hours** — the Greek lemma step alone says an hour, the word index fifteen minutes, Latin lemma six |
| disk | not mentioned | **~9 GB of downloads plus a ~3 GB library**, and the largest step wants ~7 GB free on the `%TEMP%` drive because archives are unpacked twice |
| schema migrations | thirty-seven | **thirty-eight** |
| places on the map | 200 | **220** |

It also described a first-run choice between Guided and Advanced Setup. There
isn't one: first run goes straight into Guided Setup, and Advanced is reached
afterwards from the toolbar. And it now says plainly that a collection added
later needs the index rebuilt.

## Old release notes stopped contradicting the current download

Twenty-three sets of notes sat in the repository root, each carrying a
**"Download the Windows ZIP"** link to the current release *and* the SHA-256 of
its own long-withdrawn one. Follow the link from any of them, check the checksum
printed beside it, and it fails — which is the single thing a checksum exists to
rule out.

They now live in [`release-notes/`](release-notes/) behind an index, each headed
with a note saying it is historical and that its link and checksum refer to the
version it describes. Two links to a 3.3.0 release page that no longer exists now
point at the notes file instead, and 1.0.0 no longer instructs you to download an
asset that is gone.

## Two corrections to things I had already published

Both are marked in the historical notes rather than quietly edited.

- **3.6.10's rationale for the four-letter prefix threshold was wrong.** It said
  three would let "le" match "less", "left", "legions" — but "le" is *two*
  letters, so three already leaves it matched exactly. Re-measured at every
  threshold across all nine multi-word map pins: the twenty-two "noble mansions"
  passages appear at **two**, and three and four are identical on every pin. The
  upper bound is real — "gulf" is four letters, so five loses three mentions. So
  the threshold has to be three or four, and four was chosen with a letter
  spare. The code comment and the test that documents the choice said the same
  wrong thing and now don't.
- **3.6.12's timings were single measurements and both too flattering.**
  Re-measured on a byte copy of the same library, replaying the whole save
  sequence: the old path cost 102 s isolated and 22–171 s across cumulative
  batches, not 75.1 s; the new path costs about 11 ms a batch, not 4–6 ms. The
  claim that mattered — that the cost stops growing with the length of the
  translation — holds.

## Also

- The libgit2 notices listed "PCRE2, zlib and xdiff". There is no xdiff section
  in that file; there are eight others there was no reason to omit — llhttp,
  ntlmclient, wildmatch, SHA-1 collision detection, the winhttp definitions and
  Clar among them. Now listed as they are.
- Choosing which edition the translation workbench writes to now filters on the
  edition's kind, not only on a substring of its identifier. The workbench
  deletes a passage when its box is cleared; 3.6.13 taught the repository to
  refuse a non-translation, and this is the half that means the refusal is never
  reached.

## Known, and not fixed here

Saving a translation commits the text and the index update in separate
transactions, with nothing recording that an edition owes an update. A crash
between them leaves a passage readable but not findable. The index is derived
data and a rebuild corrects it, but nothing currently says it needs one.

## Checks

**1,151 tests, zero warnings on a clean build.**

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  1482C9024AC2AB76E76B0EBC908061104006F1C0BE6F6731AF2E5B31D5F62D05
```
