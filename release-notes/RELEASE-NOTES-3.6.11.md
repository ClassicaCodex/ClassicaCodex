> **Historical.** These are the notes for Classica Codex 3.6.11, which is no longer
> available for download. Only the current release is published; get it from
> [the releases page](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest).
> Any download link or SHA-256 below refers to the 3.6.11 ZIP, not to the current
> one, so do not check a current download against a checksum printed here.

# Classica Codex 3.6.11

Licensing paperwork, from a second adversarial review. Nothing in the
application behaves differently; what changes is what the download tells you
about the code inside it, and what it owes you.

Nothing here changes your data: **no schema change, no re-ingest, no
re-index**, and nothing to run afterwards. Extract and run.

**[Download the Windows ZIP](https://github.com/ClassicaCodex/ClassicaCodex/releases/latest)** —
extract all of it, run `ClassicaCodex.UI.exe`. Windows will show a blue "Windows
protected your PC" box on first run because the app isn't code-signed; click
**More info**, then **Run anyway**.

## The one licence that asks for something was the one not honoured

3.6.7 put a licence in the download. 3.6.9 corrected it. Neither noticed that
**libgit2 — the only copyleft component here — had no licence text in the ZIP
at all.**

The download reproduced the MIT terms in full and the Apache-2.0 terms in full,
and those are the licences that ask least. GPLv2 section 1 asks that a copy of
the licence travel with the program, and section 3 that recipients be able to
get the source. libgit2 got a URL in a table.

`THIRD-PARTY-NOTICES-LIBGIT2.txt` now ships beside the executable — verbatim
from libgit2's own package: the GPLv2 text, the linking exception that permits
this use, and the notices for the PCRE2, zlib and xdiff code vendored inside
`git2-5853918.dll`, none of which had reached anyone either. The notices file
now also names the exact source (libgit2 v1.8.6, unmodified, as distributed in
LibGit2Sharp.NativeBinaries 2.0.324) and carries a standing three-year written
offer to supply it.

## Two Microsoft binaries that are not .NET

`vcruntime140_cor3.dll` describes itself as the Microsoft C Runtime Library,
part of Microsoft Visual Studio. `D3DCompiler_47_cor3.dll` describes itself as
the Direct3D HLSL compiler, part of the Microsoft Windows Operating System.
Both ship inside the executable, and both were covered only by a table row
reading ".NET runtime and libraries — MIT".

This is not the fonts again, and the notes say so plainly: both come from
Microsoft's own WindowsDesktop runtime pack, whose only licence file is MIT,
with Microsoft as author and licensor, and neither carries terms of its own.
The right to redistribute them is not in question. But a row that implies they
are .NET Foundation code is a claim about someone else's property, and after
the last two releases that is not a claim to leave loose.

## A sentence that said the cleanup was complete

3.6.9 said `PdfSharp.WPFonts.dll` was excluded "along with
`PdfSharp.Snippets.dll`, which was the only thing referencing it." That was
untrue: `PdfSharp.Quality.dll` referenced it too, and was still in the bundle.
3.6.10 removed Quality; this release removes the sentence, which was the one
line telling a reader the removal had been complete when it had not.

## Corrections

- The notices claimed zlib and Brotli ship in `System.IO.Compression.Native.dll`.
  No such file is in the download — this is a single-file build and that code
  is statically linked into the executable.
- "Eleven of the forty-four sections" carried BSD-family reproduction terms.
  Forty-four is right; eleven was not measured, and the count depends on how
  you read the phrasings. It now says "several", which is what is actually
  known.

## Checks

**1,133 tests, zero warnings on a clean build.** The release script now also
refuses to finish if the libgit2 licence file is missing from the payload,
alongside the checks it already made for the other three.

The download is not code-signed, so if you would rather check it than trust it:

```
SHA-256  5DEABC1F5CFF27C18FC85580A50F487F22D80A9781F1B3444FC174691138DDD1
```
