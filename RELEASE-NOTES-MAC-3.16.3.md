# Classica Codex for Mac 3.16.3 (preview)

Notarised by Apple: it opens like any other app.

Every Mac preview until now was signed ad hoc, so macOS refused to open a
downloaded copy until you found **Open Anyway** in System Settings. This one is
signed with Skirnir Studios' Developer ID and notarised by Apple, so the
disk image and the app open as any downloaded app does. The app inside is
the same as 3.16.2's. It is still a preview: the Mac app has had far less use than the
Windows one, and the Intel build has not yet been opened on an Intel Mac.

The Windows app is at
[Classica Codex 3.16.1](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/v3.16.1),
with the same features.

**[Download the disk image for your Mac](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.16.3)** —
`arm64` for Apple silicon, `x64` for Intel. **Apple menu → About This Mac**
says which you have: a Chip line means Apple silicon, a Processor line means
Intel. Open the disk image and drag Classica Codex into Applications. It needs
macOS 12 or later, and brings its own copy of .NET, so there is nothing else to
install. If you have an earlier Mac preview, let this one replace it. Your
library and settings stay where they are.

The first time, macOS asks whether you want to open an app downloaded from
the internet. Click **Open**, and it won't ask again.

## Coming from 3.16.1 or earlier

3.16.2 fixed two setup steps, and its
[notes](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.16.2)
say what changed. If you skipped it, two things are worth doing once:

- **Run the Menota step again** (Setup → Advanced Setup → Medieval Nordic
  Texts), then rebuild the word index with **Word Index…**. Setup used to lose
  works whose headings repeated, 172 of them in three manuscripts, and a re-run
  brings them back without changing anything already in your library.
- **Run the AI translations step again** if it is still unticked. It now
  clears out files the repository has renamed or withdrawn.

## If something goes wrong

An error in one window puts up a message and leaves the rest of the app
running. The details go to `~/Library/Application Support/ClassicaCodex/errors.log`;
please [report it](https://github.com/ClassicaCodex/ClassicaCodex/issues) with
that file attached.

The Mac app builds with zero warnings, and 1,753 tests pass on macOS: the Mac
app's 126 and the shared libraries' 1,627.

The downloads are signed and notarised, so macOS checks them for you. If you
would also like to check them yourself:

```
SHA-256  9DF77328270F2254EEFCAEE9AB1E805A4DE68BA4C2925B87B838F9849BD2570A  ClassicaCodex-3.16.3-mac-arm64.dmg
SHA-256  FADEC22E02FC571AAF11D1BB77680E4E7C54128CCEB008C3212E16D72A304D30  ClassicaCodex-3.16.3-mac-x64.dmg
```
