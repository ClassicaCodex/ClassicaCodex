# Classica Codex for Mac: art brief

Artwork for the macOS front end. Everything here belongs to the Mac app only and
lives in this folder (`src/ClassicaCodex.Mac/Assets/Art/`), separate from the
Windows icons in `src/ClassicaCodex.UI/Icons/`.

The app loads each file by the exact name given below. Any file that is missing
falls back to built-in vector artwork, so files can arrive one at a time and
nothing breaks while the rest are being made.

## Delivery

- **Where:** put every file in `src/ClassicaCodex.Mac/Assets/Art/` under the
  exact filename listed. Lower-case, hyphenated, `.png`.
- **How:** commit them on a branch of their own, `codex/mac-art`, and push it.
  Please don't push to `claude/bold-brown-3gstog`: that is where the Mac front
  end is being written, and two writers on one branch collide. The art branch
  is merged from there.
- **Format:** PNG-32. Where an item says *transparent*, the PNG must carry a
  real alpha channel: the corners and the empty background at alpha 0. A
  checkerboard painted into the pixels is not transparency, and is the most
  common way generated art fails this brief, so please check a corner pixel
  before committing.
- **Colour:** sRGB.
- **No text.** No words, signatures, watermarks or captions in any image.
  Greek or Latin letters appear only where an item asks for them, and then no
  more than the two or three characters it names. Generated lettering beyond
  that comes out as near-letters, and in an app for reading Greek and Latin a
  wrong letter is worse than none.

## The look

**The family:** the Windows icons are painted, softly lit objects: an owl on a
leather-bound book, a parchment under a magnifying glass, bronze roundels with
figures in them. The Mac art is the same family made finer: cleaner
silhouettes, one gentle key light from the upper left, a soft contact shadow,
real materials (vellum, bronze with verdigris, oak, lapis, gold leaf, papyrus,
marble). Rich, but calm: this sits beside text people read for hours.

**Palette:**

| Role | Light ("Parchment") | Dark ("Lamplight") |
|---|---|---|
| Page | `#FAF7F0` | `#18181A` |
| Background | `#EDE7DA` | `#1E1E20` |
| Panel | `#E4DCCC` | `#2C2C32` |
| Rule / border | `#C7BEAC` | `#3C3C40` |
| Ink | `#1C1712` | `#E8E4DA` |
| Bronze accent | `#A0612B` | `#E0A15E` |
| Gold | `#C9A45C` | `#D8B66E` |
| Verdigris | `#3F7F74` | `#5FA597` |
| Lapis | `#2F4A7A` | `#6F8FC8` |

Objects should read on both the light and dark backgrounds above. Test a tile
on `#FAF7F0` and on `#1E1E20` before committing: a dark object needs a
lighter rim, a pale one a darker edge.

**References in this repository** (all 256×256, transparent):

- `src/ClassicaCodex.UI/Icons/AppIcon.png`: the owl on the book, the app's mark
- `src/ClassicaCodex.UI/Icons/Library.png`, `Search.png`, `WordStudy.png`,
  `Antikythera.png`: the painted-object style
- `src/ClassicaCodex.UI/Icons/Figures/Athena.png`, `Figures/Achilles.png`,
  `Figures/Altar.png`: the bronze-roundel style

The same files are on GitHub, e.g.
`https://raw.githubusercontent.com/ClassicaCodex/ClassicaCodex/main/src/ClassicaCodex.UI/Icons/AppIcon.png`.

---

## 1. App icon

| | |
|---|---|
| File | `app-icon-1024.png` |
| Size | 1024 × 1024 |
| Transparency | Transparent outside the icon shape |

macOS icon grid: the artwork sits on a rounded square ("squircle") 824 × 824,
centred, corner radius about 185 px, with a 100 px transparent margin all
round. A soft drop shadow may fall into the lower margin, as Apple's own icons
do. Nothing else goes in the margin.

**Subject:** the app's mark redrawn for the Mac. A bronze owl, Athena's owl,
standing on an open codex whose pages fan slightly, on a deep lapis tile with a
fine gold Greek-key border inset from the edge. Warm light on the owl, cool
depth in the lapis. The owl and book are the silhouette: they must still read
as "owl on a book" at 32 × 32 and as a clear shape at 16 × 16. No lettering.

## 2. Welcome art

Shown behind the "Open your library" screen on first run, and on the Home view.

| | |
|---|---|
| Files | `welcome-hero-light.png`, `welcome-hero-dark.png` |
| Size | 2880 × 1620 |
| Transparency | Opaque |

**Subject:** a reading room of the ancient world: a colonnade opening onto a
calm sea, scroll pigeonholes in a cedar wall, a reading desk with an open codex
and an oil lamp. Light version: late-morning sun, parchment and marble. Dark
version: the same room at night, lit by the lamp and a moon over the water.
Same composition in both, so switching appearance feels like time passing in
one place. The left 45 % must stay quiet and low in contrast (soft wall, sky
or shadow), because the app draws its title and buttons over it. The detail
belongs on the right.

## 3. Textures

Seamless tiles behind the reading panes and the sidebar.

| File | Size | Transparency | Subject |
|---|---|---|---|
| `parchment-tile.png` | 1024 × 1024 | Opaque, seamless | Fine vellum, very faint fibres and mottling, centred on `#FAF7F0`. Contrast so low it reads as warmth, not pattern: no stain, crease or edge. |
| `lamplight-tile.png` | 1024 × 1024 | Opaque, seamless | The dark counterpart: dark linen-paper grain centred on `#18181A`, the faintest warm unevenness. |

"Seamless" means it tiles with no visible join in either direction; offsetting
it by half its width and height should show no seam.

## 4. About window

| | |
|---|---|
| File | `about-art.png` |
| Size | 1600 × 900 |
| Transparency | Opaque |

**Subject:** the Antikythera mechanism's bronze gears and an Alexandrian scroll
together on dark oak, warm light raking across them. The right third stays dark
and simple, because the version and credits sit there.

## 5. Empty states

Small illustrations for windows with nothing in them yet.

| File | Size | Transparency | Subject |
|---|---|---|---|
| `empty-library.png` | 800 × 600 | Transparent | An empty cedar scroll-rack, one scroll waiting on the floor beside it. |
| `empty-results.png` | 800 × 600 | Transparent | An oil lamp and a magnifying glass over a blank wax tablet. |
| `empty-selection.png` | 800 × 600 | Transparent | A closed codex with a ribbon bookmark, a stylus laid across it. |

## 6. Feature tiles

One tile per tool, shown in the Tools gallery and the sidebar. Each is a single
object in the painted style, centred, filling about 80 % of the canvas, with a
soft contact shadow inside the canvas.

| | |
|---|---|
| Size | 512 × 512 each |
| Transparency | Transparent |

The **Windows icon** column is the matching file in
`src/ClassicaCodex.UI/Icons/`: the idea to keep, redrawn in the finer Mac
style, not copied.

| File | Tool | Subject | Windows icon |
|---|---|---|---|
| `tile-reader.png` | Reader | Open codex, Greek column on the left page, English on the right, shown as ruled lines rather than letters | `SideBySide.png` |
| `tile-word-study.png` | Word Study | Leather lexicon open, a gold-tipped stylus pointing at one entry; the letters "ΑΩ" on the cover | `WordStudy.png` |
| `tile-search.png` | Search | Magnifying glass with a bronze handle over a scroll | `Search.png` |
| `tile-concordance.png` | Concordance | Strips of parchment aligned down a central gold rule, like a printed concordance | `Concordance.png` |
| `tile-core-vocabulary.png` | Core Vocabulary | A stack of wax tablets, the top one with a laurel sprig | `CoreVocabulary.png` |
| `tile-morphology.png` | Morphology search | A bronze balance weighing small tiles | `Morphology.png` |
| `tile-echo-finder.png` | Echo Finder | Two scrolls with a thin gold thread running between matching places on each | `SimilarWorks.png` |
| `tile-reception.png` | Reception Tracker | A scroll handing on to a codex handing on to a printed book, left to right | `ReceptionTracker.png` |
| `tile-cross-language.png` | Cross-Language Echo | Two tablets, one marble and one wax, joined by a gold arc | `Translate.png` |
| `tile-compare.png` | Compare | Two parchment leaves overlapping, offset | `CompareTexts.png` |
| `tile-collation.png` | Collate editions | Two printed pages with small bronze markers at the lines where they differ | `Collate.png` |
| `tile-tags.png` | Tags | Bronze tags on cords, tied to a scroll's handle | `AutoTag.png` |
| `tile-bookmarks.png` | Bookmarks | Silk ribbon markers in red, blue and gold hanging from a closed codex | `Bookmarks.png` |
| `tile-myth-network.png` | Myth Network | Bronze roundels with figure silhouettes, joined by gold threads | `MythNetwork.png` |
| `tile-places-map.png` | Places Map | A painted Mediterranean chart on vellum with a bronze pin | `PlaceMap.png` |
| `tile-timeline.png` | Timeline | A marble frieze strip with small medallions along it | `Timeline.png` |
| `tile-stylometry.png` | Stylometry | A brass instrument measuring two quills, like calipers | `Stylometry.png` |
| `tile-validation.png` | Validation bench | An apothecary's balance with a set of graduated bronze weights | `StylometryCompare.png` |
| `tile-workbench.png` | Translation workbench | A writing desk: an open source text, a wax tablet beside it, a stylus | `Translate.png` |
| `tile-ai-translate.png` | AI translation | A codex with a faint, cool light rising from its pages | `Translate.png` |
| `tile-reactions.png` | Fictional Ancient Reactions | Theatre masks, comic and tragic, half-turned toward each other as if talking | `GettingStarted.png` |
| `tile-antikythera.png` | Antikythera mechanism | The bronze gear train, verdigris in the teeth | `Antikythera.png` |
| `tile-almagest.png` | Ptolemy's Cosmos | An armillary sphere, brass rings, Earth at the centre | `Almagest.png` |
| `tile-harmonics.png` | Ptolemy's Harmony | A monochord: one string over a long sounding box with a movable bridge | `Harmonics.png` |
| `tile-medieval-hands.png` | Medieval Hands | A manuscript leaf with a decorated initial and a quill resting on it | `Palaeography.png` |
| `tile-nordic.png` | Medieval Nordic manuscripts | A vellum leaf in a carved Norse wooden cover | `BrowseText.png` |
| `tile-research.png` | Research Bench | A cork board with pinned parchment slips joined by red thread | `DataTable.png` |
| `tile-starting-points.png` | Where should I start? | A milestone carved with a single arrow, a path leading past it | `GettingStarted.png` |
| `tile-alphabet.png` | Alphabet reference | A bronze plaque with three engraved letters "ΑΒΓ" | `Contents.png` |
| `tile-read-aloud.png` | Read Aloud | A bronze horn beside an open scroll | `Pronunciation.png` |
| `tile-export.png` | Export | A sealed letter with a wax seal, an arrow-shaped ribbon | `Export.png` |
| `tile-setup.png` | Setup | Crates of scrolls arriving on a cart | `IngestCorpus.png` |
| `tile-authors.png` | Authors | A row of three small marble busts | `Library.png` |
| `tile-settings.png` | Settings | A brass astrolabe-like dial with a small lever | `Settings.png` |

## 7. Disk image background

For the window that opens when someone mounts the `.dmg` to install the app.

| | |
|---|---|
| File | `dmg-background.png` |
| Size | 1320 × 800 (shown at 660 × 400) |
| Transparency | Opaque |

**Subject:** the parchment texture with a soft, hand-painted gold arrow running
left to right across the middle, where the app icon (left) is dragged to the
Applications folder (right). Leave a clear 256 × 256 area at x 330, y 400 and at
x 990, y 400 (2× pixels) for those two icons.

---

## Delivered

All 44 files arrived from Codex, generated with its image tool from this
brief (the prompts are in `prompts.json`), and were checked on import:
every name, size and format matches the tables above; every transparent
file has real alpha (corners at 0, nothing painted in); every opaque file is
fully opaque; the two textures tile with no visible seam; the ΑΩ and ΑΒΓ are
the right letters.

One change was made on import. The icon's lapis tile came at 716 pixels
inside the canvas rather than the 824 of Apple's grid, which would have sat
noticeably smaller than its neighbours in the Dock; it was scaled to the
grid, centred, with the 100-pixel margin.

Codex's own notes, kept because they are true: these are illustrations, not
reconstructions - the authors group includes one bronze bust among marble
ones, the manuscript initial is an abstract ornament, and some objects carry
added decoration. The welcome scenes are upscaled from the generator's
native resolution.
