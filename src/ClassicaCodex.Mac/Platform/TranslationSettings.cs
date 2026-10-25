namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// The AI settings, with the same shape as the Windows class of the same
/// name so the ported windows read alike: two keys and whether to confirm
/// before anything is sent.
///
/// The keys live in the keychain (see <see cref="Keychain"/>), under
/// "Classica Codex" in Keychain Access. The confirmation switch is not a
/// secret and stays in a plain file beside the other preferences; absent
/// means confirm, so a fresh install asks before it sends anything.
///
/// Read once and cached, as on Windows: every send asks whether to confirm,
/// and a keychain lookup per question would be felt.
/// </summary>
public static class TranslationSettings
{
    private const string AnthropicAccount = "Anthropic API key";
    private const string GeminiAccount = "Google Gemini API key";

    private static (string? Anthropic, string? Gemini, bool Confirm)? _cached;
    private static readonly object CacheLock = new();

    private static string ConfirmFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "translate-confirm.txt");

    public static string? AnthropicApiKey => Load().Anthropic;

    public static string? GeminiApiKey => Load().Gemini;

    public static bool AlwaysConfirmBeforeSending => Load().Confirm;

    /// <summary>Whether keys can be kept at all on this machine.</summary>
    public static bool CanStoreKeys => Keychain.IsAvailable;

    /// <returns>False if the keychain refused either key.</returns>
    public static bool Save(string? anthropicKey, string? geminiKey, bool alwaysConfirm)
    {
        var anthropic = NullIfEmpty(anthropicKey?.Trim());
        var gemini = NullIfEmpty(geminiKey?.Trim());

        var stored = Keychain.Write(AnthropicAccount, anthropic) & Keychain.Write(GeminiAccount, gemini);

        try
        {
            var directory = Path.GetDirectoryName(ConfirmFile);
            if (directory != null) Directory.CreateDirectory(directory);
            File.WriteAllText(ConfirmFile, alwaysConfirm ? "confirm" : "noconfirm");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The switch still applies for this session.
        }

        lock (CacheLock)
        {
            _cached = (anthropic, gemini, alwaysConfirm);
        }

        return stored;
    }

    private static (string? Anthropic, string? Gemini, bool Confirm) Load()
    {
        lock (CacheLock)
        {
            return _cached ??= (Keychain.Read(AnthropicAccount), Keychain.Read(GeminiAccount), ReadConfirm());
        }
    }

    private static bool ReadConfirm()
    {
        try
        {
            return !File.Exists(ConfirmFile)
                   || !File.ReadAllText(ConfirmFile).Trim().Equals("noconfirm", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
