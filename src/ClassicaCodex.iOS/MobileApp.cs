using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ClassicaCodex.Data;
using ClassicaCodex.Mac;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.iOS;

/// <summary>
/// The Mac's App, started the way iOS needs. The library is a copy of a
/// desktop library placed in the app's Documents folder, which the Files app
/// can see. The main window's no-library page shows if it is not there.
/// </summary>
public sealed class MobileApp : App
{
    private const string LibraryFileName = "classicacodex.db";

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            TouchGestures.Install();

            var host = new PageHost();
            single.MainView = host;

            var library = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), LibraryFileName);
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
}
