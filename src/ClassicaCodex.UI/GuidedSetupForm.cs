using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using LibGit2Sharp;

namespace ClassicaCodex.UI;

/// <summary>
/// One step at a time, plain language, no repo URLs or file paths on
/// screen - the front door for someone who just wants ClassicaCodex to
/// work and has never seen a destination folder in their life.
///
/// Does the exact same downloading and ingesting SetupWizardForm does, via
/// the same SetupDataSourceCatalog - this form is a different way of
/// presenting that work, not a different implementation of it. Advanced
/// Setup (the original all-at-once form) stays one click away throughout,
/// for anyone who wants to point at an existing folder or skip around.
/// </summary>
public class GuidedSetupForm : Form
{
    public event Action? CorpusChanged;

    private readonly AuthorRepository _authorRepo = new();
    private readonly ArtifactRepository _artifactRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();
    private readonly WordIndexRepository _wordIndexRepo = new();
    private readonly List<SetupDataSource> _sources;

    // Step 0 = welcome; DatabaseStepIndex = database location; then one
    // step per data source; then word index; then finish. Named rather
    // than hardcoded so inserting the database step didn't mean re-deriving
    // every other offset by hand.
    private int _currentStep;
    private const int DatabaseStepIndex = 1;
    private int FirstSourceStepIndex => DatabaseStepIndex + 1;
    private int WordIndexStepIndex => FirstSourceStepIndex + _sources.Count;
    private int FinishStepIndex => WordIndexStepIndex + 1;
    private int TotalSteps => FinishStepIndex + 1;

    private bool _databaseComplete;
    private readonly List<bool> _sourceComplete = new();
    private bool _wordIndexComplete;

    private CancellationTokenSource? _cts;
    private System.Windows.Forms.Timer? _heartbeat;
    private DateTime _operationStart;

    private Label _stepIndicatorLabel = null!;

    private Panel _welcomePanel = null!;
    private Panel _contentPanel = null!;
    private Panel _finishPanel = null!;

    private PictureBox _statusIcon = null!;
    private Label _titleLabel = null!;
    private Label _descriptionLabel = null!;
    private TextBox _pathBox = null!;
    private Button _browseButton = null!;
    private Button _actionButton = null!;
    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;
    private Label _elapsedLabel = null!;

    private Button _backButton = null!;
    private Button _nextButton = null!;

    public GuidedSetupForm()
    {
        _sources = SetupDataSourceCatalog.Build(_authorRepo, _lemmaRepo, _definitionRepo, _artifactRepo);

        Text = "Set Up Classica Codex";
        AppIcons.ApplyWindowIcon(this, "Settings");
        ClientSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        BuildTopStrip();
        BuildWelcomePanel();
        BuildContentPanel();
        BuildFinishPanel();
        BuildNavBar();

        Load += async (_, _) =>
        {
            await RefreshAllCompletionAsync();
            RenderStep();
        };
        FormClosed += (_, _) => CorpusChanged?.Invoke();
        ReadingTheme.AttachTo(this);
    }

    private void BuildTopStrip()
    {
        _stepIndicatorLabel = new Label { Left = 12, Top = 14, Width = 300, Height = 20, ForeColor = Color.DimGray };
        Controls.Add(_stepIndicatorLabel);
    }

    private void BuildWelcomePanel()
    {
        _welcomePanel = new Panel { Left = 12, Top = 50, Width = 616, Height = 370 };

        var title = new Label
        {
            Text = "Welcome to Classica Codex",
            Left = 0,
            Top = 0,
            Width = 616,
            Height = 36,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold)
        };

        var description = new Label
        {
            Text = "First, a single file needs a place to live - your library, tags, and bookmarks all " +
                   "go there, and it's what the rest of this wizard writes into. After that, a few open " +
                   "data sources need to be downloaded: the ancient texts themselves, dictionaries to look " +
                   "words up in, and some word-form data that makes search smarter. Altogether it's " +
                   "usually a few hundred megabytes and a few minutes, depending on your connection. You " +
                   "only need to do this once.\r\n\r\n" +
                   "Each step below does one thing, with a plain explanation of what it's for and why " +
                   "it's worth waiting for.",
            Left = 0,
            Top = 50,
            Width = 616,
            Height = 170
        };

        var advancedHint = new Label
        {
            Text = "In a hurry, or already have these files somewhere? Every step after the database " +
                   "is optional - clicking Next without running one just moves on, so you can go " +
                   "straight through to Finish having only set up the database, then use Setup Wizard " +
                   "on the main toolbar afterward and choose Advanced Setup for more control over where " +
                   "things go.",
            Left = 0,
            Top = 230,
            Width = 616,
            Height = 70,
            ForeColor = Color.DimGray
        };

        _welcomePanel.Controls.Add(title);
        _welcomePanel.Controls.Add(description);
        _welcomePanel.Controls.Add(advancedHint);
        Controls.Add(_welcomePanel);
    }

    private void BuildContentPanel()
    {
        _contentPanel = new Panel { Left = 12, Top = 50, Width = 616, Height = 370 };

        _statusIcon = new PictureBox { Left = 0, Top = 4, Width = 32, Height = 32, SizeMode = PictureBoxSizeMode.Zoom };
        _titleLabel = new Label
        {
            Left = 44,
            Top = 8,
            Width = 572,
            Height = 30,
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold)
        };
        _descriptionLabel = new Label { Left = 0, Top = 50, Width = 616, Height = 70 };

        // Visible only on the Database step - every other step's action is
        // a single button, no path to choose, which is exactly the point
        // of hiding this everywhere else.
        _pathBox = new TextBox { Left = 0, Top = 130, Width = 470, Height = 24 };
        _browseButton = new Button { Text = "Browse...", Left = 478, Top = 128, Width = 138, Height = 28 };
        _browseButton.Click += (_, _) =>
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

        _actionButton = new Button { Left = 0, Top = 166, Width = 240, Height = 38 };
        _actionButton.Click += async (_, _) => await RunCurrentStepActionAsync();

        _progressBar = new ProgressBar { Left = 0, Top = 214, Width = 616, Height = 22 };
        _statusLabel = new Label { Left = 0, Top = 244, Width = 616, Height = 20 };
        _elapsedLabel = new Label { Left = 0, Top = 268, Width = 616, Height = 20, ForeColor = Color.DimGray };

        _contentPanel.Controls.Add(_statusIcon);
        _contentPanel.Controls.Add(_titleLabel);
        _contentPanel.Controls.Add(_descriptionLabel);
        _contentPanel.Controls.Add(_pathBox);
        _contentPanel.Controls.Add(_browseButton);
        _contentPanel.Controls.Add(_actionButton);
        _contentPanel.Controls.Add(_progressBar);
        _contentPanel.Controls.Add(_statusLabel);
        _contentPanel.Controls.Add(_elapsedLabel);
        Controls.Add(_contentPanel);
    }

    private void BuildFinishPanel()
    {
        _finishPanel = new Panel { Left = 12, Top = 50, Width = 616, Height = 370 };

        var title = new Label
        {
            Text = "You're all set!",
            Left = 0,
            Top = 0,
            Width = 616,
            Height = 36,
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold)
        };
        var description = new Label
        {
            Text = "That's the guided setup - close this and start reading. If you skipped anything, or " +
                   "want to load more later, Setup Wizard is always there from the main toolbar; choose " +
                   "Advanced Setup from there for everything on one screen with more control over where " +
                   "things go.",
            Left = 0,
            Top = 50,
            Width = 616,
            Height = 90
        };

        _finishPanel.Controls.Add(title);
        _finishPanel.Controls.Add(description);
        Controls.Add(_finishPanel);
    }

    private void BuildNavBar()
    {
        _backButton = new Button { Text = "Back", Left = 12, Top = 432, Width = 90, Height = 32 };
        AppIcons.Apply(_backButton, "Back", 16);
        _backButton.Click += (_, _) =>
        {
            _currentStep--;
            RenderStep();
        };

        _nextButton = new Button { Left = 488, Top = 432, Width = 140, Height = 32 };
        AppIcons.Apply(_nextButton, "Forward", 16);
        _nextButton.Click += (_, _) =>
        {
            if (_currentStep == DatabaseStepIndex && !_databaseComplete)
            {
                MessageBox.Show(this,
                    "Set up the database first - everything else in this wizard needs somewhere to write to.",
                    "Database Not Set Up Yet", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_currentStep == FinishStepIndex)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                GoNext();
            }
        };

        Controls.Add(_backButton);
        Controls.Add(_nextButton);
    }

    private void GoNext()
    {
        _currentStep++;
        RenderStep();
    }

    /// <summary>
    /// Re-checks the database for every source and the word index - not
    /// just whichever one is currently on screen, since Advanced Setup (or
    /// a previous step in this same session) could have changed any of
    /// them.
    ///
    /// Checks whether a database is configured at all first: this form can
    /// now be reached with none configured yet (that's the whole point of
    /// the Database step), and every other check needs a database to query
    /// - without this guard they'd throw instead of just reporting "not
    /// done yet" like everything else does.
    /// </summary>
    private async Task RefreshAllCompletionAsync()
    {
        _databaseComplete = DbConnectionFactory.IsConfigured;

        _sourceComplete.Clear();
        if (!_databaseComplete)
        {
            foreach (var _ in _sources) _sourceComplete.Add(false);
            _wordIndexComplete = false;
            return;
        }

        foreach (var source in _sources)
        {
            _sourceComplete.Add(await source.CheckComplete());
        }

        _wordIndexComplete = await _wordIndexRepo.HasDataAsync();
    }

    private void RenderStep()
    {
        var isWelcome = _currentStep == 0;
        var isDatabase = _currentStep == DatabaseStepIndex;
        var isFinish = _currentStep == FinishStepIndex;
        var isContent = !isWelcome && !isFinish;

        _welcomePanel.Visible = isWelcome;
        _finishPanel.Visible = isFinish;
        _contentPanel.Visible = isContent;

        _pathBox.Visible = isDatabase;
        _browseButton.Visible = isDatabase;

        _backButton.Enabled = !isWelcome;
        _nextButton.Text = isWelcome ? "Get Started" : isFinish ? "Start Reading" : "Next";
        _stepIndicatorLabel.Text = isContent
            ? $"Step {_currentStep} of {WordIndexStepIndex}"
            : string.Empty;

        if (!isContent) return;

        if (isDatabase)
        {
            _titleLabel.Text = "Database Location";
            _descriptionLabel.Text =
                "This is the one file everything else in this wizard writes into - your library, tags, " +
                "and bookmarks, plus everything you download next. The default location below works " +
                "fine for almost everyone; only change it if you want your library to live somewhere " +
                "specific.";
            _pathBox.Text = DbConnectionFactory.PreferredDatabasePath;
            _statusIcon.Image = AppIcons.Get(_databaseComplete ? "Complete" : "Error", 32);
            _actionButton.Text = _databaseComplete ? "Reconfigure" : "Prepare Database";
            _statusLabel.Text = _databaseComplete
                ? $"Ready: {DbConnectionFactory.DatabasePath}"
                : "Not set up yet.";
        }
        else
        {
            var stepInSources = _currentStep - FirstSourceStepIndex;
            if (stepInSources < _sources.Count)
            {
                var source = _sources[stepInSources];
                var complete = _sourceComplete[stepInSources];

                _titleLabel.Text = source.Title;
                _descriptionLabel.Text = source.PlainLanguageDescription;
                _statusIcon.Image = AppIcons.Get(complete ? "Complete" : "Error", 32);
                _actionButton.Text = complete ? "Re-download && Re-install" : "Download && Install";
                _statusLabel.Text = complete ? "Already loaded." : "Not loaded yet.";
            }
            else
            {
                _titleLabel.Text = "Build Word Index";
                _descriptionLabel.Text =
                    "Makes searching fast once the texts above are loaded - without it, every search has " +
                    "to scan the whole library from scratch. Run this once everything above is done; safe " +
                    "to run again any time.";
                _statusIcon.Image = AppIcons.Get(_wordIndexComplete ? "Complete" : "Error", 32);
                _actionButton.Text = _wordIndexComplete ? "Rebuild Index" : "Build Word Index";
                _statusLabel.Text = _wordIndexComplete ? "Already built." : "Not built yet.";
            }
        }

        _elapsedLabel.Text = string.Empty;
        _progressBar.Value = 0;
    }

    private async Task RunCurrentStepActionAsync()
    {
        if (_currentStep == DatabaseStepIndex)
        {
            await RunDatabaseSetupAsync();
            return;
        }

        var stepInSources = _currentStep - FirstSourceStepIndex;
        if (stepInSources >= 0 && stepInSources < _sources.Count)
        {
            await RunSourceAsync(_sources[stepInSources]);
        }
        else
        {
            await RunWordIndexAsync();
        }
    }

    /// <summary>
    /// Same two calls SettingsForm's "Prepare Database" button makes -
    /// Configure to remember the path, EnsureSchemaAsync to create the
    /// tables if they aren't there yet (a no-op on a database that already
    /// has them, every statement inside is IF NOT EXISTS-guarded). No
    /// separate confirmation step needed the way SettingsForm has one;
    /// arriving at this specific step already is the deliberate choice.
    /// </summary>
    private async Task RunDatabaseSetupAsync()
    {
        var path = _pathBox.Text.Trim();
        if (path.Length == 0)
        {
            MessageBox.Show(this, "Choose a database location first.", "Nothing to Do",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetNavEnabled(false);
        _statusLabel.Text = "Preparing database...";

        try
        {
            DbConnectionFactory.Configure(path);
            await SchemaInitializer.EnsureSchemaAsync();
            _statusLabel.Text = "Database ready.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Something went wrong - see message.";
            MessageBox.Show(this, DescribeError(ex, "the database"), "Setup Step Failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetNavEnabled(true);
            await RefreshAllCompletionAsync();
            RenderStep();
        }
    }

    private async Task RunSourceAsync(SetupDataSource source)
    {
        SetNavEnabled(false);
        _cts = new CancellationTokenSource();
        _progressBar.Style = ProgressBarStyle.Marquee;
        StartHeartbeat();

        var progress = new Progress<string>(message => _statusLabel.Text = message);

        try
        {
            Directory.CreateDirectory(source.DefaultDestination);

            _statusLabel.Text = "Downloading...";
            if (source.FetchMode == SetupFetchMode.SelfManaged)
            {
                // Nothing to do here - RunIngest below does its own
                // fetching, however many files that takes.
            }
            else if (source.FetchMode == SetupFetchMode.DirectDownload)
            {
                var downloadService = new FileDownloadService();
                var target = Path.Combine(source.DefaultDestination, source.DownloadFileName!);
                await downloadService.DownloadAsync(source.RepoUrl, target, progress, _cts.Token);
            }
            else
            {
                var fetchService = new GitCorpusFetchService();
                var fetchProgress = new Progress<FetchProgress>(p => _statusLabel.Text = p.Message);
                await fetchService.FetchAsync(source.RepoUrl, source.DefaultDestination, fetchProgress, _cts.Token);
            }

            _statusLabel.Text = "Installing...";
            await Task.Run(() => source.RunIngest(source.DefaultDestination, progress, _cts.Token), _cts.Token);

            _statusLabel.Text = $"{source.Title} is ready.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Something went wrong - see message.";
            MessageBox.Show(this, DescribeError(ex, source.Title), "Setup Step Failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            StopHeartbeat();
            _progressBar.Style = ProgressBarStyle.Blocks;
            SetNavEnabled(true);
            await RefreshAllCompletionAsync();
            RenderStep();
        }
    }

    private async Task RunWordIndexAsync()
    {
        SetNavEnabled(false);
        _cts = new CancellationTokenSource();
        _progressBar.Style = ProgressBarStyle.Marquee;
        StartHeartbeat();

        var progress = new Progress<WordIndexProgress>(p =>
        {
            if (p.TotalNodes > 0 && p.Phase == "Indexing")
            {
                var percent = (int)Math.Min(100, p.NodesProcessed * 100 / p.TotalNodes);
                _progressBar.Style = ProgressBarStyle.Blocks;
                _progressBar.Value = percent;
                _statusLabel.Text = $"Indexing... {p.NodesProcessed:N0}/{p.TotalNodes:N0} lines ({percent}%).";
            }
            else
            {
                _progressBar.Style = ProgressBarStyle.Marquee;
                _statusLabel.Text = p.Phase;
            }
        });

        try
        {
            var service = new WordIndexService();
            await Task.Run(() => service.BuildAsync(progress, _cts.Token), _cts.Token);
            _statusLabel.Text = "Word index built.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Something went wrong - see message.";
            MessageBox.Show(this, DescribeError(ex, "the word index"), "Build Failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            StopHeartbeat();
            _progressBar.Style = ProgressBarStyle.Blocks;
            SetNavEnabled(true);
            await RefreshAllCompletionAsync();
            RenderStep();
        }
    }

    private void SetNavEnabled(bool enabled)
    {
        _actionButton.Enabled = enabled;
        _backButton.Enabled = enabled && _currentStep > 0;
        _nextButton.Enabled = enabled;
    }

    /// <summary>
    /// Turns a caught exception into something a non-developer can actually
    /// act on - a plain-language headline plus what to try next, not the
    /// raw exception text. That raw text is exactly right in Advanced
    /// Setup, for the audience that already knows what an HttpRequestException
    /// is; it's the wrong first thing to show here.
    ///
    /// Classified by exception TYPE, not by matching on ex.Message - message
    /// text isn't a stable contract and guessing at wording a library might
    /// use is fragile in a way that silently stops working across versions.
    /// The one thing always trustworthy is that the real message is
    /// preserved below the headline, clearly marked as the technical detail
    /// rather than hidden - honest that something broke, without leading
    /// with a stack trace.
    /// </summary>
    private static string DescribeError(Exception ex, string stepTitle)
    {
        var headline = ex switch
        {
            LibGit2SharpException =>
                $"Couldn't download {stepTitle} - check your internet connection, then try again.",

            UnauthorizedAccessException =>
                $"Windows blocked saving files for {stepTitle}. Try Advanced Setup to pick a different " +
                "folder, or check you have permission to write to this one.",

            IOException =>
                $"Ran into a problem saving {stepTitle} to disk - check you have enough free space, " +
                "then try again.",

            _ =>
                $"Something unexpected went wrong installing {stepTitle}. Trying again sometimes clears " +
                "it; Advanced Setup has more detail if it keeps happening."
        };

        return $"{headline}\r\n\r\nTechnical detail (useful if you ask for help): {ex.GetType().Name}: {ex.Message}";
    }

    /// <summary>Same heartbeat pattern SetupWizardForm uses - "UI thread alive, nothing new to report yet" vs genuinely stuck.</summary>
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
}
