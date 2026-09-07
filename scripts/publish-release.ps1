<#
.SYNOPSIS
    Builds the Windows download for a release, and prints its SHA-256.

.DESCRIPTION
    Every release up to 3.6.6 was published by typing this command out by
    hand, which is how two things went wrong that this script exists to stop.

    The commit stamp was typed. ProductVersion reads "3.6.6+d7d2654" because
    someone copied that short hash onto the command line; a stale or mistyped
    one would have been indistinguishable from a correct one, and the version
    string is the only thing tying a download back to a commit. Here it comes
    from git.

    The debug paths were not thought about. -p:DebugType=none is what keeps
    C:\Projects\ClassicaCodex\src\...\obj\Release\... out of the executable -
    those are CodeView debug-directory entries, one per assembly, and deleting
    the .pdb files beside the exe does not remove them because they live
    inside it. Users get no line numbers either way, since the .pdb files were
    never in the ZIP.

    ZipFile.CreateFromDirectory, not Compress-Archive: PowerShell's own cmdlet
    writes backslash separators, which a strict extractor reads as part of the
    filename. That is what left 3.4.0 with 184 loose files where the Icons
    folder should have been, so the archive is checked for them afterwards.

.PARAMETER OutputRoot
    Where to build. Defaults to a temp folder, deliberately outside the
    repository: a 170MB publish tree inside it is one 'git add -A' away from
    being committed, which has happened.

.EXAMPLE
    .\scripts\publish-release.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputRoot = (Join-Path $env:TEMP 'classicacodex-release')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$uiProject = Join-Path $repo 'src\ClassicaCodex.UI\ClassicaCodex.UI.csproj'

# The version is whatever the project says it is - never a parameter, or the
# ZIP name and the executable's own version could disagree.
$version = ([xml](Get-Content $uiProject)).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $uiProject" }

$commit = (& git -C $repo rev-parse --short HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or -not $commit) { throw 'Could not read the commit from git.' }

$dirty = (& git -C $repo status --porcelain)
if ($dirty) {
    Write-Warning "The working tree is not clean, so $commit does not describe what is being built:"
    $dirty | ForEach-Object { Write-Warning "  $_" }
}

$payload = Join-Path $OutputRoot "publish-$version"
$zip = Join-Path $OutputRoot "ClassicaCodex-v$version-win-x64.zip"

Write-Host "Classica Codex $version from $commit"
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

& dotnet publish $uiProject -c Release -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:SourceRevisionId=$commit `
    -o $payload -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# The XML documentation files are a build product of GenerateDocumentationFile,
# not something a reader needs - a megabyte of doc comments for an application
# nobody references as a library. Every release so far has dropped them by
# hand, one `rm` that had to be remembered each time.
Get-ChildItem $payload -Filter 'ClassicaCodex.*.xml' | Remove-Item -Force
Get-ChildItem $payload -Filter '*.pdb' | Remove-Item -Force

# The licence and notices come from the project file, so their absence means
# that broke rather than that someone forgot a copy step.
foreach ($required in 'ClassicaCodex.UI.exe', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md') {
    if (-not (Test-Path (Join-Path $payload $required))) { throw "$required is missing from the publish output." }
}
if (-not (Test-Path (Join-Path $payload 'Icons'))) { throw 'The Icons folder is missing from the publish output.' }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Entry names are built here, with a forward slash, rather than left to
# ZipFile.CreateFromDirectory. Windows PowerShell runs on .NET Framework,
# whose CreateFromDirectory writes the platform separator into the archive -
# so it produces exactly the backslash names Compress-Archive does, and the
# check below caught it doing so on all 239 icons. The ZIP specification says
# forward slash. .NET Core fixed this; the framework this script runs on did
# not, so the names are not its decision to make.
$payloadRoot = (Resolve-Path $payload).Path.TrimEnd([char]92)
$stream = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
try {
    $archive = New-Object System.IO.Compression.ZipArchive(
        $stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $payloadRoot -Recurse -File) {
            $relative = $file.FullName.Substring($payloadRoot.Length + 1).Replace([char]92, '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $relative,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }

$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $backslashes = @($archive.Entries | Where-Object { $_.FullName.Contains([char]92) }).Count
    $icons = @($archive.Entries | Where-Object { $_.FullName.StartsWith('Icons/') }).Count
    $entries = $archive.Entries.Count
} finally { $archive.Dispose() }
if ($backslashes -gt 0) { throw "$backslashes entries use a backslash separator; extractors will not make folders from them." }

$exe = Get-Item (Join-Path $payload 'ClassicaCodex.UI.exe')

# findstr rather than Select-String: this is a 180MB binary, and Select-String
# reads it as text. Its exit code is 1 for "no match", which is the outcome we
# want, so it is read deliberately and then cleared - left alone it makes a
# successful run look like a failed one to whatever called this script.
& cmd /c "findstr /m /c:`"$repo`" `"$($exe.FullName)`" >nul 2>&1"
$pathLeaked = ($LASTEXITCODE -eq 0)
$global:LASTEXITCODE = 0
if ($pathLeaked) {
    Write-Warning "The executable still contains the build path $repo - check -p:DebugType=none survived."
}

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash

Write-Host ''
Write-Host "  $zip"
Write-Host "  $('{0:N0}' -f (Get-Item $zip).Length) bytes, $entries entries, $icons under Icons/"
Write-Host "  ProductVersion  $($exe.VersionInfo.ProductVersion)"
Write-Host "  SHA-256         $hash"
Write-Host ''
Write-Host 'Paste into the release notes:'
Write-Host ''
Write-Host 'The download is not code-signed, so if you would rather check it than trust it:'
Write-Host ''
Write-Host '```'
Write-Host "SHA-256  $hash"
Write-Host '```'
