# Classica Codex 3.6.7

Three things about the download rather than the application, and one colour.
If you are on 3.6.6 nothing in the program behaves differently except one
status message, so upgrade at your leisure — but this is the build worth
linking to, because it is the first one that carries its own licence.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The download had no licence in it

Every ZIP up to 3.6.6 held the executable and the Icons folder and nothing
else. Classica Codex is MIT, and MIT asks that its notice travel with every
copy of the software — a binary ZIP is such a copy. The repository has always
had a `LICENSE`; nobody who downloaded the program ever saw it.

The bundle also carries other people's work with conditions of its own, none
of which were acknowledged anywhere: **SQLitePCLRaw** is Apache-2.0, whose
section 4(a) asks that recipients be given a copy of that licence, and
**libgit2** — which ships inside the executable as `git2-5853918.dll` — is
GPLv2 with a linking exception. Also in there: the .NET 8 runtime,
Microsoft.Data.Sqlite, DocumentFormat.OpenXml, PDFsharp, LibGit2Sharp,
System.Speech and SQLite itself.

The ZIP now contains `LICENSE.txt` and `THIRD-PARTY-NOTICES.md`, which names
every component, its version, its copyright holder and its terms, and carries
the full text of the Apache licence. Both are declared in the project file
rather than copied in at packaging time, so a future release cannot be cut
without them.

## The executable named the folder it was built in

Four strings, one per assembly:

```
C:\Projects\ClassicaCodex\src\ClassicaCodex.Core\obj\Release\net8.0\…
```

CodeView debug-directory entries. Deleting the `.pdb` files next to the
executable did not remove them, because they live inside the executable. No
username and nothing personal, but it is somebody's directory layout in a file
handed to strangers, and it was there in every release to date. Built now with
debug information off, which removes the entries; you lose nothing, since the
`.pdb` files were never in the download to give line numbers with.

## One error message stayed unreadable

3.6.5 said error text in six windows had stopped being drawn in a red that is
illegible on the dark surface. Seven of the eight places were converted; the
partial-failure message in Create Translation — *"Finished this pass, but N
line(s) never came back"* — was missed, and went on drawing at **1.66:1**.
Its quiet counterpart, the message that says the pass finished cleanly, was at
3.03:1. They are 6.95:1 and 5.48:1 now.

The reason it survived a fix that caught the other seven: the theme remaps a
label that was *built* in that red, and this one is assigned afterwards, at the
moment the message is written. Nothing about the first fix could have reached
it.

## Releases are built by a script now

`scripts/publish-release.ps1`. Until now the publish command was typed out each
time, and two of the three problems above are what that costs.

It takes the version from the project file and the commit from `git` — the
commit stamp in `ProductVersion` used to be copied onto the command line by
hand, where a stale one would have looked exactly like a correct one. It
refuses to finish if the licence files are missing from the output, and it
checks the finished archive for the fault that broke 3.4.0, where entry names
were written with backslashes and 184 files spilled out of the Icons folder
they should have been in.

That check earned its place immediately: Windows PowerShell's own
`ZipFile.CreateFromDirectory` writes backslash entry names, so the first run of
the script produced exactly the 3.4.0 archive and refused to hand it over. The
script builds entry names itself now.

## Checks

**1,119 tests, zero warnings on a clean build** — unchanged from 3.6.6; none
of this is behaviour a test can hold, except the colour, which is a literal
replaced by the theme value the other seven already used.
