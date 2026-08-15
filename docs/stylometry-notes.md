# Notes on Burrows's Delta in Classica Codex

What the stylometry tool can and cannot tell you, written up after using it to
work on a real disputed-authorship question. The short version: it found four
genuine corpus bugs, and it did not answer the authorship question. Both halves
are worth recording.

A later session added a validation and experiments bench - leave-one-out
validation, parameter grids, controlled perturbation - and produced five further
candidate findings, all five of which dissolved on checking. Section 5 records
them, because how each one dissolved is more useful than any of them would have
been.

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

**It has since failed to replicate three times.** Two later runs at 3,500 tokens
with a wider pool put *Rhesus*' nearest neighbour back inside Euripides at rank
1, and a run at 2,500 did the same. Three configurations, two answers, and the
two that showed the Aeschylean neighbour were the earliest and least controlled.
Treat it as withdrawn rather than open.

---

### 5. The validation bench, and five findings that dissolved

The measures above were all read off single runs. A later session built a
validation and experiments bench to attack them properly: leave-one-work-out
validation, a parameter grid, and controlled perturbation with synthetic
contamination. The bench worked. It is worth recording exactly what it did,
because what it mostly did was destroy results.

**Margin replaced depth as the headline, and inherited the same confound.**
Margin is mean Delta from a work's samples to samples by other authors, minus
mean Delta to samples by its own author - a difference of distances rather than
a rank, chosen precisely because ranks inherit pool composition. It correlates
with text length at rho +0.64 at 2,500-token samples. Depth correlated at +0.58.
The replacement was no cleaner than the thing it replaced.

**No parameter region escapes it.** Forty configurations - sample sizes 2,000 to
5,000, feature counts 100 to 500, accent folding on and off - returned length
correlations from +0.42 to +0.73, with 100% recovery in every single cell.
Recovery saturates on a same-genre pool and discriminates nothing.

And the grid's own spread is not interpretable: over nineteen works, rho +0.42
carries a 95% interval of about [-0.04, +0.73] and rho +0.73 carries
[+0.41, +0.89]. Best against worst is 1.36 standard errors, where about two
would be needed. **The visible range of a forty-cell grid fits inside the
estimation error of any one cell in it.** Telling those two values apart would
take roughly fifty works.

**Raising the sample size hides the confound rather than fixing it.** At 2,500
tokens all nineteen plays are testable, spanning 4,141 to 10,060 tokens - a
ratio of 2.43. At 5,000 the shortest are dropped and the span falls to 1.85, and
rho falls with it. A correlation needs spread on both axes, so a low rho at a
narrow spread is uninformative rather than reassuring. Any anomaly ranking has
to report how many works a setting could actually test.

**Contamination works, and the same-author control is what makes it readable.**
Injecting donor tokens in place of a work's own - length held exactly constant,
so the length confound cannot move while composition does - decays margin
monotonically. At 20% injection the margin falls to roughly 70-85% of baseline.
Injecting *more of the work's own author* moves it the other way, to 134% and as
far as 186%: the work is pulled toward its author's centre and away from its own
idiosyncrasy. Without that control a falling curve cannot distinguish
"disturbed by another author's style" from "disturbed".

Sophocles moves Euripides about 1.6 times as far as Aeschylus does, consistently
across works - median drop -0.031 against -0.019 at 20%. That is a fact about
the three tragedians, and it is the first thing to rule out whenever one play
appears to respond oddly to one donor.

**Recovery as a sign test is far too blunt to be a headline.** A Euripides play
carrying 50% synthetic Sophocles stayed "recovered" in twelve trials out of
twelve, at 29% of its uncontaminated margin. A text can lose seven-tenths of its
authorial signal and still count as recovered. The useful statistic is the
proportion of baseline surviving, or the drop in Delta.

**And the drop in Delta is not independent of baseline either.** rho between
baseline margin and the size of the drop is +0.75 to +0.78 across sweeps: works
with more margin to lose lose more of it. This is the fourth measure in this
document to turn out to be reading something other than what it claimed - depth
read length, margin read length, and now the response to contamination reads
margin. Each was caught by checking rather than by suspecting, which is the
argument for the check being automatic. It now is, in all three places.

#### The five findings that did not survive

Listed because the pattern is more instructive than any of them.

1. ***Rhesus* susceptible to Aeschylus where *Alcestis* was not.** *Alcestis*
   moved -0.10 SD under 20% Aeschylus, *Rhesus* -2.27. Explained by headroom:
   *Alcestis* has a low baseline and less room to fall. Both curves were
   tracking baseline margin.
2. ***Heracleidae* anomalous on raw drop.** Flagged at 3.3 median absolute
   deviations in the Sophocles sweep. It has the lowest baseline margin in the
   corpus. Fit drop against baseline and it sits at 1.2 - ordinary.
3. ***Heracleidae*'s Aeschylean sign flip.** Its margin *rose* +0.004 under 20%
   Aeschylus while three other plays fell -0.019 to -0.020. Flagged at 8.6 MAD
   on the raw ranking. After the baseline fit: 1.9. It is the extreme end of a
   straight line, not a different behaviour.
4. **Three works flagged at 3 MAD in the seed-42 Aeschylus sweep.** Re-run at
   seed 43, changing nothing but the mixing seed: zero flags. The threshold was
   also weaker than it sounded - sigma is 1.4826 x MAD, so three MAD is about
   two sigma, and over nineteen works 0.8 false flags per sweep are expected.
5. **Four works significant at 100 iterations.** *Helen*, *Rhesus*, *Hecuba* and
   *Ion* all cleared Bonferroni correction, at p x 19 between 0.0002 and 0.002.
   This was the subtlest failure and is worth stating carefully.

#### Why the fifth one is a null, not a result

With 100 iterations per level the mixing standard error falls to about 0.00086,
while the scatter of residuals across works is 0.0020. **Measurement noise
explains only 18% of the residual variance.** The works genuinely differ from
the fitted line - as they should, since a straight line in baseline margin is a
crude model of how a play responds to contamination.

So a z computed against the measurement error answers "is this work's drop
reliably different from what the line predicts?", and with enough iterations the
answer is yes for anything off the line. It measures the fit, not the play.

The anomaly question needs the between-work scatter. On that scale *Rhesus* is
+2.20 sigma and *Hecuba* +2.03 - and the expected maximum of nineteen draws is
about 2.2 sigma. *Rhesus* sits exactly where the most extreme of nineteen works
should sit.

**More iterations bought precision, and precision revealed that the thing being
measured precisely was the regression's lack of fit.** Two standard errors of a
tightly estimated mean is not the same quantity as two standard deviations of
the population it belongs to, and it is easy to report the first while believing
the second.

---

### 6. What the method could have detected

A null result is worthless without this, and it took the whole bench to get to
it. "*Rhesus* shows no sign of foreign material" and "*Rhesus* shows no sign of
foreign material, and material below thirty percent would have been invisible"
are different statements, and only the second is worth reporting.

The comparison is between two spreads. Genuine Euripides plays scatter around
the margin-against-length line by **0.029**, for reasons that have nothing to do
with authorship - date, genre, subject, transmission, the editor. That scatter
is the noise any real signal has to clear. Contamination moves a play by some
other amount. If the movement is small against the scatter, nothing can be
distinguished however precisely each work is measured.

Measured over nineteen plays contaminated with Aeschylus and Sophocles, 100
iterations per level:

| contamination | shift | shift / scatter | AUC | overlap |
| --- | --- | --- | --- | --- |
| 1% | -0.0010 | 0.03 | 0.51 | 99% |
| 2% | -0.0021 | 0.07 | 0.52 | 97% |
| 5% | -0.0054 | 0.17 | 0.55 | 93% |
| 10% | -0.0111 | 0.38 | 0.61 | 85% |
| 20% | -0.0234 | 0.80 | 0.71 | 69% |

AUC is the probability of correctly ranking one contaminated work above one
clean work. **At 20% synthetic Sophocles this method gets it right seven times
in ten. At 10%, six. At 5% it is a coin flip.** Nothing in the tested range
reaches 0.80, the conventional floor for a usable diagnostic.

AUC rather than a p-value deliberately: with enough iterations a mean shift of
any size becomes statistically significant, and none of that helps identify
which text is which. Discrimination is the question.

*Rhesus* against a line fitted on the eighteen undisputed plays without it sits
at **+0.48 deviations - the 56th percentile**, marginally more typical than the
median rather than less.

#### How much the idealised donor flatters the method: measured

The contamination is not a spliced passage. Each injected word is drawn
independently, with replacement, from the donor's entire surviving corpus, and
the words land at random positions in the target.

Contiguity is not what is given up - the engine shuffles token positions into
bags before counting, so a spliced passage and the same words scattered reach
Delta as nearly the same frequency profile. What is given up is that independent
draws from a whole corpus are an IDEALISED donor: expected frequencies exactly
matching that author's overall profile. A real interpolation is one passage by
one author on one topic, and a single work's profile can sit some way from its
author's average.

This was recorded as an unquantified caveat and has since been measured. Both
sweeps below are nineteen Euripides plays against Sophocles, 100 iterations,
seed 42, differing only in whether each mixture drew from the whole donor corpus
or from one donor work.

| | whole corpus | one work per mixture |
| --- | --- | --- |
| AUC at 20% | 0.76 | 0.74 |
| AUC at 10% | 0.63 | 0.62 |
| median SD of the drop at 20% | - | **1.43x higher** |

**The mean effect barely moves; the variance is where the idealisation was
hiding.** Sophocles shifts a play by about the same amount however the words are
drawn - the mean drop changed by around 6% - but each mixture varies far more
from the next, because each is drawing on one play's topic and register rather
than an average of seven.

Most of that inflation is not about style. The same-author control inflates
1.30x, and the control has no cross-author signal at all, so drawing from a
smaller pool is itself most of the effect. The excess attributable to genuine
heterogeneity between Sophocles' plays is **1.43 / 1.30 = 1.10x** - real, since
cross-author exceeds control in 15 works of 19 (sign test p = 0.010), and small.

**So the earlier caveat was half wrong.** It implied the idealisation inflated
the mean effect and therefore the detection figures. It does not: it buys
precision, not power. And the precision is worth little here, because detection
is limited by how much genuine works differ from each other (0.029) rather than
by how much one work's contamination varies between draws (0.012). Folding the
second into the first gives 0.032 and moves the AUC at 20% from 0.74 to 0.73.

That refinement was considered and not implemented: a parameter, its plumbing
and its tests, to move a number by one hundredth. The figures in the table above
stand as measured, and single-work draws are available on the form for anyone
who wants the realistic version.
---

### 7. The positive control

Everything above is negative, which is consistent with two different things:
that Delta is weak on same-genre tragedy, or that the bench has a fault making
every result come back null. Nothing in sections 1-6 distinguishes them.

So: Plato against Homer. Prose against epic verse, four centuries apart, a case
where the method should separate trivially. Same sweep, same settings, 33
measurable works.

| contamination | Plato vs Homer | Euripides vs Aeschylus and Sophocles |
| --- | --- | --- |
| 5% | 0.64 | 0.55 |
| 10% | 0.77 | 0.61 |
| 20% | **0.94** | 0.71 |

Baseline margins run to a median of 0.354 against 0.130 for the tragedians.
**The bench works.** The tragic-corpus result is a fact about Greek tragedy and
not a broken instrument, and section 6 can be read as it stands.

#### And the control found a bug, which is the other reason to run one

Four Platonic dialogues - *Cleitophon*, *Definitiones*, *Hipparchus*, *Lovers* -
are under 2,500 tokens, so a sweep at that sample size measures nothing for them
and reports zeros. Those were being folded into the cross-work statistics as
real works with no length and no margin. Sitting at the origin, far from a
cluster of texts three to twenty thousand tokens long, they dragged the fitted
line towards themselves and inflated the reference scatter from 0.082 to 0.137.

Which pulled the AUC at 20% from 0.94 down to 0.80: the difference between a
positive control that plainly passes and one that looks marginal.

**A bug that makes a method look weaker than it is may be the hardest kind to
notice in a project where every result so far has been negative.** It would have
passed unremarked on the tragic corpus, where every Euripides play is long
enough to sample and no zero rows appear. It was found only by running a case
whose answer was known in advance - which is the whole argument for running one.

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
- **Check the length confound before believing any ranking.** Four separate
  measures here turned out to be reading text length or baseline margin. The
  Validation bench now computes the correlation on every run rather than
  offering it on a tab somebody has to remember to open, because the one time
  it was optional it was skipped.
- **Replicate at more than one sample size, and at more than one seed.** The
  single most encouraging result in the early work was a sampling artifact, and
  three of the five later ones were mixing noise that a second seed removed.
- **A correlation needs spread on both axes.** A low length correlation at a
  narrow spread of work lengths is uninformative, not reassuring - and raising
  the sample size narrows the spread by dropping the shortest works.
- **Run the same-author control on any perturbation series.** A falling curve
  alone cannot distinguish disturbance by another author's style from
  disturbance. The control curve rises; that divergence is the evidence.
- **Do not read a z-score against a measurement error as an anomaly score.**
  With enough iterations, everything off a fitted line is significantly off it.
  Anomaly means unusual among works, which needs the between-work scatter.
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
