namespace ClassicaCodex.UI;

/// <summary>
/// The one thing Translate's AI options need outside the SQLite database:
/// optional API keys for the two providers it can use, and whether to keep
/// confirming before every send. Stored as a small local text file under
/// %LocalAppData%, the same place and same plain-file approach
/// DbConnectionFactory already uses for the remembered database path - this
/// is machine-local configuration, not corpus data, so it doesn't belong in
/// the (portable, shareable) database file.
///
/// Two providers, deliberately kept side by side rather than one replacing
/// the other, because they have genuinely different tradeoffs:
///
///  - Anthropic (Claude) - paid, no free tier, billed by usage. Doesn't
///    train on API traffic.
///  - Google (Gemini, via AI Studio) - a real, indefinite free tier with no
///    card required, which is exactly what makes it worth having here for
///    anyone who doesn't want to (or can't) pay. The tradeoff: Google's free
///    tier may use what you send to improve their models - see Help before
///    assuming "free" means "private" too.
///
/// Both keys sit in plain text on disk. Windows Credential Manager would be
/// the more careful answer, but that's more machinery than a single-user
/// hobby app's threat model needs - the thing that actually matters is that
/// neither key ends up in the SQLite database, in logs, or in anything
/// Export produces, and neither does.
/// </summary>
public static class TranslationSettings
{
    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "translate-settings.txt");

    /// <summary>Null when no Anthropic key has been configured yet.</summary>
    public static string? AnthropicApiKey => Load().AnthropicKey;

    /// <summary>Null when no Google AI Studio key has been configured yet.</summary>
    public static string? GeminiApiKey => Load().GeminiKey;

    /// <summary>
    /// True by default - deliberately opt-out, not opt-in, since sending
    /// text to a third-party API (either one) is the one thing in this app
    /// that isn't offline, and that should stay visible until someone
    /// decides otherwise.
    /// </summary>
    public static bool AlwaysConfirmBeforeSending => Load().AlwaysConfirm;

    public static void Save(string? anthropicKey, string? geminiKey, bool alwaysConfirm)
    {
        var directory = Path.GetDirectoryName(SettingsFile)!;
        Directory.CreateDirectory(directory);

        // Three plain lines, deliberately not JSON - nothing here needs a
        // parser, and a key that happened to contain a stray quote or brace
        // shouldn't be able to corrupt the file.
        File.WriteAllLines(SettingsFile, new[]
        {
            anthropicKey?.Trim() ?? string.Empty,
            geminiKey?.Trim() ?? string.Empty,
            alwaysConfirm ? "confirm" : "noconfirm"
        });
    }

    private static (string? AnthropicKey, string? GeminiKey, bool AlwaysConfirm) Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return (null, null, true);

            var lines = File.ReadAllLines(SettingsFile);
            var anthropicKey = lines.Length > 0 && lines[0].Length > 0 ? lines[0] : null;
            var geminiKey = lines.Length > 1 && lines[1].Length > 0 ? lines[1] : null;
            var alwaysConfirm = lines.Length < 3
                || !string.Equals(lines[2].Trim(), "noconfirm", StringComparison.OrdinalIgnoreCase);
            return (anthropicKey, geminiKey, alwaysConfirm);
        }
        catch
        {
            // Unreadable preference file - same fallback DbConnectionFactory
            // uses for its own preference file: behave as if unconfigured
            // rather than throwing on startup over a corrupted settings file.
            return (null, null, true);
        }
    }
}
