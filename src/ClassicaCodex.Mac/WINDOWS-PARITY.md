# The classic Windows app and this one, window by window

Written to check the claim before anything makes it: can a Windows reader do
everything in this front end that they can do in the WinForms app?

**Every tool has a counterpart, and the five behaviours that didn't have now
come across.** All 84 functional forms have an equivalent here, and the 85th is
a WinForms scaling base class with nothing to port. What is left is the smaller
differences below.

## What this is based on

The window-by-window table is a reading of the source, not of the running
windows. The five behaviours under Brought across are also tested: by headless
tests on every system, and on a real Windows desktop by
`packaging/windows/smoke-test.ps1`, which presses the keys, starts the app with
its library missing and then broken, and crashes it on a background thread.
Retry is the exception there, covered by the headless tests alone.

- **read both**: the WinForms form and its counterpart were read and compared.
- **controls compared**: both sides' controls, handlers and messages were listed and compared.
- **strings**: every string in the form was looked for in its counterpart, and any miss followed up across the project. This shows the buttons and messages are there, not that each window behaves the same.

Across the project: all 268 methods the WinForms app calls in Core, Data and
Ingestion are also called from here or from a file this project compiles, and
all 33 Help topic titles match. The first is a check of method names, and the
bodies of the Help topics were not compared.

## Brought across

| Behaviour | Classic app | Here |
|---|---|---|
| F1 opens Help | `MainForm.cs:3204` | `Shell/MainWindow.Menus.cs`, off a Mac. The Help menu prints it, and Ctrl+Shift+? still works |
| Alt+Left and Alt+Right go Back and Forward | `MainForm.cs:3184`, `3188` | `Shell/MainWindow.Menus.cs`, off a Mac. The Go menu prints them, and Ctrl+[ and Ctrl+] still work |
| A library that isn't available at launch is named, with Retry. This is usually a drive that isn't plugged in | `Program.cs:91-97` | `LibraryFile.OpenRememberedAsync` and `Shell/NoLibraryView.cs`, a page in the main window. The remembered path stays until a library that opens replaces it, and the page warns of that |
| A library that can't be opened (locked, corrupt) is reported at launch | `Program.cs:113-119` | The same page, with the reason, and with Retry, which the classic app doesn't offer here |
| A fatal crash on a background thread shows a dialog before the app closes | `CrashReporter.cs:51`, `102-106` | `CrashLog.ReportFatal`, called from `Program.cs` |

## Smaller differences

- **A fatal crash when the UI thread can't answer.** The classic app's message box runs on the failing thread. Here the dialog is the UI thread's to show, so a fatal error on that thread, or one while it stays stuck for ten seconds, goes to `errors.log` alone. Errors in actions on the UI thread still show their own dialog.
- **A library that didn't open.** The classic app shows message boxes and then its setup wizard, and closes if the reader leaves that with nothing set up. Here the page stays in the main window, and Open Library and Create a New Library stand in for the wizard.
- **Text size.** The sliders, the link between them and live changes are all here, in the toolbar's flyout and Settings. The Greek and English sample lines aren't, and nor is a Cancel that restores the opening sizes. The reader itself shows the result, and half-point steps and keyboard sizing are new here.
- **AI Translation Settings.** Everything is here, in Settings. The Tools menu's item opens Settings on its General pane rather than AI, and closing without Save stands in for a Cancel button.
- **Theme.** A theme chosen in the classic app (`theme.txt`) isn't picked up. This app follows Windows until told otherwise.
- **Layout cache.** The classic app keeps measured text in `layout-cache` to reopen very long works quickly. This app has no such cache, and how it compares on a work as long as Pliny hasn't been measured.

## Window by window

The 22 forms with no same-named window are in bold.

| WinForms form | Here | Verdict | Checked by |
|---|---|---|---|
| About | `Features/About/AboutWindow.cs` | same | strings |
| AiProjectSuggestion | `Features/Research/Bench/AiProjectSuggestionWindow.cs` | same | strings |
| **Almagest** | `Features/Instruments/AlmagestCard.cs` and `Pages/Almagest.html` | same job: a card, then the page in a browser | read both |
| Alphabet | `Features/Alphabet/AlphabetWindow.cs` | same | strings |
| **Antikythera** | `Features/Instruments/AntikytheraCard.cs` and its page | same job | controls compared |
| **Apparatus** | `Features/Apparatus/EditorNotesWindow.cs` | other name | read both |
| ArtifactBrowser | `Features/Artifacts/ArtifactBrowserWindow.cs` | same | strings |
| Attribution | `Features/WorkInfo/AttributionWindow.cs` | same | strings |
| AutoTag | `Features/Tags/AutoTagWindow.cs` | same | strings |
| BibliographyImport | `Features/Research/Bibliography/BibliographyImportWindow.cs` | same | strings |
| **BookmarkPrompt** | `Features/Bookmarks/BookmarkPrompt.cs` | same | read both |
| Bookmarks | `Features/Bookmarks/BookmarksWindow.cs` | same | strings |
| BronzeArcade | `Features/Arcade/BronzeArcadeWindow.cs` | same | strings |
| BronzeCollection | `Features/Arcade/BronzeCollectionWindow.cs` | same | strings |
| BronzeGift | `Features/Arcade/BronzeGiftWindow.cs` | same | strings |
| BronzeWitness | `Features/Arcade/BronzeWitnessWindow.cs` | same | strings |
| CategoryShapes | `Features/MythNetwork/CategoryShapesWindow.cs` | same | strings |
| Collation | `Features/Compare/CollationWindow.cs` | same | strings |
| **Compare** | `Features/Tags/CompareSourcesWindow.cs` | other name | read both |
| CompareTranslations | `Features/Compare/CompareTranslationsWindow.cs` | same | strings |
| Concordance | `Features/Concordance/ConcordanceWindow.cs` | same | strings |
| CorpusInvestigator | `Features/Research/Echoes/CorpusInvestigatorWindow.cs` | same | strings |
| CreateTranslation | `Features/Translation/CreateTranslationWindow.cs` | same | strings |
| CriticCard | `Features/Reactions/CriticCardWindow.cs` | same | strings |
| CrossLanguageEcho | `Features/Echoes/CrossLanguageEchoWindow.cs` | same | strings |
| EchoResults | `Features/Echoes/EchoResultsWindow.cs` | same | strings |
| EditionChoice | `Features/Translation/EditionChoiceDialog.cs` | same | strings |
| EvidenceSources | `Features/Research/Sources/EvidenceSourcesWindow.cs` | same | strings |
| ExperimentPicker | `Features/Stylometry/ExperimentPickerWindow.cs` | same | strings |
| GuidedSetup | `Features/Setup/GuidedSetupWindow.cs` | same | strings |
| **Harmonics** | `Features/Instruments/HarmonicsCard.cs` and its page | same job | controls compared |
| Help | `Features/Help/HelpWindow.cs`, `HelpTopics.cs` | same | strings |
| HypothesisLab | `Features/Research/Hypotheses/HypothesisLabWindow.cs` | same | strings |
| Ingest | `Features/Setup/IngestWindow.cs` | same | strings |
| IntertextualAtlas | `Features/Research/Echoes/IntertextualAtlasWindow.cs` | same | strings |
| LemmaIngest | `Features/Setup/LemmaIngestWindow.cs` | same | strings |
| Main | `Shell/MainWindow*.cs`, `LibrarySidebar.cs`, `ReaderPane.cs` | same: see Brought across | menus, toolbar and context menus read both |
| ManuscriptPages | `Features/MedievalHands/ManuscriptPagesWindow.cs` | same | strings |
| MenotaPlan | `Features/Setup/MenotaPlanWindow.cs` | same | strings |
| Morphology | `Features/Morphology/MorphologyWindow.cs` | same | strings |
| MythNetwork | `Features/MythNetwork/MythNetworkWindow.cs` | same | strings |
| **Palaeography** | `Features/MedievalHands/MedievalHandsWindow.cs` | other name | controls compared |
| **ParallelPassageStudio** | `Features/Research/Echoes/ParallelStudioWindow.cs` | other name, all three tabs | read both |
| ParameterGrid | `Features/Stylometry/ParameterGridWindow.cs` | same | strings |
| PassageExport | `Features/Export/PassageExportWindow.cs` | same | strings |
| PassageInquiry | `Features/Research/Inquiry/PassageInquiryWindow.cs` | same | strings |
| PassageSetExport | `Features/Export/PassageSetExportWindow.cs` | same | strings |
| Perturbation | `Features/Stylometry/PerturbationWindow.cs` | same | strings |
| PlacesMap | `Features/PlacesMap/PlacesMapWindow.cs` | same | strings |
| Preface | `Features/Apparatus/PrefaceWindow.cs` | same | strings |
| Reactions | `Features/Reactions/ReactionsWindow.cs` | same | strings |
| **ReadingFontSize** | toolbar flyout, Settings → Reading, the View menu | partial: see Smaller differences | read both |
| ReceptionTracker | `Features/Echoes/ReceptionTrackerWindow.cs` | same | strings |
| ResearchAudit | `Features/Research/Audit/ResearchAuditWindow.cs` | same | strings |
| ResearchBench | `Features/Research/Bench/ResearchBenchWindow*.cs` | same | strings |
| ResearchBibliography | `Features/Research/Bibliography/ResearchBibliographyWindow.cs` | same | strings |
| **ResearchCorpusSnapshots** | `Features/Research/Snapshots/CorpusSnapshotsWindow.cs` | other name | read both |
| **ResearchEchoCapture** | `Features/Research/Echoes/EchoCaptureWindow.cs` | other name | read both |
| **ResearchEchoInvestigations** | `Features/Research/Echoes/EchoInvestigationsWindow.cs` | other name | read both |
| ResearchLog | `Features/Research/Log/ResearchLogWindow.cs` | same | strings |
| **ResearchReadingQueue** | `Features/Research/Reading/ReadingQueueWindow.cs` | other name | controls compared |
| **ResearchSynthesis** | `Features/Research/Synthesis/SynthesisWindow.cs` | other name | controls compared |
| **Scaled** | none | not needed: the WinForms DPI base class | read the WinForms side |
| ScholarlyClaims | `Features/Research/Claims/ScholarlyClaimsWindow.cs` | same | strings |
| Search | `Features/Search/SearchWindow.cs` | same | strings |
| Settings (the library's location) | the Library File dialog in `Features/Setup/AdvancedSetupWindow.cs`, Settings → Library, the File menu | merged | WinForms read, dialog controls compared |
| **SetupModeChoice** | `Features/Setup/SetupChoiceWindow.cs` | other name | read both |
| **SetupWizard** | `Features/Setup/AdvancedSetupWindow.cs` | other name | controls compared |
| StartingPoints | `Features/Translation/StartingPointsWindow.cs` | same | strings |
| StylometryAnalysis | `Features/Stylometry/StylometryAnalysisWindow.cs` | same | strings |
| Stylometry | `Features/Stylometry/StylometryWindow.cs` | same | strings |
| **StylometryRunEvidencePicker** | `Features/Research/Bench/StylometryRunPickerWindow.cs` | other name | read both |
| TagBrowser | `Features/Tags/TagBrowserWindow.cs` | same | strings |
| **TagPrompt** | `Features/Tags/TagPrompt.cs` | same | read both |
| **TextPrompt** | `Common/TextPrompt.cs` | same | read both |
| Timeline | `Features/Timeline/TimelineWindow.cs` | same | strings |
| **TranslateApiSettings** | the AI pane of `Features/Settings/SettingsWindow.cs` | merged: see Smaller differences | read both |
| **Translate** | `Features/Translation/TranslatePassageWindow.cs` | other name | controls compared |
| TranslationWorkbench | `Features/Translation/TranslationWorkbenchWindow.cs` | same | strings |
| Validation | `Features/Stylometry/ValidationWindow.cs` | same | strings |
| Vocabulary | `Features/Vocabulary/VocabularyWindow.cs` | same | strings |
| WordIndex | `Features/Setup/WordIndexWindow.cs` | same | strings |
| WordStudy | `Features/WordStudy/WordStudyWindow.cs` | same | strings |
| WorkDetails | `Features/WorkInfo/WorkDetailsWindow.cs` | same | strings |
| WorkPicker | `Features/WordStudy/WorkPickerWindow.cs` | same | strings |

All of the classic main window's 16 tool buttons, 9 other toolbar buttons, 8
library menu items and 16 reader line menu items are here. The classic app has
no printing, drag and drop, image export, column sorting or update check, so
there is nothing there to bring across.
