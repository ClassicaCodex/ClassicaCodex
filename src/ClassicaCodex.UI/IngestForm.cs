using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

public class IngestForm : ScaledForm
{
    private readonly TextBox _greekPathBox;
    private readonly TextBox _latinPathBox;
    private readonly Button _browseGreekButton;
    private readonly Button _browseLatinButton;
    private readonly Button _startButton;
    private readonly Button _cancelButton;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private readonly Label _elapsedLabel;
    private System.Windows.Forms.Timer? _heartbeat;
    private DateTime _operationStart;

    private CancellationTokenSource? _cts;

    public IngestForm()
    {
        Text = "Ingest Perseus Corpus";
        AppIcons.ApplyWindowIcon(this, "IngestCorpus");
        Width = 620;
        Height = 350;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var greekLabel = new Label { Text = "canonical-greekLit \\data folder:", Left = 16, Top = 16, Width = 300 };
        _greekPathBox = new TextBox { Left = 16, Top = 40, Width = 460 };
        _browseGreekButton = new Button { Text = "Browse...", Left = 484, Top = 38, Width = 100 };
        _browseGreekButton.Click += (_, _) => BrowseFor(_greekPathBox);

        var latinLabel = new Label { Text = "canonical-latinLit \\data folder:", Left = 16, Top = 76, Width = 300 };
        _latinPathBox = new TextBox { Left = 16, Top = 100, Width = 460 };
        _browseLatinButton = new Button { Text = "Browse...", Left = 484, Top = 98, Width = 100 };
        _browseLatinButton.Click += (_, _) => BrowseFor(_latinPathBox);

        var hint = new Label
        {
            Left = 16,
            Top = 136,
            Width = 570,
            Height = 40,
            ForeColor = Color.DimGray,
            Text = "Clone the repos first (git clone https://github.com/PerseusDL/canonical-greekLit " +
                   "and .../canonical-latinLit), then point each box at that repo's \"data\" folder."
        };

        _progressBar = new ProgressBar { Left = 16, Top = 185, Width = 570, Height = 22 };
        _statusLabel = new Label { Left = 16, Top = 212, Width = 570, Height = 24, Text = "Idle." };

        // Ticks on its own regardless of what the ingest code is doing -
        // this is the actual diagnostic. If this keeps counting up smoothly
        // the whole time, the UI thread is alive and the gap is somewhere
        // not being reported; if this freezes too, the UI thread itself is
        // genuinely blocked. Either way, it answers the question instead of
        // guessing at it.
        _elapsedLabel = new Label { Left = 16, Top = 238, Width = 570, Height = 20, ForeColor = Color.DimGray };

        _startButton = new Button { Text = "Start Ingest", Left = 16, Top = 270, Width = 140, Height = 32 };
        _startButton.Click += StartButton_Click;

        _cancelButton = new Button { Text = "Cancel", Left = 164, Top = 270, Width = 100, Height = 32, Enabled = false };
        _cancelButton.Click += (_, _) => _cts?.Cancel();

        Controls.Add(greekLabel);
        Controls.Add(_greekPathBox);
        Controls.Add(_browseGreekButton);
        Controls.Add(latinLabel);
        Controls.Add(_latinPathBox);
        Controls.Add(_browseLatinButton);
        Controls.Add(hint);
        Controls.Add(_progressBar);
        Controls.Add(_statusLabel);
        Controls.Add(_elapsedLabel);
        Controls.Add(_startButton);
        Controls.Add(_cancelButton);
    }

    private void StartHeartbeat()
    {
        _operationStart = DateTime.UtcNow;
        _heartbeat = new System.Windows.Forms.Timer { Interval = 500 };
        _heartbeat.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - _operationStart;
            _elapsedLabel.Text = $"Elapsed: {elapsed:mm\\:ss}";
        };
        _heartbeat.Start();
    }

    private void StopHeartbeat()
    {
        _heartbeat?.Stop();
        _heartbeat?.Dispose();
        _heartbeat = null;
    }

    private void BrowseFor(TextBox target)
    {
        using var dialog = new FolderBrowserDialog();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        var repoPaths = new List<(string DataPath, string Namespace)>();
        if (!string.IsNullOrWhiteSpace(_greekPathBox.Text)) repoPaths.Add((_greekPathBox.Text.Trim(), "greekLit"));
        if (!string.IsNullOrWhiteSpace(_latinPathBox.Text)) repoPaths.Add((_latinPathBox.Text.Trim(), "latinLit"));

        if (repoPaths.Count == 0)
        {
            MessageBox.Show(this, "Point at least one repo's data folder first.", "Nothing to ingest",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _startButton.Enabled = false;
        _cancelButton.Enabled = true;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _cts = new CancellationTokenSource();
        StartHeartbeat();

        var progress = new Progress<IngestProgress>(p =>
        {
            _statusLabel.Text = $"{p.CurrentAuthor}: {p.CurrentWork}  ({p.WorksProcessed}/{p.TotalWorks} textgroups)";
        });

        try
        {
            var service = new PerseusIngestService();
            await Task.Run(() => service.IngestAsync(repoPaths, progress, _cts.Token), _cts.Token);

            // Both lists, and through the same reporter the Guided path uses,
            // rather than a second hand-built message that can drift from it.
            var outcome = IngestOutcome.From(service.FailedFiles, service.RecoveredWithoutCatalog, service.FilesAttempted);

            _statusLabel.Text = outcome.Describe("Ingest");

            // ShowIfAny decides for itself whether the skips are worth a box -
            // see IngestOutcome.SkipsAreWorthInterrupting. When they are not,
            // this still confirms the run finished, because a manual ingest was
            // started by a button and wants an answer.
            if (outcome.SkipsAreWorthInterrupting)
            {
                SetupSkipReport.ShowIfAny(this, "Ingest", outcome);
            }
            else
            {
                SetupSkipReport.ShowIfAny(this, "Ingest", outcome);
                MessageBox.Show(this, outcome.Describe("Ingest"), "Done",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Failed - see message.";
            MessageBox.Show(this, ex.Message, "Ingest failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            StopHeartbeat();
            _progressBar.Style = ProgressBarStyle.Blocks;
            _startButton.Enabled = true;
            _cancelButton.Enabled = false;
        }
    }
}
