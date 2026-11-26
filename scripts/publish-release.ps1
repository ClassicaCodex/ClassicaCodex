<#
.SYNOPSIS
    Builds the Windows downloads for a release, and prints their SHA-256.

.DESCRIPTION
    From 3.17.0 the Windows download is the Avalonia app, the one the Mac and
    Linux run, in place of the classic WinForms app. This builds it for x64
    and for arm64 with src\ClassicaCodex.Mac\packaging\build-windows.ps1,
    which carries the checks on what goes in the zip: no proprietary fonts,
    no debug symbols, and every file deps.json names actually present.

    What this script adds is what makes a build a release, carried over from
    the classic release script (now scripts\publish-classic-release.ps1):

    The commit comes from git and the tree has to be clean. ProductVersion
    reads "3.17.0+abc1234", and that stamp is the only thing tying a download
    back to the commit it was built from. A dirty tree would put a commit in
    it that does not describe the build.

    The build machine's paths stay out of the binaries. -p:DebugType=none is
    what keeps C:\Projects\...\obj\Release\... out of them, and the binaries
    are searched for the repository path afterwards, because shipping the
    build machine's directory layout is what 3.6.7 was released to stop.

    Builds go to a temp folder, outside the repository: a publish tree inside
    it is one 'git add -A' away from being committed, which has happened.

.PARAMETER AllowDirty
    Build from a dirty working tree. For trying the script out only: the
    result carries a commit stamp that does not describe it.

.EXAMPLE
    .\scripts\publish-release.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputRoot = (Join-Path $env:TEMP 'classicacodex-release'),
    [switch] $AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\ClassicaCodex.Mac\ClassicaCodex.Mac.csproj'
$build = Join-Path $repo 'src\ClassicaCodex.Mac\packaging\build-windows.ps1'

# The version is whatever the project says - never a parameter, or the zip
# name and the executable's own version could disagree.
$version = ([xml](Get-Content $project)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if (-not $version) { throw "No <Version> found in $project" }

$commit = (& git -C $repo rev-parse --short HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or -not $commit) { throw 'Could not read the commit from git.' }

$dirty = (& git -C $repo status --porcelain)
if ($dirty) {
    $dirty | ForEach-Object { Write-Host "  $_" }
    if (-not $AllowDirty) {
        throw "The working tree is not clean, so $commit would not describe what is built. Commit first, or pass -AllowDirty for a trial run."
    }
    Write-Warning "-AllowDirty: building anyway. $commit does NOT describe this build - do not publish it."
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$results = @()

foreach ($architecture in 'x64', 'arm64') {
    & $build -Architecture $architecture -Commit $commit -OutputRoot $OutputRoot
    if (-not $?) { throw "The $architecture build failed." }

    $name = "ClassicaCodex-v$version-win-$architecture"
    $zip = Join-Path $OutputRoot "$name.zip"
    $app = Join-Path (Join-Path $OutputRoot $name) 'Classica Codex'
    if (-not (Test-Path $zip)) { throw "$zip was not written." }

    # The stamp, read back from the executable rather than assumed.
    $exe = Get-Item (Join-Path $app 'Classica Codex.exe')
    $productVersion = $exe.VersionInfo.ProductVersion
    if ($productVersion -ne "$version+$commit") {
        throw "The $architecture executable says ProductVersion $productVersion, not $version+$commit."
    }

    # findstr rather than Select-String, which reads binaries as text. Its exit
    # code is 1 for "no match", the outcome wanted, so it is read and cleared.
    $ours = @(Get-ChildItem $app -File | Where-Object { $_.Name -like 'ClassicaCodex.*.dll' -or $_.Name -eq 'Classica Codex.exe' })
    foreach ($binary in $ours) {
        & cmd /c "findstr /m /c:`"$repo`" `"$($binary.FullName)`" >nul 2>&1"
        $leaked = ($LASTEXITCODE -eq 0)
        $global:LASTEXITCODE = 0
        if ($leaked) { throw "$($binary.Name) contains the build path $repo - -p:DebugType=none did not take effect." }
    }

    $results += [pscustomobject]@{
        Zip = $zip
        Bytes = (Get-Item $zip).Length
        Hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

Write-Host ''
Write-Host "Classica Codex $version from $commit"
foreach ($result in $results) {
    Write-Host "  $($result.Zip)"
    Write-Host "  $('{0:N0}' -f $result.Bytes) bytes, SHA-256 $($result.Hash)"
}

Write-Host ''
Write-Host 'Paste into the release notes:'
Write-Host ''
Write-Host 'The downloads are not code-signed, so if you would rather check one than trust it:'
Write-Host ''
Write-Host '```'
foreach ($result in $results) { Write-Host "SHA-256  $($result.Hash)  $(Split-Path $result.Zip -Leaf)" }
Write-Host '```'
