using System.Security.Cryptography;
using System.Text;

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
/// The keys are encrypted at rest with Windows' own DPAPI, scoped to the
/// current user - the same mechanism Windows uses for saved browser
/// passwords. Credential Manager would be the more thorough answer, but
/// DPAPI is two lines and removes the thing that actually mattered: a key
/// sitting in readable plaintext at a predictable path, where any process
/// running as this user - or anything that ever syncs or backs up
/// %LocalAppData% - could pick it up by accident. What it does not defend
/// against is code already running as this user that means to go looking;
/// nothing short of a prompt-per-use would, and that isn't the tradeoff a
/// reading tool should make.
///
/// Beyond that, the same rule as before still holds: neither key ends up in
/// the SQLite database, in logs, or in anything Export produces.
/// </summary>
public static class TranslationSettings
{
    /// <summary>
    /// Marks a line as DPAPI-encrypted base64 rather than a legacy plaintext
    /// key. An API key won't start with this by coincidence, and an explicit
    /// marker means the upgrade path is a check rather than a guess about
    /// whether base64 decoding "looked like" it worked.
    /// </summary>
    private const string EncryptedPrefix = "dpapi:";

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "translate-settings.txt");

    // Every property below used to re-read and re-parse the file on each
    // access, so a single "should I confirm before sending?" check cost three
    // separate reads of the same three lines. Cached instead, and invalidated
    // by Save - this process is the only writer.
    private static Settings? _cached;
    private static readonly object CacheLock = new();

    private sealed record Settings(string? AnthropicKey, string? GeminiKey, bool AlwaysConfirm);

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

        var anthropic = NullIfEmpty(anthropicKey?.Trim());
        var gemini = NullIfEmpty(geminiKey?.Trim());

        // Three plain lines, deliberately not JSON - nothing here needs a
        // parser, and a key that happened to contain a stray quote or brace
        // shouldn't be able to corrupt the file. The key lines are ciphertext
        // in base64, which is line-safe by construction.
        File.WriteAllLines(SettingsFile, new[]
        {
            Protect(anthropic),
            Protect(gemini),
            alwaysConfirm ? "confirm" : "noconfirm"
        });

        lock (CacheLock)
        {
            _cached = new Settings(anthropic, gemini, alwaysConfirm);
        }
    }

    private static Settings Load()
    {
        lock (CacheLock)
        {
            return _cached ??= ReadFromDisk();
        }
    }

    private static Settings ReadFromDisk()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new Settings(null, null, true);

            var lines = File.ReadAllLines(SettingsFile);
            var anthropicKey = lines.Length > 0 ? Unprotect(lines[0]) : null;
            var geminiKey = lines.Length > 1 ? Unprotect(lines[1]) : null;
            var alwaysConfirm = lines.Length < 3
                || !string.Equals(lines[2].Trim(), "noconfirm", StringComparison.OrdinalIgnoreCase);

            return new Settings(anthropicKey, geminiKey, alwaysConfirm);
        }
        catch
        {
            // Unreadable preference file - same fallback DbConnectionFactory
            // uses for its own preference file: behave as if unconfigured
            // rather than throwing on startup over a corrupted settings file.
            return new Settings(null, null, true);
        }
    }

    private static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        try
        {
            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return EncryptedPrefix + Convert.ToBase64String(cipher);
        }
        catch (CryptographicException)
        {
            // DPAPI unavailable - it shouldn't be on a normal Windows install,
            // but a locked-down or roaming-profile machine can surprise you.
            // Storing the key unencrypted still beats silently losing it and
            // leaving the person wondering why Translate stopped working, and
            // the read path below handles either form.
            return value;
        }
    }

    private static string? Unprotect(string line)
    {
        if (line.Length == 0) return null;

        // A file written before keys were encrypted - read it as-is. The next
        // Save writes it back encrypted, so this path retires itself.
        if (!line.StartsWith(EncryptedPrefix, StringComparison.Ordinal)) return line;

        try
        {
            var cipher = Convert.FromBase64String(line[EncryptedPrefix.Length..]);
            var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return NullIfEmpty(Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Encrypted by a different Windows user, or the file was copied
            // from another machine - DPAPI can't reverse either. Treat it as
            // "no key configured" so Settings shows an empty box to re-enter,
            // rather than handing a garbled string to the API and surfacing a
            // confusing 401.
            return null;
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;
}
