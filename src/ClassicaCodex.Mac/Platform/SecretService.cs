using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// The desktop's keyring on Linux, for the API keys: GNOME Keyring, KWallet or
/// whatever else answers the Secret Service, through the secret-tool command
/// from libsecret.
///
/// A command rather than a library, because libsecret is not on every machine
/// and a missing command is simple to detect: without it <see cref="Keychain"/>
/// says there is nowhere to keep a key, and the key lasts until the app quits.
/// It is never written to a plain file instead. The secret goes to secret-tool
/// on standard input and comes back on standard output, so it never appears
/// on a command line, where another process could read it.
/// </summary>
internal static class SecretService
{
    /// <summary>
    /// Long enough for someone to answer the keyring's unlock prompt, short
    /// enough that a desktop with no one answering at all doesn't hang setup.
    /// </summary>
    private const int TimeoutMilliseconds = 30_000;

    private static readonly Lazy<string?> Tool = new(() => SystemTools.Find("secret-tool"));

    public static bool IsAvailable => OperatingSystem.IsLinux() && Tool.Value != null;

    /// <summary>The secret filed under this service and account, or null if there is none.</summary>
    public static string? Read(string service, string account)
    {
        var (succeeded, output) = Run(new[] { "lookup", "service", service, "account", account }, input: null);
        return succeeded && output.Length > 0 ? output : null;
    }

    /// <summary>Stores or, for an empty secret, removes the entry.</summary>
    /// <returns>False if the keyring refused.</returns>
    public static bool Write(string service, string account, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            // Clearing an entry that isn't there is an exit status of 1, and
            // the wanted state all the same, so the answer is whether anything
            // is left.
            Run(new[] { "clear", "service", service, "account", account }, input: null);
            return Read(service, account) == null;
        }

        return Run(new[] { "store", $"--label={service}: {account}", "service", service, "account", account }, secret).Succeeded;
    }

    private static (bool Succeeded, string Output) Run(IEnumerable<string> arguments, string? input)
    {
        var tool = Tool.Value;
        if (tool == null) return (false, string.Empty);

        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(start);
            if (process == null) return (false, string.Empty);

            if (input != null) process.StandardInput.Write(input);
            process.StandardInput.Close();

            // Read as it runs: a full pipe would otherwise stall secret-tool
            // until the timeout, and the error stream is of no use here.
            var output = process.StandardOutput.ReadToEndAsync();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();

            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill();
                return (false, string.Empty);
            }

            return (process.ExitCode == 0, output.GetAwaiter().GetResult().TrimEnd('\n'));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            return (false, string.Empty);
        }
    }
}
