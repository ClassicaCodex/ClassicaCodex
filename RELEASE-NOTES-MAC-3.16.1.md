# Classica Codex for Mac 3.16.1 (preview)

English for passages that had none.

This release brings the Mac level with
[Windows 3.16.0](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/v3.16.0).
Setup has one new collection, the Classica Codex AI translations, which came
with Windows 3.16.0, after the last Mac preview had gone out. Everything else
is as it was in Mac 3.16.0. It is still a preview: the Mac app has had far less
use than the Windows one, and the Intel build has not yet been opened on an
Intel Mac.

**[Download the disk image for your Mac](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.16.1)** —
`arm64` for Apple silicon, `x64` for Intel. **Apple menu → About This Mac**
says which you have: a Chip line means Apple silicon, a Processor line means
Intel. Open the disk image and drag Classica Codex into Applications. It needs
macOS 12 or later, and brings its own copy of .NET, so there is nothing else to
install. If you have 3.16.0 or 3.15.1, let it replace that one. Your library
and settings stay where they are.

The app isn't notarised by Apple — that needs a paid developer account — so
macOS refuses to open a downloaded copy the first time, and may do so again
for this new one. Click **Done**, open **System Settings → Privacy &
Security**, scroll down to the message about Classica Codex, and click
**Open Anyway**.

## The AI translations

English for passages that have no published English: about 15,900 lines from
446 works in twelve languages. They are the passages that the
[Atlas of Ancient Myth](https://skirnirstudios.com/atlas-of-ancient-myth/),
[Magic in the Ancient Texts](https://skirnirstudios.com/magic-in-the-ancient-texts/)
and a study of demons and angels cite, in Greek and Latin, Old Norse, Middle
High German, Egyptian, Old French, Dante's Italian, Hebrew and Aramaic.

- **They are AI translations, and labelled so.** Claude made each one from the
  original, line for line, without a published translation to lean on. A
  second AI, Codex, checked every passage against the original, and the
  objections that held up were corrected. **No specialist has reviewed any of
  it.** The edition menu calls each one "Claude (AI-generated)", its Work
  Details say so, and an exported passage carries the label with it.
- **Each is filed beside the text it translates**, under the same references,
  as another translation of that work. Only the cited passages were translated,
  so most cover part of a work, and the edition menu marks them INCOMPLETE
  with how many lines they have. That is by design, not a download that stopped.
- **The texts come first.** A translation arrives only if the text it
  translates is already in your library. One whose text is not there is
  skipped and noted, and the step stays unticked. Install that collection and
  run the step again.
- **Corrections reach you.** The translations live in a public repository,
  [ClassicaCodex/ai-translations](https://github.com/ClassicaCodex/ai-translations),
  with the translators' briefs and the review of every passage. Running the
  step again brings in corrections and new passages, and changes only the
  lines that changed.

About 8 MB. Each translation carries its source's licence, CC BY-SA 4.0 or
CC BY 4.0, and the credit that licence asks for. Help has a topic on them,
**Classica Codex AI translations**, which also says how they differ from the
translations Create Translation asks Gemini for.

## Adding them to a library you already have

Open **Setup**, choose **Advanced Setup**, and run the AI translations step.
Run it after the collections it translates, since it installs only beside
texts already in the library. On a full library it takes about ten seconds.
The word index is kept up to date as they go in, so there is nothing to
rebuild.

## Two that didn't install, since corrected

When this release came out, two of the 446 did not install into a library set
up that day. They were two lines of Seneca's *Moral Letters* and thirty lines
of AM 677 4to, the Old Icelandic manuscript of Gregory's Homilies and
Dialogues, and with them missing the step stayed unticked. Both files have
since been corrected in the repository. Running the step now installs every
translation whose text is in your library, and the step ticks. The same is
true on Windows 3.16.0.

Three things are still to be put right in the app, in a coming release:

- **If you ran the step on 2 November or earlier,** a re-run can still leave
  it unticked, with `menota_anonymous_untitled-section-1.json` named in the
  setup log. That is the old file: a re-run adds files but never removes
  them. Delete the `ai-translations` folder in your download folder and run
  the step again.
- **If your Latin texts were installed before 16 October,** in a library
  brought over from Windows, say, Seneca's two lines don't install. Perseus
  changed how that text is cited on that date, and the corrected file follows
  the new form.
- **Twenty-five lines of Gregory's Homily 34 are held back.** In AM 677 4to,
  setup keeps only one of seven homilies that share a heading, and Homily 34 is
  one it loses, so there is nothing yet to file them beside.

## If something goes wrong

An error in one window puts up a message and leaves the rest of the app
running. The details go to `~/Library/Application Support/ClassicaCodex/errors.log`;
please [report it](https://github.com/ClassicaCodex/ClassicaCodex/issues) with
that file attached.

The Mac app builds with zero warnings, and 1,728 tests pass on macOS: the Mac
app's 125 and the shared libraries' 1,603.

The downloads are signed ad hoc but not notarised, so if you would rather check
them than trust them:

```
SHA-256  BCAA4BCBF3EAF7AE37C8C32796B5DEAA85F9358B243C6DBB8A189B6E3168A64B  ClassicaCodex-3.16.1-mac-arm64.dmg
SHA-256  2BD3E4BC1C5A5A200B541C7B9F66A6A5EBC9EAAFA12D883EB704E8919030DA2D  ClassicaCodex-3.16.1-mac-x64.dmg
```
