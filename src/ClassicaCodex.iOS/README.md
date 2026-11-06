# Classica Codex for iPad: proof of concept

This is the Mac front end running on an iPad. It is a test of whether a mobile
version is practical, not a product. It has been tested in the simulator and
installed on one real iPad.

It holds almost no code of its own. `ClassicaCodex.iOS.csproj` compiles every
file in `src/ClassicaCodex.Mac` where it is, so a change there reaches the iPad.
What it adds is what a device with no windows needs:

| File | What it does |
|---|---|
| `GlobalUsings.cs` | Sends `Window` and `TopLevel` to `PageWindow` for every shared file. This is why the 83 tool windows needed no changes. |
| `PageWindow.cs` | Offers the members of `Window` that the shared code uses. |
| `PageHost.cs` | The app's one view. Shows a tool as a full-screen page and a dialog as a card, and keeps both clear of the status bar. |
| `TouchGestures.cs` | Selects the line under a long-press, because the line menu acts on the selected line. |
| `Shell/MainWindow.axaml` | The Mac's main window with a page for its root, and 44-point toolbar buttons. |
| `MobileApp.cs`, `AppDelegate.cs` | Start the app, connect the two files below, settle the download folder and open the library in its Documents folder. |
| `DeviceShell.cs` | Opens links in the browser and folders in the Files app. A Mac does this by starting `open`, and iOS does not let an app start a process. |
| `DeviceKeychain.cs` | Keeps the API keys in the iOS keychain. |
| `DeviceScreen.cs` | Keeps the screen on during a setup step, because iOS suspends an app when the screen locks. |

The Mac code knows about the iPad in a few places:

- `Features/Research/ResearchFeature.cs` named `Avalonia.Controls.Window` in full,
  where an alias cannot reach it, and now does not.
- `MacDataFolder` keeps an iPad's downloads in `Documents/ClassicaCodexData`,
  whatever was saved before. The top of the app's container is read-only on a real
  iPad, though not in the simulator, and the container's path changes when the app
  is updated, so a saved path goes stale. The folder is left out of iCloud backups.
- `MacShell.OpenInstead`, `Keychain.StoreInstead` and `SetupRunner.StayAwake` are
  filled in by `MobileApp`, because those files are also compiled for the Mac and
  cannot name the iOS SDK.
- `SetupRunner` fetches a step that clones through `GitHubArchiveFetchService`, in
  `ClassicaCodex.Ingestion`, because LibGit2 has no iOS library. It reads GitHub's
  archive of a repository as it arrives, and writes the same files by the same rules.
- `SystemWords` has a short table of what would be untrue on an iPad. Everything
  else there reads as it does on Linux.

## Building it

The project is not in `ClassicaCodex.sln`, because building it needs the .NET iOS
workload and that would break the build for anyone without it. The workload
cannot be installed into the system .NET without an administrator password, so
this was built with a private copy of the SDK in `~/.dotnet-mobile`:

```bash
export DOTNET_ROOT="$HOME/.dotnet-mobile" DOTNET_MULTILEVEL_LOOKUP=0 PATH="$HOME/.dotnet-mobile:$PATH"
```

```bash
dotnet workload install ios
```

```bash
dotnet build src/ClassicaCodex.iOS
```

The app is built to `bin/Debug/net10.0-ios/iossimulator-arm64/ClassicaCodex.Mac.app`.

Delete `bin` and `obj` in this folder after a package or target framework
changes, for example after merging `main`. An incremental build can keep the old
native code beside the new DLL, and the app then quits at launch with `Failed to
load AOT module ... doesn't match assembly` in the system log.

The workload asks for Xcode 27.1 exactly, so `ValidateXcodeVersion` is off in the
project for the sake of Xcode 27.2. A release for the App Store should use the
Xcode the workload names.

## Giving it a library

The app reads `classicacodex.db` from its Documents folder, and shows the
no-library page if the file is not there. Create a New Library makes that file
without asking where, because the app would not find one anywhere else, and goes
on to Setup.

To test with a full library instead, in the simulator, run the app once so it has
a folder, and then clone a library into it. On APFS a clone takes no extra disk
until one of the copies changes, and the app can only write to its own copy:

```bash
cp -c ~/Documents/classicacodex.db "$(xcrun simctl get_app_container booted org.classicacodex.ipad data)/Documents/"
```

## Putting it on a real iPad

The iPad needs Developer Mode on and to be paired with the Mac. A team
provisioning profile that includes the iPad is enough to sign the app. The
signing details go on the command line, so nobody's identity is in the project.
This is a full ahead-of-time build and takes about 10 minutes:

```bash
dotnet build src/ClassicaCodex.iOS -c Release -r ios-arm64 "-p:CodesignKey=Apple Development" "-p:CodesignProvision=iOS Team Provisioning Profile: *"
```

Find the iPad's identifier, and then install the app and copy a library to it.
Over a home network the 3.4 GB library took about 10 minutes:

```bash
xcrun devicectl list devices
```

```bash
xcrun devicectl device install app --device <identifier> src/ClassicaCodex.iOS/bin/Release/net10.0-ios/ios-arm64/ClassicaCodex.Mac.app
```

```bash
xcrun devicectl device copy to --device <identifier> --domain-type appDataContainer --domain-identifier org.classicacodex.ipad --source ~/Documents/classicacodex.db --destination Documents/classicacodex.db --user mobile
```

## What was tested

On a real iPad Pro 13-inch (M4), the app installs, opens a full library and stays
running. A throwaway copy of the app, running the same code, also showed this on
that iPad:

- The download folder moving to Documents, off a bad path saved by an earlier
  build, and its exclusion from backups
- Saving, reading, replacing and removing a key in the keychain
- World Map, The Arabian Nights and Aquinas in Setup, downloaded and ingested
- All 11 steps that clone, followed by the word index, from an empty library in
  about 31 minutes, on a home connection. That is 8.8 GB of downloads and a 2.9 GB
  library of 3,747 works and 2.15 million passages. The app had 4.9 GB of memory
  available to it, and never less than 4.4 GB. Edition by edition, the six main
  collections match a library built on a desktop
- An error in an action going to the log, with the app still running
- A link handed to iOS, which accepted it, and the Files app reported as reachable

Everything below was tested in the iPad simulator, against a full library of
3.4 GB:

- Opening the library, the library tree, and the reader in both orientations and
  both themes
- Touch scrolling, and a long-press for the line menu
- Word Study, with the parse, the LSJ entry and the occurrences
- Search across the whole library
- Dialogs, tested with Bookmark, and typing Latin and pasting Greek into a text box
- The API key links opening Safari, and Show in Files opening the Files app
- A key saved in Settings surviving a reinstall, and then being removed
- Steps run from Advanced Setup, and from Guided Setup starting with no library:
  Create a New Library, install a step, and the work appears in the library
- Git and the archive fetch compared on four real repositories, on a Mac: all
  2,568 files identical, and the archive about ten times faster
- Greek, Arabic, Hebrew, Egyptian hieroglyphs and cuneiform, all with the
  system's own fonts

## What does not work yet

- **Wrapped cuneiform is clipped.** Rows are too short for the signs.
- **Most tools have not been opened.** All 83 compile, and only Word Study,
  Search and the bookmark dialog have been run. A tool laid out for a desktop
  window will not necessarily fit, and some need another layout.
- **The on-screen keyboard covers the lower part of a page.** Nothing moves out
  of its way yet.
- **Typing on iOS's own Greek keyboard has not been tried.** Search ignores
  accents, so it should not need polytonic input, but nobody has checked.
- **Going to a passage from a tool page stays on the tool page.** The reader
  moves behind it. The double-tap that does this has not been tested.
- **Desktop words are still on screen**, such as "right-click" and "Ctrl+Enter".
- **Read Aloud does nothing**, because `say` does not exist on iOS. It needs
  AVSpeechSynthesizer.
- **BFM is unproven.** It is let through iOS's block on plain http, but the site
  was not answering when this was written.
- **A setup step stops if the app leaves the screen.** The screen is kept on while
  one runs, but iOS suspends an app that is switched away from, and a step does
  not resume. It has to be run again.
- **The Choose buttons in Setup do not last.** The download folder goes back to
  Documents the next time the app starts.
- **The instrument pages are missing**, because `src/ClassicaCodex.UI/Pages` is not
  bundled, and nothing could show them yet.
- **An iPhone would need a new reader.** Two panes do not fit, and `UIDeviceFamily`
  in `Info.plist` asks for iPad only for that reason.
- **Only an M4 iPad has run Setup.** A smaller iPad has less memory and a slower
  processor, and nobody has tried one.
- **The downloads stay on the device after they are installed**, which leaves
  8.8 GB beside the library. Whether Setup still needs them has not been checked.
