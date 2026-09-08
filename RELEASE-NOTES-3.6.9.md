# Classica Codex 3.6.9

The download stops carrying six Microsoft typefaces it had no right to
redistribute, and the notices file stops describing them as MIT. Nothing in
the application behaves differently — this is entirely about what is in the
ZIP and what the paperwork says about it.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## Six fonts that were never ours to give away

PDFsharp ships an assembly called `PdfSharp.WPFonts.dll`, and it embeds six
Microsoft Segoe WP typefaces. Their own metadata says what they are:

```
Microsoft Corporation. All Rights Reserved.
You may use this font as permitted by the EULA for the product
in which this font is included
```

The product being the Windows Phone SDK, not this one. empira licenses
PDFsharp's own code under MIT and cannot relicense Microsoft's typefaces on
Microsoft's behalf — so every release of Classica Codex up to and including
3.6.8 redistributed proprietary fonts, while 3.6.7's new notices file described
the whole of PDFsharp, fonts included, as MIT. That is the exact failure 3.6.7
existed to prevent, committed in the act of preventing it.

Nothing here ever used them. This application draws PDFs through `PdfSharp.dll`
with its own font resolver, using fonts already installed on the machine. The
assembly is now excluded from the build, along with `PdfSharp.Snippets.dll`,
which held the only reference to it. **No font of any kind is redistributed
with this application.**

The executable is 578 KB smaller, and the release script now refuses to produce
a build containing that font data.

## The notices file was incomplete and in places wrong

Four things ship that it did not name: `DocumentFormat.OpenXml.Framework`,
`Microsoft.Extensions.Logging.Abstractions`,
`Microsoft.Extensions.DependencyInjection.Abstractions` — that last one
arriving two dependencies below anything this project asks for — and the .NET
runtime's own third-party components. A self-contained build bundles the
runtime, and the runtime bundles zlib and Brotli in turn; eleven of the
forty-four notices in `THIRD-PARTY-NOTICES-DOTNET.txt` are BSD-family terms
asking to be reproduced in binary redistributions like this one. That file now
travels with the download, verbatim from the runtime pack.

Corrections to what was there:

| | said | is |
|---|---|---|
| libgit2 | 2.0.324 | **1.8.6** — 2.0.324 is the wrapper package's version, not the library's |
| SQLite | 3.x | **3.41.2** |
| .NET runtime | 8.0 | **8.0.30** |
| the licence file beside it | `LICENSE` | **`LICENSE.txt`** — what the ZIP actually contains |

What was checked and is right: the embedded Apache-2.0 is byte-identical to the
canonical text, all nine sections intact. The MIT text is verbatim. LibGit2Sharp
is MIT, confirmed from the licence file inside its own package. libgit2 is GPLv2
with a linking exception whose wording does permit this use. SQLite is public
domain. And the .NET runtime really is MIT here — the EULA at
`C:\Program Files\dotnet\LICENSE.txt` governs an installed SDK, not the runtime
packs a self-contained build copies from, which declare MIT and ship their own
MIT licence file.

## Two checks that could not work

Both were written to prevent exactly what shipped, and neither could have.

**A check that could not fail.** The first attempt at excluding the fonts tested
for `PdfSharp.WPFonts.dll` in the publish folder. This is a single-file build:
every dependency is bundled inside the executable and nothing else is on disk
to test for, so the check passed on a build that still contained the fonts.

**A check that could not pass.** Correcting it to search the executable then
failed a build that had correctly dropped them — the assembly's *name* survives
in the embedded `deps.json` manifest even when the assembly does not. The test
is now for the fonts' own name-table text, decoded three ways, because
UTF-16 needs two-byte alignment and checking only one alignment found one of
three markers in a build that contained all three.

In between, the exclusion itself silently did nothing twice: once from an
MSBuild `Remove` with a metadata condition, which does not batch the way it
reads, and once from hooking `BeforeTargets="GenerateSingleFileBundle"` when
the target that consumes the file list runs as its *dependency*, and had
therefore already taken its copy. Both times the build reported success.

## Checks

**1,129 tests, zero warnings on a clean build** — unchanged from 3.6.8; none of
this is behaviour a unit test can hold. What holds it instead is the release
script, which now refuses to finish if the font data is present, if the licence
or notice files are missing, if the archive has no icons, if entry names carry
backslashes, if the build path survived into the executable, or if the working
tree was dirty when the commit stamp was read.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  6E3025FC75989D68235A57181FB0812E12D687DC1C51F66221A5A5588554E495
```
