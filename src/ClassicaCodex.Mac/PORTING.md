# The Mac front end: how it is built, and how a Windows window comes across

`src/ClassicaCodex.Mac` is an [Avalonia](https://avaloniaui.net/) 12 front end
for macOS over the same libraries the Windows app uses. Everything that knows
what a passage, an edition or a lemma is lives in `ClassicaCodex.Core`,
`ClassicaCodex.Data` and `ClassicaCodex.Ingestion`, which target plain
`net8.0`; this project draws the windows.

## Building and running

```
dotnet build ClassicaCodex.Mac.slnf          # the Mac app, the shared libraries and their tests
dotnet run --project src/ClassicaCodex.Mac   # on a Mac
dotnet test tests/ClassicaCodex.Mac.Tests    # headless: runs on any OS
```

`ClassicaCodex.Mac.slnf` leaves out the WinForms projects, which do not build
on macOS. The Windows solution includes the Mac projects, so Visual Studio on
Windows builds them too - which means a change to a shared file that would
break the Mac breaks the Windows build first.

### A library to develop against

`tools/MacSampleLibrary` builds a small library with the setup wizard's own
ingestion code from sparse clones of Perseus and the lemma and dictionary
repositories - a few authors, both dictionaries, lemma data for those
authors. See the comment at the top of its `Program.cs` for the clone and the
command.

### Screenshots

The tests run the real app on Avalonia's headless platform with Skia
drawing, so a test can photograph a window exactly as it renders:

```
CC_SAMPLE_LIBRARY=/path/to/sample.db CC_SCREENSHOT_DIR=/tmp/shots dotnet test tests/ClassicaCodex.Mac.Tests
```

`CC_MAC_LOOK=1` (set by the tests) draws the unified title bar on a machine
that is not a Mac. Without `CC_SAMPLE_LIBRARY` the tests that need a library
return early; without `CC_SCREENSHOT_DIR` nothing is written.

## The layout of the project

| Folder | What is in it |
|---|---|
| `Shell/` | The main window: sidebar, reader panes, toolbar, menu bar, welcome page, tool gallery |
| `Features/<Name>/` | One folder per tool. Each has a `<Name>Feature.cs` with static entry points the shell calls |
| `Common/` | Shared building blocks: `FeatureWindow`, `Alert`, `TextPrompt`, `Files`, `ClipboardText`, `TableExport`, `Art`, `Glyphs` |
| `Platform/` | macOS specifics: `Keychain` + `TranslationSettings` (API keys), `SpeechService` (`say`), `ScriptFonts`, `MacShell` (`open`) |
| `Shared/` | Shims that let shared Windows files compile here (`WindowsShims.cs`) |
| `Theme/` | `Palette.axaml` (colours, fonts), `Styles.axaml` (classes), `Glyphs.axaml` (icons) |
| `Assets/Art/` | The Mac's painted artwork, and `ART-BRIEF.md`, the brief it is made to |

### Files compiled from the Windows folder

Some files in `src/ClassicaCodex.UI` are plain logic - no WinForms, no
`System.Drawing` - and the Mac compiles them where they are rather than
copying them: the reading position, linked panes, text size, node-kind
visibility, edition labels, the setup catalogue. They keep their
`ClassicaCodex.UI` namespace. The list is in `ClassicaCodex.Mac.csproj`.

A feature that needs another such file links it from a `.props` file in its
own folder (`Features/Stylometry/Stylometry.props`), which the project imports
by wildcard:

```xml
<Project>
  <ItemGroup>
    <Compile Include="$(MSBuildThisFileDirectory)..\..\..\ClassicaCodex.UI\Something.cs" Link="Shared\Something.cs" />
  </ItemGroup>
</Project>
```

Only a file with no Windows types in it. If one ever gains a `Form` or a
`Color`, the Mac build breaks, which is the point.

## Porting a Windows window

Read the Windows form first, all of it. The comments in this codebase carry
the reasons behind the behaviour - why a filter gives way to a jump, why a
count is an upper bound, why a line is italic - and a port that keeps the
behaviour and drops the reason will lose the behaviour the next time someone
touches it.

**Keep:** every control's purpose, every message and caveat (the windows are
careful to say what a result is not - "not proof of borrowing", "an upper
bound", "fiction"), every guard, every export. The repositories and services
the form calls are the same ones; call them the same way.

**Change:** what only existed because of WinForms - owner-drawing, DPI
arithmetic, row splitting, theme walks over the control tree. Avalonia does
those, and the code that did them goes.

**Make it a Mac window:** ⌘ shortcuts, sheets of controls rather than rows of
buttons where it helps, the palette's colours, the system font for chrome and
the reading fonts for text.

### Entry points

The shell never constructs a feature's window itself; it calls the static
methods in `Features/<Name>/<Name>Feature.cs`, whose signatures are fixed:
they are what the menus, the toolbar and the context menus are wired to.
Replace the body, keep the signature.

### Windows

Derive tool windows from `Common/FeatureWindow`. It gives:

- `OnNavigate(workId, textNodeId)` and `OnOpenWork(workId)` - the same two
  callbacks the Windows forms receive. Set them from the `IReaderHost`:
  `OnNavigate = host.NavigateToPassageAsync`.
- Escape and ⌘W close it.
- `ShowErrorAsync`, `ShowInfoAsync`.

Tool windows open modeless beside the reader (`window.Show(host.Window)`), so
the reader stays usable and a jump to a result is visible at once. A window
the Windows app keeps alive between openings (Search) does the same here.
Prompts and confirmations are modal (`await window.ShowDialog(host.Window)`).

Building controls in C#, as the Windows forms do, is the norm here; XAML is
fine where a layout is large and static. No MVVM framework.

### The host

`Shell/IReaderHost` is what a feature may ask of the main window: navigate,
open a work, the open work, the work selected in the library, an author's
name, refresh the passage marks, reload the library or the open work.
`PassageContext` is a right-clicked passage with its edition, the edition
opposite it, which side it came from, and the pane's font.

### Look

Colours and brushes are resources, never literals:

| Key | Use |
|---|---|
| `Cc.Window`, `Cc.Page`, `Cc.PageHeader`, `Cc.Card`, `Cc.Panel` | Surfaces |
| `Cc.Ink`, `Cc.InkMuted`, `Cc.InkFaint` | Text |
| `Cc.Accent`, `Cc.AccentStrong`, `Cc.AccentSoft`, `Cc.OnAccent` | The bronze accent |
| `Cc.Rule`, `Cc.RuleStrong` | Borders and dividers |
| `Cc.Selection`, `Cc.SelectionInk`, `Cc.Hover` | Selection and hover |
| `Cc.Gold`, `Cc.Verdigris`, `Cc.Lapis`, `Cc.Danger`, `Cc.Link` | Accents for charts, warnings, links |
| `Cc.SourceFont`, `Cc.TranslationFont`, `Cc.DisplayFont`, `Cc.MonoFont` | Font families |

In code: `control.Bind(Property, control.GetResourceObservable("Cc.Ink"))`
so the colour follows the appearance when it changes.

Style classes in `Theme/Styles.axaml`: text `display h1 h2 h3 section caption
muted faint warning source translation mono`; borders `page card panel notice
rule vrule`; buttons `toolbar accent link tile`; `TextBox.search`;
`ComboBox.edition`; `ListBox.reader`.

Icons: `Glyphs.Icon("Search")` or `{StaticResource Glyph.Search}`; the names
are in `Theme/Glyphs.axaml`. Painted tiles: `Art.Tile(tileKey, glyph, size)`,
which draws a medallion until Codex's painting for that key arrives.

### Dialogs, files, export

- `Alert.Info / Warn / Error / Confirm / Show` for MessageBox.
- `TextPrompt.Ask` for a line of text.
- `Files.SaveAsync / OpenAsync / FolderAsync` return plain paths.
- `ClipboardText.SetAsync(control, text)`.
- `TableExport.Attach(control, name, rows, selectedRows, notes)` gives any
  table the Windows right-click: copy, CSV, tab-separated, Excel - written by
  the same rules as Windows' `ResultExport`.

### macOS specifics

- API keys: `Platform.TranslationSettings` (same shape as Windows'; keys in
  the login keychain).
- Read Aloud: `Platform.SpeechService` (same shape as Windows'; uses `say`).
- Fonts for cuneiform, hieroglyphs, Hebrew, Arabic: `Platform.ScriptFonts`.
- Opening a URL, a file or a folder: `Platform.MacShell`.

### Tests

One file per feature in `tests/ClassicaCodex.Mac.Tests/Features/`, with
`[AvaloniaFact]` tests that open the window against the sample library, drive
it, assert on what it shows, and call `Screens.Save(window, "name")`. A test
that writes to the library copies it first.

Builds have zero warnings, and stay that way.
