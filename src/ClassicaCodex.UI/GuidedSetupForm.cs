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
public class GuidedSetupForm : ScaledForm
{
    public event Action? CorpusChanged;

    private readonly AuthorRepository _authorRepo = new();
    private readonly ArtifactRepository _artifactRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();
    private readonly WordIndexRepository _wordIndexRepo = new();
    // Not readonly: every source's destination is computed from the download
    // folder when the catalogue is built, so choosing a different folder means
    // building it again - see RebuildSources.
    private List<SetupDataSource> _sources;

    // Step 0 = welcome; DatabaseStepIndex = database location; then one
    // step per data source; then word index; then finish. Named rather
    // than hardcoded so inserting the database step didn't mean re-deriving
    // every other offset by hand.
    private int _currentStep;
    private const int DatabaseStepIndex = 1;

    // Straight after the database and before anything is fetched, because it
    // decides where everything fetched afterwards lands. Asking later would
    // mean asking someone to move gigabytes they had already downloaded.
    private const int DataFolderStepIndex = DatabaseStepIndex + 1;
    private int FirstSourceStepIndex => DataFolderStepIndex + 1;
    private int WordIndexStepIndex => FirstSourceStepIndex + _sources.Count;
    private int FinishStepIndex => WordIndexStepIndex + 1;
    private int TotalSteps => FinishStepIndex + 1;

    private bool _databaseComplete;
    private bool _dataFolderComplete;
    private readonly List<bool> _sourceComplete = new();
    private bool _wordIndexComplete;
    private long _indexedLines;
    private long _totalLines;

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
    private Panel _descriptionScroll = null!;
    private TextBox _pathBox = null!;
    private Button _browseButton = null!;
    private Button _actionButton = null!;
    private Button _secondaryButton = null!;
    private LinkLabel[] _sourceLinks = null!;
    private Label _readinessLabel = null!;
    private ProgressBar _progressBar = null!;
    private Label _statusLabel = null!;
    private Label _elapsedLabel = null!;
    private TextBox _outputBox = null!;

    private Button _backButton = null!;
    private Button _nextButton = null!;

    public GuidedSetupForm()
    {
        _sources = SetupDataSourceCatalog.Build(_authorRepo, _lemmaRepo, _definitionRepo, _artifactRepo, _editionRepo);

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
            // Rendered before the counts, not after. Only the tick marks
            // depend on them; the step's layout does not, and asking the
            // library whether its word index is up to date means counting
            // distinct rows in a 70-million-row table - twenty-four seconds
            // on a full library, warm. Rendering afterwards left the wizard
            // on screen for all of that with every panel visible at once,
            // stacked, which is what a first-time reader saw when they
            // clicked Setup: something indistinguishable from a broken
            // window.
            //
            // Rendering twice costs a few milliseconds and the flags are
            // false-by-default, so the first pass shows the right step with
            // no ticks and the second fills them in.
            RenderStep();
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
        _welcomePanel = new Panel { Left = 12, Top = 50, Width = 616, Height = 370, Visible = false };

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
                   "usually about 9 gigabytes, and the better part of an hour on a decent connection - " +
                   "mostly unattended. You only need to do this once, and you can skip any step and " +
                   "come back to it later.\r\n\r\n" +
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
        _contentPanel = new Panel { Left = 12, Top = 50, Width = 616, Height = 370, Visible = false };

        _statusIcon = new PictureBox { Left = 0, Top = 4, Width = 32, Height = 32, SizeMode = PictureBoxSizeMode.Zoom };
        _titleLabel = new Label
        {
            // Holds a source's name, which can contain an ampersand - see the
            // note in SetupWizardForm, which shows the same names.
            UseMnemonic = false,
            Left = 44,
            Top = 8,
            Width = 572,
            Height = 30,
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold)
        };
        // The description grows with its text and scrolls when the step has
        // more to say than the panel has room for.
        //
        // It was a fixed-height Label, and a Label clips: the Patrologia
        // Latina step has the longest description here - it has to explain
        // that most of the corpus is under provisional reference numbers, and
        // what that means for a note tied to one - and the last two lines of
        // it simply were not on screen. Nothing said so, which is the worst
        // version: the sentence about re-attaching notes ended mid-thought.
        //
        // A read-only TextBox would scroll too, and would arrive with a border
        // around it - ReadingTheme sets BorderStyle.FixedSingle on every
        // TextBoxBase, correctly, because everywhere else one is an input. A
        // description that looks like a field you can type in is a different
        // wrong answer. So: an AutoSize label, which wraps at MaximumSize and
        // grows downward, inside a panel that scrolls.
        _descriptionScroll = new Panel { Left = 0, Top = 50, Width = 616, Height = 70, AutoScroll = true };

        _descriptionLabel = new Label
        {
            Left = 0,
            Top = 0,
            AutoSize = true,

            // Width zero means "no limit"; the width here is the panel's less
            // room for the scrollbar, so wrapping does not change when one
            // appears.
            MaximumSize = new Size(596, 0)
        };

        _descriptionScroll.Controls.Add(_descriptionLabel);

        // Visible only on the Database step - every other step's action is
        // a single button, no path to choose, which is exactly the point
        // of hiding this everywhere else.
        _pathBox = new TextBox { Left = 0, Top = 130, Width = 470, Height = 24 };
        _browseButton = new Button { Text = "Browse...", Left = 478, Top = 128, Width = 138, Height = 28 };
        _browseButton.Click += (_, _) =>
        {
            // Two steps share this button and they want different dialogs: one
            // names a file to create, the other a folder to fill. The note that
            // used to sit on the path box - that Browse opens a file dialog and
            // that is the wrong dialog for choosing a download folder - was
            // written when only the first existed.
            if (_currentStep == DataFolderStepIndex)
            {
                using var folderDialog = new FolderBrowserDialog
                {
                    Description = "Choose where downloaded texts and dictionaries should be kept",
                    UseDescriptionForTitle = true,
                    SelectedPath = Directory.Exists(_pathBox.Text) ? _pathBox.Text : DataFolderSettings.DefaultRoot,
                    ShowNewFolderButton = true
                };

                if (folderDialog.ShowDialog(this) == DialogResult.OK) _pathBox.Text = folderDialog.SelectedPath;
                return;
            }

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

        // Sits between the description and the buttons, for sources whose
        // files come from a website. Opening a browser is not an operation
        // with progress and an outcome, so it does not belong on the action
        // button beside things that are.
        // Two is what Menota needs and no source has asked for more. Built
        // once and hidden rather than created per step, so the panel's control
        // collection stays stable.
        _sourceLinks = new LinkLabel[2];
        for (var i = 0; i < _sourceLinks.Length; i++)
        {
            var link = new LinkLabel
            {
                Left = 0,
                Top = 150 + (i * 18),
                Width = 370,
                Height = 18,
                Visible = false
            };

            link.LinkClicked += (sender, _) => OpenLink((sender as LinkLabel)?.Tag as string);
            _sourceLinks[i] = link;
        }

        _actionButton = new Button { Left = 0, Top = 172, Width = 280, Height = 38 };
        _actionButton.Click += async (_, _) => await RunCurrentStepActionAsync();

        // Only sources that declare one show this; it stays hidden otherwise,
        // which is every source but Menota today.
        _secondaryButton = new Button { Left = 292, Top = 172, Width = 280, Height = 38, Visible = false };
        _secondaryButton.Click += async (_, _) => await RunCurrentStepSecondaryAsync();

        _progressBar = new ProgressBar { Left = 0, Top = 214, Width = 616, Height = 22 };
        _statusLabel = new Label { Left = 0, Top = 244, Width = 616, Height = 20 };
        _elapsedLabel = new Label { Left = 0, Top = 268, Width = 616, Height = 20, ForeColor = Color.DimGray };

        // Where a step's report actually lands.
        //
        // Everything a step had to say used to go through the single-line
        // status label, one Report call overwriting the last, so a
        // twenty-line survey flashed past and left only its final line - and
        // then RenderStep in the finally block replaced even that with "Not
        // loaded yet." A step whose entire output is a report had no way to
        // show one. Most steps still have nothing to put here and it stays
        // hidden for them.
        _outputBox = new TextBox
        {
            Left = 0,
            Top = 302,
            Width = 616,
            Height = 64,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 8.25F),
            Visible = false
        };

        _contentPanel.Controls.Add(_statusIcon);
        _contentPanel.Controls.Add(_titleLabel);
        _contentPanel.Controls.Add(_descriptionScroll);
        _contentPanel.Controls.Add(_pathBox);
        _contentPanel.Controls.Add(_browseButton);
        // Beside the second link rather than below it, so the answer to "have
        // I got that file yet" sits where the question was asked.
        _readinessLabel = new Label
        {
            Left = 380,
            Top = 168,
            Width = 236,
            Height = 18,
            Visible = false
        };

        foreach (var link in _sourceLinks) _contentPanel.Controls.Add(link);
        _contentPanel.Controls.Add(_readinessLabel);
        _contentPanel.Controls.Add(_actionButton);
        _contentPanel.Controls.Add(_secondaryButton);
        _contentPanel.Controls.Add(_progressBar);
        _contentPanel.Controls.Add(_statusLabel);
        _contentPanel.Controls.Add(_elapsedLabel);
        _contentPanel.Controls.Add(_outputBox);
        Controls.Add(_contentPanel);
    }

    private void BuildFinishPanel()
    {
        _finishPanel = new Panel { Left = 12, Top = 50, Width = 616, Height = 370, Visible = false };

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
        _nextButton.Click += async (_, _) =>
        {
            if (_currentStep == DatabaseStepIndex && !_databaseComplete)
            {
                MessageBox.Show(this,
                    "Set up the database first - everything else in this wizard needs somewhere to write to.",
                    "Database Not Set Up Yet", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // A folder typed into the box, or picked with Browse, and then not
            // confirmed with the button. Next is the obvious thing to press
            // after choosing something, and dropping the choice silently would
            // send the whole download to the folder they had just rejected.
            if (_currentStep == DataFolderStepIndex
                && !string.Equals(_pathBox.Text.Trim(), DataFolderSettings.Root, StringComparison.OrdinalIgnoreCase))
            {
                await ApplyDataFolderAsync();

                // Left on this step when it would not take: the status line has
                // already said why, and moving on would bury it.
                if (!string.Equals(_pathBox.Text.Trim(), DataFolderSettings.Root, StringComparison.OrdinalIgnoreCase))
                {
                    RenderStep();
                    return;
                }
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

        // "Could downloads go there?", not "is it already there?". On a fresh
        // install the default folder has never been created, and judging it by
        // existence would greet every newcomer with a red cross beside a
        // perfectly good default - on the step whose whole message is that the
        // default is fine unless you are short of space.
        //
        // Checked without creating anything: someone who has not reached this
        // step yet should not have folders made for them on the strength of
        // opening the wizard.
        _dataFolderComplete = DataFolderSettings.LooksUsable(DataFolderSettings.Root);

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

        // "Is every line indexed?", not "does the index have any rows?".
        //
        // HasDataAsync was what this asked, and it is true of an index built
        // once and never touched again - so ingesting a collection afterwards
        // left this step showing Complete and "Already built" while the new
        // collection was entirely absent from the index. No ingest service
        // touches WordIndex; a re-ingest also gives every line a new id, which
        // orphans the rows the old ones had. And search decides whether to
        // consult the index by asking the same has-any-rows question, so it
        // consults a half-populated index and the missing lines are invisible
        // rather than merely slow to find.
        //
        // The README tells a newcomer that anything skipped "can be added
        // later", which is exactly the path into this.
        //
        // Off the UI thread because the honest count is expensive: WordIndex
        // is keyed (word, line), so counting distinct lines reads every row -
        // seventy million of them on a full library, about twenty seconds.
        // WordIndexForm has done it this way since it was written; this is the
        // same check, finally asked on the screen a newcomer actually sees.
        var (totalLines, indexedLines) = await Task.Run(async () => (
            await _wordIndexRepo.GetTextNodeCountAsync(),
            await _wordIndexRepo.GetIndexedTextNodeCountAsync()));

        _indexedLines = indexedLines;
        _totalLines = totalLines;
        _wordIndexComplete = totalLines > 0 && indexedLines >= totalLines;
    }

    private void RenderStep()
    {
        var isWelcome = _currentStep == 0;
        var isDatabase = _currentStep == DatabaseStepIndex;
        var isDataFolder = _currentStep == DataFolderStepIndex;
        var isFinish = _currentStep == FinishStepIndex;
        var isContent = !isWelcome && !isFinish;

        _welcomePanel.Visible = isWelcome;
        _finishPanel.Visible = isFinish;
        _contentPanel.Visible = isContent;

        _pathBox.Visible = isDatabase || isDataFolder;
        _browseButton.Visible = isDatabase || isDataFolder;
        // The description shares vertical space with the path box, which the
        // Database step uses for a file to write, the Download Folder step for
        // a folder to fill, and a source step to show the folder its files
        // live in - read-only on that last one, since the source folders are
        // all decided by the Download Folder step rather than one at a time.
        //
        // This note used to end by saying Browse opens a file dialog filtered
        // to .db and that this is the wrong dialog for choosing a download
        // folder. It was right, and it is why Browse now looks at which step
        // is showing before deciding which dialog to open.
        foreach (var link in _sourceLinks) link.Visible = false;
        _readinessLabel.Visible = false;
        _secondaryButton.Visible = false;
        _outputBox.Visible = false;
        _outputBox.Clear();

        // The path box sits at 130 on the two steps that are ABOUT a path and
        // is hidden on every other, so the description gets that room back when
        // it is not there. Anything longer than the room available scrolls
        // rather than being cut.
        var isPathStep = isDatabase || isDataFolder;
        _descriptionScroll.Height = isPathStep ? 70 : 96;
        _pathBox.ReadOnly = !isPathStep;
        _pathBox.Top = isPathStep ? 130 : 190;
        _browseButton.Visible = isPathStep;

        _actionButton.Top = isPathStep ? 172 : 218;
        _secondaryButton.Top = _actionButton.Top;
        _progressBar.Top = isPathStep ? 214 : 258;
        _statusLabel.Top = isPathStep ? 244 : 280;
        _statusLabel.Width = isPathStep ? 616 : 430;
        _elapsedLabel.Top = isPathStep ? 268 : 280;
        _elapsedLabel.Left = isPathStep ? 0 : 440;
        _elapsedLabel.Width = isPathStep ? 616 : 176;

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
        else if (isDataFolder)
        {
            _titleLabel.Text = "Download Folder";
            _descriptionLabel.Text =
                "Everything downloaded in the steps after this one goes here: the texts themselves, the " +
                "dictionaries, and the word-form data. All of it together comes to about nine gigabytes, " +
                "most of that the Greek and Latin word-form data, so if your main drive is short of room " +
                "this is the thing to put somewhere else. The folder below is fine if you have the space. " +
                "These are working copies of public data - your own library, tags and bookmarks live in " +
                "the database file from the last step, not here.";
            _pathBox.Text = DataFolderSettings.Root;
            _actionButton.Text = "Use This Folder";

            // Three states, as the word-index step has: a folder that will work
            // but is short of room is not an error, and it is not a green tick
            // either. A tick beside the words "only 4 GB free" is the reading
            // someone takes away, and it is the wrong one.
            _statusIcon.Image = AppIcons.Get(DataFolderIconName(), 32);
            _statusLabel.Text = _dataFolderComplete
                ? DescribeDataFolder()
                : "Pick a folder that exists, or one that can be created.";
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
                // A source can name its own action. "Download & Install" is
                // wrong for a step that opens a website and inspects a folder,
                // and a button that misdescribes itself is worse than a plain
                // one.
                _actionButton.Text = source.ActionButtonText is { } label
                    ? label.Replace("&", "&&")
                    : complete ? "Re-download && Re-install" : "Download && Install";

                for (var i = 0; i < _sourceLinks.Length && i < source.Links.Count; i++)
                {
                    _sourceLinks[i].Text = source.Links[i].Text;
                    _sourceLinks[i].Tag = source.Links[i].Url;
                    _sourceLinks[i].Visible = true;
                }

                if (source.SecondaryButtonText is { } secondaryLabel && source.RunSecondary is not null)
                {
                    _secondaryButton.Text = secondaryLabel.Replace("&", "&&");
                    _secondaryButton.Visible = true;
                }

                if (source.CheckReadiness is { } check)
                {
                    // Never let a check throw into RenderStep - the step would
                    // fail to draw, and a file that cannot be inspected is a
                    // problem to report rather than a crash.
                    SetupReadiness readiness;
                    try
                    {
                        readiness = check(source.DefaultDestination);
                    }
                    catch (Exception ex)
                    {
                        readiness = new SetupReadiness(SetupReadinessState.Problem, ex.Message);
                    }

                    _readinessLabel.Text = readiness.State switch
                    {
                        SetupReadinessState.Ready => "\u2713 " + readiness.Message,
                        SetupReadinessState.Problem => "\u26A0 " + readiness.Message,
                        _ => readiness.Message
                    };

                    // Set after the theme has been applied, so these have to
                    // carry their own dark values - all three literals that
                    // used to be here were between 3.0:1 and 3.5:1 on the dark
                    // surface, the Problem branch included, and that branch
                    // renders a caught exception's message on the first screen
                    // a new reader ever sees.
                    _readinessLabel.ForeColor = readiness.State switch
                    {
                        SetupReadinessState.Ready => ReadingTheme.IsDark
                            ? Color.FromArgb(120, 205, 145)
                            : Color.FromArgb(26, 112, 55),
                        SetupReadinessState.Problem => ReadingTheme.WarningText,
                        _ => ReadingTheme.MutedText
                    };

                    _readinessLabel.Visible = true;
                }

                if (source.ShowDestinationPath)
                {
                    _pathBox.Visible = true;
                    _pathBox.Text = source.DefaultDestination;
                    _outputBox.Visible = true;
                }

                _statusLabel.Text = complete ? "Already loaded." : "Not loaded yet.";
            }
            else
            {
                _titleLabel.Text = "Build Word Index";
                _descriptionLabel.Text =
                    "Makes searching fast once the texts above are loaded - without it, every search has " +
                    "to scan the whole library from scratch. Run this once everything above is done; safe " +
                    "to run again any time. Takes about fifteen minutes.";
                // Three states, not two. "Built once and now out of date" is
                // the one that used to read as finished, and it is the one a
                // reader is most likely to be in: build the index, ingest
                // another collection afterwards, and the lines from it are
                // absent from the index and therefore absent from search.
                var partial = !_wordIndexComplete && _indexedLines > 0;

                _statusIcon.Image = AppIcons.Get(
                    _wordIndexComplete ? "Complete" : partial ? "Warning" : "Error", 32);

                _actionButton.Text = _indexedLines > 0 ? "Rebuild Index" : "Build Word Index";

                _statusLabel.Text = _wordIndexComplete
                    ? $"Up to date - {_indexedLines:N0} lines indexed."
                    : partial
                        ? $"Out of date - {_indexedLines:N0} of {_totalLines:N0} lines indexed. " +
                          $"{_totalLines - _indexedLines:N0} added since the last build will not turn up in " +
                          "searches until this is rebuilt."
                        : "Not built yet.";
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

        if (_currentStep == DataFolderStepIndex)
        {
            await ApplyDataFolderAsync();
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
    /// Rebuilds the step list against the current download folder.
    ///
    /// The catalogue reads the folder once, as it builds, and hands each source
    /// a finished destination path - so the sources made when this window
    /// opened are still aimed at wherever the folder was then. The count and
    /// order are fixed by the catalogue, so the completion flags beside them
    /// stay valid; only the paths move.
    /// </summary>
    private void RebuildSources() =>
        _sources = SetupDataSourceCatalog.Build(_authorRepo, _lemmaRepo, _definitionRepo, _artifactRepo, _editionRepo);

    /// <summary>
    /// Takes the folder in the box, having satisfied itself that downloads can
    /// actually be written there.
    ///
    /// Creating it is part of accepting it: the folder someone types or picks
    /// with New Folder may not exist yet, and finding that out at the start of
    /// an hour-long download is finding out too late. So it is created and
    /// written to here, while there is still a person looking at the screen to
    /// tell.
    /// </summary>
    private async Task ApplyDataFolderAsync()
    {
        if (!DataFolderSettings.TryPrepare(_pathBox.Text, out var chosen, out var error))
        {
            _statusLabel.Text = error!.StartsWith("Enter ", StringComparison.Ordinal)
                ? error
                : $"Can't use that folder: {error}";
            _statusIcon.Image = AppIcons.Get("Error", 32);
            return;
        }

        var moved = !string.Equals(chosen, DataFolderSettings.Root, StringComparison.OrdinalIgnoreCase);
        DataFolderSettings.Root = chosen;
        _pathBox.Text = chosen;

        // Rebuilt because every source's destination is derived from the folder
        // - see SetupDataSourceCatalog.DataRoot - so the steps after this one
        // are pointing at the old place until they are made again.
        RebuildSources();

        // The map holds its geometry in a static cache keyed to nothing, so a
        // map opened before the folder moved would go on drawing what it read
        // from the old one. Cheap to drop; it reloads on next use.
        NaturalEarthCoastline.InvalidateCache();

        if (moved)
        {
            // Navigation off for the same reason every other awaiting path in
            // this form turns it off: this is fourteen indexed counts, quick
            // but not instant, and the button that started them stays under
            // the pointer throughout.
            SetNavEnabled(false);
            try { await RefreshSourceCompletionAsync(); }
            finally { SetNavEnabled(true); }
        }

        _dataFolderComplete = true;
        _statusIcon.Image = AppIcons.Get(DataFolderIconName(), 32);
        _statusLabel.Text = DescribeDataFolder();

        if (moved) WarnAboutAlreadyDownloadedData();
    }

    /// <summary>
    /// Re-asks every source whether it is done, after the folder beneath them
    /// changed.
    ///
    /// Deliberately not RefreshAllCompletionAsync, which also counts the word
    /// index - two aggregates over seventy million rows, half a minute on a
    /// full library, and nothing the download folder can possibly have
    /// affected. These fifteen are indexed counts and a File.Exists.
    ///
    /// It matters for exactly one of them today: fourteen decide completeness
    /// from the database, which a folder cannot change, but the map decides it
    /// from a file in this folder. Written to ask all of them anyway, so that
    /// a future source which reads from disk is not quietly left stale.
    /// </summary>
    private async Task RefreshSourceCompletionAsync()
    {
        if (!_databaseComplete) return;

        for (var i = 0; i < _sources.Count && i < _sourceComplete.Count; i++)
        {
            _sourceComplete[i] = await _sources[i].CheckComplete();
        }
    }

    /// <summary>
    /// The chosen folder and what is free on its drive, which is the number
    /// that decides whether the next hour is going to work.
    /// </summary>
    /// <summary>
    /// Which of the three marks the Download Folder step shows: usable and
    /// roomy, usable but tight, or not usable at all.
    /// </summary>
    private string DataFolderIconName()
    {
        if (!_dataFolderComplete) return "Error";

        var freeGb = DataFolderSettings.FreeGigabytesAt(DataFolderSettings.Root);

        // Unknown free space is not a warning. A network path cannot answer,
        // and refusing to tick a folder for being unmeasurable would flag the
        // one kind of location most likely to have room.
        return freeGb != null && freeGb < DataFolderSettings.ComfortableFreeGigabytes
            ? "Warning"
            : "Complete";
    }

    private static string DescribeDataFolder()
    {
        var root = DataFolderSettings.Root;
        var freeGb = DataFolderSettings.FreeGigabytesAt(root);

        if (freeGb == null) return $"Ready: {root}{TemporaryDriveNote(root)}";

        return freeGb < DataFolderSettings.ComfortableFreeGigabytes
            ? $"{root} - only {freeGb:N1} GB free, and a full set needs about " +
              $"{DataFolderSettings.FullSetGigabytes:N0} GB. You can still go on and skip the larger steps."
            : $"Ready: {root} ({freeGb:N0} GB free){TemporaryDriveNote(root)}";
    }

    /// <summary>
    /// Names the temporary drive as well, when it is a different one.
    ///
    /// The big collections arrive as a git clone into the system temporary
    /// folder and are copied out from there, so the download needs room on two
    /// drives, not one. Choosing a roomy folder on D: and being told "500 GB
    /// free" is exactly the reassurance that precedes running out of space on
    /// C: an hour later - and the person most likely to move the folder is the
    /// person whose C: is already full.
    ///
    /// Said rather than solved: moving the clone into the chosen folder would
    /// change the download path for everyone, including everyone who has no
    /// problem, which is not a change to make two days before an announcement.
    /// </summary>
    private static string TemporaryDriveNote(string root)
    {
        var tempRoot = Path.GetPathRoot(Path.GetTempPath());
        var chosenRoot = Path.GetPathRoot(root);

        if (string.IsNullOrEmpty(tempRoot) || string.IsNullOrEmpty(chosenRoot)) return string.Empty;
        if (string.Equals(tempRoot, chosenRoot, StringComparison.OrdinalIgnoreCase)) return string.Empty;

        var tempFree = DataFolderSettings.FreeGigabytesAt(tempRoot);
        if (tempFree == null) return string.Empty;

        return tempFree < DataFolderSettings.ComfortableFreeGigabytes
            ? $" - note the largest downloads also unpack through {tempRoot.TrimEnd('\\')} first, which has {tempFree:N1} GB free."
            : string.Empty;
    }

    /// <summary>
    /// Said once, when the folder changes with data already downloaded
    /// elsewhere - because what happens next is not obvious and is mostly
    /// reassuring.
    /// </summary>
    private void WarnAboutAlreadyDownloadedData()
    {
        if (!Directory.Exists(DataFolderSettings.DefaultRoot) && !_sourceComplete.Any(c => c)) return;

        MessageBox.Show(this,
            "Anything you have already downloaded stays where it is - nothing is moved or deleted, and " +
            "your library is unaffected, because the texts you have ingested are in the database rather " +
            "than in these folders.\r\n\r\n" +
            "What changes is where the steps after this one look, so any step you have not run yet will " +
            "download into the new folder.\r\n\r\n" +
            "A few steps read from this folder rather than only downloading into it, and those will need " +
            "running again or pointing at the old folder. The map is one: it is read every time the map " +
            "opens rather than being ingested, and it is under a megabyte. Adding Stephanus and Bekker " +
            "numbers is another - it reads the texts back out of this folder rather than fetching " +
            "anything, so it will find nothing in an empty one. And if you imported Medieval Nordic " +
            "manuscripts, their files and the work divisions you confirmed are in the old folder too; " +
            "those are your own decisions rather than a download, and worth copying across by hand " +
            "rather than redoing.",
            "Downloads already on disk", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task RunCurrentStepSecondaryAsync()
    {
        var stepInSources = _currentStep - FirstSourceStepIndex;
        if (stepInSources < 0 || stepInSources >= _sources.Count) return;

        var source = _sources[stepInSources];
        if (source.RunSecondary is null) return;

        // On the UI thread, deliberately: this is where a step gets to ask
        // something before it starts, and RunSourceActionAsync is past the
        // point where a dialog can be shown.
        if (source.PrepareSecondary is { } prepare && !prepare(this)) return;

        await RunSourceActionAsync(source, source.RunSecondary, fetch: false);
    }

    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // A browser that won't launch is not a setup failure, and a modal
            // error box would imply it was. The address is the useful thing to
            // hand over.
            _statusLabel.Text = $"Couldn't open a browser ({ex.Message}). Go to {url} manually.";
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

    private Task RunSourceAsync(SetupDataSource source) =>
        RunSourceActionAsync(source, source.RunIngest, fetch: true);

    /// <param name="fetch">
    /// False for a secondary action, which works on files already sitting in
    /// the destination folder and must not re-download anything to do it.
    /// </param>
    private async Task RunSourceActionAsync(
        SetupDataSource source,
        Func<string, IProgress<string>, CancellationToken, Task<IngestOutcome>> action,
        bool fetch)
    {
        SetNavEnabled(false);
        _cts = new CancellationTokenSource();
        _progressBar.Style = ProgressBarStyle.Marquee;
        StartHeartbeat();

        var progress = new Progress<string>(message =>
        {
            _statusLabel.Text = message;
            if (_outputBox.Visible) _outputBox.AppendText(message + Environment.NewLine);
        });

        if (_outputBox.Visible) _outputBox.Clear();

        try
        {
            Directory.CreateDirectory(source.DefaultDestination);

            _statusLabel.Text = fetch ? "Downloading..." : "Working...";
            if (!fetch)
            {
                // Secondary action - the files are already here.
            }
            else if (source.FetchMode == SetupFetchMode.SelfManaged)
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

            if (fetch) _statusLabel.Text = "Installing...";
            var outcome = await Task.Run(
                () => action(source.DefaultDestination, progress, _cts.Token), _cts.Token);

            // The status line carries both outcomes; the dialog below opens
            // only for the one worth interrupting someone for. See
            // SetupSkipReport for why those are different questions.
            _statusLabel.Text = outcome.Describe(source.Title);

            SetupSkipReport.ShowIfAny(this, source.Title, outcome);
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

            // RenderStep rewrites the status label from the step's completion
            // state and clears the log, which is right when arriving at a step
            // and wrong the instant a run has just finished: it replaced the
            // result of the run - the count of skipped files, the plan names
            // to go and confirm - with "Not loaded yet." The step looked like
            // it had done nothing, on every run, however much it had done.
            var finalStatus = _statusLabel.Text;
            var finalLog = _outputBox.Text;

            RenderStep();

            _statusLabel.Text = finalStatus;
            if (_outputBox.Visible) _outputBox.Text = finalLog;
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
