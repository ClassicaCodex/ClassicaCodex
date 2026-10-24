using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Entry points for setup, called from the File menu, the tools list and the
/// welcome page - and by the shell straight after it creates a new library.
///
/// The Windows "Setup Wizard" button: the choice between Guided and Advanced,
/// then the chosen one, as a dialog over the reader. Modal, as on Windows,
/// and not only for tidiness: a setup step writes into the open library for
/// up to an hour, and the reader's own Open Library and New Library would
/// otherwise be one menu away from pointing the rest of that ingest at a
/// different file.
/// </summary>
public static class SetupFeature
{
    private static FeatureWindow? _open;

    public static void Open(IReaderHost host) => _ = OpenAsync(host);

    /// <summary>Opens setup and returns when it has closed.</summary>
    internal static async Task OpenAsync(IReaderHost host)
    {
        // One setup at a time. Two running side by side would be two ingests
        // into one library.
        if (_open is { IsVisible: true })
        {
            _open.Activate();
            return;
        }

        try
        {
            var mode = await SetupChoiceWindow.AskAsync(host.Window, LibraryIsEmpty(host));
            if (mode == null) return;

            _open = mode == SetupMode.Guided ? new GuidedSetupWindow(host) : new AdvancedSetupWindow(host);
            await _open.ShowDialog(host.Window);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "running setup");
            await Alert.Error(host.Window, "Setup", ex.Message);
        }
        finally
        {
            _open = null;
        }

        // Each step reloads the library as it finishes; this is for whatever
        // finished without one - a library file chosen, a step cancelled.
        if (DbConnectionFactory.IsConfigured) await host.ReloadLibraryAsync();
    }

    /// <summary>A library with nothing in it - or no library at all - is the case Guided exists for.</summary>
    private static bool LibraryIsEmpty(IReaderHost host) =>
        !DbConnectionFactory.IsConfigured || (host.Window is MainWindow main && main.LibraryWorkCount == 0);
}
