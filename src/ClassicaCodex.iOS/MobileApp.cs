using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ClassicaCodex.Data;
using ClassicaCodex.Mac;
using ClassicaCodex.Mac.Features.Setup;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Foundation;

namespace ClassicaCodex.iOS;

/// <summary>
/// The Mac's App, started the way iOS needs. The library is a copy of a
/// desktop library placed in the app's Documents folder, which the Files app
/// can see. The main window's no-library page shows if it is not there.
/// </summary>
public sealed class MobileApp : App
{
    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            CrashLog.Install();

            // Here rather than at the first export: the Files app shows a folder
            // made a moment before the jump to it as empty.
            Directory.CreateDirectory(ClassicaCodex.Mac.Common.DeviceFiles.Exports);

            MacShell.OpenInstead = DeviceShell.Open;
            Keychain.StoreInstead = new DeviceKeychain();
            SetupRunner.StayAwake = DeviceScreen.StayAwake;
            SpeechService.EngineInstead = new DeviceSpeech();
            PrepareDownloadFolder();

            TouchGestures.Install();
            TouchTargets.Install(Styles);
            TouchWords.Install();

            var host = new PageHost();
            single.MainView = host;

            var library = MacDataFolder.DeviceLibrary;
            if (File.Exists(library))
            {
                try
                {
                    DbConnectionFactory.Configure(library, remember: false);
                    await SchemaInitializer.EnsureSchemaAsync();
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex, "opening the library on iPad");
                }
            }

            new MainWindow().Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Settles the folder before anything reads it, and keeps it out of iCloud
    /// backups, which would otherwise take everything in Documents. What is in
    /// it can be fetched again. The library beside it is left alone, because
    /// it holds the reader's bookmarks and notes.
    ///
    /// The flag goes on the folder because that covers what is put in it
    /// later, and on every launch because it goes if the folder does.
    /// </summary>
    private static void PrepareDownloadFolder()
    {
        try
        {
            MacDataFolder.AdoptProposalIfUnchosen();
            Directory.CreateDirectory(DataFolderSettings.Root);

            using var folder = NSUrl.FromFilename(DataFolderSettings.Root);
            if (!folder.SetResource(NSUrl.IsExcludedFromBackupKey, NSNumber.FromBoolean(true), out var error))
                CrashLog.Write(new IOException(error?.LocalizedDescription), "keeping the downloads out of backups");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write(ex, "preparing the download folder");
        }
    }
}
