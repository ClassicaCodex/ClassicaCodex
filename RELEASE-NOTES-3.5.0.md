# Classica Codex 3.5.0

Fifty-five more portraits for the Myth Network — and something else, which the
Fates have declined to explain.

Nothing here changes your data: **no schema change, no re-ingest, no re-index.**
Upgrading is extract-and-run, and everything below applies to the library you
already have.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Fifty-five more portraits

The Myth Network draws every tag as a node, and a tag naming a figure, a place
or an object gets that figure's portrait inside its node. Sixty shipped with the
feature. There are now **a hundred and fifteen**, drawn in the same style, and
the additions are the ones a reader actually keeps meeting:

| | |
|---|---|
| **On the tragic stage** | Antigone, Electra, Oedipus, Orestes, Iphigenia, Medea, Philoctetes, Hecuba |
| **At Troy** | Ajax, Patroclus, Helen, Thetis — and the Horse |
| **In the Odyssey's way** | Circe, Scylla, Charybdis, the Sirens, the Cyclops, Penelope, Telemachus |
| **Rome and its founding** | Aeneas, Dido, Carthage, Rome, Romulus |
| **Divine** | Hades, Persephone, Hecate, Hestia, Nemesis, Prometheus, Asclepius, Atlas |
| **Monstrous** | Medusa, Cerberus, the Hydra, the Sphinx, Typhon, Pegasus, the Lion, the Serpent |
| **Places** | Argos, Corinth, Olympia, Arcadia, Egypt, the Nile, the Hellespont |
| **Objects** | the Altar, the Chariot, the Scroll, the Ship, the Sword, the Temple, the Theatre Mask |

Matching is on the tag's own text, normalised the way search is — so a tag
written `Zeus`, `zeus`, or with different accentuation all find the same face.
A tag with no portrait keeps the plain coloured node it always had, and the
category ring stays drawn around the portrait, so the network still tells gods
from kings at a glance.

Portraits appear only on nodes large enough to carry one — tags you have used
half a dozen times or more. Below that a face is a smudge, and those are the
tags you least need help picking out.

**They are replaceable.** Put a PNG named for the tag in a `Figures` folder
beside your database file and it wins over the one shipped here. That folder
travels with the library it describes and survives reinstalling, so a portrait
set you build is yours to keep.

## And one thing more

The Fates have put something else in this version, and have not seen fit to say
what it is.

It is not on a menu. It is not on the toolbar. There is no button for it, and
asking the application directly will get you nowhere at all.

It is, however, written down. Anyone who reads the Help window *properly* —
patiently, to the end of a section rather than to the end of a paragraph — will
come upon a sentence that has no business being there, and that sentence will
tell them precisely what to do.

Clotho spun it. Lachesis measured it. Atropos has said nothing whatsoever.
Whether it is worth the finding is between you and the oracle.

## Fixes

**A window new in this version could throw when closed at the wrong moment.**
Windows delivers a deactivate message to a form that is already being torn down,
and the form answered it by refreshing controls whose images it had just
released — which GDI+ reports as `Parameter is not valid`, its least helpful
sentence. It now ignores messages that arrive once it has started closing, stops
its clock the moment the close actually goes through, and hands its bitmaps back
before freeing them.

**Four windows new in this version did not scale with the system text size.**
Every other window in this application derives from a base class that
establishes font scaling before a single control is positioned; these four
derived from `Form` directly, which is precisely the bug that clipped three
labels at 125% in 3.4.1. They scale like everything else now.

**The Myth Network help had gone stale.** It has said "sixty are included" since
the first portraits shipped. It says a hundred and fifteen, because that is how
many there are.

## Still free, still open

The application code is **MIT**, as it has been throughout. This release adds no
dependency to the project and bundles no third-party asset. Nothing in it
changes the licensing position the README sets out — including the single
noncommercial constraint that comes from the Greek lemma data the setup wizard
fetches, which this does not touch and which is documented where it always was.

## Known and deferred

**Plato still cannot be cited properly**, and 3.4.1's notes said this would be
fixed in 3.5. It is not. Perseus ships Stephanus pagination as inline milestone
markers that the parser discards, so *Euthyphro* 2a still displays as `[2.1]`.
It needs a parser change and a full re-ingest of every affected text, which
deserves its own release and its own verification rather than being folded in
alongside other work. It is
[issue #14](https://github.com/ClassicaCodex/ClassicaCodex/issues/14), and it is
next.

## Checks

**986 tests, zero warnings on a clean solution build.** The archive was built
with correct forward-slash paths — 240 entries, zero backslashes, all 239 icons
in an `Icons` folder — then extracted and launched from that extraction to
confirm it runs as shipped.
