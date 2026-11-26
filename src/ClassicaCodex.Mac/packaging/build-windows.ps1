<#
.SYNOPSIS
    Builds a zip of Classica Codex for Windows. Since 3.17.0 this is the Windows
    download, in place of the classic WinForms app: scripts\publish-release.ps1
    calls it once for each architecture.

.DESCRIPTION
    Run on Windows, from anywhere:

        src\ClassicaCodex.Mac\packaging\build-windows.ps1              # x64
        src\ClassicaCodex.Mac\packaging\build-windows.ps1 -Architecture arm64

    Needs the .NET 10 SDK (or newer). Writes to src\ClassicaCodex.Mac\bin\windows-app,
    which git ignores, unless -OutputRoot names somewhere else.

    -Commit stamps the short commit into the executable's ProductVersion, as
    "3.17.0+abc1234". The release script passes it once it has checked the
    tree is clean, because that stamp is the only thing tying a download back
    to the commit it was built from. Without it the version is left bare,
    rather than carrying a commit that may not describe what was built.

    The app is self-contained, so the .NET runtime travels inside it and
    unzipping it is all the installing there is. It is not code-signed, as the
    classic download was not, so SmartScreen says what it always says.

    The project is still called ClassicaCodex.Mac, and that is not a name to
    put in front of a Windows reader. The executable is renamed once it is
    published. That is safe because all it holds is the name of
    ClassicaCodex.Mac.dll, which stays beside it. Its icon and the names Task
    Manager shows come from the project file.

    The archive is written entry by entry, for the reason given in
    scripts\publish-release.ps1: on Windows PowerShell the library's own
    CreateFromDirectory writes backslashes into the names, which a strict
    extractor reads as part of the file name.
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64',

    [ValidatePattern('^[0-9a-f]{7,40}$')]
    [string] $Commit,

    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$project = Join-Path $repo 'src\ClassicaCodex.Mac\ClassicaCodex.Mac.csproj'
$rid = "win-$Architecture"
$exe = 'Classica Codex.exe'

$version = ([xml](Get-Content $project)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if (-not $version) { throw "No <Version> found in $project" }

$out = if ($OutputRoot) { $OutputRoot } else { Join-Path $repo 'src\ClassicaCodex.Mac\bin\windows-app' }
$name = "ClassicaCodex-v$version-$rid"
$stage = Join-Path $out $name
$app = Join-Path $stage 'Classica Codex'
$zip = "$stage.zip"

Write-Host ("Classica Codex $version for $rid" + $(if ($Commit) { " from $Commit" } else { '' }))
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Path $app -Force | Out-Null

# The SDK would otherwise append the full commit from git whatever the state
# of the tree, which is the stamp-that-may-not-describe-it this avoids.
$stamp = if ($Commit) { "-p:SourceRevisionId=$Commit" } else { '-p:IncludeSourceRevisionInInformationalVersion=false' }
& dotnet publish $project -c Release -r $rid --self-contained true `
    -p:UseAppHost=true -p:DebugType=none $stamp -o $app -v q --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Move-Item (Join-Path $app 'ClassicaCodex.Mac.exe') (Join-Path $app $exe)

# The shared libraries' doc comments, which nobody running the app reads.
Get-ChildItem $app -Filter 'ClassicaCodex.*.xml' | Remove-Item -Force

Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $app 'LICENSE.txt')
Copy-Item (Join-Path $PSScriptRoot 'windows\README.txt') (Join-Path $app 'README.txt')
foreach ($notice in 'THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-NOTICES-DOTNET.txt', 'THIRD-PARTY-NOTICES-LIBGIT2.txt',
                    'src\ClassicaCodex.Mac\THIRD-PARTY-NOTICES-MAC.md') {
    Copy-Item (Join-Path $repo $notice) $app
}

# What must not be in it. An executable called ClassicaCodex.Mac.exe is the
# reason for this script; the fonts are the reason given in publish-release.ps1.
foreach ($unwanted in 'ClassicaCodex.Mac.exe', 'PdfSharp.WPFonts.dll', 'PdfSharp.Snippets.dll', 'PdfSharp.Quality.dll') {
    if (Test-Path (Join-Path $app $unwanted)) { throw "$unwanted is in the download." }
}
if (Get-ChildItem $app -Filter '*.pdb') { throw 'Debug symbols are in the download.' }

# Everything deps.json names has to be there: one missing file stops the app
# before it starts, and only on the reader's machine if nothing looks here.
$deps = Get-Content (Join-Path $app 'ClassicaCodex.Mac.deps.json') -Raw | ConvertFrom-Json
$libraries = $deps.targets.PSObject.Properties[$deps.runtimeTarget.name].Value
foreach ($library in $libraries.PSObject.Properties) {
    foreach ($kind in 'runtime', 'native') {
        $assets = $library.Value.PSObject.Properties[$kind]
        if (-not $assets) { continue }
        foreach ($asset in $assets.Value.PSObject.Properties) {
            $file = Split-Path $asset.Name -Leaf
            if (-not (Test-Path -LiteralPath (Join-Path $app $file))) {
                throw "deps.json names $file, and it is not in the download."
            }
        }
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = (Resolve-Path $stage).Path.TrimEnd([char]92)
$stream = [System.IO.File]::Open($zip, [System.IO.FileMode]::Create)
try {
    $archive = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $root -Recurse -File) {
            $relative = $file.FullName.Substring($root.Length + 1).Replace([char]92, '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }

Write-Host ''
Write-Host "Zip: $zip"
Write-Host ('{0:N0} MB' -f ((Get-Item $zip).Length / 1MB))
Write-Host ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant())
