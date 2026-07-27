using ClassicaCodex.Data;

namespace ClassicaCodex.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

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
