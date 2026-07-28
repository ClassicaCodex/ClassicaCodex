# Classica Codex

A desktop reader and research tool for the [Perseus Digital Library](http://www.perseus.tufts.edu/) — the Greek and Latin classics, their English translations, dictionaries, and the linguistic data that makes searching them work properly.

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
- **Tag** people, places, and themes across every author at once, and browse everything tagged with a given name
- **Myth Network** — a graph of which figures and places co-occur, built from your own tags as you read, not a fixed dataset
- **Places Map** — an actual map of the ancient world; click a place to see every passage that mentions it
- **Word Study** — dictionary definitions (LSJ for Greek, Lewis & Short for Latin) and every attested form of a word
- **Timeline** of authors and works across time
- **Stylometry** — authorial "fingerprints" for comparing writing style
- **Concordance** (KWIC) search across the whole library
- **Echo Finder** and **Reception Tracker** — find intertextual echoes, and track how a passage gets reused by later authors
- **Compare** two passages, or two translations of the same work, side by side
- **Export** passages to plain text, Word, or PDF, citations intact
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

The Greek lemma data is the one entry above marked **noncommercial** — it can't be sold, and because it's woven into the search and Word Study features, that restriction carries over to the whole project as distributed. Which is fine: Classica Codex is a free personal tool, and it's going to stay that way regardless.

## Platform

Windows only, for now — it's built on WinForms, which doesn't run elsewhere. No Mac or Linux build exists, and none is planned in the near term.

## License

The code in this repository is [MIT licensed](LICENSE). That covers the application itself, not the data it downloads at setup — see [Data sources & licensing](#data-sources--licensing) above for those.

## Status

This is an actively developed personal project, not a polished commercial release. Expect rough edges, and expect them to keep changing. Issues and pull requests are welcome; there's no roadmap or support commitment beyond what a hobby project can reasonably offer.
