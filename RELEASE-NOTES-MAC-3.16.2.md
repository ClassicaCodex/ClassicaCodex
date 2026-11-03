# Classica Codex for Mac 3.16.2 (preview)

Setup puts right what the 3.16.1 notes left open.

Two fixes to setup, both found while bringing in the AI translations. The AI
translations step now keeps its folder as the repository has it, and installs
in every library. The Menota step no longer loses works whose headings repeat:
172 of them, in three manuscripts. Nothing else changes. It is still a preview:
the Mac app has had far less use than the Windows one, and the Intel build has
not yet been opened on an Intel Mac.

**[Download the disk image for your Mac](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.16.2)** —
`arm64` for Apple silicon, `x64` for Intel. **Apple menu → About This Mac**
says which you have: a Chip line means Apple silicon, a Processor line means
Intel. Open the disk image and drag Classica Codex into Applications. It needs
macOS 12 or later, and brings its own copy of .NET, so there is nothing else to
install. If you have an earlier Mac preview, let it replace that one. Your
library and settings stay where they are.

The app isn't notarised by Apple — that needs a paid developer account — so
macOS refuses to open a downloaded copy the first time, and may do so again
for this new one. Click **Done**, open **System Settings → Privacy &
Security**, scroll down to the message about Classica Codex, and click
**Open Anyway**.

## The AI translations

- **A re-run leaves the folder as the repository has it.** Running the step
  again used to add the repository's files and remove none, so a file the
  repository had renamed or withdrawn stayed on your disk. Now it is removed,
  and a translation the repository has withdrawn is removed from your library
  too. Only translations this step installed are touched, never one you made
  with Create Translation. If you ran the step on 2 November or earlier and it
  is still unticked, run it again with this release; there is no folder to
  delete by hand.
- **Seneca's letters install in every library.** Perseus changed how Seneca's
  *Moral Letters* is cited on 16 October, so a library holds its lines in one
  form or the other depending on when its Latin texts were installed. The step
  now accepts either.

## The Menota manuscripts

- **Works that share a heading each keep their text.** Setup divides a Menota
  manuscript into works and names each from its heading. Where two works
  opened with the same heading, as seven homilies in AM 677 4to open with
  "Gregorius", they were given the same identifier, and each one imported
  replaced the one before without a word. The default plans lost 172 works
  this way, about 36,000 words: 10 in AM 677 4to, 158 in Holm A 80 and 4 in
  Wolf Aug 9 10 4to. Each now has an identifier of its own. The last keeps the
  old one and the earlier ones are numbered, so everything already in your
  library keeps its text, and bookmarks, tags, research notes and AI
  translations on it keep working.
- **To get them back,** open **Setup**, choose **Advanced Setup**, and run the
  **Medieval Nordic Texts (Menota)** step again; it took under a minute here.
  The plans you confirmed still apply, and the setup log names each
  manuscript whose works were numbered.
  Then click **Word Index…** and rebuild it, or the recovered works won't turn
  up in Search.
- **The review window numbers repeated headings as it saves,** so a plan you
  confirm names the works the library will hold.

## Still to come

- **Gregory's Homily 34.** Its twenty-five translated lines were held back,
  because no library held the homily. Re-running the Menota step with this
  release installs it, so the lines can now be filed in the AI translations
  repository. Once they are, running that step again brings them in.
- **Windows.** These fixes are in the shared code but not yet in a Windows
  release. Windows 3.16.0 still behaves as the
  [Mac 3.16.1 notes](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.16.1)
  describe.

## If something goes wrong

An error in one window puts up a message and leaves the rest of the app
running. The details go to `~/Library/Application Support/ClassicaCodex/errors.log`;
please [report it](https://github.com/ClassicaCodex/ClassicaCodex/issues) with
that file attached.

The Mac app builds with zero warnings, and 1,753 tests pass on macOS: the Mac
app's 126 and the shared libraries' 1,627.

The downloads are signed ad hoc but not notarised, so if you would rather check
them than trust them:

```
SHA-256  (to come)  ClassicaCodex-3.16.2-mac-arm64.dmg
SHA-256  (to come)  ClassicaCodex-3.16.2-mac-x64.dmg
```
