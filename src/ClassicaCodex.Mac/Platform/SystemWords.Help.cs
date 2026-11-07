namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// The Help as an iPad should read it. The Help describes a desktop app, with a
/// menu bar, keyboard shortcuts and folders to open. Most of that is a phrase
/// to swap, but where a passage has nothing true to say on an iPad it is
/// replaced whole, from one landmark in the Mac text up to another, because
/// the folder paths written into the middle differ on every machine.
/// </summary>
internal static partial class SystemWords
{
    /// <summary>Where a file sits, as a person with the Files app would find it.</summary>
    private const string LogsFolder = "in the ClassicaCodex folder, inside this app's folder in the Files app";

    /// <summary>A plain path outside the app's own folder is refused once the picker has closed.</summary>
    private const string KeepInAppFolder = "Keep the file in this app's folder in the Files app: iPadOS doesn't let the app reach one kept elsewhere.";

    private static string RewritePassages(string text)
    {
        foreach (var (from, to, device) in DevicePassages)
        {
            var start = text.IndexOf(from, StringComparison.Ordinal);
            if (start < 0) continue;

            var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            if (end < 0) continue;

            text = string.Concat(text.AsSpan(0, start), device, text.AsSpan(end));
        }

        return text;
    }

    private static readonly (string From, string To, string Device)[] DevicePassages =
    {
        ("Everything starts in Setup - File > Setup…", "Downloads are large and take a while.",
            """
            Everything starts in Setup, at the foot of the sidebar's Tools list. It asks how you'd like to set things up:

            Quick Start (recommended)
               The Greek and Latin classics with their translations, and the dictionaries, then the word index. It uses about 2 GB of space and takes about ten minutes, and the Iliad opens when it finishes. It leaves out Greek Lemma Data and Latin Lemma Data, which Word Study and Morphology Search need. Add them from Choose Collections, and allow about an hour for the Greek.

            Choose Collections
               Every collection, one step at a time, so you can pick the ones you want. The main collections use about 12 GB of space. Every step is optional, so you can skip one and come back for it whenever you like.

            Whichever you use, once the texts are in, build the word index. Quick Start does this for you, and in Choose Collections it is the last step. This is what makes searching fast - without it, every search scans the whole corpus once per word form. It takes a few minutes and can be rerun at any time.

            Keep Classica Codex on screen while a step runs: iPadOS pauses an app that is switched away from, and the step would then have to start again.


            """),
        ("Keyboard shortcuts\n", "Where you left off\n", string.Empty),
        ("The library is a single SQLite file.", "None of the texts, dictionaries, or linguistic data belong to this app",
            """
            Everything is on this iPad, and the files are in this app's folder in the Files app.

            classicacodex.db
               Your library: a single SQLite file holding everything you create - tags, bookmarks, research projects - alongside the ingested texts. It is included when you back up this iPad.

            ClassicaCodexData
               The files Setup downloaded. A full set is about nine gigabytes, and the folder is left out of backups. These are mostly working copies of public data, so deleting them costs nothing but the time to fetch them again. Your tags and bookmarks live in the library, not in this folder.

            ClassicaCodex
               Preferences and the error log, in small files.

            API keys
               Kept in this iPad's keychain rather than in a file. They stay on this iPad and are left out of its backups.

            Deleting Classica Codex removes all of this from the iPad. iPadOS can keep keychain items after an app is deleted, so press Remove Key in Settings first.


            """),
        ("The first several are listed, and the full list goes to:", "Something went wrong with that action",
            $"The first several are listed, and the full list goes to ingest-skipped.log, {LogsFolder}.\n\n"),
        ("Details go to:", "Search finds nothing for a word you can see on screen",
            $"Details go to errors.log, {LogsFolder}.\n\n"),
        ("   What makes a report worth answering is usually one file.", "   Nothing is sent anywhere by the app itself.",
            "   What makes a report worth answering is usually one file. Most faults are already written down in "
            + $"errors.log, {LogsFolder}. It holds what the dialog showed plus where in the app it happened. Attach it, "
            + "say what you were doing at the time, and include the version number from About Classica Codex, in the "
            + "sidebar's Tools list.\n\n"),
    };

    /// <summary>Longer phrases come first, so "Tools menu" only catches what is left.</summary>
    private static readonly (string Mac, string Device)[] DeviceHelpPhrases =
    {
        ("Hover any line to see its citation reference.\n\n", string.Empty),
        ("Clearing the box brings them all back, and ⌘L jumps to it.", "Clearing the box brings them all back."),
        ("Hovering the icon says which state it is in, and it takes the accent colour", "The icon takes the accent colour"),
        ("; ⌘1 and ⌘2 do the same.", "."),
        (" ⌘[ and ⌘] do the same, as in Safari, and both are in the Go menu.", string.Empty),
        ("Touch and hold (or Control-click)", "Touch and hold"),
        ("Only clicking a line updates it.", "Only tapping a line updates it."),
        ("And if, patient reader, you would step beyond the words and meet your fate... return to the main window and press ⌃⇧F12 - Control, Shift and F12, with fn if your keyboard asks for it. The bronze is drawn. The thunder answers. Tread carefully: the oracle has warned you.",
            string.Empty),

        ("until you click a button", "until you press a button"),
        ("can be turned off from AI Translation Settings, in the Tools menu,", "can be turned off from the AI tab of Settings,"),
        ("Where should I start? (Go and Help menus)", "Where should I start? (Tools list)"),
        ("In the Go menu and the Help menu, and on the welcome page.", "In the sidebar's Tools list, and on the welcome page."),
        ("clicking Translate This", "tapping Translate This"),
        ("Click Search in the toolbar, or press ⌘F, to open the search window. It stays open beside the reader rather than blocking it, so you can double-tap a result, read it in context, and come straight back to the list.",
            "Tap Search in the toolbar to open search. Double-tap a result to read it in context, then tap Back to return to the list."),

        ("click a line between two nodes", "tap a line between two nodes"),
        ("Bookmarks (toolbar, ⇧⌘B)", "Bookmarks (toolbar)"),
        ("then click any word in it", "then tap any word in it"),
        ("click a node or edge", "tap a node or edge"),
        (", or select it and press ⌘E (File > Export Passage…).", "."),
        ("the row you clicked", "the row you tapped"),

        ("; Bigger Text, Smaller Text and Default Text Size in the View menu do the same on ⌘+, ⌘- and ⌘0.", "."),
        ("Where the library, tags, and bookmarks are stored: one file. File > Open Library… switches to another, and File > New Library… starts an empty one wherever you choose and goes straight on into Setup - so separate libraries are simply separate files. A library copied from a PC opens as it is: the Mac brings its structure up to date the same way the Windows app does, and nothing has to be downloaded again. You're only asked for this on a first run, or if the file has been moved or deleted.",
            "Where the library, tags, and bookmarks are stored: one file, in this app's folder in the Files app."),

        ("In the Tools menu, and in the sidebar's Tools list under Analysis.", "In the sidebar's Tools list, under Analysis."),
        ("Downloads are large and take a while. Run one at a time and let each finish.",
            """
            Downloads are large and take a while. Run one at a time and let each finish.

            Finding your way around
               The first button in the toolbar shows or hides the sidebar, which holds the library and the tools.

               A tool opens as a full page, with Back at its top left. The Help calls these windows. When a result takes you to the reader, a Back button floats over the page to return you to the tool.

               Touch and hold a line, a work or a row for its menu, and double-tap a result to open it. Pinch the Places Map to zoom.
            """),

        ("Touch and hold a line and choose Word Study, then tap any word in it.",
            "Touch and hold a line and choose Word Study, then tap any word in it. Word Study is on the menu once the library "
            + "has lemma data for that language: Greek Lemma Data or Latin Lemma Data, under Choose Collections in Setup."),
        (", and so does View > Link the Two Pages: linked panes", ": linked panes"),
        (" - the same three the Windows app draws, so a line marked on either machine reads the same on both.", "."),
        ("put a PNG in a \"Figures\" folder beside your database file, named for the tag.",
            "put a PNG in a folder called Figures in this app's folder in the Files app, named for the tag."),
        ("A long work means many requests over several minutes; progress saves",
            "A long work means many requests over several minutes, so keep Classica Codex on screen while it runs; progress saves"),
        ("docs/stylometry-notes.md in the repository is", "docs/stylometry-notes.md in the project's GitHub repository is"),
        ("or choose Research Bench… in the Research menu.", "or choose Research Bench in the sidebar's Tools list."),
        ("; the Zotero route requires Zotero's local integration to be available.", "."),
        ("Source files & page notes attaches a local PDF to evidence without putting the PDF inside the database.",
            "Source files & page notes attaches a PDF to evidence without putting the PDF inside the database. " + KeepInAppFolder),
        ("imports structured citations without flattening their fields.",
            "imports structured citations without flattening their fields. " + KeepInAppFolder),
        ("There are two exports, for two different shapes of thing.",
            "There are two exports, for two different shapes of thing. Each asks where to save. Choose a place inside the "
            + "Classica Codex folder, under On My iPad: iPadOS can refuse the app a file saved anywhere else."),
        ("The README lists which is which.", "The project's README on GitHub lists which is which."),

        ("the hieroglyph font that ships with macOS has not caught up with all of them. Nothing is wrong with the text — installing a font with wider coverage, such as Aegyptus or NewGardiner (double-tap it to add it with Font Book), fills them in.",
            "the hieroglyph font on this iPad has not caught up with all of them. Nothing is wrong with the text."),
        ("Hebrew is drawn from the right edge of the pane, in a font that has the vowel points and the accents — Times New Roman, which every Mac has, unless a font made for pointed Hebrew such as SBL Hebrew or Ezra SIL is installed.",
            "Hebrew is drawn from the right edge of the pane."),
        ("Arabic reads from right to left and is shown that way, in a font with the Quran's marks — Amiri Quran or Scheherazade New if either is installed, since they are made for it, and otherwise Geeza Pro, which every Mac has.",
            "Arabic reads from right to left and is shown that way."),

        ("The appearance button in the toolbar, or View > Appearance: Follow", "The appearance button in the toolbar: Follow"),
        ("Following macOS changes with the system - at sunset, if your Mac is set to Auto.",
            "Following the system changes when this iPad's light or dark setting does."),
        ("Following macOS switches between them with the system, at sunset if you have it set that way.",
            "Following the system switches between them when this iPad's light or dark setting does."),

        ("That is the point of having it.",
            "That is the point of having it. A few of those signs show as small boxes, because iPadOS has no font for them. "
            + "The photograph beside the line shows what the scribe wrote."),
        ("Nothing is saved to your download folder;", "Nothing is saved to ClassicaCodexData;"),
        ("Once fetched they are on your computer and the window needs no connection at all.",
            "Once fetched they are on this iPad and the window needs no connection at all. Keep Classica Codex on screen while one downloads."),

        ("The first of the Instruments, in the menu bar and in the sidebar's Tools list.", "The first of the Instruments, in the sidebar's Tools list."),
        ("opens a working simulation of it in your browser", "opens a working simulation of it on the Skirnir Studios site, in your browser"),
        ("It works offline\n   The page carries everything it needs and asks the network for nothing. It ships inside the app, and opening it hands it to whatever browser you normally use.",
            "It needs a connection\n   The page opens on the Skirnir Studios site, so it needs an internet connection."),
        ("Beside the mechanism in the Instruments menu.", "Beside the mechanism in the sidebar's Tools list."),
        ("opens a working planetarium of it in your browser", "opens a working planetarium of it on the Skirnir Studios site, in your browser"),
        ("It works offline\n   Like the mechanism, the page carries everything it needs and asks the network for nothing.",
            "It needs a connection\n   Like the mechanism, the page opens on the Skirnir Studios site, so it needs an internet connection."),

        ("In Setup, under Tools, open Word Index.", "Open Setup, tap Choose Collections and go on to Build Word Index."),
        ("nothing leaves this machine", "nothing leaves this iPad"),
        ("hitting the free tier's daily limit", "hitting a daily limit"),

        ("Tools menu", "Tools list"),
    };
}
