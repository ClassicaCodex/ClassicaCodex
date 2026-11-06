<#
.SYNOPSIS
    Starts a built zip the way a reader would, photographs it, and checks what
    it does to the classic Windows app's files.

.DESCRIPTION
    The Windows workflow runs it. It sets up the preferences folder as the
    classic app leaves it, with a library chosen, a reading position and an
    encrypted key, so it needs a machine with no Classica Codex settings of
    its own. On a reader's machine it would write over theirs.

        smoke-test.ps1 <zip> <library.db> <photo folder> [<classic zip>] [<crash hook>]

    With the classic zip it also runs the released classic app before, during
    and after, and fails if this app changes any of that app's files, or if
    either app stops or writes to errors.log.

    The crash hook is tools\BackgroundCrash, built. With it the app is crashed
    on a background thread, and has to stay open until its dialog is answered
    and then close.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Zip,
    [Parameter(Mandatory)] [string] $Library,
    [Parameter(Mandatory)] [string] $Photos,
    [string] $ClassicZip,
    [string] $CrashHook
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Security
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class TopWindows
{
    private delegate bool Visit(IntPtr window, IntPtr data);

    [DllImport("user32.dll")] private static extern bool EnumWindows(Visit visit, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);

    private static List<KeyValuePair<IntPtr, string>> Visible(int processId)
    {
        var windows = new List<KeyValuePair<IntPtr, string>>();
        EnumWindows((window, _) =>
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId && IsWindowVisible(window))
            {
                var text = new StringBuilder(256);
                GetWindowText(window, text, text.Capacity);
                windows.Add(new KeyValuePair<IntPtr, string>(window, text.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    public static string[] Titles(int processId)
    {
        var titles = new List<string>();
        foreach (var window in Visible(processId))
        {
            if (window.Value.Length > 0) titles.Add(window.Value);
        }
        return titles.ToArray();
    }

    // An empty title finds an alert, which is the one kind of window without one.
    public static IntPtr Find(int processId, string title)
    {
        foreach (var window in Visible(processId))
        {
            if (window.Value == title) return window.Key;
        }
        return IntPtr.Zero;
    }
}
'@

$Library = (Resolve-Path $Library).Path
New-Item -ItemType Directory -Path $Photos -Force | Out-Null
$Photos = (Resolve-Path $Photos).Path
$work = Join-Path $env:RUNNER_TEMP 'smoke'
New-Item -ItemType Directory -Path $work -Force | Out-Null

$preferences = Join-Path $env:LOCALAPPDATA 'ClassicaCodex'
$errors = Join-Path $preferences 'errors.log'
New-Item -ItemType Directory -Path $preferences -Force | Out-Null

function Photo([string] $name) {
    $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
    $bitmap.Save((Join-Path $Photos "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

function Alive($process, [string] $when) {
    if ($process.HasExited) {
        Get-Content $errors -ErrorAction SilentlyContinue | Write-Host
        throw "The app stopped $when, with exit code $($process.ExitCode)."
    }
}

function Start-App([string] $path, [int] $wait) {
    $process = Start-Process -FilePath $path -PassThru
    Start-Sleep -Seconds $wait
    Alive $process 'starting up'
    $process.Refresh()
    return $process
}

function Close-App($process) {
    $process.Refresh()
    [void] $process.CloseMainWindow()
    if (-not $process.WaitForExit(20000)) { throw 'The app did not close when asked.' }
}

function Hashes {
    $hashes = @{}
    foreach ($file in Get-ChildItem $preferences -Recurse -File) {
        $hashes[$file.FullName.Substring($preferences.Length + 1)] = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
    }
    return $hashes
}

# Keys, which have to land on this window, so it is put in front first.
function Send-To([IntPtr] $window, [string] $keys) {
    [void] [TopWindows]::SetForegroundWindow($window)
    Start-Sleep -Seconds 1
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Seconds 5
}

function Find-Window($process, [string] $title) {
    $window = [TopWindows]::Find($process.Id, $title)
    if ($window -eq [IntPtr]::Zero) {
        throw "No window called '$title', and the windows are: $([TopWindows]::Titles($process.Id) -join ', ')"
    }
    return $window
}

function Remember-Library([string] $path) {
    [System.IO.File]::WriteAllText((Join-Path $preferences 'database-path.txt'), $path)
}

function Main-Title($process) {
    $process.Refresh()
    return $process.MainWindowTitle
}

# A press on the main window, and the window it should open.
function Press($process, [string] $keys, [string] $photo, [string] $expected) {
    $process.Refresh()
    Send-To $process.MainWindowHandle $keys
    Alive $process "after $keys"
    Photo $photo
    $titles = [TopWindows]::Titles($process.Id)
    if ($expected -and $titles -notcontains $expected) {
        throw "$keys should have opened $expected, and the windows are: $($titles -join ', ')"
    }
}

# Escape on a window, which should close it.
function Dismiss($process, [string] $title, [string] $photo) {
    Send-To (Find-Window $process $title) '{ESC}'
    Alive $process "after Escape on $title"
    Photo $photo
    if ([TopWindows]::Titles($process.Id) -contains $title) { throw "Escape should have closed $title." }
}

# As the classic app leaves things: a library chosen, a passage open, an
# encrypted key. Opens on Greek, so the first photograph shows polytonic text.
Remember-Library $Library

$query = Join-Path $work 'passage.py'
Set-Content $query @'
import sqlite3, sys
work, line = sqlite3.connect(sys.argv[1]).execute("""
    SELECT w.CtsUrn, t.CitationRef
    FROM TextNodes t
    JOIN Editions e ON e.EditionId = t.EditionId
    JOIN Works w ON w.WorkId = e.WorkId
    WHERE e.Language = 'grc' AND length(t.Text) > 30
    ORDER BY e.EditionId, t.SortOrder
    LIMIT 1""").fetchone()
print(work)
print(line)
'@
$passage = & python $query $Library
if ($LASTEXITCODE -ne 0) { throw 'Could not read a passage from the library.' }
[System.IO.File]::WriteAllLines((Join-Path $preferences 'reading-position.txt'), [string[]] $passage)

$secret = [System.Security.Cryptography.ProtectedData]::Protect(
    [System.Text.Encoding]::UTF8.GetBytes('sk-ant-smoke-test'), $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
[System.IO.File]::WriteAllLines((Join-Path $preferences 'translate-settings.txt'),
    [string[]] @("dpapi:$([Convert]::ToBase64String($secret))", '', 'noconfirm'))

$classic = $null
if ($ClassicZip) {
    $folder = Join-Path $work 'classic'
    Expand-Archive -Path $ClassicZip -DestinationPath $folder
    $classic = Join-Path $folder 'ClassicaCodex.UI.exe'

    $running = Start-App $classic 25
    Photo '01-classic-before'
    Close-App $running
}

$before = Hashes

Expand-Archive -Path $Zip -DestinationPath (Join-Path $work 'preview')
$exe = Join-Path $work 'preview\Classica Codex\Classica Codex.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "No $exe in the zip." }

$app = Start-App $exe 25
Photo '02-opened'
Set-Content (Join-Path $Photos 'windows-open.txt') ([TopWindows]::Titles($app.Id))

Press $app '^,' '03-settings-from-its-key' 'Settings'
Dismiss $app 'Settings' '04-settings-closed'
Press $app '^f' '05-search-from-its-key' 'Search'
Dismiss $app 'Search' '06-search-closed'
Press $app '{F1}' '07-help-from-its-key' 'Classica Codex Help'
Dismiss $app 'Classica Codex Help' '08-help-closed'

# Back and Forward need somewhere to go: a jump to Virgil from Search, by keys
# alone. Three Shift+Tabs from the search box reach its results.
$opened = Main-Title $app
Press $app '^f' '09-search-for-a-jump' 'Search'
Send-To (Find-Window $app 'Search') 'arma{ENTER}'
Send-To (Find-Window $app 'Search') '+{TAB}+{TAB}+{TAB}{DOWN}{ENTER}'
$jumped = Main-Title $app
if ($jumped -eq $opened) { throw "A result from Search should have left $opened." }
Dismiss $app 'Search' '10-jumped'
Press $app '%{LEFT}' '11-back-from-its-key' $opened
Press $app '%{RIGHT}' '12-forward-from-its-key' $jumped

Press $app '^+t' '13-all-tools-from-its-key' ''

if ($classic) {
    $both = Start-App $classic 20
    Photo '14-both-open-on-one-library'
    Alive $app 'with the classic app open beside it'
    Close-App $both
}

Close-App $app

if (Test-Path $errors) {
    Copy-Item $errors $Photos
    Get-Content $errors | Write-Host
    throw 'The app logged errors.'
}

$after = Hashes
$report = foreach ($file in (@($before.Keys) + @($after.Keys) | Sort-Object -Unique)) {
    if (-not $after.ContainsKey($file)) { "gone      $file" }
    elseif (-not $before.ContainsKey($file)) { "new       $file" }
    elseif ($before[$file] -ne $after[$file]) { "changed   $file" }
    else { "unchanged $file" }
}
Set-Content (Join-Path $Photos 'preferences-after.txt') $report
$report | Write-Host

# The reading position moves when a passage is read, in either app.
$harmed = $report | Where-Object { $_ -match '^(gone|changed)' -and $_ -notmatch 'reading-position\.txt$' }
if ($harmed) { throw "This app changed the classic app's files:`n$($harmed -join "`n")" }

if ($classic) {
    $running = Start-App $classic 25
    Photo '15-classic-after'
    Close-App $running

    if (Test-Path $errors) {
        Copy-Item $errors $Photos
        Get-Content $errors | Write-Host
        throw 'The classic app logged errors after this one had run.'
    }
}

# A library on a drive that isn't plugged in: named, and still remembered.
$unplugged = Join-Path $work 'unplugged\classicacodex.db'
Remember-Library $unplugged
$running = Start-App $exe 15
Photo '16-library-not-available'
Close-App $running
$remembered = (Get-Content (Join-Path $preferences 'database-path.txt') -Raw).Trim()
if ($remembered -ne $unplugged) { throw "The app forgot where the library was, and now remembers $remembered." }
if (Test-Path $errors) { throw 'A drive that is not plugged in is not an error, and the app logged one.' }

# A file that is there and won't open, which is reported.
$broken = Join-Path $work 'broken.db'
[System.IO.File]::WriteAllText($broken, ('x' * 8192))
Remember-Library $broken
$running = Start-App $exe 15
Photo '17-library-will-not-open'
Close-App $running
if (-not (Select-String -Path $errors -Pattern 'opening the library at launch' -Quiet)) { throw 'The library that would not open was not logged.' }
Remove-Item $errors

# Last, because the log it leaves is the point of it. The runtime ends the
# process once its crash handler returns, so the process being there at all
# after the crash means the handler is waiting on the dialog.
if ($CrashHook) {
    Remember-Library $Library
    $env:DOTNET_STARTUP_HOOKS = (Resolve-Path $CrashHook).Path
    $env:CC_CRASH_AFTER_SECONDS = '15'
    $crashing = Start-App $exe 30
    Remove-Item Env:DOTNET_STARTUP_HOOKS, Env:CC_CRASH_AFTER_SECONDS

    Photo '18-crash-dialog'
    Copy-Item $errors $Photos
    if (-not (Select-String -Path $errors -Pattern 'running \(fatal\)' -Quiet)) { throw 'The crash was not logged as fatal.' }

    Send-To (Find-Window $crashing '') '{ENTER}'
    if (-not $crashing.WaitForExit(60000)) {
        Photo '19-crash-dialog-answered'
        throw 'The app did not close once its crash dialog was answered.'
    }
    Write-Host "Closed after its crash dialog, with exit code $($crashing.ExitCode)."
}

Write-Host 'It ran, and logged nothing it should not have.'
