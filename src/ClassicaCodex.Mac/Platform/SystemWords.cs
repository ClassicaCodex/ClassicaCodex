namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// What the app says on a system that is not a Mac. The windows and the Help
/// are written the way a Mac does things - ⌘ keys, the Finder, the keychain -
/// and <see cref="Say"/> rewords a string for Linux, Windows or an iPad as it is shown,
/// so the Mac text stays the one that is written and kept up to date.
///
/// One table for all of them, so the Help and the window it describes cannot
/// come to disagree. Longer phrases are listed before the shorter ones inside
/// them. The Help's own passages are checked against what is left afterwards
/// by the tests, which fail on any Mac word the table has let through.
///
/// CC_MAC_LOOK keeps the Mac wording on any machine, as it keeps the Mac
/// title bar: it is how the headless tests see the same window on Linux.
/// </summary>
internal static class SystemWords
{
    public static bool IsMac => OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("CC_MAC_LOOK") == "1";

    /// <summary>This text as it should read on the system the app is running on.</summary>
    public static string Say(string text) =>
        IsMac ? text
        : OperatingSystem.IsWindows() ? ForWindows(text)
        : OperatingSystem.IsIOS() ? ForDevice(text)
        : ForLinux(text);

    internal static string ForLinux(string text)
    {
        foreach (var (mac, pc) in Keys) text = text.Replace(mac, pc, StringComparison.Ordinal);
        foreach (var (mac, linux, _) in Phrases) text = text.Replace(mac, linux, StringComparison.Ordinal);
        return text;
    }

    /// <summary>
    /// The Linux wording is the nearest an iPad has, so it starts from that,
    /// and <see cref="DevicePhrases"/> goes first for what would be untrue there.
    /// </summary>
    internal static string ForDevice(string text)
    {
        text = ForTouch(text);
        foreach (var (mac, device) in DevicePhrases) text = text.Replace(mac, device, StringComparison.Ordinal);
        return ForLinux(text);
    }

    /// <summary>
    /// A line of the interface as it should read under a finger. The iPad app
    /// runs this over everything shown, not only what passes through
    /// <see cref="Say"/>, which most instructions do not, so it also sees every
    /// line of every text in the reader. That is why
    /// <see cref="TouchPhrases"/> holds whole phrases of the interface, and
    /// never a bare "click": a translation can use that word.
    /// </summary>
    internal static string ForTouch(string text)
    {
        if (!text.Contains("lick", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("LICK", StringComparison.Ordinal)
            && !text.Contains("File menu", StringComparison.Ordinal)
            && !text.Contains("mouse wheel", StringComparison.Ordinal)) return text;

        foreach (var (mouse, finger) in TouchPhrases) text = text.Replace(mouse, finger, StringComparison.Ordinal);
        return text;
    }

    internal static string ForWindows(string text)
    {
        foreach (var (mac, pc) in Keys) text = text.Replace(mac, pc, StringComparison.Ordinal);
        foreach (var (mac, _, windows) in Phrases) text = text.Replace(mac, windows, StringComparison.Ordinal);
        return text;
    }

    private static readonly (string Mac, string Device)[] DevicePhrases =
    {
        ("Show it in the Finder?", "Show it in Files?"),
        ("hear the line in a macOS voice", "hear the line in one of this iPad's voices"),
        ("Speaks the selected passage using whatever voices macOS already has installed - completely offline",
            "Speaks the selected passage using whatever voices this iPad already has - completely offline"),
        ("macOS has voices for modern Greek but none for the ancient language",
            "iOS has voices for modern Greek but none for the ancient language"),
        ("The voices are the Mac's own, from System Settings > Accessibility > Spoken Content > System voice > Manage Voices. This app can't add voices macOS doesn't already have - add one there and it will appear here.",
            "The voices are the iPad's own. More can be added in its Settings app, under Accessibility, and they will appear here."),
        ("Hands a passage to the voices macOS already has.", "Hands a passage to the voices on this iPad."),
        ("More voices: System Settings → Accessibility → Spoken Content → System Voice → Manage Voices.",
            "More voices can be added in the iPad's Settings app, under Accessibility."),
        ("Following macOS switches between them with the system, at sunset if you have it set that way.",
            "Following the system switches between them with this iPad's appearance setting."),
        ("⌘Return to bookmark", string.Empty),
        ("Show in Finder", "Show in Files"),
        ("Keys are kept in your login keychain, under \"Classica Codex\" in Keychain Access - where macOS keeps every other app's passwords - rather than in a file. Another account on the same Mac can't read them.",
            "Keys are kept in this iPad's keychain, where iOS keeps every other app's passwords, rather than in a file. They stay on this iPad and are left out of its backups."),
        ("Saved. The keys are in your login keychain, under “Classica Codex” in Keychain Access.",
            "Saved. The keys are in this iPad's keychain."),
        ("The keychain refused to store a key - check Keychain Access and try again.",
            "The keychain refused to store a key. Try again."),
        ("and two to three hours on a decent connection with everything selected - mostly unattended, and the Greek word-form data is about an hour of that on its own.",
            "and about half an hour for the main Greek and Latin collections on a good connection. Keep Classica Codex on screen while a step runs: iOS pauses an app that is switched away from, and the step would then have to start again.")
    };

    private static readonly (string Mouse, string Finger)[] TouchPhrases =
    {
        ("DOUBLE-CLICK", "DOUBLE-TAP"),
        ("Double-click", "Double-tap"),
        ("double-click", "double-tap"),
        ("Right-click", "Touch and hold"),
        ("right-click", "touch and hold"),
        ("CLICK A WORD", "TAP A WORD"),
        ("Click a word", "Tap a word"),
        ("Click any word", "Tap any word"),
        ("Click any place", "Tap any place"),
        ("Click a place", "Tap a place"),
        ("Click a tag", "Tap a tag"),
        ("Click a node", "Tap a node"),
        ("Click an author", "Tap an author"),
        ("Click to unlink", "Tap to unlink"),
        ("Click to link", "Tap to link"),
        ("click the photo", "tap the photo"),
        ("click a face", "tap a face"),
        ("collections - click to", "collections - tap to"),
        ("click one to", "tap one to"),
        ("you click the corresponding", "you tap the corresponding"),
        ("you click one of the two buttons", "you tap one of the two buttons"),
        ("clicking Next", "tapping Next"),
        ("or click Choose", "or tap Choose"),
        ("Select two or more rows to merge. Click a row, then ⇧-click another.", "Select two or more rows to merge."),
        (" ⇧-click or ⌘-click to select several rows.", string.Empty),
        ("Pinch to zoom (or scroll, with a mouse wheel), drag or swipe with two fingers to pan", "Pinch to zoom, drag with two fingers to pan"),
        ("choose Setup from the File menu afterward", "open Setup from All Tools afterward"),
        ("Setup is always there in the File menu and among the tools in the sidebar", "Setup is always there among the tools in the sidebar"),
        ("open Setup from the Tools list or the File menu", "open Setup from the Tools list"),
        ("(Setup, in the File menu)", "(Setup, among the tools)")
    };

    /// <summary>The same on any PC keyboard. The chords go first, so that ⇧⌘ doesn't become "Shift+Ctrl+".</summary>
    private static readonly (string Mac, string Pc)[] Keys =
    {
        ("⌥⌘", "Ctrl+Alt+"),
        ("⇧⌘", "Ctrl+Shift+"),
        ("⌃⇧", "Ctrl+Shift+"),
        ("⌘Return", "Ctrl+Enter"),
        ("⌘↩", "Ctrl+Enter"),
        ("⌘-click", "Ctrl-click"),
        ("⇧-click", "Shift-click"),
        ("⌘", "Ctrl+"),
        ("⇧", "Shift+"),
        ("⌥", "Alt+"),
        ("⌃", "Ctrl+"),
        ("Option-Left and Option-Right", "Alt+Left and Alt+Right")
    };

    /// <summary>Applied after <see cref="Keys"/>, so a chord in a phrase reads as it does by then.</summary>
    internal static readonly (string Mac, string Linux, string Windows)[] Phrases =
    {
        // The Finder.
        ("To open any of these in the Finder, choose Go > Go to Folder… (Ctrl+Shift+G) and paste the path. Folders inside ~/Library are otherwise hidden in the Finder.",
            "To open any of these, paste the path into your file manager. Folders whose names begin with a dot, such as ~/.local, are hidden there unless you ask to see them.",
            "To open any of these, paste the path into the address bar of File Explorer. AppData is otherwise hidden there."),
        ("In the Finder, Go > Go to Folder… (Ctrl+Shift+G) takes the path as it is written here.",
            "Your file manager takes the path as it is written here.",
            "The address bar of File Explorer takes the path as it is written here."),
        ("Show it in the Finder?", "Show it in its folder?", "Show it in File Explorer?"),
        ("Show in Finder", "Show in Folder", "Show in File Explorer"),
        ("copying across in the Finder rather than redoing", "copying across rather than redoing", "copying across in File Explorer rather than redoing"),

        // Following the system.
        ("Following macOS changes with the system - at sunset, if your Mac is set to Auto.",
            "Following the system changes when your desktop's light or dark setting does.",
            "Following Windows changes when its light or dark setting does."),
        ("Following macOS switches between them with the system, at sunset if you have it set that way.",
            "Following the system switches between them when your desktop's light or dark setting does.",
            "Following Windows switches between them when its light or dark setting does."),
        ("Follow macOS", "Follow System", "Follow Windows"),
        ("Appearance: follows macOS", "Appearance: follows the system", "Appearance: follows Windows"),

        // Where the keys are kept.
        ("Keys are kept in your login keychain, under \"Classica Codex\" in Keychain Access - where macOS keeps every other app's passwords - rather than in a file. Another account on the same Mac can't read them.",
            "Keys are kept in your desktop's keyring, under \"Classica Codex\", through the Secret Service - where other Linux apps keep their passwords - rather than in a file. This needs the secret-tool command, usually in a package called libsecret-tools; without it a key lasts until the app quits, and is never written to a file.",
            "Keys are kept in an encrypted file, translate-settings.txt, in the folder of preferences that Where your data lives names. Windows does the encrypting, for your account alone, so another account on the same PC can't read them and a copy of the file is no use on another PC. The classic Windows app keeps its keys in the same file, so a key saved in either is there in both."),
        ("Saved. The keys are in your login keychain, under “Classica Codex” in Keychain Access.",
            "Saved. The keys are in your desktop's keyring, under “Classica Codex”.",
            "Saved. The keys are in an encrypted file that only your Windows account can read."),
        ("The keychain refused to store a key - check Keychain Access and try again.",
            "The keyring refused to store a key - check that it is unlocked and try again.",
            "The encrypted key file could not be saved, so the keys apply until Classica Codex quits. Check that you can write to the folder of preferences and try again."),
        ("This system has no keychain to keep the keys in, so they apply until Classica Codex quits.",
            "Keys can't be saved without the secret-tool command, usually in a package called libsecret-tools, so they apply until Classica Codex quits.",
            "Keys can't be saved on this system, so they apply until Classica Codex quits."),

        // Read Aloud.
        ("hear the line in a macOS voice", "hear the line in an espeak-ng voice", "hear the line in a Windows voice"),
        ("Speaks the selected passage using whatever voices macOS already has installed - completely offline",
            "Speaks the selected passage through espeak-ng, if it is installed - completely offline",
            "Speaks the selected passage using whatever voices Windows already has installed - completely offline"),
        ("macOS has voices for modern Greek but none for the ancient language",
            "espeak-ng has a voice for modern Greek but none for the ancient language",
            "Windows has voices for modern Greek but none for the ancient language"),
        ("The voices are the Mac's own, from System Settings > Accessibility > Spoken Content > System voice > Manage Voices. This app can't add voices macOS doesn't already have - add one there and it will appear here.",
            "The voices are espeak-ng's own. Install the espeak-ng package and they appear here; without it, Read Aloud is not offered at all.",
            "The voices are Windows' own, from Settings > Time & Language > Speech. This app can't add voices Windows doesn't already have - add one there and it will appear here."),
        ("Hands a passage to the voices macOS already has.", "Hands a passage to espeak-ng.", "Hands a passage to the voices Windows already has."),
        ("More voices: System Settings → Accessibility → Spoken Content → System Voice → Manage Voices.",
            "The voices are espeak-ng's, and are all installed with it.",
            "More voices: Settings → Time & Language → Speech."),
        ("Speech is not available on this system.", "Read Aloud needs espeak-ng, which is not installed.", "Speech is not available on this system."),

        // Fonts.
        ("the hieroglyph font that ships with macOS has not caught up with all of them",
            "Noto Sans Egyptian Hieroglyphs has not caught up with all of them",
            "Segoe UI Historic, the font that ships with Windows, has not caught up with all of them"),
        ("(double-click it to add it with Font Book)", "(copy it into ~/.local/share/fonts)", "(right-click it and choose Install)"),
        ("Times New Roman, which every Mac has, unless a font made for pointed Hebrew such as SBL Hebrew or Ezra SIL is installed.",
            "Noto Serif Hebrew, unless a font made for pointed Hebrew such as SBL Hebrew or Ezra SIL is installed.",
            "Times New Roman, which every Windows PC has, unless a font made for pointed Hebrew such as SBL Hebrew or Ezra SIL is installed."),
        ("and otherwise Geeza Pro, which every Mac has.", "and otherwise Noto Naskh Arabic.", "and otherwise Times New Roman, which every Windows PC has."),

        // Files and disks.
        ("macOS wouldn't let Classica Codex save files for", "Classica Codex wasn't allowed to save files for", "Windows wouldn't let Classica Codex save files for"),
        ("allow access to this one in System Settings → Privacy & Security → Files and Folders.", "check that you can write to this one.", "check that you can write to this one."),
        (", and Time Machine includes it with everything else", string.Empty, string.Empty),
        ("if your Mac's disk is short of room", "if your disk is short of room", "if your PC's disk is short of room"),
        ("your Mac's startup disk", "your main disk", "your main disk"),
        ("Keep it outside iCloud Drive, Dropbox and the like", "Keep it outside Dropbox and the like", "Keep it outside OneDrive, Dropbox and the like"),

        // The Mac itself.
        ("studied on your own Mac", "studied on your own machine", "studied on your own PC"),
        ("another Mac, a PC", "another machine, a PC", "another PC"),
        ("The Mac App", "This App", "This App"),
        ("as the Windows app, reading the same library file, with a Mac interface of its own",
            "as the Windows app, reading the same library file, with an interface of its own",
            "as the classic Windows app, reading the same library file, with an interface of its own"),
        ("On a genuine first run the Mac opens on", "On a genuine first run the app opens on", "On a genuine first run the app opens on"),
        ("the Mac brings its structure up to date the same way the Windows app does",
            "this app brings its structure up to date the same way the Windows app does",
            "this app brings its structure up to date the same way the classic Windows app does"),
        ("Already have a library on the PC? It is one file, and the Mac reads it as it is",
            "Already have a library on the PC? It is one file, and this app reads it as it is",
            "Already have a library? It is one file, and this app reads it as it is"),
        ("Copy it across (AirDrop, a USB drive, a shared folder)", "Copy it across (a USB drive, a shared folder)", "Copy it across (a USB drive, a shared folder)"),
        (", as in any Mac app.", ".", "."),
        (", as in Safari,", ",", ","),
        (" - the same keys Safari uses, for the same reason.", ".", "."),

        // The other app, on the system where there are two.
        ("the Windows app ships with", "the Windows app ships with", "the classic Windows app ships with"),
        ("the same file name the Windows app uses", "the same file name the Windows app uses", "the same file the classic Windows app uses"),
        ("copied from the PC", "copied from the PC", "copied from another PC")
    };
}
