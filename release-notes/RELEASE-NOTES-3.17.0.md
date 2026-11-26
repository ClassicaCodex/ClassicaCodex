> **Historical.** These are the notes for Classica Codex 3.17.0, the first Windows
> release of the app the Mac and Linux run. It has been followed by
> [Classica Codex 3.18.0](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/v3.18.0).
> Any download link or SHA-256 below refers to the 3.17.0 ZIPs, not to the current
> ones, so do not check a current download against a checksum printed here.

# Classica Codex 3.17.0

Windows gets the new interface.

The app the Mac and Linux run is now the Windows download, in place of the
classic Windows app. It reads the same library and keeps its settings in the
same folder, so there is nothing to migrate: it opens on your library at the
passage you left. Bringing it to Windows turned up three things a Windows
reader would have met first, all fixed here. Greek lines ran into each other,
the reader's lines ignored the text size, and a jump deep into a long work
could take half a minute.

**[Download for Windows](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
`win-x64` for most PCs, `win-arm64` for Windows on Arm (new with this release,
and not yet tried on an Arm PC). Extract all of it and run `Classica Codex.exe`. Windows will show a blue "Windows protected your PC"
box on first run because the app isn't code-signed; click More info, then Run
anyway.

## Coming from the classic app

- **Nothing to migrate.** The new app keeps its settings in
  `%LOCALAPPDATA%\ClassicaCodex`, the classic app's own folder: your library,
  reading position, AI keys, text sizes, download folder, favourite collection
  and Read Aloud voice are all where you left them. A light or dark theme you
  chose in the classic app comes across the first time it opens.
- **The same tools, laid out differently.** The sidebar holds the library and,
  under Tools, everything the classic toolbar had. **View → All Tools…**
  (Ctrl+Shift+T) shows them all at once, and the menu bar has the rest.
- **The keys you know.** F1 opens Help, Alt+Left and Alt+Right go Back and
  Forward, Ctrl+F searches. The full list is in the README inside the zip.
- **The classic app still works** if you keep its folder, and the two can sit
  side by side. Close one before setting up or updating a library in the
  other.

## Fixed on the way

- **Greek lines no longer run into each other.** Palatino Linotype, the Greek
  face on Windows, has no elision mark (the `ʼ` of δʼ and παρʼ, which every
  Perseus Greek text uses). Windows borrowed it from a taller face, and the
  line holding it dropped onto the next. That was about three Greek passages
  in ten, and Middle High German, Old Norse and the Egyptian transliterations
  were much the same. Every line now keeps its place, in the reader and in
  every window that shows passages.
- **The reader's lines follow your text size.** They were the same height at
  every size, which at 12 point left them almost touching. They are now
  spaced in proportion, close to how the classic app spaced them.
- **A jump into a long work lands at once.** Following a search result or a
  bookmark deep into Livy took up to 38 seconds, and the middle of the Iliad
  scholia six, because the reader drew every passage on the way. Each now
  takes about a fifth of a second.
- **A library on a drive that isn't plugged in is named, with Retry,** rather
  than greeted as a first run. Setting up a new library from that page would
  have made the app forget the old one.
- **A library that won't open is reported at launch,** with its path and the
  reason, rather than written to the log while the window opened as if
  nothing were wrong. And the app lets go of the file as soon as it fails,
  so it can be moved or replaced without quitting first.
- **A fatal error says so before the app closes,** as the classic app's did.

## On the Mac

The Mac preview is still 3.16.3. It shares this app, so these fixes reach it
with its next release, and its reading lines will be more open than they are
now.

1,806 tests, zero warnings.

The downloads are not code-signed, so if you would rather check one than trust it:

```
SHA-256  c52b13491fdeef24ee08e35047878a6753f1dfe83dc0cd6e7bb30ae656e79720  ClassicaCodex-v3.17.0-win-x64.zip
SHA-256  fa517ea3b9b01ac55dc35d362e1fb848c53df21f26e9a9936b23d9e4561bf13b  ClassicaCodex-v3.17.0-win-arm64.zip
```
