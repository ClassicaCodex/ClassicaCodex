namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// What the app says on a system that is not a Mac. The windows and the Help
/// are written the way a Mac does things - ⌘ keys, the Finder, the keychain -
/// and <see cref="Say"/> rewords a string for Linux as it is shown, so the Mac
/// text stays the one that is written and kept up to date.
///
/// One table for both, so the Help and the window it describes cannot come to
/// disagree. Longer phrases are listed before the shorter ones inside them.
/// The Help's own passages are checked against what is left afterwards by the
/// tests, which fail on any Mac word the table has let through.
///
/// CC_MAC_LOOK keeps the Mac wording on any machine, as it keeps the Mac
/// title bar: it is how the headless tests see the same window on Linux.
/// </summary>
internal static class SystemWords
{
    public static bool IsMac => OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("CC_MAC_LOOK") == "1";

    /// <summary>This text as it should read on the system the app is running on.</summary>
    public static string Say(string text) => IsMac ? text : ForLinux(text);

    internal static string ForLinux(string text)
    {
        foreach (var (mac, linux) in Phrases) text = text.Replace(mac, linux, StringComparison.Ordinal);
        return text;
    }

    private static readonly (string Mac, string Linux)[] Phrases =
    {
        // Keys. The chords go first, so that ⇧⌘ doesn't become "Shift+Ctrl+".
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
        ("Option-Left and Option-Right", "Alt+Left and Alt+Right"),

        // The Finder.
        ("To open any of these in the Finder, choose Go > Go to Folder… (Ctrl+Shift+G) and paste the path. Folders inside ~/Library are otherwise hidden in the Finder.",
            "To open any of these, paste the path into your file manager. Folders whose names begin with a dot, such as ~/.local, are hidden there unless you ask to see them."),
        ("In the Finder, Go > Go to Folder… (Ctrl+Shift+G) takes the path as it is written here.", "Your file manager takes the path as it is written here."),
        ("Show it in the Finder?", "Show it in its folder?"),
        ("Show in Finder", "Show in Folder"),
        ("copying across in the Finder rather than redoing", "copying across rather than redoing"),

        // Following the system.
        ("Following macOS changes with the system - at sunset, if your Mac is set to Auto.", "Following the system changes when your desktop's light or dark setting does."),
        ("Following macOS switches between them with the system, at sunset if you have it set that way.", "Following the system switches between them when your desktop's light or dark setting does."),
        ("Follow macOS", "Follow System"),
        ("Appearance: follows macOS", "Appearance: follows the system"),

        // Where the keys are kept.
        ("Keys are kept in your login keychain, under \"Classica Codex\" in Keychain Access - where macOS keeps every other app's passwords - rather than in a file. Another account on the same Mac can't read them.",
            "Keys are kept in your desktop's keyring, under \"Classica Codex\", through the Secret Service - where other Linux apps keep their passwords - rather than in a file. This needs the secret-tool command, usually in a package called libsecret-tools; without it a key lasts until the app quits, and is never written to a file."),
        ("Saved. The keys are in your login keychain, under “Classica Codex” in Keychain Access.", "Saved. The keys are in your desktop's keyring, under “Classica Codex”."),
        ("The keychain refused to store a key - check Keychain Access and try again.", "The keyring refused to store a key - check that it is unlocked and try again."),
        ("This system has no keychain to keep the keys in, so they apply until Classica Codex quits.", "Keys can't be saved without the secret-tool command, usually in a package called libsecret-tools, so they apply until Classica Codex quits."),

        // Read Aloud.
        ("hear the line in a macOS voice", "hear the line in an espeak-ng voice"),
        ("Speaks the selected passage using whatever voices macOS already has installed - completely offline", "Speaks the selected passage through espeak-ng, if it is installed - completely offline"),
        ("macOS has voices for modern Greek but none for the ancient language", "espeak-ng has a voice for modern Greek but none for the ancient language"),
        ("The voices are the Mac's own, from System Settings > Accessibility > Spoken Content > System voice > Manage Voices. This app can't add voices macOS doesn't already have - add one there and it will appear here.",
            "The voices are espeak-ng's own. Install the espeak-ng package and they appear here; without it, Read Aloud is not offered at all."),
        ("Hands a passage to the voices macOS already has.", "Hands a passage to espeak-ng."),
        ("More voices: System Settings → Accessibility → Spoken Content → System Voice → Manage Voices.", "The voices are espeak-ng's, and are all installed with it."),
        ("Speech is not available on this system.", "Read Aloud needs espeak-ng, which is not installed."),

        // Fonts.
        ("the hieroglyph font that ships with macOS has not caught up with all of them", "Noto Sans Egyptian Hieroglyphs has not caught up with all of them"),
        ("(double-click it to add it with Font Book)", "(copy it into ~/.local/share/fonts)"),
        ("Times New Roman, which every Mac has, unless a font made for pointed Hebrew such as SBL Hebrew or Ezra SIL is installed.", "Noto Serif Hebrew, unless a font made for pointed Hebrew such as SBL Hebrew or Ezra SIL is installed."),
        ("and otherwise Geeza Pro, which every Mac has.", "and otherwise Noto Naskh Arabic."),

        // Files and disks.
        ("macOS wouldn't let Classica Codex save files for", "Classica Codex wasn't allowed to save files for"),
        ("allow access to this one in System Settings → Privacy & Security → Files and Folders.", "check that you can write to this one."),
        (", and Time Machine includes it with everything else", string.Empty),
        ("if your Mac's disk is short of room", "if your disk is short of room"),
        ("Keep it outside iCloud Drive, Dropbox and the like", "Keep it outside Dropbox and the like"),

        // The Mac itself.
        ("studied on your own Mac", "studied on your own machine"),
        ("another Mac, a PC", "another machine, a PC"),
        ("The Mac App", "This App"),
        ("with a Mac interface of its own", "with an interface of its own"),
        ("On a genuine first run the Mac opens on", "On a genuine first run the app opens on"),
        ("the Mac brings its structure up to date", "this app brings its structure up to date"),
        ("and the Mac reads it as it is", "and this app reads it as it is"),
        ("Copy it across (AirDrop, a USB drive, a shared folder)", "Copy it across (a USB drive, a shared folder)"),
        (", as in any Mac app.", "."),
        (", as in Safari,", ","),
        (" - the same keys Safari uses, for the same reason.", ".")
    };
}
