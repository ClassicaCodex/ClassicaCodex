using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ClassicaCodex.Data;

namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// The API keys on Windows: the Windows app's own translate-settings.txt, so
/// that a reader's keys are already here and a key saved here is there too.
///
/// The format is that app's TranslationSettings: three lines, the Anthropic
/// key, the Gemini key and "confirm" or "noconfirm", each key encrypted with
/// DPAPI for the current user and written as "dpapi:" and base64. A line
/// without the prefix is a key from before encryption, read as it stands. A
/// line that won't decrypt came from another account or another machine, and
/// reads as no key, so Settings shows an empty box rather than a garbled key
/// reaching the provider as a confusing 401.
///
/// One difference, for the same reason the keychain and the keyring have none
/// of it: where the Windows app falls back to plain text if DPAPI fails, this
/// says so and writes nothing. The keys already in the file are left there.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsKeyFile
{
    private const string EncryptedPrefix = "dpapi:";

    private static string SettingsFile => Path.Combine(PreferencesFolder.Root, "translate-settings.txt");

    public static (string? Anthropic, string? Gemini, bool Confirm) Read()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return (null, null, true);

            var lines = File.ReadAllLines(SettingsFile);
            var confirm = lines.Length < 3 || !lines[2].Trim().Equals("noconfirm", StringComparison.OrdinalIgnoreCase);

            return (lines.Length > 0 ? Unprotect(lines[0]) : null, lines.Length > 1 ? Unprotect(lines[1]) : null, confirm);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable preference is an unconfigured one, as in the Windows app.
            return (null, null, true);
        }
    }

    /// <returns>False if a key could not be encrypted, or the file could not be written.</returns>
    public static bool Write(string? anthropic, string? gemini, bool alwaysConfirm)
    {
        try
        {
            Directory.CreateDirectory(PreferencesFolder.Root);

            var lines = new string[3];
            var existing = File.Exists(SettingsFile) ? File.ReadAllLines(SettingsFile) : Array.Empty<string>();
            Array.Copy(existing, lines, Math.Min(existing.Length, 2));
            lines[2] = alwaysConfirm ? "confirm" : "noconfirm";

            var encrypted = true;
            try
            {
                lines[0] = Protect(anthropic);
                lines[1] = Protect(gemini);
            }
            catch (CryptographicException)
            {
                encrypted = false;
            }

            File.WriteAllLines(SettingsFile, lines.Select(line => line ?? string.Empty));
            return encrypted;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return EncryptedPrefix + Convert.ToBase64String(cipher);
    }

    private static string? Unprotect(string line)
    {
        if (line.Length == 0) return null;
        if (!line.StartsWith(EncryptedPrefix, StringComparison.Ordinal)) return line;

        try
        {
            var cipher = Convert.FromBase64String(line[EncryptedPrefix.Length..]);
            var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return plain.Length == 0 ? null : Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
