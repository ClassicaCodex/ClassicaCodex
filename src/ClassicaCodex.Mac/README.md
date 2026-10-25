# Classica Codex for the Mac

The Mac app reads the same libraries as the Windows app, using the same
setup wizard and the same tools. It has its own window design and artwork.
A library built on one machine opens on the other: copy the `.db` across,
or keep it in a folder both machines can reach, and **File → Open
Library…** finds it.

It is written in [Avalonia](https://avaloniaui.net/) 12 over the shared
`ClassicaCodex.Core`, `ClassicaCodex.Data` and `ClassicaCodex.Ingestion`
libraries. Everything that knows what a passage, an edition or a lemma is
lives in those libraries; this folder only draws the windows. Its version
number follows the Windows app's.

## Downloading it

The first release is a preview,
[Classica Codex for Mac 3.15.1](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.15.1),
with a disk image for each kind of Mac: `arm64` for Apple silicon, `x64`
for Intel. Open it and drag **Classica Codex** into Applications. It needs
macOS 12 or later. The first time you open it macOS stops it, as described
below; [the main README](../../README.md#on-a-mac) walks through that and
the first library.

## Building it

You need the [.NET SDK](https://dotnet.microsoft.com/download), version 8 or
newer, on a Mac with Apple silicon or Intel.

```
dotnet run --project src/ClassicaCodex.Mac              # run it from the source
src/ClassicaCodex.Mac/packaging/build-app.sh            # build "Classica Codex.app" and a .dmg
```

`build-app.sh` builds for the Mac you run it on; pass `arm64` or `x64` to
build for the other kind. It writes to `src/ClassicaCodex.Mac/bin/mac-app/`.
The app carries its own copy of .NET, so the Mac you install it on does not
need one.

The app is signed ad hoc, not notarised; notarising needs a paid Apple
Developer account. A copy you build yourself opens straight away. A
downloaded copy is stopped the first time: open **System Settings →
Privacy & Security** and click **Open Anyway**.

`ClassicaCodex.Mac.slnf` is the solution filter for working on the Mac. It
leaves out the WinForms projects, which do not build there.

## What is the same as Windows

Every tool the Windows app offers is here:
- the reader, its two linked pages, prefaces, apparatus and history
- search, concordance, bookmarks and tags
- comparing translations and collating editions
- the translation workbench and its AI translation
- word study, morphology, vocabulary and the alphabet
- echoes and reception
- the myth network, places map and timeline
- stylometry
- Medieval Hands and the manuscript pages
- the three instruments
- the whole research area
- export to text, Word, PDF, BibTeX and RIS
- the setup wizard, settings and help
- and the arcade, for anyone who finds it

New collections added to the Windows setup wizard show up here too, because
both apps read one shared catalogue. Their help topics and About credits do
not: those are the Windows text reworded for the Mac, in
`Features/Help/HelpTopics.cs` and `Features/About/AboutWindow.cs`, and have
to be copied across when a collection adds one.

## What is different, and why

- **Layout.** The Windows app's eighteen toolbar buttons are replaced by a
  sidebar with two lists, **Library** (⌘1) and **Tools** (⌘2), plus a
  gallery of every tool (⇧⌘T) and the menu bar. The toolbar keeps only the
  reader's everyday controls.
- **API keys** for AI translation are kept in the macOS Keychain, under
  "Classica Codex".
- **Read Aloud** uses the Mac's own voices through `say`. As on Windows,
  Greek is transliterated first and Latin is read as written.
- **Files.** The library and settings are kept in
  `~/Library/Application Support/ClassicaCodex`.
- **Keys** are Mac keys: ⌘ in place of Ctrl, ⌘, for Settings, ⌘W to close a
  window, ⌘[ and ⌘] for Back and Forward.
- **Light and dark** follow the system appearance unless **View →
  Appearance** says otherwise.

## Everyday keys

| | |
|---|---|
| ⌘O / ⇧⌘N | Open a library / start a new one |
| ⌘L | Filter the library |
| ⌘F | Search |
| ⇧⌘K | Concordance |
| ⇧⌘B | Bookmarks |
| ⌘E | Export the selected passage |
| ⇧⌘C | Copy the selected passage |
| ⌘+ / ⌘− / ⌘0 | Bigger, smaller, default text |
| ⌥⌘S | Hide or show the sidebar |
| ⌘[ / ⌘] | Back / Forward |
| ⌘, | Settings |

## For developers

[PORTING.md](PORTING.md) covers:
- how the project is laid out, and which Windows files it compiles directly
- how a Windows window is brought across
- the headless tests, which photograph every window in light and dark
- the small sample library to develop against

The artwork is in `Assets/Art`. [ART-BRIEF.md](Assets/Art/ART-BRIEF.md) is
the brief it was painted to. Third-party licences are in
[THIRD-PARTY-NOTICES-MAC.md](THIRD-PARTY-NOTICES-MAC.md).
