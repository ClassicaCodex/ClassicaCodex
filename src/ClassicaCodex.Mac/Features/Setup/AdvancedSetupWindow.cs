using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Advanced Setup: every data source on one screen, each with its address,
/// the folder it goes to, and one button that fetches and ingests it - the
/// Mac counterpart of SetupWizardForm, built from the same catalogue the
/// guided assistant uses, so the two never drift apart on what a step does or
/// how "is this already loaded" is decided.
///
/// For the reader who already has the files somewhere, wants a collection in
/// a folder of its own, or wants to skip around. Below the sources are the
/// manual ingests, the library file and download folder, the word index and
/// the AI keys, and two reading preferences, as on Windows.
/// </summary>
public sealed class AdvancedSetupWindow : FeatureWindow
{
    private sealed class Row
    {
        // The full source definition rather than copied fields, so a row can
        // know anything its source knows without another field being added.
        public SetupDataSource Source = null!;
        public StateMark Mark = null!;
        public TextBox Destination = null!;
        public Button Browse = null!;
        public Button Action = null!;
        public Button? Secondary;
        public TextBlock Status = null!;
    }

    private readonly IReaderHost _host;
    private readonly List<Row> _rows = new();
    private readonly StackPanel _rowList = new();
    private readonly List<Control> _otherControls = new();
    private CancellationTokenSource? _cts;

    /// <summary>
    /// The row whose fetch or ingest is running, or null when nothing is. The
    /// row rather than a flag, because its own button is the Cancel button -
    /// see SetAllRowsEnabled.
    /// </summary>
    private Row? _runningRow;
    private Button? _runningButton;

    /// <summary>What the button that is now Cancel said before, to say it again afterwards.</summary>
    private string _runningButtonText = string.Empty;

    /// <summary>
    /// Set when someone closes this window with a fetch running and confirms
    /// they want it stopped. The window stays up until it has actually
    /// stopped, and then closes itself - so that its disappearance is the
    /// answer to "did that stop?", which nothing else here could give.
    /// </summary>
    private bool _closeWhenStopped;
    private bool _askingToStop;

    // Used only to answer "has this been loaded already" for the marks -
    // separate from the services inside each source's RunIngest, which do the
    // writing.
    private readonly AuthorRepository _authorRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();
    private readonly ArtifactRepository _artifactRepo = new();
    private readonly EditionRepository _editionRepo = new();

    private readonly TextBlock _libraryPath = new() { Classes = { "mono" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _folderPath = new() { Classes = { "mono" }, TextWrapping = TextWrapping.Wrap };

    // Which collection wins when several carry the same work. Filled from what
    // is actually in the library rather than what the catalogue can fetch - a
    // source never run has no editions to prefer.
    private readonly ComboBox _preferredCollection = new() { MinWidth = 320, IsEnabled = false };
    private bool _loadingPreferredCollection;
    private int _collectionCount;

    private sealed record CollectionOption(string? Key, string Title)
    {
        public override string ToString() => Title;
    }

    public AdvancedSetupWindow(IReaderHost host)
    {
        _host = host;
        OnNavigate = host.NavigateToPassageAsync;
        OnOpenWork = host.OpenWorkAsync;

        // See MacDataFolder.AdoptProposalIfUnchosen: this window never shows
        // the Download Folder step, so it must not inherit a synced default
        // that nobody chose.
        MacDataFolder.AdoptProposalIfUnchosen();

        Title = "Advanced Setup";
        Width = 920;
        Height = 800;
        MinWidth = 760;
        MinHeight = 520;

        Content = BuildLayout();
        BuildRows();
        Closing += OnWindowClosing;

        Opened += async (_, _) =>
        {
            await RefreshCompletionMarksAsync();
            await LoadPreferredCollectionAsync();
        };
    }

    internal int RowCount => _rows.Count;

    internal string RowTitle(int index) => _rows[index].Source.Title;

    internal StepState RowState(int index) => _rows[index].Mark.State;

    private Control BuildLayout()
    {
        var stack = new StackPanel { Margin = new Thickness(32, 26, 32, 28) };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var tile = Art.Tile("setup", "Setup", 52);
        tile.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(tile, Dock.Left);
        header.Children.Add(tile);
        var headerText = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(new TextBlock { Text = "Advanced Setup", Classes = { "h1" } });
        headerText.Children.Add(SetupLook.Text(
            "Each of these downloads a full open-data repository and ingests it in one step. These are real downloads "
            + "(the largest run to several gigabytes) and can take a few minutes each - only run one at a time. "
            + "See About for what's licensed how.", "muted"));
        header.Children.Add(headerText);
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock { Text = "COLLECTIONS", Classes = { "section" }, Margin = new Thickness(2, 0, 0, 8) });
        stack.Children.Add(new Border { Classes = { "card" }, Child = _rowList, Margin = new Thickness(0, 0, 0, 26), ClipToBounds = true });

        stack.Children.Add(BuildManualSection());
        stack.Children.Add(BuildPlacesSection());
        stack.Children.Add(BuildToolsSection());
        stack.Children.Add(BuildReadingSection());

        var close = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Close", Classes = { "accent" }, MinWidth = 96, IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var footer = new Border { Child = close, Padding = new Thickness(24, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable("Cc.Rule"));

        var dock = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(new ScrollViewer { Content = stack });
        return dock;
    }

    /// <summary>
    /// The source rows, from the catalogue as it stands - built again when the
    /// download folder changes, because every row's folder is derived from it.
    ///
    /// On Windows this window closed itself instead, since its rows were
    /// positioned controls that could not be rebuilt in place, and reopening
    /// was the only way to point them at the new folder. Here they are a list,
    /// so they are rebuilt and the window stays.
    /// </summary>
    private void BuildRows()
    {
        _rows.Clear();
        _rowList.Children.Clear();

        var sources = SetupDataSourceCatalog.Build(_authorRepo, _lemmaRepo, _definitionRepo, _artifactRepo, _editionRepo);
        for (var i = 0; i < sources.Count; i++)
        {
            if (i > 0) _rowList.Children.Add(new Border { Classes = { "rule" }, Margin = new Thickness(18, 0) });
            _rowList.Children.Add(BuildRow(sources[i]));
        }
    }

    private Control BuildRow(SetupDataSource source)
    {
        // Starts as a plain ring rather than a guess: "hasn't been checked yet"
        // and "checked and missing" are different things, and this should not
        // claim the second before RefreshCompletionMarksAsync has asked.
        var row = new Row { Source = source, Mark = new StateMark(18) };

        var title = new TextBlock { Text = source.Title, Classes = { "h3" }, TextWrapping = TextWrapping.Wrap };
        var address = new SelectableTextBlock
        {
            Text = source.DisplayNote == null ? source.RepoUrl : $"{source.RepoUrl}   ({source.DisplayNote})",
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap
        };
        address.Bind(SelectableTextBlock.ForegroundProperty, address.GetResourceObservable("Cc.InkMuted"));

        row.Destination = new TextBox
        {
            Text = source.DefaultDestination,
            FontFamily = (FontFamily)Application.Current!.FindResource("Cc.MonoFont")!,
            FontSize = 12,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        row.Browse = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Choose…", MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
        row.Browse.Click += async (_, _) =>
        {
            var folder = await Files.FolderAsync(this, $"Choose a folder for {source.Title}");
            if (folder != null) row.Destination.Text = folder;
        };

        row.Action = new Button { Content = "Fetch & Ingest", MinWidth = 132, HorizontalContentAlignment = HorizontalAlignment.Center };
        row.Action.Click += async (_, _) =>
        {
            if (_runningRow != null)
            {
                if (ReferenceEquals(_runningButton, row.Action)) CancelRun();
                return;
            }

            await RunRowAsync(row, row.Action, source.RunIngest, fetch: true);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(row.Browse);
        buttons.Children.Add(row.Action);

        // Windows' Advanced Setup has only the one button per row, which left
        // the Menota step's import - its secondary action - reachable from the
        // guided assistant alone. The catalogue declares it; this shows it.
        if (source.SecondaryButtonText is { } secondaryText && source.RunSecondary is { } runSecondary)
        {
            row.Secondary = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = secondaryText };
            row.Secondary.Click += async (_, _) =>
            {
                if (_runningRow != null)
                {
                    if (ReferenceEquals(_runningButton, row.Secondary)) CancelRun();
                    return;
                }

                var folder = (row.Destination.Text ?? string.Empty).Trim();
                if (folder.Length > 0 && !await MenotaReview.PrepareSecondaryAsync(source, this, folder)) return;

                await RunRowAsync(row, row.Secondary, runSecondary, fetch: false);
            };
            buttons.Children.Add(row.Secondary);
        }

        var destinationRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Right);
        destinationRow.Children.Add(buttons);
        destinationRow.Children.Add(row.Destination);

        row.Status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(title);
        body.Children.Add(address);

        if (source.Links.Count > 0)
        {
            var links = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var link in source.Links)
            {
                var button = SetupLook.Link(link.Text, link.Url, message => row.Status.Text = message);
                button.FontSize = 12;
                button.Margin = new Thickness(0, 0, 18, 0);
                links.Children.Add(button);
            }

            body.Children.Add(links);
        }

        if (source.CheckReadiness is { } check)
        {
            SetupReadiness readiness;
            try
            {
                readiness = check(source.DefaultDestination);
            }
            catch (Exception ex)
            {
                readiness = new SetupReadiness(SetupReadinessState.Problem, ex.Message);
            }

            var state = readiness.State switch
            {
                SetupReadinessState.Ready => StepState.Complete,
                SetupReadinessState.Problem => StepState.Problem,
                _ => StepState.Pending
            };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            line.Children.Add(new StateMark(12, state));
            var text = new TextBlock { Text = readiness.Message, FontSize = 12 };
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(SetupLook.InkFor(state)));
            line.Children.Add(text);
            body.Children.Add(line);
        }

        body.Children.Add(destinationRow);
        body.Children.Add(row.Status);

        var grid = new DockPanel { Margin = new Thickness(18, 14, 18, 12) };
        row.Mark.Margin = new Thickness(0, 1, 12, 0);
        row.Mark.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(row.Mark, Dock.Left);
        grid.Children.Add(row.Mark);
        grid.Children.Add(body);

        _rows.Add(row);
        return grid;
    }

    private Control BuildManualSection()
    {
        var section = SetupLook.Section("Already have the files?",
            "If you've already downloaded these repositories yourself - or keep them somewhere else on disk - you can "
            + "point straight at those folders instead of downloading again. Same result, no second copy.");

        var ingest = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Ingest Corpus…", MinWidth = 150 };
        ingest.Click += async (_, _) =>
        {
            await new IngestWindow(_host).ShowDialog(this);
            await RefreshCompletionMarksAsync();
            await LoadPreferredCollectionAsync();
        };

        var lemmas = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Load Lemmas…", MinWidth = 150 };
        lemmas.Click += async (_, _) =>
        {
            await new LemmaIngestWindow().ShowDialog(this);
            await RefreshCompletionMarksAsync();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(ingest);
        row.Children.Add(lemmas);
        section.Children.Add(row);
        section.Children.Add(SetupLook.Text(
            "Ingest Corpus takes the text repositories' \"data\" folders. Load Lemmas handles lemma data and dictionaries.",
            "caption"));

        _otherControls.Add(ingest);
        _otherControls.Add(lemmas);
        return section;
    }

    /// <summary>
    /// The library file and the download folder, side by side because they
    /// are the same kind of choice - where something large goes - and someone
    /// who came here to put the library on another disk is usually here to move
    /// the downloads too.
    /// </summary>
    private Control BuildPlacesSection()
    {
        var root = new StackPanel();

        var library = SetupLook.Section("Library File",
            "Where the library, tags, and bookmarks are stored. Point at a different file to keep separate libraries.");
        library.Children.Add(_libraryPath);
        var libraryButton = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Library File…", MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left };
        libraryButton.Click += async (_, _) => await ChooseLibraryFileAsync();
        library.Children.Add(libraryButton);
        root.Children.Add(library);

        var folder = SetupLook.Section("Download Folder",
            "Where downloaded texts, dictionaries and word-form data are kept - about nine gigabytes for a full set. "
            + "Sets the folders shown above; anything already downloaded stays where it is.");
        folder.Children.Add(_folderPath);
        var folderButton = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Download Folder…", MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left };
        folderButton.Click += async (_, _) => await ChooseDataFolderAsync();
        folder.Children.Add(folderButton);
        root.Children.Add(folder);

        _otherControls.Add(libraryButton);
        _otherControls.Add(folderButton);
        RefreshPlaces();
        return root;
    }

    private void RefreshPlaces()
    {
        _libraryPath.Text = DbConnectionFactory.DatabasePath ?? "No library is open.";
        _folderPath.Text = DataFolderSettings.Root;
    }

    /// <summary>
    /// Word index and AI translation, each opening on its own. The index check
    /// counts distinct indexed lines against every line in the library, and it
    /// once ran every time this window opened, so glancing at the sources paid
    /// for a scan nobody asked for. Behind a button, only someone who wants the
    /// answer waits for it.
    /// </summary>
    private Control BuildToolsSection()
    {
        var section = SetupLook.Section("Tools");

        var wordIndex = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Word Index…", MinWidth = 150 };
        wordIndex.Click += async (_, _) => await new WordIndexWindow().ShowDialog(this);
        section.Children.Add(ToolRow(wordIndex,
            "Check whether the word index covers everything ingested, and rebuild it. This is what makes lemma-aware search fast."));

        var translation = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "AI Translation…", MinWidth = 150 };
        translation.Click += async (_, _) => await SettingsFeature.OpenAiSettingsAsync(this);
        section.Children.Add(ToolRow(translation,
            "API keys for Claude and Gemini, and whether to confirm before anything is sent. Optional - the app is fully offline without them."));

        _otherControls.Add(wordIndex);
        _otherControls.Add(translation);
        return section;
    }

    private static Control ToolRow(Button button, string description)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
        DockPanel.SetDock(button, Dock.Left);
        button.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(button);
        var text = SetupLook.Text(description, "caption");
        text.Margin = new Thickness(14, 4, 0, 0);
        row.Children.Add(text);
        return row;
    }

    /// <summary>
    /// Reading preferences, in the one window that already collects everything
    /// that isn't a corpus. They are in Settings as well; these write the same
    /// preferences.
    /// </summary>
    private Control BuildReadingSection()
    {
        var section = SetupLook.Section("Reading");

        var reopen = new CheckBox { Content = "Open where I last left off", IsChecked = ReadingPosition.ReopenOnLaunch };
        reopen.IsCheckedChanged += (_, _) => ReadingPosition.ReopenOnLaunch = reopen.IsChecked == true;
        section.Children.Add(reopen);
        section.Children.Add(SetupLook.Text(
            "Reopens the passage you were last reading when the app starts. Turn it off if opening a long work at launch "
            + "feels slow - your place is still remembered either way, so switching it back on picks up where you were.",
            "caption"));

        var pickerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        pickerRow.Children.Add(new TextBlock { Text = "Open works from:", VerticalAlignment = VerticalAlignment.Center });
        pickerRow.Children.Add(_preferredCollection);
        section.Children.Add(pickerRow);
        _preferredCollection.SelectionChanged += (_, _) =>
        {
            if (_loadingPreferredCollection) return;
            PreferredCollectionSettings.Preferred = (_preferredCollection.SelectedItem as CollectionOption)?.Key;
        };
        section.Children.Add(SetupLook.Text(
            "When more than one collection has the same work - Perseus and First1KGreek both carry the Agamemnon - this is "
            + "the one it opens on. The others stay in the edition dropdown; only which is already selected changes. Leave "
            + "it on no preference and works open on whichever edition sorts first.", "caption"));

        return section;
    }

    /// <summary>
    /// Offers the collections actually present in the library, plus no
    /// preference at all.
    ///
    /// Disabled with fewer than two of them: with one collection nothing ever
    /// overlaps, so there is no choice to make, and a live control implying
    /// otherwise would be a small lie. Visible rather than hidden, because a
    /// setting that appears only once a second corpus is installed is a setting
    /// nobody discovers. A stored preference naming a collection not installed
    /// is left alone rather than reset, so reinstalling it brings it back.
    /// </summary>
    private async Task LoadPreferredCollectionAsync()
    {
        if (!DbConnectionFactory.IsConfigured) return;

        List<string> collections;
        try
        {
            collections = await Task.Run(() => _editionRepo.GetCollectionsAsync());
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "listing collections in Advanced Setup");
            return;
        }

        _loadingPreferredCollection = true;
        try
        {
            var options = new List<CollectionOption> { new(null, "No preference") };
            options.AddRange(collections
                .Select(k => new CollectionOption(k, SetupDataSourceCatalog.DescribeCollection(k)))
                .OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase));

            _preferredCollection.ItemsSource = options;

            var stored = PreferredCollectionSettings.Preferred;
            _preferredCollection.SelectedItem = options.FirstOrDefault(c => c.Key != null
                && string.Equals(c.Key, stored, StringComparison.OrdinalIgnoreCase)) ?? options[0];
            _collectionCount = collections.Count;
            _preferredCollection.IsEnabled = collections.Count > 1 && _runningRow == null;
        }
        finally
        {
            _loadingPreferredCollection = false;
        }
    }

    /// <summary>
    /// Asks the library about each row and sets its mark - so the marks say
    /// what is really in the library, rather than echoing a status message
    /// that resets the moment this window is closed and reopened.
    ///
    /// Reachable with no library open yet, from the guided assistant's welcome
    /// or a new Mac: with nothing to query, every mark is honestly "not done"
    /// rather than an exception.
    /// </summary>
    private async Task RefreshCompletionMarksAsync()
    {
        RefreshPlaces();

        if (!DbConnectionFactory.IsConfigured)
        {
            foreach (var row in _rows) row.Mark.State = StepState.Pending;
            return;
        }

        var rows = _rows.ToList();
        var states = await Task.Run(async () =>
        {
            var results = new List<StepState>();
            foreach (var row in rows)
            {
                var complete = await row.Source.CheckComplete();
                var partial = !complete && row.Source.CheckHasSomeContent != null && await row.Source.CheckHasSomeContent();
                results.Add(complete ? StepState.Complete : partial ? StepState.Attention : StepState.Pending);
            }

            return results;
        });

        for (var i = 0; i < rows.Count; i++)
        {
            if (!ReferenceEquals(rows[i], _runningRow)) rows[i].Mark.State = states[i];
        }
    }

    private async Task RunRowAsync(
        Row row, Button button,
        Func<string, IProgress<string>, CancellationToken, Task<IngestOutcome>> action, bool fetch)
    {
        var destination = (row.Destination.Text ?? string.Empty).Trim();
        if (destination.Length == 0)
        {
            await Alert.Info(this, "Nothing to do", "Choose a destination folder first.");
            return;
        }

        // Token first, so a Cancel arriving on the very first click has
        // something to cancel.
        _cts = new CancellationTokenSource();
        _runningRow = row;
        _runningButton = button;
        _runningButtonText = button.Content as string ?? string.Empty;
        SetAllRowsEnabled(false);
        row.Mark.State = StepState.Running;

        var progress = new Progress<string>(message => row.Status.Text = message);

        try
        {
            var outcome = await SetupRunner.RunAsync(
                row.Source, destination, action, fetch, progress,
                status => row.Status.Text = status, "Fetching...", "Ingesting...", _cts.Token);

            row.Status.Text = "Done - " + outcome.Describe(row.Source.Title);
            await SetupSkipReport.ShowIfAnyAsync(this, row.Source.Title, outcome);
        }
        catch (OperationCanceledException)
        {
            row.Status.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            // The raw message, deliberately - see SetupRunner.DescribeError.
            row.Status.Text = "Failed - see message.";
            CrashLog.Write(ex, $"Advanced Setup: {row.Source.Title}");
            await Alert.Error(this, "Setup step failed", ex.Message);
        }
        finally
        {
            button.Content = _runningButtonText;
            _runningRow = null;
            _runningButton = null;

            if (DbConnectionFactory.IsConfigured)
            {
                try
                {
                    await _host.ReloadLibraryAsync();
                }
                catch (Exception ex)
                {
                    CrashLog.Write(ex, "reloading the library after Advanced Setup");
                }
            }

            if (_closeWhenStopped)
            {
                // The fetch has stopped, which is what the window was being
                // held open to establish.
                Close();
            }
            else
            {
                SetAllRowsEnabled(true);
                await RefreshCompletionMarksAsync();
                await LoadPreferredCollectionAsync();
            }
        }
    }

    /// <summary>
    /// Every row's buttons, except the one that started the running step - that
    /// one stays live and says Cancel. Until 3.10.1 it was disabled with the
    /// rest on Windows, which left nothing to press while a fetch ran.
    ///
    /// The library file, the download folder and the manual ingests are locked
    /// with the rows. Changing the download folder mid-download would leave the
    /// running step writing to one folder and every later step reading another;
    /// changing the library file mid-ingest would send the rest of the ingest
    /// into a different library altogether.
    /// </summary>
    private void SetAllRowsEnabled(bool enabled)
    {
        foreach (var row in _rows)
        {
            foreach (var button in new[] { row.Action, row.Secondary })
            {
                if (button == null) continue;

                var isRunning = ReferenceEquals(button, _runningButton);
                button.IsEnabled = enabled || isRunning;
                if (isRunning && !enabled) button.Content = "Cancel";
            }

            row.Destination.IsEnabled = enabled;
            row.Browse.IsEnabled = enabled;
        }

        foreach (var control in _otherControls) control.IsEnabled = enabled;
        _preferredCollection.IsEnabled = enabled && _collectionCount > 1;
    }

    /// <summary>Asks the running fetch or ingest to stop; the runner reports "Stopped." on the row when it does.</summary>
    private void CancelRun()
    {
        if (_cts is null || _cts.IsCancellationRequested) return;

        if (_runningButton != null) _runningButton.IsEnabled = false;
        if (_runningRow != null) _runningRow.Status.Text = "Stopping...";

        _cts.Cancel();
    }

    /// <summary>
    /// Closing this window stops what it is running, and asks before it does -
    /// a close box that let a download carry on unseen would take the progress
    /// display away from a download that was still running.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_runningRow == null || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;

        // Held open until the runner's finally calls Close.
        e.Cancel = true;
        if (!_closeWhenStopped && !_askingToStop) _ = AskToStopAndCloseAsync(_runningRow.Source.Title);
    }

    private async Task AskToStopAndCloseAsync(string title)
    {
        _askingToStop = true;
        try
        {
            var stop = await Alert.Confirm(this, "Stop and close?",
                $"{title} is still running. Closing this window stops it.\n\n"
                + "Whatever has already been installed is kept, and the step can be run again later.\n\n"
                + "The window stays up until it has actually stopped, which can take a few seconds, and then closes itself.",
                confirm: "Stop and Close", cancel: "Keep Running");

            if (!stop || _runningRow == null) return;

            _closeWhenStopped = true;
            CancelRun();
        }
        finally
        {
            _askingToStop = false;
        }
    }

    /// <summary>
    /// The Windows "Database Location" dialog: a file to open or create, and a
    /// button to make it the library. A different file is an entirely different
    /// library, so everything here is asked again afterwards.
    /// </summary>
    private async Task ChooseLibraryFileAsync()
    {
        var box = new TextBox
        {
            Text = LibraryFile.Current,
            MinWidth = 440,
            FontFamily = (FontFamily)Application.Current!.FindResource("Cc.MonoFont")!,
            FontSize = 12
        };

        var dialog = new Window
        {
            Title = "Library File",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var open = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Open Existing…" };
        open.Click += async (_, _) => { if (await LibraryFile.ChooseExistingAsync(dialog) is { } path) box.Text = path; };
        var create = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "New File…" };
        create.Click += async (_, _) => { if (await LibraryFile.ChooseNewAsync(dialog, box.Text ?? LibraryFile.Current) is { } path) box.Text = path; };

        string? chosen = null;
        var use = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Use This Library File", Classes = { "accent" }, IsDefault = true };
        use.Click += (_, _) => { chosen = box.Text; dialog.Close(); };
        var cancel = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => dialog.Close();

        var pick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        pick.Children.Add(open);
        pick.Children.Add(create);

        var confirm = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        confirm.Children.Add(cancel);
        confirm.Children.Add(use);

        var layout = new StackPanel { Margin = new Thickness(22, 18), Spacing = 12, MaxWidth = 560 };
        layout.Children.Add(new TextBlock { Text = "Library File", Classes = { "h2" } });
        layout.Children.Add(SetupLook.Text(
            "The one file that holds the library, tags and bookmarks. Choose an existing one - a library copied from the PC is "
            + "used as it is - or name a new one to start an empty library.", "muted"));
        layout.Children.Add(box);
        layout.Children.Add(pick);
        layout.Children.Add(confirm);
        dialog.Content = layout;

        await dialog.ShowDialog(this);
        if (string.IsNullOrWhiteSpace(chosen)) return;

        try
        {
            if (!await LibraryFile.UseAsync(_host, chosen.Trim())) return;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "Advanced Setup: the library file");
            await Alert.Error(this, "Library File", ex.Message);
            return;
        }

        await RefreshCompletionMarksAsync();
        await LoadPreferredCollectionAsync();
        await Alert.Info(this, "Library File", "Library location updated. The library has been reloaded from the new file.");
    }

    /// <summary>
    /// Chooses where downloads go - the counterpart of the library file, for the
    /// far larger of the two things this application puts on a disk.
    /// </summary>
    private async Task ChooseDataFolderAsync()
    {
        var picked = await Files.FolderAsync(this, "Choose where downloaded texts and dictionaries should be kept");
        if (picked == null) return;

        if (!DataFolderSettings.TryPrepare(picked, out var chosen, out var error))
        {
            await Alert.Warn(this, "Download folder", $"That folder can't be used: {error}");
            return;
        }

        if (MacDataFolder.SamePath(chosen, DataFolderSettings.Root)) return;

        DataFolderSettings.Root = chosen;

        // As the guided assistant does. Nothing resets a static cache, so a map
        // opened earlier in this session would go on drawing the geometry it
        // read from the old folder.
        NaturalEarthCoastline.InvalidateCache();

        var freeGb = MacDataFolder.FreeGigabytesAt(chosen);
        var room = freeGb == null
            ? string.Empty
            : freeGb < DataFolderSettings.ComfortableFreeGigabytes
                ? $"\n\nThat disk has {freeGb:N1} GB free, and a full set needs about {DataFolderSettings.FullSetGigabytes:N0} GB."
                : $"\n\nThat disk has {freeGb:N0} GB free.";

        // Said before anything else, because it is the one thing here that
        // costs the reader something they would not get back.
        var synced = MacDataFolder.SyncedBy(chosen) is { } sync
            ? $"\n\nBe aware: this folder is synced by {sync.Service}{(sync.Reason != null ? $" - {sync.Reason}" : string.Empty)}. "
              + $"The downloads are about {DataFolderSettings.FullSetGigabytes:N0} GB in tens of thousands of small files, and "
              + $"{sync.Service} would upload all of it. Choose Download Folder again to pick a folder outside it."
            : string.Empty;

        BuildRows();
        await RefreshCompletionMarksAsync();

        var message =
            $"Downloads will go to:\n\n{chosen}{synced}\n\n"
            + "Anything already downloaded stays where it is - nothing is moved or deleted, and your library is unaffected, "
            + "since ingested texts live in the library file rather than in these folders. The map is read from this folder "
            + "each time it opens, so if you have already fetched it, run that step once more." + room + "\n\n"
            + "The folders above now point at the new place.";

        if (synced.Length > 0) await Alert.Warn(this, "Download folder", message);
        else await Alert.Info(this, "Download folder", message);
    }
}
