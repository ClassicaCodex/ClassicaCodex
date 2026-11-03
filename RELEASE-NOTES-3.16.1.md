# Classica Codex 3.16.1

Setup puts right two things the AI translations turned up.

Both fixes are to setup, and both were found while bringing in the AI
translations; the Mac preview had them in 3.16.2. The AI translations step now
keeps its folder as the repository has it, and installs in every library. The
Menota step no longer loses works whose headings repeat: 172 of them, in three
manuscripts. Nothing else changes. Your library and settings stay where they
are.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
More info, then Run anyway.

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
  **Medieval Nordic Texts (Menota)** step again. The plans you confirmed still
  apply. Then click **Word Index...** and rebuild it, or the recovered works
  won't turn up in Search.
- **The review window numbers repeated headings as it saves,** so a plan you
  confirm names the works the library will hold.

## Still to come

- **Gregory's Homily 34.** Its twenty-five translated lines were held back,
  because no library held the homily. Re-running the Menota step with this
  release installs it, so the lines can now be filed in the AI translations
  repository. Once they are, running that step again brings them in.

2,015 tests, zero warnings.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  (added once the release is published)
```
