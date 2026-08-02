# Classica Codex

A desktop reader and research tool for the [Perseus Digital Library](http://www.perseus.tufts.edu/) — the Greek and Latin classics (plus optional Post-Classical Greek and the Renaissance authors who reworked the classics in English), their translations, dictionaries, and the linguistic data that makes searching them work properly.

Built as a personal project, for reading and researching the classics more closely than a browser tab really allows.

## Download

Download the latest Windows release from the
[GitHub Releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).

1. Download the Windows ZIP file.
2. Extract the entire archive.
3. Run `ClassicaCodex.UI.exe`.
4. Follow the setup wizard to download and ingest the classical corpora.

> Windows may show an “Unknown publisher” warning because the application is not code-signed.
> 

<img width="1805" height="769" alt="image" src="https://github.com/user-attachments/assets/d680005b-7226-4a14-94b0-1fadf02ba954" />

<img width="1168" height="770" alt="image" src="https://github.com/user-attachments/assets/db82c1b9-5421-4a62-9c07-a2c11273e26b" />

<img width="1171" height="759" alt="image" src="https://github.com/user-attachments/assets/01cbec82-500e-4d85-bea4-673bb3dc3acc" />


## Features

- **Read** the original alongside a translation, for any work in the Perseus corpus
- **Search** that understands word forms — searching a headword finds every inflection of it, not just exact spellings
- **Morphology search** — find every line matching a specific grammatical form (case, tense, mood, voice…), not just a specific word
- **Tag** people, places, and themes across every author at once, and browse everything tagged with a given name (with **Auto-Tag** to suggest matches for a name automatically), and **bookmark** individual lines with your own notes
- **Myth Network** — a graph of which figures and places co-occur, built from your own tags as you read, not a fixed dataset
- **Places Map** — an actual map of the ancient world; click a place to see every passage that mentions it
- **Word Study** — dictionary definitions (LSJ for Greek, Lewis & Short for Latin) and every attested form of a word
- **Timeline** of authors and works across time
- **Stylometry** — authorial "fingerprints" for comparing writing style
- **Concordance** (KWIC) search across the whole library
- **Echo Finder** and **Reception Tracker** — find intertextual echoes, and track how a passage gets reused by later authors
- **Cross-Language Echo** — the same idea across languages, for finding where a Latin (or English) passage is reworking a Greek original, or vice versa
- **Compare** two passages, or two translations of the same work, side by side
- **Translate it yourself** — a workbench for working through a text one passage at a time, with the passage before and after shown for context, every word clickable for its dictionary headword, grammatical parse and LSJ or Lewis & Short entry, and an alphabet reference for a script you don't read yet. Your translation becomes an edition like any other. AI help is available per word or per passage, but always appears beside your work rather than in it, and the published translation stays out of reach until you've written something
- **AI-assisted translation** — translate a single passage on demand, or an entire work at once, using Claude or Gemini. Off by default and opt-in per use — nothing is sent anywhere unless you ask for it, and the app works completely offline without it
- **Read Aloud** — text-to-speech for Greek, Latin, or English, using whatever voices are already installed on Windows; fully offline, no network involved
- **Export** passages to plain text, Word, or PDF, citations intact
- **Recent searches** — the last ten searches you ran, with every filter, recorded automatically; nothing to save and nothing to tidy up
- **Picks up where you left off** — reopens the passage you were last reading, and can be turned off if you'd rather it didn't
- Dark mode

## Getting started

There's no installer yet — this runs from source. You'll need:

- **Windows** (this is a WinForms app — see [Platform](#platform))
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) or later (or `dotnet build` from the command line)

Clone the repo, open `ClassicaCodex.sln`, build, and run. On first launch, a setup wizard walks you through everything else — it'll ask which of two ways you'd like to do that:

- **Guided Setup** (default on first run) — one step at a time, plain language, no file paths or repository URLs on screen
- **Advanced Setup** — every data source on one screen, for pointing at files you've already downloaded or wanting more control over where things go

Either way, the first real step is the database — where your library, tags, and bookmarks will live. Everything after that (the texts, dictionaries, lemma data, map data, and the word index that makes search fast) can be done in whatever order suits, and it's safe to skip anything and come back to it later from the same wizard.

## Data sources & licensing

Classica Codex doesn't own or bundle any of the texts, dictionaries, or linguistic data it reads — none of it ships in this repository. All of it is fetched by the setup wizard from the following open projects, each under its own license:

| Source | Provides | License |
|---|---|---|
| [PerseusDL/canonical-greekLit](https://github.com/PerseusDL/canonical-greekLit) & [canonical-latinLit](https://github.com/PerseusDL/canonical-latinLit) | The Greek and Latin texts themselves | CC BY-SA 4.0 |
| [PerseusDL/lexica](https://github.com/PerseusDL/lexica) | LSJ (Greek) and Lewis & Short (Latin) dictionaries | CC BY-SA 4.0 |
| [gcelano/LemmatizedAncientGreekXML](https://github.com/gcelano/LemmatizedAncientGreekXML) | Greek word-form → headword mapping | **CC BY-NC 4.0** |
| [lascivaroma/latin-lemmatized-texts](https://github.com/lascivaroma/latin-lemmatized-texts) | Latin word-form → headword mapping | CC BY-SA 4.0 |
| [Natural Earth](https://www.naturalearthdata.com/) (`ne_110m_land.geojson`) | Coastline for the Places Map | Public domain |
| [perseus-aa/json](https://github.com/perseus-aa/json) | Art & Archaeology catalog data (vases, coins, sites…) for the Places Map and Myth Network | Perseus terms; catalog only — images are always loaded live from Perseus, never downloaded |
| [Princeton WordNet](https://wordnet.princeton.edu) | English word-form → headword mapping and definitions, for search and Word Study on translations | WordNet License (permissive, free for any use) |
| [PerseusDL/canonical-engLit](https://github.com/PerseusDL/canonical-engLit) | Renaissance & Early Modern English texts (Shakespeare, Marlowe, Hakluyt…), optional | CC BY-SA 4.0 |
| [OpenGreekAndLatin/First1KGreek](https://github.com/OpenGreekAndLatin/First1KGreek) | Post-Classical Greek texts extending the corpus into late antiquity, optional | CC BY-SA 4.0 |

The Greek lemma data is the one entry above marked **noncommercial** — it can't be sold, and because it's woven into the search and Word Study features, that restriction carries over to the whole project as distributed. Which is fine: Classica Codex is a free personal tool, and it's going to stay that way regardless. (WordNet's license, despite doing a similar job for English, doesn't carry the same restriction — it's permissive and doesn't add a second constraint on top of the Greek lemma data's.)

The AI-assisted translation feature is a separate case from all of the above: it isn't a bundled dataset at all, just an optional connection to a third-party API (Anthropic's Claude or Google's Gemini) that you provide your own key for. Nothing about it is required to use the app, and nothing is sent anywhere unless you explicitly ask for a translation.

## Platform

Windows only, for now — it's built on WinForms, which doesn't run elsewhere. No Mac or Linux build exists, and none is planned in the near term.

## License

The code in this repository is [MIT licensed](LICENSE). That covers the application itself, not the data it downloads at setup — see [Data sources & licensing](#data-sources--licensing) above for those.

## Status

Version 2.0.

Version 1 was a first draft. This is a substantially different application: a
searchable, taggable, cross-referenced library rather than a reader, plus the
translation workbench, and a schema that has moved through six migrations
since. Existing databases upgrade in place on first launch — annotations,
bookmarks and tags are carried forward.

Built for my own reading, and shared in case it's useful to someone else doing
the same thing.

