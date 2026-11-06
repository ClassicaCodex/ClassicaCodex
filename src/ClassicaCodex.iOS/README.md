# Classica Codex for iPad: proof of concept

This is the Mac front end running on an iPad, in the simulator. It is a test of
whether a mobile version is practical, not a product. Nothing here has been run
on a real device.

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
| `MobileApp.cs`, `AppDelegate.cs` | Start the app and open the library in its Documents folder. |

The one change to the Mac code is in `Features/Research/ResearchFeature.cs`,
which named `Avalonia.Controls.Window` in full, where an alias cannot reach it.

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
The workload asks for Xcode 27.1 exactly, so `ValidateXcodeVersion` is off in the
project for the sake of Xcode 27.2. A release for the App Store should use the
Xcode the workload names.

## Giving it a library

The app reads `classicacodex.db` from its Documents folder, and shows the
no-library page if the file is not there. In the simulator, run the app once so
it has a folder, and then clone a library into it. On APFS a clone takes no
extra disk until one of the copies changes, and the app can only write to its
own copy:

```bash
cp -c ~/Documents/classicacodex.db "$(xcrun simctl get_app_container booted org.classicacodex.ipad data)/Documents/"
```

## What was tested

All of this is in the iPad simulator, against a full library of 3.4 GB:

- Opening the library, the library tree, and the reader in both orientations and
  both themes
- Touch scrolling, and a long-press for the line menu
- Word Study, with the parse, the LSJ entry and the occurrences
- Search across the whole library
- Dialogs, tested with Bookmark, and typing Latin and pasting Greek into a text box
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
- **Read Aloud and saved API keys do nothing**, because `say` and the `security`
  command do not exist on iOS. They need AVSpeechSynthesizer and the iOS keychain.
- **The Setup wizard should not be offered.** It cannot run on a device.
- **An iPhone would need a new reader.** Two panes do not fit, and `UIDeviceFamily`
  in `Info.plist` asks for iPad only for that reason.
- **Getting a library onto a device** is only solved for the simulator.
