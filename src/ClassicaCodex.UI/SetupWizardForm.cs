using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Fetches and ingests each external data source in one click, instead of
/// the manual clone-then-point-a-dialog-at-a-folder dance this used to
/// require. Uses GitCorpusFetchService under the hood, which sidesteps the
/// Windows colon-in-filename problem that broke a plain `git clone` on
/// every one of these repos earlier.
/// </summary>
public class SetupWizardForm : Form
{
    private class WizardRow
    {
        // The full source definition, not copied fields - AddRow used to
        // copy Title/RepoUrl/etc. individually, which quietly capped what
        // a row could know about its source; FetchMode and DownloadFileName
        // would have needed two more copied fields, and the next addition
        // two more again.
        public SetupDataSource Source = null!;
        public string Title => Source.Title;
        public Func<string, IProgress<string>, CancellationToken, Task> RunIngest => Source.RunIngest;
        public Func<Task<bool>> CheckComplete => Source.CheckComplete;
        public TextBox DestinationBox = null!;
        public Button ActionButton = null!;
        public Label StatusLabel = null!;
        public PictureBox StatusIcon = null!;
    }

    /// <summary>Raised after a manual ingest, so the library tree can refresh.</summary>
    public event Action? CorpusChanged;

    private readonly List<WizardRow> _rows = new();
    private CancellationTokenSource? _cts;

    // Used only to answer "has this actually been loaded already" for the
    // completion icons below - separate from the ingestion services inside
    // each row's RunIngest, which do the actual writing.
    private readonly AuthorRepository _authorRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();
    private readonly ArtifactRepository _artifactRepo = new();
    private readonly WordIndexRepository _wordIndexRepo = new();

    private PictureBox _wordIndexStatusIcon = null!;

    private ProgressBar _wordIndexProgressBar = null!;
    private Label _wordIndexStatusLabel = null!;
    private Label _wordIndexElapsedLabel = null!;
    private Button _wordIndexButton = null!;
    private System.Windows.Forms.Timer? _heartbeat;
    private DateTime _operationStart;

    public SetupWizardForm()
    {
        Text = "Setup Wizard - Fetch Data Sources";
        AppIcons.ApplyWindowIcon(this, "Settings");
        // ClientSize, not Width/Height (see AboutForm for why), and taller
        // than the old Height=870: the accumulated content here - five
        // AddRow rows, the word-index section, then Close - actually runs
        // to y=890 by the time Close is placed, which past even the old
        // *outer* height before chrome was ever subtracted from it.
        // 910 fit five data-source rows; the World Map Data row added a
        // sixth at 98px each, pushing everything below it down by the same.
        // 910 fit five rows; each added row is another 98px. Eight now -
        // this is getting tall enough that a scrollable panel is the real
        // answer before a ninth gets added.
        ClientSize = new Size(900, 1204);
        StartPosition = FormStartPosition.CenterParent;

        var explainer = new Label
        {
            Left = 12,
            Top = 10,
            Width = 860,
            Height = 54,
            ForeColor = Color.DimGray,
            Text = "Each of these downloads a full open-data repository and ingests it in one step. These are real " +
                   "downloads (the largest run several hundred MB) and can take a few minutes each - only run one " +
                   "at a time. See About for what's licensed how."
        };
        Controls.Add(explainer);

        var y = 74;

        foreach (var source in SetupDataSourceCatalog.Build(_authorRepo, _lemmaRepo, _definitionRepo, _artifactRepo))
        {
            AddRow(ref y, source);
        }

        var manualTitle = new Label
        {
            Text = "Already have the files?",
            Left = 12,
            Top = y,
            Width = 860,
            Font = new Font(Font, FontStyle.Bold)
        };
        var manualDesc = new Label
        {
            Text = "If you've already downloaded these repositories yourself - or keep them somewhere else on disk - " +
                   "you can point straight at those folders instead of downloading again. Same result, no second copy.",
            Left = 12,
            Top = y + 20,
            Width = 860,
            Height = 32,
            ForeColor = Color.DimGray
        };

        var manualIngestButton = new Button { Text = "Ingest Corpus...", Left = 12, Top = y + 56, Width = 150, Height = 30 };
        manualIngestButton.Click += (_, _) =>
        {
            using var ingestForm = new IngestForm();
            ingestForm.ShowDialog(this);
            CorpusChanged?.Invoke();
        };

        var manualLemmaButton = new Button { Text = "Load Lemmas...", Left = 172, Top = y + 56, Width = 150, Height = 30 };
        manualLemmaButton.Click += (_, _) =>
        {
            using var lemmaForm = new LemmaIngestForm();
            lemmaForm.ShowDialog(this);
        };

        var manualHint = new Label
        {
            Text = "Ingest Corpus takes the text repositories' \"data\" folders. Load Lemmas handles lemma data and dictionaries.",
            Left = 332,
            Top = y + 62,
            Width = 540,
            ForeColor = Color.DimGray
        };

        var databaseButton = new Button { Text = "Database Location...", Left = 12, Top = y + 92, Width = 170, Height = 30 };
        databaseButton.Click += (_, _) =>
        {
            using var settingsForm = new SettingsForm();
            if (settingsForm.ShowDialog(this) != DialogResult.OK) return;

            // A different database means an entirely different library.
            CorpusChanged?.Invoke();
            MessageBox.Show(this,
                "Database location updated. The library has been reloaded from the new file.",
                "Database", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var databaseHint = new Label
        {
            Text = "Where the library, tags, and bookmarks are stored. Point at a different file to keep separate libraries.",
            Left = 192,
            Top = y + 98,
            Width = 680,
            ForeColor = Color.DimGray
        };

        AppIcons.Apply(manualIngestButton, "IngestCorpus", 16);
        AppIcons.Apply(manualLemmaButton, "LoadLemmas", 16);
        AppIcons.Apply(databaseButton, "Settings", 16);

        Controls.Add(manualTitle);
        Controls.Add(manualDesc);
        Controls.Add(manualIngestButton);
        Controls.Add(manualLemmaButton);
        Controls.Add(manualHint);
        Controls.Add(databaseButton);
        Controls.Add(databaseHint);

        y += 140;

        _wordIndexStatusIcon = new PictureBox
        {
            Left = 12,
            Top = y + 1,
            Width = 18,
            Height = 18,
            SizeMode = PictureBoxSizeMode.Zoom
        };

        var wordIndexTitle = new Label
        {
            Text = "Build Word Index",
            Left = 36,
            Top = y,
            Width = 836,
            Font = new Font(Font, FontStyle.Bold)
        };
        var wordIndexDesc = new Label
        {
            Text = "Run this once after the text corpora above are loaded - it's what makes lemma-aware search fast. " +
                   "Safe to rerun any time; it always rebuilds from scratch.",
            Left = 12,
            Top = y + 20,
            Width = 860,
            ForeColor = Color.DimGray
        };

        _wordIndexProgressBar = new ProgressBar { Left = 12, Top = y + 44, Width = 860, Height = 22 };
        _wordIndexStatusLabel = new Label { Left = 12, Top = y + 70, Width = 860, Height = 20, Text = "Idle." };
        _wordIndexElapsedLabel = new Label { Left = 12, Top = y + 92, Width = 860, Height = 20, ForeColor = Color.DimGray };

        _wordIndexButton = new Button { Text = "Build Word Index", Left = 12, Top = y + 116, Width = 160, Height = 30 };
        _wordIndexButton.Click += async (_, _) => await BuildWordIndexAsync();

        Controls.Add(_wordIndexStatusIcon);
        Controls.Add(wordIndexTitle);
        Controls.Add(wordIndexDesc);
        Controls.Add(_wordIndexProgressBar);
        Controls.Add(_wordIndexStatusLabel);
        Controls.Add(_wordIndexElapsedLabel);
        Controls.Add(_wordIndexButton);

        y += 156;

        var closeButton = new Button
        {
            Text = "Close",
            Left = 796,
            Top = y,
            Width = 76,
            Height = 30,
            DialogResult = DialogResult.OK
        };
        Controls.Add(closeButton);

        Load += async (_, _) => await RefreshCompletionIconsAsync();
        ReadingTheme.AttachTo(this);
    }

    /// <summary>
    /// Checks the database for each row (and the word index) and sets its
    /// icon accordingly - Complete if that source has actually been loaded,
    /// Error otherwise. Run on open, and again after anything here finishes,
    /// so the icons always reflect what's really in the database rather
    /// than an ephemeral status message that resets the moment this dialog
    /// is closed and reopened.
    /// </summary>
    private async Task RefreshCompletionIconsAsync()
    {
        // Reachable now with no database configured yet - GuidedSetupForm's
        // Advanced Setup button leads here directly from its Welcome screen,
        // before its own Database step has necessarily run. Every check
        // below needs something to query; with nothing configured, every
        // icon is honestly "not done" rather than throwing.
        if (!DbConnectionFactory.IsConfigured)
        {
            foreach (var row in _rows) row.StatusIcon.Image = AppIcons.Get("Error", 18);
            _wordIndexStatusIcon.Image = AppIcons.Get("Error", 18);
            return;
        }

        foreach (var row in _rows)
        {
            var complete = await row.CheckComplete();
            row.StatusIcon.Image = AppIcons.Get(complete ? "Complete" : "Error", 18);
        }

        var wordIndexComplete = await _wordIndexRepo.HasDataAsync();
        _wordIndexStatusIcon.Image = AppIcons.Get(wordIndexComplete ? "Complete" : "Error", 18);
    }

    private async Task BuildWordIndexAsync()
    {
        var confirm = MessageBox.Show(this,
            "Build the word index over every ingested line?\r\n\r\n" +
            "This is what makes lemma-aware search fast - without it, each search scans the whole corpus " +
            "once per word form. It takes a few minutes on a full corpus and can be rebuilt any time.",
            "Build Word Index", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK) return;

        SetAllRowsEnabled(false);
        _wordIndexProgressBar.Style = ProgressBarStyle.Marquee;
        _cts = new CancellationTokenSource();
        StartHeartbeat(_wordIndexElapsedLabel);

        var progress = new Progress<WordIndexProgress>(p =>
        {
            if (p.TotalNodes > 0 && p.Phase == "Indexing")
            {
                var percent = (int)Math.Min(100, p.NodesProcessed * 100 / p.TotalNodes);
                _wordIndexProgressBar.Style = ProgressBarStyle.Blocks;
                _wordIndexProgressBar.Value = percent;
                _wordIndexStatusLabel.Text =
                    $"Indexing... {p.NodesProcessed:N0}/{p.TotalNodes:N0} lines ({percent}%), {p.EntriesWritten:N0} entries written.";
            }
            else
            {
                _wordIndexProgressBar.Style = ProgressBarStyle.Marquee;
                _wordIndexStatusLabel.Text = p.Phase;
            }
        });

        try
        {
            var service = new WordIndexService();
            await Task.Run(() => service.BuildAsync(progress, _cts.Token), _cts.Token);

            _wordIndexStatusLabel.Text = "Word index built.";
            MessageBox.Show(this, "Word index built. Lemma searches should be much faster now.", "Done",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _wordIndexStatusLabel.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _wordIndexStatusLabel.Text = "Failed - see message.";
            MessageBox.Show(this, ex.Message, "Build failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            StopHeartbeat();
            _wordIndexProgressBar.Style = ProgressBarStyle.Blocks;
            SetAllRowsEnabled(true);
            await RefreshCompletionIconsAsync();
        }
    }

    /// <summary>
    /// Ticks on its own regardless of what the build is doing - the actual
    /// diagnostic for telling "UI thread alive but nothing reported yet"
    /// apart from "UI thread genuinely blocked".
    /// </summary>
    private void StartHeartbeat(Label targetLabel)
    {
        _operationStart = DateTime.UtcNow;
        _heartbeat = new System.Windows.Forms.Timer { Interval = 500 };
        _heartbeat.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - _operationStart;
            targetLabel.Text = $"Elapsed: {elapsed:mm\\:ss}";
        };
        _heartbeat.Start();
    }

    private void StopHeartbeat()
    {
        _heartbeat?.Stop();
        _heartbeat?.Dispose();
        _heartbeat = null;
    }

    private void AddRow(ref int y, SetupDataSource source)
    {
        var row = new WizardRow { Source = source };

        // Set once RefreshCompletionIconsAsync actually checks the database
        // (on Load, and again after this row runs) - starts blank rather
        // than guessing, since "hasn't been checked yet" and "checked and
        // missing" are different things and this shouldn't claim the latter.
        row.StatusIcon = new PictureBox
        {
            Left = 12,
            Top = y + 1,
            Width = 18,
            Height = 18,
            SizeMode = PictureBoxSizeMode.Zoom
        };

        var titleLabel = new Label
        {
            Text = source.Title,
            Left = 36,
            Top = y,
            Width = 836,
            Font = new Font(Font, FontStyle.Bold)
        };

        var urlDisplayText = source.DisplayNote == null ? source.RepoUrl : $"{source.RepoUrl}   ({source.DisplayNote})";
        var urlLabel = new Label { Text = urlDisplayText, Left = 12, Top = y + 20, Width = 860, ForeColor = Color.DimGray };

        row.DestinationBox = new TextBox { Left = 12, Top = y + 42, Width = 600, Text = source.DefaultDestination };
        var browseButton = new Button { Text = "Browse...", Left = 620, Top = y + 40, Width = 90, Height = 26 };
        browseButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = row.DestinationBox.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) row.DestinationBox.Text = dialog.SelectedPath;
        };

        row.ActionButton = new Button { Text = "Fetch && Ingest", Left = 718, Top = y + 40, Width = 154, Height = 26 };
        row.ActionButton.Click += async (_, _) => await RunRowAsync(row);

        row.StatusLabel = new Label { Left = 12, Top = y + 72, Width = 860, Height = 20, ForeColor = Color.DarkSlateGray };

        Controls.Add(row.StatusIcon);
        Controls.Add(titleLabel);
        Controls.Add(urlLabel);
        Controls.Add(row.DestinationBox);
        Controls.Add(browseButton);
        Controls.Add(row.ActionButton);
        Controls.Add(row.StatusLabel);

        _rows.Add(row);
        y += 98;
    }

    private async Task RunRowAsync(WizardRow row)
    {
        var destination = row.DestinationBox.Text.Trim();
        if (destination.Length == 0)
        {
            MessageBox.Show(this, "Choose a destination folder first.", "Nothing to do",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetAllRowsEnabled(false);
        _cts = new CancellationTokenSource();

        var progress = new Progress<string>(message => row.StatusLabel.Text = message);

        try
        {
            row.StatusLabel.Text = "Fetching...";

            if (row.Source.FetchMode == SetupFetchMode.SelfManaged)
            {
                // Nothing to do here - RunIngest below does its own
                // fetching, however many files that takes.
            }
            else if (row.Source.FetchMode == SetupFetchMode.DirectDownload)
            {
                var downloadService = new FileDownloadService();
                var target = Path.Combine(destination, row.Source.DownloadFileName!);
                await downloadService.DownloadAsync(row.Source.RepoUrl, target, progress, _cts.Token);
            }
            else
            {
                var fetchService = new GitCorpusFetchService();
                var fetchProgress = new Progress<FetchProgress>(p => row.StatusLabel.Text = p.Message);
                await fetchService.FetchAsync(row.Source.RepoUrl, destination, fetchProgress, _cts.Token);
            }

            row.StatusLabel.Text = "Ingesting...";
            await Task.Run(() => row.RunIngest(destination, progress, _cts.Token), _cts.Token);

            row.StatusLabel.Text = $"Done - {row.Title} is ready.";
        }
        catch (OperationCanceledException)
        {
            row.StatusLabel.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            row.StatusLabel.Text = "Failed - see message.";
            MessageBox.Show(this, ex.Message, "Setup step failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetAllRowsEnabled(true);
            await RefreshCompletionIconsAsync();
        }
    }

    private void SetAllRowsEnabled(bool enabled)
    {
        foreach (var row in _rows)
        {
            row.ActionButton.Enabled = enabled;
        }
        _wordIndexButton.Enabled = enabled;
    }
}
