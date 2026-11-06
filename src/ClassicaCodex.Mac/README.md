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

The current release is a preview,
[Classica Codex for Mac 3.16.3](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.16.3),
with a disk image for each kind of Mac: `arm64` for Apple silicon, `x64`
for Intel. Open it and drag **Classica Codex** into Applications. It needs
macOS 12 or later, and is notarised by Apple, so it opens like any other
app. [The main README](../../README.md#on-a-mac) walks through the first
library.

## Building it

You need the [.NET SDK](https://dotnet.microsoft.com/download), version 10 or
newer, on a Mac with Apple silicon or Intel.

```
dotnet run --project src/ClassicaCodex.Mac              # run it from the source
src/ClassicaCodex.Mac/packaging/build-app.sh            # build "Classica Codex.app" and a .dmg
```

`build-app.sh` builds for the Mac you run it on; pass `arm64` or `x64` to
build for the other kind. It writes to `src/ClassicaCodex.Mac/bin/mac-app/`.
The app carries its own copy of .NET, so the Mac you install it on does not
need one.

How the app is signed depends on what your Mac has. With a **Developer ID
Application** certificate in the keychain, `build-app.sh` signs with it,
under the hardened runtime. Set `NOTARY_PROFILE` to a profile made once
with `xcrun notarytool store-credentials`, and it also has Apple notarise
the app and the disk image, and staples the tickets to both:

```
NOTARY_PROFILE=<profile> src/ClassicaCodex.Mac/packaging/build-app.sh arm64
```

A notarised download opens on any Mac as any downloaded app does. Without a
certificate the app is signed ad hoc. A copy you build yourself still
opens straight away, but a downloaded copy is stopped the first time:
open **System Settings → Privacy & Security** and click **Open Anyway**.
`packaging/ClassicaCodex.entitlements` holds the one exception the
hardened runtime has to make, letting .NET compile code while the app runs.

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

## On Linux

The same project runs on Linux, as a preview. There is no release to
download yet, so you build it yourself:

```
src/ClassicaCodex.Mac/packaging/build-linux.sh            # x64
src/ClassicaCodex.Mac/packaging/build-linux.sh arm64
```

That works on Linux or a Mac, and writes a tarball to
`src/ClassicaCodex.Mac/bin/linux-app/`. Unpack it anywhere and run
`./classica-codex`; `./install.sh` adds it to the application menu. It
carries its own copy of .NET, so it needs only fontconfig, libX11 and ICU from
the system, which most desktops have. Wayland desktops run it through XWayland.

Everything shared works as it does on a Mac: the library, Setup and the
tools. What a Mac does through the system depends here on what is installed,
and each of these turns on one thing only:

| | |
|---|---|
| The ancient scripts | the Noto fonts (`fonts-noto-core` on Debian and Ubuntu); without them, cuneiform, hieroglyphs, Hebrew and Arabic show boxes |
| Read Aloud | `espeak-ng` |
| Saving API keys | `secret-tool` and a keyring; without them a key lasts until the app quits, and is never written to a file |
| Arcade sound | `pw-play`, `paplay` or `aplay` |

The window draws its own menu bar, with Ctrl in place of ⌘, and Help and the
windows are worded for Linux. Settings is in the File menu. The library and
settings are kept in `~/.local/share/ClassicaCodex`.

`.github/workflows/linux.yml` runs the tests on Linux, then starts the tarball
on a virtual display and presses its keys, keeping photographs of the result.

The project is still called `ClassicaCodex.Mac`, which only a developer sees:
the tarball's launcher is `classica-codex`.

## On Windows

The same project runs on Windows, as a preview beside the classic Windows app,
which stays as it is. There is no release yet. To build a zip on Windows:

```
src\ClassicaCodex.Mac\packaging\build-windows.ps1                    # x64
src\ClassicaCodex.Mac\packaging\build-windows.ps1 -Architecture arm64
```

If PowerShell refuses to run a script, start it with
`powershell -ExecutionPolicy Bypass -File <the script>`.

That writes a zip to `src\ClassicaCodex.Mac\bin\windows-app\`. Unzip it
anywhere and run `Classica Codex.exe`. It carries its own copy of .NET, needs
Windows 10 or later, and isn't code-signed, so SmartScreen warns about it as it
does about the classic download.

It keeps its settings in `%LOCALAPPDATA%\ClassicaCodex`, the classic app's own
folder, so a reader of that app needs no migration:

| | |
|---|---|
| Used by both, as they stand | the library, the reading position, text sizes, linked panes, hidden node kinds, the favourite collection, the download folder, the voice, category shapes, arcade saves, `errors.log`, `ingest-skipped.log` |
| API keys | the classic app's `translate-settings.txt`, read and written in its own format, encrypted with DPAPI for the Windows account |
| This app's own | `mac-settings.json`: appearance, sidebar, window placement |
| The classic app's own | `theme.txt`, so a dark theme chosen there isn't picked up here, and `layout-cache` |

Read Aloud uses Windows' voices through SAPI, PDF export reads the fonts in
`%WINDIR%\Fonts` and the reader's own fonts folder, and Segoe UI Historic draws
the ancient scripts. The window draws its own menu bar, with Ctrl in place of
⌘, and Help and the windows are worded for Windows.

[WINDOWS-PARITY.md](WINDOWS-PARITY.md) goes through the classic app window by
window. Every tool has a counterpart, but a few behaviours don't yet.

`.github/workflows/windows.yml` runs the tests on Windows, including the
classic app's own UI tests, then starts the zip on the runner's desktop beside
the released classic app and keeps photographs of the result.

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
