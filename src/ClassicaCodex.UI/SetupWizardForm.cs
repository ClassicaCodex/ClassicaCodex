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
public class SetupWizardForm : ScaledForm
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
        public Func<string, IProgress<string>, CancellationToken, Task<IngestOutcome>> RunIngest => Source.RunIngest;
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
    private readonly EditionRepository _editionRepo = new();

    // Which collection wins when several carry the same work. Filled on Load,
    // because the choices are whatever is actually in the library rather than
    // whatever the catalog knows how to fetch - a source listed above and never
    // run has no editions to prefer.
    private readonly ComboBox _preferredCollectionBox;
    private bool _loadingPreferredCollection;

    private sealed record CollectionOption(string? Key, string Title)
    {
        public override string ToString() => Title;
    }


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
        // Capped and scrolling rather than growing with each source. Every
        // added row was another 98px, and at nine sources the form would be
        // ~1300px tall - past the usable height of a 1080p screen, which
        // would put the bottom rows and the Close button somewhere they
        // simply can't be reached. AutoScroll keeps everything reachable no
        // matter how many sources the catalog grows to.
        ClientSize = new Size(900, 900);
        AutoScroll = true;
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

        foreach (var source in SetupDataSourceCatalog.Build(_authorRepo, _lemmaRepo, _definitionRepo, _artifactRepo, _editionRepo))
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

        // Word index and AI translation both open on their own now.
        //
        // The index check counts distinct indexed lines against every line
        // in the library - two aggregates over a million rows plus - and it
        // ran every time this dialog opened, so just glancing at the data
        // sources paid for a scan nobody asked for. Behind a button, only
        // someone who wants the answer waits for it.
        var toolsTitle = new Label
        {
            Text = "Tools",
            Left = 36,
            Top = y,
            Width = 836,
            Font = new Font(Font, FontStyle.Bold)
        };

        var wordIndexButton = new Button
        {
            Text = "Word Index...", Left = 12, Top = y + 24, Width = 170, Height = 30
        };
        wordIndexButton.Click += (_, _) =>
        {
            using var form = new WordIndexForm();
            form.ShowDialog(this);
        };

        var wordIndexDesc = new Label
        {
            Text = "Check whether the word index covers everything ingested, and rebuild it. " +
                   "This is what makes lemma-aware search fast.",
            Left = 192,
            Top = y + 30,
            Width = 680,
            ForeColor = Color.DimGray
        };

        var translationButton = new Button
        {
            Text = "AI Translation...", Left = 12, Top = y + 62, Width = 170, Height = 30
        };
        translationButton.Click += (_, _) =>
        {
            using var form = new TranslateApiSettingsForm();
            form.ShowDialog(this);
        };

        var translationDesc = new Label
        {
            Text = "API keys for Claude and Gemini, and whether to confirm before anything is sent. " +
                   "Optional - the app is fully offline without them.",
            Left = 192,
            Top = y + 68,
            Width = 680,
            ForeColor = Color.DimGray
        };

        Controls.Add(toolsTitle);
        Controls.Add(wordIndexButton);
        Controls.Add(wordIndexDesc);
        Controls.Add(translationButton);
        Controls.Add(translationDesc);

        y += 108;

        // Reading preferences, in the one dialog that already collects
        // everything that isn't a corpus - the database location and the
        // word index both live here too.
        var readingTitle = new Label
        {
            Text = "Reading",
            Left = 36,
            Top = y,
            Width = 836,
            Font = new Font(Font, FontStyle.Bold)
        };

        var reopenCheck = new CheckBox
        {
            Text = "Open where I last left off",
            Left = 12,
            Top = y + 22,
            Width = 300,
            Checked = ReadingPosition.ReopenOnLaunch
        };

        var reopenDesc = new Label
        {
            Text = "Reopens the passage you were last reading when the app starts. " +
                   "Turn it off if opening a long work at launch feels slow - your place is still " +
                   "remembered either way, so switching it back on picks up where you were.",
            Left = 32,
            Top = y + 44,
            Width = 840,
            Height = 34,
            ForeColor = Color.DimGray
        };

        reopenCheck.CheckedChanged += (_, _) => ReadingPosition.ReopenOnLaunch = reopenCheck.Checked;

        var preferredLabel = new Label
        {
            Text = "Open works from:",
            Left = 12,
            Top = y + 86,
            Width = 110
        };

        _preferredCollectionBox = new ComboBox
        {
            Left = 126,
            Top = y + 82,
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Enabled = false
        };
        _preferredCollectionBox.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingPreferredCollection) return;
            PreferredCollectionSettings.Preferred =
                (_preferredCollectionBox.SelectedItem as CollectionOption)?.Key;
        };

        var preferredDesc = new Label
        {
            Text = "When more than one collection has the same work - Perseus and First1KGreek both " +
                   "carry the Agamemnon - this is the one it opens on. The others stay in the edition " +
                   "dropdown; only which is already selected changes. Leave it on no preference and " +
                   "works open on whichever edition sorts first.",
            Left = 32,
            Top = y + 110,
            Width = 840,
            Height = 34,
            ForeColor = Color.DimGray
        };

        Controls.Add(readingTitle);
        Controls.Add(reopenCheck);
        Controls.Add(reopenDesc);
        Controls.Add(preferredLabel);
        Controls.Add(_preferredCollectionBox);
        Controls.Add(preferredDesc);

        y += 154;

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

        Load += async (_, _) =>
        {
            await RefreshCompletionIconsAsync();
            await LoadPreferredCollectionAsync();
        };
        ReadingTheme.AttachTo(this);
    }

    /// <summary>
    /// Offers the collections actually present in the library, plus the option
    /// of having no preference at all.
    ///
    /// Left disabled with fewer than two of them: with one collection nothing
    /// ever overlaps, so there is no choice to make and a live control implying
    /// otherwise would be a small lie. It stays visible rather than hidden,
    /// because a setting that appears only once you happen to have installed a
    /// second corpus is a setting nobody discovers.
    ///
    /// A stored preference naming a collection that is not installed is left
    /// alone rather than reset - the box falls back to showing no preference,
    /// which is what is in force, but reinstalling that collection brings the
    /// preference back rather than having silently discarded it.
    /// </summary>
    private async Task LoadPreferredCollectionAsync()
    {
        if (!DbConnectionFactory.IsConfigured) return;

        var collections = await _editionRepo.GetCollectionsAsync();

        _loadingPreferredCollection = true;
        try
        {
            _preferredCollectionBox.Items.Clear();
            _preferredCollectionBox.Items.Add(new CollectionOption(null, "No preference"));

            foreach (var key in collections
                         .Select(k => new CollectionOption(k, SetupDataSourceCatalog.DescribeCollection(k)))
                         .OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                _preferredCollectionBox.Items.Add(key);
            }

            var stored = PreferredCollectionSettings.Preferred;
            var match = _preferredCollectionBox.Items.OfType<CollectionOption>()
                .FirstOrDefault(c => c.Key != null
                                     && string.Equals(c.Key, stored, StringComparison.OrdinalIgnoreCase));

            _preferredCollectionBox.SelectedItem = match ?? _preferredCollectionBox.Items[0];
            _preferredCollectionBox.Enabled = collections.Count > 1;
        }
        finally
        {
            _loadingPreferredCollection = false;
        }
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
            return;
        }

        foreach (var row in _rows)
        {
            var complete = await row.CheckComplete();
            row.StatusIcon.Image = AppIcons.Get(complete ? "Complete" : "Error", 18);
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
            var outcome = await Task.Run(() => row.RunIngest(destination, progress, _cts.Token), _cts.Token);

            row.StatusLabel.Text = "Done - " + outcome.Describe(row.Title);

            SetupSkipReport.ShowIfAny(this, row.Title, outcome);
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
    }
}
