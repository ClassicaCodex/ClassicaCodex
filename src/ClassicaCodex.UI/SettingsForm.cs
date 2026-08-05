using ClassicaCodex.Data;

namespace ClassicaCodex.UI;

/// <summary>
/// SQLite means there's no server, no instance name, no authentication to
/// configure - just a file on disk. This dialog used to ask for a full SQL
/// Server connection string; now it's a file path with a sensible default,
/// which is most of the point of this migration.
/// </summary>
public class SettingsForm : Form
{
    private readonly TextBox _pathBox;
    private readonly Button _connectButton;
    private readonly Label _statusLabel;

    public SettingsForm()
    {
        Text = "Classica Codex - Database Location";
        AppIcons.ApplyWindowIcon(this, "Settings");
        Width = 600;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Text = "Database file (created automatically if it doesn't exist yet):",
            Left = 16,
            Top = 16,
            Width = 550
        };

        // The remembered location, not the built-in default - reopening this
        // from Setup Wizard should show where the database actually is.
        _pathBox = new TextBox
        {
            Left = 16,
            Top = 40,
            Width = 450,
            Text = DbConnectionFactory.PreferredDatabasePath
        };

        var browseButton = new Button { Text = "Browse...", Left = 474, Top = 38, Width = 90 };
        browseButton.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
                FileName = Path.GetFileName(_pathBox.Text),
                InitialDirectory = Path.GetDirectoryName(_pathBox.Text),
                OverwritePrompt = false,
                Title = "Choose or create a database file"
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dialog.FileName;
        };

        _statusLabel = new Label
        {
            Left = 16,
            Top = 100,
            Width = 550,
            Height = 40,
            ForeColor = Color.DarkRed,
            Text = string.Empty
        };

        _connectButton = new Button
        {
            Text = "Prepare Database",
            Left = 16,
            Top = 145,
            Width = 180,
            Height = 32
        };
        _connectButton.Click += ConnectButton_Click;

        Controls.Add(label);
        Controls.Add(_pathBox);
        Controls.Add(browseButton);
        Controls.Add(_statusLabel);
        Controls.Add(_connectButton);

        WindowShortcuts.CloseOnEscape(this);
    }

    private async void ConnectButton_Click(object? sender, EventArgs e)
    {
        _connectButton.Enabled = false;
        _statusLabel.ForeColor = Color.Black;
        _statusLabel.Text = "Preparing database...";

        try
        {
            DbConnectionFactory.Configure(_pathBox.Text.Trim());

            // Builds the schema if it doesn't exist yet - every CREATE
            // statement inside is IF NOT EXISTS-guarded, so it's a no-op on
            // every run after the first.
            await SchemaInitializer.EnsureSchemaAsync();

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.DarkRed;
            _statusLabel.Text = $"Couldn't prepare the database: {ex.Message}";
            _connectButton.Enabled = true;
        }
    }
}
