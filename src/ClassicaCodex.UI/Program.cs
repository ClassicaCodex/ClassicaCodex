using ClassicaCodex.Data;

namespace ClassicaCodex.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Before anything that can throw, and before Application.Run: most of
        // this app's event handlers are async lambdas, and an exception
        // escaping one of those ends the process silently unless something is
        // listening. See CrashReporter.
        CrashReporter.Install();

        // The icons ship in a folder beside the executable, and AppIcons hands
        // back null for every one of them when that folder is not there - so
        // the toolbar comes up as nineteen blank squares with nothing to say
        // why. Every button's text is cleared once its icon is applied, and
        // the buttons are flat, so there is not even an outline to hover.
        //
        // Reachable by the commonest mistake there is: running the executable
        // from inside Explorer's view of the ZIP, or dragging just the
        // executable out. The build is a single-file bundle that carries every
        // library it needs, so it starts perfectly well - it simply starts
        // looking broken, and the reader has nothing to search for. Said here,
        // once, and then the app carries on, because nothing else needs the
        // folder.
        var iconsFolder = Path.Combine(AppContext.BaseDirectory, "Icons");
        if (!Directory.Exists(iconsFolder))
        {
            MessageBox.Show(
                "The Icons folder that ships beside ClassicaCodex.UI.exe is missing, so the toolbar "
                + "buttons will be blank.\r\n\r\n"
                + "This usually means the download was not fully extracted. Extract the whole ZIP to a "
                + "folder and run ClassicaCodex.UI.exe from there.\r\n\r\n"
                + $"Looked for:\r\n{iconsFolder}",
                "Classica Codex", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Only ask where the database should live when there isn't one to
        // open - a first run, or the file having been moved or deleted.
        // Otherwise go straight in; the location is still changeable any
        // time from Setup Wizard.
        if (!TryOpenExistingDatabase())
        {
            using var guidedSetup = new GuidedSetupForm();
            guidedSetup.ShowDialog();

            // The wizard's own DialogResult isn't the right gate here - it's
            // reachable via Finish having only done the database step and
            // skipped everything else, which is a perfectly valid way to
            // leave it. The one thing that actually has to be true to
            // continue is that something got configured, regardless of how
            // the wizard was exited (Finish, the X button, Escape).
            if (!DbConnectionFactory.IsConfigured)
            {
                return; // user closed the wizard without setting up a database
            }
        }

        Application.Run(new MainForm());
    }

    private static bool TryOpenExistingDatabase()
    {
        try
        {
            if (!DbConnectionFactory.TryConfigureFromPreferred()) return false;

            // Cheap on an existing database - every statement is guarded
            // with IF NOT EXISTS - and it means a schema added by a later
            // version gets created without the user having to do anything.
            SchemaInitializer.EnsureSchemaAsync().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            // The remembered database exists but couldn't be opened - a
            // locked file, a permissions change, a corrupt file. Say so
            // plainly and fall back to the location dialog rather than
            // failing to start with no explanation.
            MessageBox.Show(
                $"The database at:\r\n\r\n{DbConnectionFactory.PreferredDatabasePath}\r\n\r\n" +
                $"couldn't be opened:\r\n\r\n{ex.Message}\r\n\r\nChoose a location to continue.",
                "Classica Codex", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }
}
