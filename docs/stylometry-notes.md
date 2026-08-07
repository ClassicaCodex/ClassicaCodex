# Notes on Burrows's Delta in Classica Codex

What the stylometry tool can and cannot tell you, written up after using it to
work on a real disputed-authorship question. The short version: it found three
genuine corpus bugs, and it did not answer the authorship question. Both halves
are worth recording.

---

## The question

The *Rhesus* transmitted under Euripides' name has been disputed since
antiquity — the ancient hypothesis records that some in antiquity already
doubted it — and modern opinion leans toward a fourth-century imitator without
settling the matter. It is a good test case: a live dispute, a well-attested
comparison set of eighteen undisputed plays, and no correct answer to check
against.

## What was found

### 1. Four corpus bugs, each of which moved the result

These were not stylometric findings. They were defects that made every earlier
number wrong, and they were only visible because the analysis kept producing
results that dissolved on inspection. The last was found while chasing something
else entirely — a set of works suspected of ingesting badly, which turned out to
be ingesting correctly.

**Critical apparatus ingested as running text.** The TEI parser collected every
descendant text node, so manuscript sigla and nineteenth-century editors'
surnames were being counted as Greek vocabulary — "seclusit Pauw", "fort.
δεσποτουμένου Dübner", "F1 V Fa: δ' ἦν M".

The effect is largest in First1KGreek, where the apparatus is encoded as whole
`<app>` blocks sitting alongside the lines. First1KGreek *Agamemnon* came to
69,302 characters against Perseus's 52,078 on the same line count — about 17,000
characters of one play were apparatus. After the fix: 53,524 against 51,913, a
3% difference.

**It is not only a First1KGreek problem, and an earlier version of this note
said it was.** Perseus files carry the same material as inline `<note>`
elements: Plutarch *An virtus doceri possit* (`tlg0007.tlg093.perseus-grc2`) has
editorial notes threaded through the running text of every section — "Stobaeus:",
"del. W", "Bywater p. 42", "Emperius:", each wrapped around a `<foreign>` with
the variant reading. Because these sit inside the `<p>` rather than beside it,
they do not inflate the character count the way a separate `<app>` block does,
which is why the corpus-wide length comparison did not catch them. They were
being counted as words all the same.

The fix covers both: `<note>` and `<app>` are skipped on both parse paths.

**Duplicate editions inflating the pool.** Works with several editions appeared
several times in the comparison pool, and each appearance contributed to the
mean and standard deviation of every feature. The multi-edition works cluster in
Aeschylus and Sophocles while most Euripides plays carry one, so the
normalisation was weighted toward exactly the authors Euripides was being
measured against.

**Heading text dropped entirely.** `<head>` is neither a structural div nor a
citable leaf, so the parser's fallback branch descended into it, found no child
elements, and emitted nothing. Usually a head merely repeats the work's title.
Sometimes it does not: in Adrianus of Tyre's *Declamatio* the head carries the
entire declamation theme — the premise the speech exists to argue against — and
it was vanishing on ingest.

**Elision marks tracking editors rather than authors.** Tokenisation matched
`\p{L}+`. U+02BC MODIFIER LETTER APOSTROPHE is Unicode category Lm, which
matches `\p{L}`; U+2019 and U+0027 do not. So `δ'` survived as its own token in
editions using one codepoint and collapsed to `δ` in editions using another —
and it was the single highest-weighted feature in every Greek run.

Corpus-wide verification after the parser fix: of 121 works holding two
editions, about 72 now differ by under 10% in characters per line, about 37 are
scope mismatches (genuinely different amounts of text, not comparable), and 3
still carry an apparatus this fix does not catch.

### 2. Depth to first outsider does not work

The measure: how far down a work's own ranked neighbour list you go before a
work by a different author appears. It looks like a natural way to ask "does
this text keep the company its attribution predicts".

It does not survive testing.

Across the eighteen undisputed Euripides plays plus *Rhesus*, at fixed sample
size, depth varied by up to **20 ranks for a single work** on a 500-token change
in sample size. *Heracleidae* read 4, 24 and 10 across three sample sizes;
*Hippolytus* 13, 21, 8; *Hecuba* 2, 11, 8.

The reason is structural rather than incidental. Depth is a rank position, and
rank positions depend on the composition of the pool. Every attempt to remove
one confound surfaced another:

| Fix | What happened |
|---|---|
| Whole works | Depth tracked text length (ρ ≈ 0.58): the four shortest works were the four shallowest |
| Equal-size samples | Depth tracked sample *count* instead — mean depth 6.5 for one-sample works, 12.9 for two, 20.0 for three |

A longer work contributes more samples to the pool, those samples pad the top of
everyone's ranking, and the confound returns wearing a different hat. The
underlying quantity being measured is length either way.

**Do not use depth to first outsider for attribution.** It is retained in the
tool because watching it move is instructive, not because it means anything.

### 3. Delta floor is better, and still not enough

Delta floor — the distance to a work's single nearest neighbour — is a distance
rather than a rank, so it does not inherit pool composition. At fixed sample
size it shows no length effect: ρ(tokens, floor) = −0.21, ρ(sample count,
floor) = −0.18, against 0.58 for depth.

At 3,000-token samples, *Rhesus* had the highest floor of nineteen works, at
0.509 against a next-highest of 0.464 — a leave-one-out z of **+3.23**, with the
other eighteen spanning 0.397 to 0.464.

That did not replicate. Re-run at 2,500 tokens it fell to roughly fourth, inside
a cluster of five separated by 0.014. At 3,500 tokens it was highest again but
by 0.024 rather than 0.045, and short of 2σ.

*Rhesus* is consistently *toward* the top on Delta floor and never consistently
isolated. That is a weak, unstable tendency. It is not a finding.

### 4. The one suggestive detail

At 3,500-token samples, *Rhesus* was the only work in any run whose nearest
neighbour was by a different author — Aeschylus' *Eumenides*.

Depth spread across the three runs was 3 / 5 / 1, and other works swung far
harder, so this rests on a single reading of a measure already shown to be
unstable. Recorded because it is the most suggestive number produced, and
flagged because one reading of an unreliable measure is not evidence.

---

## Conclusion on the *Rhesus*

Burrows's Delta on this corpus provides no reliable evidence either way about
the authorship of the *Rhesus*.

Not "weak evidence for authenticity" and not "weak evidence against". The
measures that looked promising either failed to replicate or turned out to be
measuring text length. The play sits within the range occupied by undisputed
Euripides on every stable measure tried.

Worth stating separately: the corpus contains no fourth-century Athenian
deliberately imitating Euripides. *Christus Patiens* — a twelfth-century
Byzantine cento assembled from genuine Euripidean lines — is the nearest
available control and is fifteen centuries downstream. Without a contemporary
imitator to test against, "looks Euripidean" and "looks like a competent
imitation of Euripides" are indistinguishable here by construction. That is a
limitation of the question as posed to this method, not of the implementation.

---

## What this means for using the tool

- **Delta measures similarity of word-frequency profile.** It does not measure
  authorship. On a same-genre corpus the two come apart more than is
  comfortable.
- **Preprocessing choices moved the answer more than the disputed authorship
  did.** Vary them deliberately and see whether a result survives. The Stability
  tab exists for this.
- **Check the length confound tab before believing a ranking.** If depth
  correlates with token count, the ranking is about length.
- **Replicate at more than one sample size.** The single most encouraging result
  in this work was a sampling artifact, and only a re-run revealed it.
- **Short texts are unreliable.** Eder (2015) puts the minimum for stable
  attribution at 2,500–5,000 words, with false-attribution rates above 60% below
  3,000. Several Greek tragedies sit at or below that line.

## Prior work

Almost everything here is documented elsewhere, and was rediscovered
independently before the literature was checked properly.

- **Eder, "Does size matter?" (2015)** — minimum sample sizes; the canonical
  treatment of the length problem.
- **Kestemont & Van Dalen-Oskam** — high-frequency function words are
  particularly sensitive to scribal adaptation, an issue they describe as
  insufficiently investigated. The same shape as the elision problem here, with
  a different cause.
- **Evert et al. (2017)** — feature scaling and distance measures in Delta.
- **Somers & Tweedie (2003)** — pastiche as an attribution control; some methods
  distinguished a Lewis Carroll pastiche from the original and some did not.
- **"Testing Burrows' Delta on Ancient Greek Authors"** — performance degrades
  on texts of similar genre.
- A 2018 *Digital Scholarship in the Humanities* paper applied PCA and SVM to
  the *Rhesus* and reported ambiguous results.

The standard tooling is `stylo` (R); the Diorisis corpus provides lemmatised
Ancient Greek. Anyone doing this seriously should start there.

What Classica Codex adds is not method. It is that the whole pipeline —
tokenise, sample, compare, save, replicate, test for confounds — runs offline
from a reader, without R, against a corpus you already have open.
