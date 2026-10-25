> **Historical.** These are the notes for Classica Codex for Mac 3.15.1, the first
> Mac preview. It has been followed by
> [Classica Codex for Mac 3.16.0](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.16.0).
> Any download link or SHA-256 below refers to the 3.15.1 disk images, not to the
> current ones, so do not check a current download against a checksum printed here.

# Classica Codex for Mac 3.15.1 (preview)

The first Mac release.

It is the same Classica Codex as on Windows — the same library file, the same
setup, every tool — drawn for the Mac rather than carried over to it: its own
windows, a sidebar in place of the toolbar, its own artwork, and light and dark
that follow the system. It is a preview because it is the first: it has had far
less use than the Windows app, and the Intel build has not yet been opened on
an Intel Mac.

**[Download the disk image for your Mac](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/mac-v3.15.1)** —
`arm64` for Apple silicon, `x64` for Intel. **Apple menu → About This Mac**
says which you have: a Chip line means Apple silicon, a Processor line means
Intel. Open the disk image and drag Classica Codex into Applications. It needs
macOS 12 or later, and brings its own copy of .NET, so there is nothing else to
install.

The app isn't notarised by Apple — that needs a paid developer account — so
macOS refuses to open it the first time. Click **Done**, open **System Settings
→ Privacy & Security**, scroll down to the message about Classica Codex, and
click **Open Anyway**. It won't ask again.

## Your library

- **Already have a library on Windows?** It is one `.db` file, and the Mac
  opens it as it is — no setup, no re-download, and your tags, bookmarks and
  research come with it. Copy it across (AirDrop, a USB drive, a shared folder)
  and choose **Open Library…** (⌘O).
- **Starting fresh?** **Create a New Library…** (⇧⌘N) runs the same setup as
  on Windows, with the same collections: two to three hours with everything
  selected, most of it unattended.
- The library and settings are kept in
  `~/Library/Application Support/ClassicaCodex`.

## What it has

Every tool the Windows app has: the reader with its two linked pages, prefaces,
apparatus and history; Search, the Concordance, bookmarks and tags; comparing
translations and collating editions; the translation workbench and its AI
translation; Word Study, Morphology, Core Vocabulary and the alphabet; echoes
and reception; the myth network, the Places Map and the Timeline; stylometry;
Medieval Hands and the manuscript pages; the three instruments; the whole
research area; export to text, Word, PDF, BibTeX and RIS — and the arcade, for
anyone who finds it.

## What is different on a Mac

- **A sidebar instead of a toolbar.** **Library** (⌘1) and **Tools** (⌘2), a
  gallery of every tool (⇧⌘T), and the menu bar. The toolbar keeps only the
  reader's everyday controls.
- **Mac keys:** ⌘ in place of Ctrl, ⌘, for Settings, ⌘W to close a window, ⌘[
  and ⌘] for Back and Forward.
- **API keys** for AI translation are kept in the macOS Keychain.
- **Read Aloud** uses the Mac's own voices.
- **Light and dark** follow the system unless **View → Appearance** says
  otherwise.
- **PDF export offers only the fonts it can embed.** Several of the Mac's own —
  Times, Helvetica, Menlo, Optima and others — are stored in a form the PDF
  library can't read, so they are left off the list. The reading fonts the app
  itself uses, Palatino, Georgia and Times New Roman, are all there.

## Ahead of Windows 3.15.1

This release is built from newer shared code than
[Windows 3.15.1](https://github.com/ClassicaCodex/ClassicaCodex/releases/tag/v3.15.1),
so setup already offers the optional collections added since, which the
Windows release doesn't have yet:

- **The Hebrew Bible** — the Westminster Leningrad Codex, every word parsed and
  keyed to Strong's dictionary, with the 1917 JPS translation beside it
- **The Quran**, in Arabic and in Pickthall's English
- **Aquinas** — the First Part of the Summa Theologiae, in English
- **Pseudo-Dionysius** — the Heavenly Hierarchy and the other works
- **Pseudepigrapha** — 1 Enoch, the Testament of Solomon, Jubilees and others
- **Rabbinic texts** on angels and demons — five tractates of the Talmud,
  Pirkei de-Rabbi Eliezer and the midrashim
- **Early English books** on devils, spirits and angels — twenty-one of them,
  from *Daemonologie* to *Paradise Lost*
- **The Goetia** of Mathers and Crowley, from Project Gutenberg
- **The Life of Antony, the Latin Life of Adam and Eve and the Arabian Nights**

## If something goes wrong

An error in one window puts up a message and leaves the rest of the app
running. The details go to `~/Library/Application Support/ClassicaCodex/errors.log`;
please [report it](https://github.com/ClassicaCodex/ClassicaCodex/issues) with
that file attached.

The Mac app builds with zero warnings, and 1,616 tests pass on macOS: the Mac
app's 122 and the shared libraries' 1,494.

The downloads are signed ad hoc but not notarised, so if you would rather check
them than trust them:

```
SHA-256  702B298696F105EB6C8D3480CFAA01AA41F20A57067E6F63EEB13A79F147D8F4  ClassicaCodex-3.15.1-mac-arm64.dmg
SHA-256  68CB87767ACD8B905D7498917322CE059091F75C4A1685BA758EEA4706710D67  ClassicaCodex-3.15.1-mac-x64.dmg
```
