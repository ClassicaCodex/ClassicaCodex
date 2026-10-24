using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// One step at a time, in plain language, with no repository addresses or
/// file paths on screen - the front door for someone who just wants Classica
/// Codex to work and has never seen a destination folder in their life. The
/// Mac counterpart of GuidedSetupForm, laid out as a Mac setup assistant: the
/// steps down the left with where each one stands, one step to a page.
///
/// It does exactly the downloading and ingesting Advanced Setup does, through
/// the same SetupDataSourceCatalog, so a collection added to the catalogue on
/// Windows is a step here the moment it is merged. This window is a way of
/// presenting that work, not a second implementation of it.
///
/// The first two numbered steps are about places rather than data: the
/// library file everything is written into, and the folder the downloads go
/// to. Both come before anything is fetched, because asking later would mean
/// asking someone to move gigabytes they had already downloaded.
/// </summary>
public sealed class GuidedSetupWindow : FeatureWindow
{
    private readonly IReaderHost _host;

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

    // Step 0 is the welcome; then the library file, the download folder, one
    // step per data source, the word index, and the finish. Named rather than
    // hardcoded so that inserting a step does not mean re-deriving every
    // other offset by hand.
    private int _currentStep;
    internal const int DatabaseStepIndex = 1;
    internal const int DataFolderStepIndex = DatabaseStepIndex + 1;
    internal int FirstSourceStepIndex => DataFolderStepIndex + 1;
    internal int WordIndexStepIndex => FirstSourceStepIndex + _sources.Count;
    internal int FinishStepIndex => WordIndexStepIndex + 1;
    internal int TotalSteps => FinishStepIndex + 1;

    private bool _databaseComplete;
    private bool _dataFolderComplete;
    private readonly List<bool> _sourceComplete = new();

    /// <summary>
    /// Whether each step has content without having finished - the state an
    /// interrupted ingest leaves. Kept beside the completion flags and
    /// refreshed with them, because asking is a database round trip and
    /// RenderStep is not the place to make one.
    /// </summary>
    private readonly List<bool> _sourcePartial = new();
    private bool _wordIndexComplete;
    private long _indexedLines;
    private long _totalLines;

    private CancellationTokenSource? _cts;

    /// <summary>
    /// True from the moment a step starts until its finally block runs. Decides
    /// what the action button does when pressed and whether closing the window
    /// has to ask first.
    /// </summary>
    private bool _stepRunning;
    private int _runningStep = -1;

    /// <summary>
    /// Set when someone closes the window with a step running and confirms
    /// they want it stopped. The window then stays up until the step has
    /// actually stopped, and closes itself.
    ///
    /// Because "did it stop?" otherwise has no answer. Letting the window go at
    /// once looks exactly like letting it go while the work carries on - which
    /// is what the Windows form did before this, for up to an hour and several
    /// gigabytes. Waiting makes the window's own disappearance the signal.
    /// </summary>
    private bool _closeWhenStopped;
    private bool _askingToStop;

    /// <summary>
    /// What the action button said before it became Cancel, so that ending the
    /// step can put it back at once rather than leaving it to the RenderStep
    /// at the far end of the finally block - see SetNavEnabled.
    /// </summary>
    private string _actionLabelBeforeStep = string.Empty;

    private readonly Heartbeat _heartbeat;

    // The step list down the left.
    private readonly StackPanel _stepList = new() { Spacing = 1 };
    private readonly List<(ToggleButton Button, StateMark Mark)> _stepRows = new();
    private ScrollViewer _stepScroll = null!;

    // The three faces of the right-hand side.
    private Control _welcomePage = null!;
    private Control _finishPage = null!;
    private Panel _stepPage = null!;

    private readonly ContentControl _tileHost = new();
    private readonly TextBlock _stepIndicator = new() { Classes = { "section" } };
    private readonly StateMark _statusMark = new(22);
    private readonly TextBlock _titleLabel = new() { Classes = { "h1" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _descriptionLabel = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 21 };
    private readonly TextBox _pathBox = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _browseButton = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Choose…", MinWidth = 96 };
    private readonly Button _newFileButton = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "New File…", MinWidth = 96 };
    private DockPanel _pathRow = null!;
    private readonly WrapPanel _links = new() { Orientation = Orientation.Horizontal };
    private readonly StateMark _readinessMark = new(14);
    private readonly TextBlock _readinessLabel = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private StackPanel _readinessRow = null!;
    private readonly Button _actionButton = SetupLook.BigButton(string.Empty, accent: true);
    private readonly Button _secondaryButton = SetupLook.BigButton(string.Empty);
    private readonly ProgressBar _progressBar = new() { Height = 6, MinHeight = 6, Minimum = 0, Maximum = 100, IsVisible = false };
    private readonly TextBlock _statusLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _elapsedLabel = new() { Classes = { "caption" }, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };

    /// <summary>
    /// Where a step's report lands, for the steps whose output is a report.
    ///
    /// Everything a step had to say used to go through the single status line,
    /// one report overwriting the last, so a twenty-line Menota survey flashed
    /// past and left only its final line. Most steps have nothing to put here
    /// and it stays hidden for them.
    /// </summary>
    private readonly TextBox _outputBox = SetupLook.LogBox(104);

    private readonly Button _backButton = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Back", MinWidth = 96, MinHeight = 32 };
    private readonly Button _nextButton = new() { Classes = { "accent" }, MinWidth = 150, MinHeight = 32, HorizontalContentAlignment = HorizontalAlignment.Center };

    public GuidedSetupWindow(IReaderHost host)
    {
        _host = host;
        OnNavigate = host.NavigateToPassageAsync;
        OnOpenWork = host.OpenWorkAsync;

        // Before the catalogue is built, because it reads the folder as it
        // builds - see MacDataFolder.AdoptProposalIfUnchosen.
        MacDataFolder.AdoptProposalIfUnchosen();
        _sources = BuildSources();

        // The two flags that need no query, so the first render is right
        // about them and Next is guarded correctly before the counts arrive.
        _databaseComplete = DbConnectionFactory.IsConfigured;
        _dataFolderComplete = DataFolderSettings.LooksUsable(DataFolderSettings.Root);

        Title = "Set Up Classica Codex";
        Width = 1000;
        Height = 700;
        MinWidth = 820;
        MinHeight = 560;

        _heartbeat = new Heartbeat(elapsed => _elapsedLabel.Text = $"Elapsed: {elapsed:mm\\:ss}");

        Content = BuildLayout();
        BuildStepList();
        WireButtons();

        Closing += OnWindowClosing;

        // Rendered before the counts, not after. Only the marks depend on them,
        // and asking whether the word index is up to date means counting
        // distinct rows in a table of seventy million on a full library -
        // twenty-odd seconds. The first pass shows the right step with plain
        // marks and the second fills them in.
        RenderStep();
        Opened += async (_, _) =>
        {
            await RefreshAllCompletionAsync();
            RenderStep();
        };
    }

    /// <summary>The step showing now - for tests, and for the sidebar.</summary>
    internal int CurrentStep => _currentStep;

    /// <summary>The catalogue as this window built it, in the order it presents it.</summary>
    internal IReadOnlyList<SetupDataSource> Sources => _sources;

    /// <summary>What the step list says each step's state is.</summary>
    internal StepState StateOfStep(int step) => _stepRows[step].Mark.State;

    /// <summary>The status line, as the reader would read it.</summary>
    internal string StatusText => _statusLabel.Text ?? string.Empty;

    internal string ActionText => _actionButton.Content as string ?? string.Empty;

    internal string PathText => _pathBox.Text ?? string.Empty;

    private List<SetupDataSource> BuildSources() =>
        SetupDataSourceCatalog.Build(_authorRepo, _lemmaRepo, _definitionRepo, _artifactRepo, _editionRepo);

    private Control BuildLayout()
    {
        // The step list, on the sidebar's colour, as a Mac assistant draws it.
        var sidebarHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(18, 22, 16, 14) };
        sidebarHeader.Children.Add(Art.Tile("setup", "Setup", 34));
        var headerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headerText.Children.Add(new TextBlock { Text = "Setup", Classes = { "h2" } });
        headerText.Children.Add(new TextBlock { Text = "Texts, dictionaries, lemma data", Classes = { "caption" } });
        sidebarHeader.Children.Add(headerText);

        _stepScroll = new ScrollViewer
        {
            Content = _stepList,
            Padding = new Thickness(8, 0, 8, 12),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var sidebar = new DockPanel();
        DockPanel.SetDock(sidebarHeader, Dock.Top);
        sidebar.Children.Add(sidebarHeader);
        sidebar.Children.Add(_stepScroll);

        var sidebarHost = new Border { Child = sidebar, BorderThickness = new Thickness(0, 0, 1, 0) };
        sidebarHost.Bind(Border.BackgroundProperty, sidebarHost.GetResourceObservable("Cc.Sidebar"));
        sidebarHost.Bind(Border.BorderBrushProperty, sidebarHost.GetResourceObservable("Cc.Rule"));

        _welcomePage = BuildWelcomePage();
        _finishPage = BuildFinishPage();
        _stepPage = BuildStepPage();

        var pages = new Grid { Margin = new Thickness(40, 30, 40, 18) };
        pages.Children.Add(_welcomePage);
        pages.Children.Add(_finishPage);
        pages.Children.Add(_stepPage);

        var nav = new DockPanel { Margin = new Thickness(24, 12, 24, 14), LastChildFill = false };
        DockPanel.SetDock(_backButton, Dock.Left);
        DockPanel.SetDock(_nextButton, Dock.Right);
        nav.Children.Add(_backButton);
        nav.Children.Add(_nextButton);

        var navHost = new Border { Child = nav, BorderThickness = new Thickness(0, 1, 0, 0) };
        navHost.Bind(Border.BorderBrushProperty, navHost.GetResourceObservable("Cc.Rule"));

        var right = new DockPanel();
        DockPanel.SetDock(navHost, Dock.Bottom);
        right.Children.Add(navHost);
        right.Children.Add(pages);
        right.Bind(Panel.BackgroundProperty, right.GetResourceObservable("Cc.Page"));

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("268,*") };
        Grid.SetColumn(right, 1);
        root.Children.Add(sidebarHost);
        root.Children.Add(right);
        return root;
    }

    private static Control BuildWelcomePage()
    {
        var stack = new StackPanel { Spacing = 14, MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var tile = Art.Tile("setup", "Setup", 88);
        tile.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(tile);
        stack.Children.Add(new TextBlock { Text = "Welcome to Classica Codex", Classes = { "display" }, FontSize = 30, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        stack.Children.Add(new TextBlock
        {
            Text = "First, a single file needs a place to live - your library, tags, and bookmarks all go there, "
                   + "and it's what the rest of this assistant writes into. After that, a few open data sources need "
                   + "to be downloaded: the ancient texts themselves, dictionaries to look words up in, and some "
                   + "word-form data that makes search smarter. Altogether it's usually about 9 gigabytes, and two to "
                   + "three hours on a decent connection with everything selected - mostly unattended, and the Greek "
                   + "word-form data is about an hour of that on its own. You only need to do this once, and you can "
                   + "skip any step and come back to it later.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            LineHeight = 21
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Each step on the left does one thing, with a plain explanation of what it's for and why it's worth waiting for.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            LineHeight = 21
        });
        stack.Children.Add(new Border
        {
            Classes = { "notice" },
            Margin = new Thickness(0, 8, 0, 0),
            Child = SetupLook.Text(
                "In a hurry, or already have these files somewhere? Every step after the library file is optional - "
                + "clicking Next without running one just moves on, so you can go straight through to Finish having "
                + "only set up the library, then choose Setup from the File menu afterward and pick Advanced Setup "
                + "for more control over where things go.")
        });

        return new ScrollViewer { Content = stack };
    }

    private static Control BuildFinishPage()
    {
        var stack = new StackPanel { Spacing = 14, MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var tile = Art.Tile("authors", "Library", 88);
        tile.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(tile);
        stack.Children.Add(new TextBlock { Text = "You're all set!", Classes = { "display" }, FontSize = 30, Margin = new Thickness(0, 6, 0, 0) });
        stack.Children.Add(new TextBlock
        {
            Text = "That's the guided setup - close this and start reading. If you skipped anything, or want to load "
                   + "more later, Setup is always there in the File menu and among the tools in the sidebar; choose "
                   + "Advanced Setup from there for everything on one screen with more control over where things go.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            LineHeight = 21
        });

        return new ScrollViewer { Content = stack };
    }

    private Panel BuildStepPage()
    {
        // Header: the step's tile, where it sits in the sequence, its name and
        // the mark that says where it stands.
        _tileHost.VerticalAlignment = VerticalAlignment.Top;
        _tileHost.Margin = new Thickness(0, 2, 18, 0);

        _statusMark.Margin = new Thickness(0, 4, 10, 0);
        _statusMark.VerticalAlignment = VerticalAlignment.Top;
        var titleRow = new DockPanel();
        DockPanel.SetDock(_statusMark, Dock.Left);
        titleRow.Children.Add(_statusMark);
        titleRow.Children.Add(_titleLabel);

        var heading = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(_stepIndicator);
        heading.Children.Add(titleRow);

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(_tileHost, Dock.Left);
        header.Children.Add(_tileHost);
        header.Children.Add(heading);

        // The description grows with its text and scrolls when the step has
        // more to say than there is room for. A fixed-height label clips, and
        // the Patrologia Latina step has the longest description here - the
        // part about provisional reference numbers and what that means for a
        // note tied to one - which ended mid-sentence on Windows until its
        // label was made to scroll.
        var descriptionScroll = new ScrollViewer
        {
            Content = new Border { Child = _descriptionLabel, MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0, 0, 14, 0) },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        // Shown on the two steps that are about a place, and read-only on a
        // source that shows the folder its files are put in by hand.
        _pathBox.FontFamily = (FontFamily)Application.Current!.FindResource("Cc.MonoFont")!;
        _pathBox.FontSize = 12;
        _browseButton.MinHeight = 32;
        _browseButton.VerticalContentAlignment = VerticalAlignment.Center;
        _newFileButton.MinHeight = 32;
        _newFileButton.VerticalContentAlignment = VerticalAlignment.Center;
        _pathRow = new DockPanel();
        var pathButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 0, 0) };
        pathButtons.Children.Add(_browseButton);
        pathButtons.Children.Add(_newFileButton);
        DockPanel.SetDock(pathButtons, Dock.Right);
        _pathRow.Children.Add(pathButtons);
        _pathRow.Children.Add(_pathBox);

        // Beside the links rather than below them, so the answer to "have I
        // got that file yet" sits where the question was asked.
        _readinessRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        _readinessRow.Children.Add(_readinessMark);
        _readinessRow.Children.Add(_readinessLabel);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(_actionButton);
        buttons.Children.Add(_secondaryButton);

        var statusRow = new DockPanel();
        DockPanel.SetDock(_elapsedLabel, Dock.Right);
        statusRow.Children.Add(_elapsedLabel);
        statusRow.Children.Add(_statusLabel);

        _links.Margin = new Thickness(0, 0, 0, 0);
        var actions = new StackPanel { Spacing = 10, Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(_pathRow);
        actions.Children.Add(_links);
        actions.Children.Add(_readinessRow);
        actions.Children.Add(buttons);
        actions.Children.Add(_progressBar);
        actions.Children.Add(statusRow);
        actions.Children.Add(_outputBox);

        var page = new StepPagePanel();
        page.Children.Add(header);
        page.Children.Add(descriptionScroll);
        page.Children.Add(actions);
        return page;
    }

    /// <summary>
    /// One row per step, built once and restated by RenderStep, so the list
    /// keeps its scroll position and its rows while the marks change.
    /// </summary>
    private void BuildStepList()
    {
        _stepList.Children.Clear();
        _stepRows.Clear();

        for (var step = 0; step < TotalSteps; step++)
        {
            var mark = new StateMark(16);
            var number = new TextBlock
            {
                Text = step == 0 || step == FinishStepIndex ? string.Empty : step.ToString(),
                Classes = { "caption" },
                Width = 22,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var label = new TextBlock
            {
                Text = StepName(step),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12.5
            };

            var row = new DockPanel();
            DockPanel.SetDock(mark, Dock.Left);
            DockPanel.SetDock(number, Dock.Left);
            row.Children.Add(mark);
            row.Children.Add(number);
            row.Children.Add(label);
            mark.Margin = new Thickness(0, 0, 4, 0);

            var button = new ToggleButton
            {
                Classes = { "toolbar" },
                Content = row,
                Height = double.NaN,
                MinHeight = 30,
                Padding = new Thickness(8, 5),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ToolTip.SetTip(button, StepName(step));

            var target = step;
            button.Click += async (_, _) => await GoToStepAsync(target);

            _stepRows.Add((button, mark));
            _stepList.Children.Add(button);
        }
    }

    private string StepName(int step)
    {
        if (step == 0) return "Welcome";
        if (step == DatabaseStepIndex) return "Library File";
        if (step == DataFolderStepIndex) return "Download Folder";
        if (step == FinishStepIndex) return "Finish";
        if (step == WordIndexStepIndex) return "Build Word Index";
        return _sources[step - FirstSourceStepIndex].Title;
    }

    private void WireButtons()
    {
        _browseButton.Click += async (_, _) =>
        {
            // Two steps share this button and they want different panels: one
            // names a file, the other a folder to fill.
            if (_currentStep == DataFolderStepIndex)
            {
                var folder = await Files.FolderAsync(this, "Choose where downloaded texts and dictionaries should be kept");
                if (folder != null) _pathBox.Text = folder;
                return;
            }

            var existing = await LibraryFile.ChooseExistingAsync(this);
            if (existing != null) _pathBox.Text = existing;
        };

        _newFileButton.Click += async (_, _) =>
        {
            var chosen = await LibraryFile.ChooseNewAsync(this, _pathBox.Text ?? LibraryFile.Current);
            if (chosen != null) _pathBox.Text = chosen;
        };

        // Doubles as Cancel while a step is running, rather than a second
        // button appearing beside it: the button that started the step is
        // where someone looks to stop it.
        _actionButton.Click += async (_, _) =>
        {
            if (_stepRunning)
            {
                CancelStep();
                return;
            }

            await RunCurrentStepActionAsync();
        };

        _secondaryButton.Click += async (_, _) => await RunCurrentStepSecondaryAsync();

        _backButton.Click += async (_, _) => await GoToStepAsync(_currentStep - 1);

        _nextButton.Click += async (_, _) =>
        {
            if (_currentStep == FinishStepIndex)
            {
                Close();
                return;
            }

            await GoToStepAsync(_currentStep + 1);
        };
    }

    /// <summary>
    /// Moves to a step - from Next, Back or a click in the step list - once the
    /// step being left has let go.
    ///
    /// The two guards Next has on Windows apply to every way of leaving, since
    /// the list makes it possible to leave a step without pressing Next.
    /// </summary>
    internal async Task GoToStepAsync(int target)
    {
        if (_stepRunning || target < 0 || target >= TotalSteps || target == _currentStep)
        {
            RenderStep();
            return;
        }

        if (target > DatabaseStepIndex && !_databaseComplete)
        {
            await Alert.Info(this, "Library Not Set Up Yet",
                "Set up the library file first - everything else in setup needs somewhere to write to.");
            if (_currentStep != DatabaseStepIndex)
            {
                _currentStep = DatabaseStepIndex;
            }

            RenderStep();
            return;
        }

        // A folder typed into the box, or picked with Choose, and then not
        // confirmed with the button. Next is the obvious thing to press after
        // choosing something, and dropping the choice silently would send the
        // whole download to the folder that had just been rejected. Going back
        // is not choosing, and leaves the folder as it was.
        if (_currentStep == DataFolderStepIndex && target > _currentStep
            && !MacDataFolder.SamePath(_pathBox.Text ?? string.Empty, DataFolderSettings.Root))
        {
            await ApplyDataFolderAsync();

            // Left on this step when it would not take: the status line has
            // already said why, and moving on would bury it. So would drawing
            // the step again, which restates the status from the folder in
            // force and puts that folder back in the box over the one typed -
            // which is what the Windows form does here, error and all.
            if (!MacDataFolder.SamePath(_pathBox.Text ?? string.Empty, DataFolderSettings.Root))
            {
                for (var step = 0; step < _stepRows.Count; step++) _stepRows[step].Button.IsChecked = step == _currentStep;
                return;
            }
        }

        _currentStep = target;
        RenderStep();
    }

    /// <summary>
    /// Re-checks the library for every source and the word index - not just
    /// the step on screen, since Advanced Setup, or an earlier step in this
    /// same sitting, could have changed any of them.
    ///
    /// Checks whether a library is open at all first: this window can be
    /// reached with none (that is what the Library File step is for), and
    /// every other check needs one to query - without the guard they would
    /// throw instead of reporting "not done yet" like everything else.
    ///
    /// Off the UI thread, all of it. The source checks are indexed counts, but
    /// there are more than twenty of them, and the word-index count reads
    /// every row of the index.
    /// </summary>
    internal async Task RefreshAllCompletionAsync()
    {
        _databaseComplete = DbConnectionFactory.IsConfigured;

        // "Could downloads go there?", not "is it already there?". On a fresh
        // install the folder has never been created, and judging it by
        // existence would greet every newcomer with a warning beside a
        // perfectly good default. Checked without creating anything: someone
        // who has not reached this step should not have folders made for them.
        _dataFolderComplete = DataFolderSettings.LooksUsable(DataFolderSettings.Root);

        if (!_databaseComplete)
        {
            _sourceComplete.Clear();
            _sourcePartial.Clear();
            foreach (var _ in _sources)
            {
                _sourceComplete.Add(false);
                _sourcePartial.Add(false);
            }

            _wordIndexComplete = false;
            _indexedLines = 0;
            _totalLines = 0;
            return;
        }

        var sources = _sources;
        var states = await Task.Run(async () =>
        {
            var results = new List<(bool Complete, bool Partial)>();
            foreach (var source in sources)
            {
                var complete = await source.CheckComplete();
                var partial = !complete && source.CheckHasSomeContent != null && await source.CheckHasSomeContent();
                results.Add((complete, partial));
            }

            return results;
        });

        _sourceComplete.Clear();
        _sourcePartial.Clear();
        foreach (var (complete, partial) in states)
        {
            _sourceComplete.Add(complete);
            _sourcePartial.Add(partial);
        }

        // "Is every line indexed?", not "does the index have any rows?".
        //
        // The has-any-rows question is true of an index built once and never
        // touched again, so ingesting a collection afterwards left this step
        // reading "Already built" while the new collection was entirely absent
        // from it. No ingest service touches the word index, and a re-ingest
        // gives every line a new id, orphaning the rows the old ones had. The
        // welcome page tells a newcomer that anything skipped can be added
        // later, which is exactly the way into this.
        var (totalLines, indexedLines) = await Task.Run(async () => (
            await _wordIndexRepo.GetTextNodeCountAsync(),
            await _wordIndexRepo.GetIndexedTextNodeCountAsync()));

        _indexedLines = indexedLines;
        _totalLines = totalLines;
        _wordIndexComplete = totalLines > 0 && indexedLines >= totalLines;
    }

    /// <summary>Each step's mark, from the flags. Never a database call.</summary>
    private StepState StepStateFor(int step)
    {
        if (_stepRunning && step == _runningStep) return StepState.Running;
        if (step == 0 || step == FinishStepIndex) return StepState.Pending;
        if (step == DatabaseStepIndex) return _databaseComplete ? StepState.Complete : StepState.Pending;
        if (step == DataFolderStepIndex) return DataFolderState();

        if (step == WordIndexStepIndex)
        {
            return _wordIndexComplete ? StepState.Complete
                : _indexedLines > 0 ? StepState.Attention
                : StepState.Pending;
        }

        var index = step - FirstSourceStepIndex;
        if (index < _sourceComplete.Count && _sourceComplete[index]) return StepState.Complete;
        if (index < _sourcePartial.Count && _sourcePartial[index]) return StepState.Attention;
        return StepState.Pending;
    }

    /// <summary>
    /// Which of three marks the Download Folder step shows: usable and roomy,
    /// usable but worth a look, or not usable at all. A folder that will work
    /// but is short of room is not an error, and not a tick either - a tick
    /// beside the words "only 4 GB free" is the reading someone takes away,
    /// and it is the wrong one.
    /// </summary>
    private StepState DataFolderState()
    {
        if (!_dataFolderComplete) return StepState.Problem;

        var root = DataFolderSettings.Root;

        // A synced folder is a warning however much local room it has, which
        // is the whole point: the disk is not what runs out.
        if (MacDataFolder.SyncedBy(root) != null) return StepState.Attention;

        var freeGb = MacDataFolder.FreeGigabytesAt(root);

        // Unknown free space is not a warning. A network volume may not answer,
        // and refusing to tick a folder for being unmeasurable would flag the
        // kind of location most likely to have room.
        return freeGb != null && freeGb < DataFolderSettings.ComfortableFreeGigabytes
            ? StepState.Attention
            : StepState.Complete;
    }

    private void RenderStep()
    {
        var isWelcome = _currentStep == 0;
        var isDatabase = _currentStep == DatabaseStepIndex;
        var isDataFolder = _currentStep == DataFolderStepIndex;
        var isFinish = _currentStep == FinishStepIndex;
        var isContent = !isWelcome && !isFinish;
        var isPathStep = isDatabase || isDataFolder;

        _welcomePage.IsVisible = isWelcome;
        _finishPage.IsVisible = isFinish;
        _stepPage.IsVisible = isContent;

        for (var step = 0; step < _stepRows.Count; step++)
        {
            var (button, mark) = _stepRows[step];
            button.IsChecked = step == _currentStep;
            mark.State = StepStateFor(step);
            mark.Opacity = step == 0 || step == FinishStepIndex ? 0 : 1;
        }

        _backButton.IsEnabled = !isWelcome && !_stepRunning;
        _nextButton.Content = isWelcome ? "Get Started" : isFinish ? "Start Reading" : "Next";

        if (!isContent) return;

        _stepIndicator.Text = $"STEP {_currentStep} OF {WordIndexStepIndex}";
        _statusMark.State = StepStateFor(_currentStep);

        // Hidden unless a step asks for them, and cleared, so nothing a step
        // showed carries over to the next.
        _pathRow.IsVisible = isPathStep;
        _pathBox.IsReadOnly = !isPathStep;
        _browseButton.IsVisible = isPathStep;
        _newFileButton.IsVisible = isDatabase;
        _browseButton.Content = isDatabase ? "Open Existing…" : "Choose…";
        _links.Children.Clear();
        _links.IsVisible = false;
        _readinessRow.IsVisible = false;
        _secondaryButton.IsVisible = false;
        _outputBox.IsVisible = false;
        _outputBox.Text = string.Empty;
        _statusLabel.Classes.Set("warning", false);

        if (isDatabase)
        {
            _tileHost.Content = Art.Tile("authors", "Database", 60);
            _titleLabel.Text = "Library File";
            _descriptionLabel.Text =
                "This is the one file everything else in setup writes into - your library, tags, and bookmarks, "
                + "plus everything you download next. The file below works fine for almost everyone; only change it "
                + "if you want your library to live somewhere specific - on another disk, say. A library copied from "
                + "the PC can be chosen here too, with Open Existing, and is used as it is.";
            _pathBox.Text = LibraryFile.Current;
            _actionButton.Content = _databaseComplete ? "Use This Library File" : "Prepare Library File";
            _statusLabel.Text = _databaseComplete
                ? $"Ready: {DbConnectionFactory.DatabasePath}"
                : "Not set up yet.";
        }
        else if (isDataFolder)
        {
            _tileHost.Content = Art.Tile("setup", "Download", 60);
            _titleLabel.Text = "Download Folder";
            _descriptionLabel.Text =
                "Everything downloaded in the steps after this one goes here: the texts themselves, the "
                + "dictionaries, and the word-form data. All of it together comes to about nine gigabytes, most of "
                + "that the Greek and Latin word-form data, so if your Mac's disk is short of room this is the thing "
                + "to put somewhere else - an external disk works. The folder below is fine if you have the space. "
                + "Keep it outside iCloud Drive, Dropbox and the like: these are working copies of public data that "
                + "can be fetched again at any time, and uploading tens of thousands of them would fill a cloud plan "
                + "for nothing. Your own library, tags and bookmarks live in the library file from the last step, "
                + "not here.";
            _pathBox.Text = DataFolderSettings.Root;
            _actionButton.Content = "Use This Folder";
            _statusLabel.Text = _dataFolderComplete
                ? DescribeDataFolder()
                : "Pick a folder that exists, or one that can be created.";

            // In the warning colour when the folder will upload or will not
            // work - the two cases where reading past the line costs something.
            _statusLabel.Classes.Set("warning", !_dataFolderComplete || MacDataFolder.SyncedBy(DataFolderSettings.Root) != null);
        }
        else
        {
            var stepInSources = _currentStep - FirstSourceStepIndex;
            if (stepInSources < _sources.Count)
            {
                RenderSourceStep(_sources[stepInSources], stepInSources);
            }
            else
            {
                RenderWordIndexStep();
            }
        }

        _elapsedLabel.Text = string.Empty;
        _progressBar.Value = 0;
    }

    private void RenderSourceStep(SetupDataSource source, int stepInSources)
    {
        var complete = stepInSources < _sourceComplete.Count && _sourceComplete[stepInSources];
        var (tileKey, glyph) = SetupLook.ArtFor(source);

        _tileHost.Content = Art.Tile(tileKey, glyph, 60);
        _titleLabel.Text = source.Title;
        _descriptionLabel.Text = source.PlainLanguageDescription.Replace("\r\n", "\n");

        // A source can name its own action. "Download & Install" is wrong for a
        // step that opens a website and inspects a folder, and a button that
        // misdescribes itself is worse than a plain one.
        _actionButton.Content = source.ActionButtonText
                                ?? (complete ? "Re-download & Re-install" : "Download & Install");

        foreach (var link in source.Links)
        {
            var button = SetupLook.Link(link.Text, link.Url, message => _statusLabel.Text = message);
            button.Margin = new Thickness(0, 0, 22, 4);
            _links.Children.Add(button);
        }

        _links.IsVisible = source.Links.Count > 0;

        if (source.SecondaryButtonText is { } secondary && source.RunSecondary is not null)
        {
            _secondaryButton.Content = secondary;
            _secondaryButton.IsVisible = true;
        }

        if (source.CheckReadiness is { } check)
        {
            // Never let a check throw into RenderStep - the step would fail to
            // draw, and a file that cannot be inspected is a problem to report
            // rather than a crash.
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

            _readinessMark.State = state;
            _readinessLabel.Text = readiness.Message;
            _readinessLabel.Bind(TextBlock.ForegroundProperty, _readinessLabel.GetResourceObservable(SetupLook.InkFor(state)));
            _readinessRow.IsVisible = true;
        }

        if (source.ShowDestinationPath)
        {
            _pathRow.IsVisible = true;
            _pathBox.Text = source.DefaultDestination;
            _outputBox.IsVisible = true;
        }

        // Three states, not two. A step that has content but never finished is
        // the one worth naming: it is what a cancelled download or a closed
        // lid leaves behind, and calling it "Not loaded yet" would mislead in
        // its own direction as much as "Already loaded." would in the other.
        var partial = stepInSources < _sourcePartial.Count && _sourcePartial[stepInSources];

        _statusLabel.Text = complete
            ? "Already loaded."
            : partial
                ? "Partly loaded - the last run didn't finish. Run it again to complete it."
                : "Not loaded yet.";
    }

    private void RenderWordIndexStep()
    {
        _tileHost.Content = Art.Tile("search", "Search", 60);
        _titleLabel.Text = "Build Word Index";
        _descriptionLabel.Text =
            "Makes searching fast once the texts above are loaded - without it, every search has to scan the whole "
            + "library from scratch. Run this once everything above is done; safe to run again any time. Takes about "
            + "fifteen minutes.";

        // Three states, not two. "Built once and now out of date" is the one
        // that used to read as finished, and the one a reader is most likely
        // to be in: build the index, ingest another collection afterwards, and
        // the lines from it are absent from the index and so from search.
        var partial = !_wordIndexComplete && _indexedLines > 0;

        _actionButton.Content = _indexedLines > 0 ? "Rebuild Index" : "Build Word Index";

        _statusLabel.Text = _wordIndexComplete
            ? $"Up to date - {_indexedLines:N0} lines indexed."
            : partial
                ? $"Out of date - {_indexedLines:N0} of {_totalLines:N0} lines indexed. "
                  + $"{_totalLines - _indexedLines:N0} added since the last build will not turn up in searches until this is rebuilt."
                : "Not built yet.";
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
            var source = _sources[stepInSources];
            await RunSourceActionAsync(source, source.RunIngest, fetch: true);
        }
        else if (_currentStep == WordIndexStepIndex)
        {
            await RunWordIndexAsync();
        }
    }

    private async Task RunCurrentStepSecondaryAsync()
    {
        var stepInSources = _currentStep - FirstSourceStepIndex;
        if (stepInSources < 0 || stepInSources >= _sources.Count) return;

        var source = _sources[stepInSources];
        if (source.RunSecondary is null) return;

        // On the UI thread, deliberately: this is where a step gets to ask
        // something before it starts, and the runner is past the point where a
        // dialog can be shown.
        if (!await MenotaReview.PrepareSecondaryAsync(source, this)) return;

        await RunSourceActionAsync(source, source.RunSecondary, fetch: false);
    }

    /// <summary>
    /// Opens or creates the library in the box. Arriving at this step already
    /// is the deliberate choice, so there is no confirmation on top of it.
    /// </summary>
    private async Task RunDatabaseSetupAsync()
    {
        var path = (_pathBox.Text ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            await Alert.Info(this, "Nothing to Do", "Choose where the library file should go first.");
            return;
        }

        SetNavEnabled(false);
        _statusLabel.Text = "Preparing the library...";

        try
        {
            _statusLabel.Text = await LibraryFile.UseAsync(_host, path)
                ? "Library ready."
                : "The library couldn't be opened - see message.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Something went wrong - see message.";
            CrashLog.Write(ex, "setup step: the library file");
            await Alert.Error(this, "Setup Step Failed", SetupRunner.DescribeError(ex, "the library"));
        }
        finally
        {
            SetNavEnabled(true);
            await RefreshAllCompletionAsync();
            RenderStep();
        }
    }

    /// <summary>
    /// Rebuilds the step list against the current download folder. The
    /// catalogue reads the folder once, as it builds, and hands each source a
    /// finished destination - so the sources made when this window opened are
    /// still aimed at wherever the folder was then. The count and order are
    /// fixed by the catalogue, so the completion flags stay valid; only the
    /// paths move.
    /// </summary>
    private void RebuildSources() => _sources = BuildSources();

    /// <summary>
    /// Takes the folder in the box, having satisfied itself that downloads can
    /// actually be written there.
    ///
    /// Creating it is part of accepting it: the folder someone types, or makes
    /// with New Folder in the panel, may not exist yet, and finding that out at
    /// the start of an hour-long download is finding out too late. So it is
    /// created and written to here, while there is still a person looking.
    /// </summary>
    internal async Task ApplyDataFolderAsync()
    {
        if (!DataFolderSettings.TryPrepare(_pathBox.Text, out var chosen, out var error))
        {
            _statusLabel.Text = error!.StartsWith("Enter ", StringComparison.Ordinal)
                ? "Enter a folder, or click Choose."
                : $"Can't use that folder: {error}";
            _statusLabel.Classes.Set("warning", true);
            _statusMark.State = StepState.Problem;
            return;
        }

        var moved = !MacDataFolder.SamePath(chosen, DataFolderSettings.Root);
        DataFolderSettings.Root = chosen;
        _pathBox.Text = chosen;

        // Every source's destination is derived from the folder, so the steps
        // after this one point at the old place until they are made again.
        RebuildSources();

        // The map holds its geometry in a static cache keyed to nothing, so a
        // map opened before the folder moved would go on drawing what it read
        // from the old one. Cheap to drop; it reloads on next use.
        NaturalEarthCoastline.InvalidateCache();

        if (moved)
        {
            // Navigation off while the steps are asked again - quick, but not
            // instant, and the button that started it stays under the pointer.
            SetNavEnabled(false);
            try
            {
                await RefreshSourceCompletionAsync();
            }
            finally
            {
                SetNavEnabled(true);
            }
        }

        _dataFolderComplete = true;
        RenderStep();
        _statusLabel.Text = DescribeDataFolder();

        if (moved) await WarnAboutAlreadyDownloadedDataAsync();
    }

    /// <summary>
    /// Re-asks every source whether it is done, after the folder beneath them
    /// changed. Deliberately not RefreshAllCompletionAsync, which also counts
    /// the word index - half a minute on a full library, and nothing a
    /// download folder can affect. Most sources decide from the library, which
    /// a folder cannot change, but the map decides from a file in this folder,
    /// and a future source that reads from disk should not be left stale.
    /// </summary>
    private async Task RefreshSourceCompletionAsync()
    {
        if (!_databaseComplete) return;

        var sources = _sources;
        var states = await Task.Run(async () =>
        {
            var results = new List<(bool Complete, bool Partial)>();
            foreach (var source in sources)
            {
                var complete = await source.CheckComplete();
                results.Add((complete, !complete && source.CheckHasSomeContent != null && await source.CheckHasSomeContent()));
            }

            return results;
        });

        for (var i = 0; i < states.Count && i < _sourceComplete.Count; i++)
        {
            _sourceComplete[i] = states[i].Complete;
            if (i < _sourcePartial.Count) _sourcePartial[i] = states[i].Partial;
        }
    }

    /// <summary>
    /// The chosen folder and what is free on its volume, which is the number
    /// that decides whether the next hour is going to work - led by a sync
    /// warning when there is one, because the free-space line is the
    /// reassuring one and it is measuring the wrong thing then.
    /// </summary>
    internal static string DescribeDataFolder()
    {
        var root = DataFolderSettings.Root;
        var freeGb = MacDataFolder.FreeGigabytesAt(root);

        if (MacDataFolder.SyncedBy(root) is { } synced)
        {
            var why = synced.Reason != null ? $" - {synced.Reason}" : string.Empty;
            return $"{root} is synced by {synced.Service}{why}. The downloads are about "
                   + $"{DataFolderSettings.FullSetGigabytes:N0} GB in tens of thousands of small files, and "
                   + $"{synced.Service} would upload all of it. Choose a folder outside it - nothing here needs "
                   + "backing up, because your library lives in the library file, not in this folder.";
        }

        if (freeGb == null) return $"Ready: {root}{MacDataFolder.TemporaryVolumeNote(root)}";

        return freeGb < DataFolderSettings.ComfortableFreeGigabytes
            ? $"{root} - only {freeGb:N1} GB free, and a full set needs about "
              + $"{DataFolderSettings.FullSetGigabytes:N0} GB. You can still go on and skip the larger steps."
            : $"Ready: {root} ({freeGb:N0} GB free){MacDataFolder.TemporaryVolumeNote(root)}";
    }

    /// <summary>
    /// Said once, when the folder changes with data already downloaded
    /// elsewhere - because what happens next is not obvious, and is mostly
    /// reassuring.
    /// </summary>
    private async Task WarnAboutAlreadyDownloadedDataAsync()
    {
        if (!Directory.Exists(DataFolderSettings.DefaultRoot) && !_sourceComplete.Any(c => c)) return;

        await Alert.Info(this, "Downloads already on disk",
            "Anything you have already downloaded stays where it is - nothing is moved or deleted, and your "
            + "library is unaffected, because the texts you have ingested are in the library file rather than in "
            + "these folders.\n\n"
            + "What changes is where the steps after this one look, so any step you have not run yet will download "
            + "into the new folder.\n\n"
            + "A few steps read from this folder rather than only downloading into it, and those will need running "
            + "again or pointing at the old folder. The map is one: it is read every time the map opens rather than "
            + "being ingested, and it is under a megabyte. Adding Stephanus and Bekker numbers is another - it reads "
            + "the texts back out of this folder rather than fetching anything, so it will find nothing in an empty "
            + "one. And if you imported Medieval Nordic manuscripts, their files and the work divisions you "
            + "confirmed are in the old folder too; those are your own decisions rather than a download, and worth "
            + "copying across in the Finder rather than redoing.");
    }

    /// <param name="fetch">
    /// False for a secondary action, which works on files already in the
    /// destination folder and must not re-download anything to do it.
    /// </param>
    internal async Task RunSourceActionAsync(
        SetupDataSource source,
        Func<string, IProgress<string>, CancellationToken, Task<IngestOutcome>> action,
        bool fetch)
    {
        // Token before the button, so a Cancel arriving on the very first
        // click has something to cancel.
        _cts = new CancellationTokenSource();
        BeginCancellableStep();
        _progressBar.IsIndeterminate = true;
        _progressBar.IsVisible = true;
        _heartbeat.Start();

        var progress = new Progress<string>(message =>
        {
            _statusLabel.Text = message;
            if (_outputBox.IsVisible) AppendLog(message);
        });

        if (_outputBox.IsVisible) _outputBox.Text = string.Empty;

        try
        {
            Directory.CreateDirectory(source.DefaultDestination);

            var outcome = await SetupRunner.RunAsync(
                source, source.DefaultDestination, action, fetch, progress,
                status => _statusLabel.Text = status, "Downloading...", "Installing...", _cts.Token);

            // The status line carries both outcomes; the alert opens only for
            // the one worth interrupting someone for. See SetupSkipReport.
            _statusLabel.Text = outcome.Describe(source.Title);
            await SetupSkipReport.ShowIfAnyAsync(this, source.Title, outcome);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Something went wrong - see message.";
            CrashLog.Write(ex, $"setup step: {source.Title}");
            await Alert.Error(this, "Setup Step Failed", SetupRunner.DescribeError(ex, source.Title));
        }
        finally
        {
            _heartbeat.Stop();

            // Whatever the step managed is in the library now - a cancelled
            // ingest keeps what it wrote - and the reader's sidebar should show
            // it without waiting for this window to close.
            await ReloadHostLibraryAsync();

            if (_closeWhenStopped)
            {
                // The step has stopped, which is the thing the window was being
                // held open to establish. Repainting it now would only be a
                // flicker before it goes.
                _stepRunning = false;
                Close();
            }
            else
            {
                _progressBar.IsIndeterminate = false;
                _progressBar.IsVisible = false;
                SetNavEnabled(true);
                await RefreshAllCompletionAsync();

                // RenderStep rewrites the status from the step's completion
                // state and clears the log, which is right when arriving at a
                // step and wrong the instant a run has finished: it replaced the
                // result - the count of skipped files, the plans to go and
                // confirm - with "Not loaded yet.", so every run looked as if it
                // had done nothing.
                var finalStatus = _statusLabel.Text;
                var finalLog = _outputBox.Text;

                RenderStep();

                _statusLabel.Text = finalStatus;
                if (_outputBox.IsVisible) _outputBox.Text = finalLog;
            }
        }
    }

    private void AppendLog(string message)
    {
        _outputBox.Text = (_outputBox.Text ?? string.Empty) + message + "\n";
        _outputBox.CaretIndex = _outputBox.Text.Length;
    }

    private async Task ReloadHostLibraryAsync()
    {
        if (!DbConnectionFactory.IsConfigured) return;

        try
        {
            await _host.ReloadLibraryAsync();
        }
        catch (Exception ex)
        {
            // The step's own result matters more than the sidebar catching up;
            // the library is reloaded again when setup closes.
            CrashLog.Write(ex, "reloading the library after a setup step");
        }
    }

    internal async Task RunWordIndexAsync()
    {
        _cts = new CancellationTokenSource();
        BeginCancellableStep();
        _progressBar.IsIndeterminate = true;
        _progressBar.IsVisible = true;
        _heartbeat.Start();

        var progress = new Progress<WordIndexProgress>(p =>
        {
            if (p.TotalNodes > 0 && p.Phase == "Indexing")
            {
                var percent = (int)Math.Min(100, p.NodesProcessed * 100 / p.TotalNodes);
                _progressBar.IsIndeterminate = false;
                _progressBar.Value = percent;
                _statusLabel.Text = $"Indexing... {p.NodesProcessed:N0}/{p.TotalNodes:N0} lines ({percent}%).";
            }
            else
            {
                _progressBar.IsIndeterminate = true;
                _statusLabel.Text = p.Phase;
            }
        });

        try
        {
            var service = new WordIndexService();
            var token = _cts.Token;
            await Task.Run(() => service.BuildAsync(progress, token), token);
            _statusLabel.Text = "Word index built.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Something went wrong - see message.";
            CrashLog.Write(ex, "setup step: the word index");
            await Alert.Error(this, "Build Failed", SetupRunner.DescribeError(ex, "the word index"));
        }
        finally
        {
            _heartbeat.Stop();

            if (_closeWhenStopped)
            {
                _stepRunning = false;
                Close();
            }
            else
            {
                _progressBar.IsIndeterminate = false;
                _progressBar.IsVisible = false;
                SetNavEnabled(true);
                await RefreshAllCompletionAsync();

                // Kept across RenderStep for the same reason the source runner
                // keeps it: RenderStep would throw away the one word the person
                // who pressed Cancel was waiting to read.
                var finalStatus = _statusLabel.Text;
                RenderStep();
                _statusLabel.Text = finalStatus;
            }
        }
    }

    private void SetNavEnabled(bool enabled)
    {
        if (enabled && _stepRunning)
        {
            _stepRunning = false;
            _runningStep = -1;

            // Restored here, not by the RenderStep at the end of the runner's
            // finally block. Between the two is the completion recount - half a
            // minute on a full library - and for all of it the button still said
            // "Cancel" while being enabled, so pressing the button labelled
            // Cancel started the download again.
            if (_actionLabelBeforeStep.Length > 0) _actionButton.Content = _actionLabelBeforeStep;
        }

        _actionButton.IsEnabled = enabled;
        _secondaryButton.IsEnabled = enabled;
        _backButton.IsEnabled = enabled && _currentStep > 0;
        _nextButton.IsEnabled = enabled;
        _pathBox.IsEnabled = enabled;
        _browseButton.IsEnabled = enabled;
        _newFileButton.IsEnabled = enabled;
        foreach (var (button, _) in _stepRows) button.IsEnabled = enabled;
    }

    /// <summary>
    /// Starts a step that can be stopped, leaving its own button live as the
    /// Cancel button.
    ///
    /// Separate from SetNavEnabled because only two of the four things this
    /// window waits on can be cancelled. Preparing the library and recounting
    /// after a folder change finish on their own in well under a second and
    /// hold no token; offering to cancel those would be a button that says it
    /// can stop something it cannot.
    /// </summary>
    private void BeginCancellableStep()
    {
        SetNavEnabled(false);

        _stepRunning = true;
        _runningStep = _currentStep;
        _actionLabelBeforeStep = _actionButton.Content as string ?? string.Empty;
        _actionButton.IsEnabled = true;
        _actionButton.Content = "Cancel";
        _statusMark.State = StepState.Running;
        _stepRows[_currentStep].Mark.State = StepState.Running;
    }

    /// <summary>
    /// Asks the running step to stop. Both runners catch the cancellation and
    /// report "Stopped." - this is only the thing that makes them do it. The
    /// button is disabled until the step has unwound, so a second press cannot
    /// arrive while it does.
    /// </summary>
    internal void CancelStep()
    {
        if (_cts is null || _cts.IsCancellationRequested) return;

        _actionButton.IsEnabled = false;
        _statusLabel.Text = "Stopping...";
        _cts.Cancel();
    }

    /// <summary>
    /// Closing the window stops the work, and says so first.
    ///
    /// On Windows it once did neither: the form went while the download or
    /// the ingest carried on against a token nobody had cancelled, still
    /// filling the download folder and writing to the library, with no
    /// progress shown anywhere. The question is asked rather than assumed
    /// because the other reading is just as likely - someone who has waited
    /// forty minutes for a corpus and hits the wrong button should not lose it
    /// to a silent close.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // Only a close the reader asked for waits. Quitting the app or logging
        // out is not a close that can be held up for a download to unwind.
        if (!_stepRunning || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;

        // Held open either way - this is the first close, about to ask, or an
        // impatient second one. The runner's finally closes the window when
        // the step has genuinely stopped.
        e.Cancel = true;
        if (!_closeWhenStopped && !_askingToStop) _ = AskToStopAndCloseAsync();
    }

    private async Task AskToStopAndCloseAsync()
    {
        _askingToStop = true;
        try
        {
            var stop = await Alert.Confirm(this, "Stop and close?",
                "A setup step is still running. Closing this window stops it.\n\n"
                + "Whatever it has already installed is kept, and you can run the step again later from Setup.\n\n"
                + "The window stays up until the step has actually stopped, which can take a few seconds, and then "
                + "closes itself.",
                confirm: "Stop and Close", cancel: "Keep Running");

            if (!stop || !_stepRunning) return;

            _closeWhenStopped = true;
            CancelStep();
        }
        finally
        {
            _askingToStop = false;
        }
    }

    /// <summary>
    /// Lays a step out top to bottom - its heading, its description, its
    /// controls - with the controls straight after the text, and the
    /// description scrolling only when there is not room for all of it.
    ///
    /// A plain grid could do one or the other: pin the controls to the bottom,
    /// which left a hand's width of empty page between a one-line description
    /// and its button, or let them follow the text, which pushed the button off
    /// the window on the longest steps. This gives the description whatever
    /// the heading and the controls leave, and no more than it needs.
    /// </summary>
    private sealed class StepPagePanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var (header, body, actions) = (Children[0], Children[1], Children[2]);
            var unbounded = new Size(availableSize.Width, double.PositiveInfinity);

            header.Measure(unbounded);
            actions.Measure(unbounded);
            body.Measure(new Size(availableSize.Width, Room(availableSize.Height)));

            var height = header.DesiredSize.Height + body.DesiredSize.Height + actions.DesiredSize.Height;
            return new Size(
                double.IsInfinity(availableSize.Width) ? Math.Max(header.DesiredSize.Width, actions.DesiredSize.Width) : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? height : Math.Min(height, availableSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var (header, body, actions) = (Children[0], Children[1], Children[2]);

            var y = 0.0;
            header.Arrange(new Rect(0, y, finalSize.Width, header.DesiredSize.Height));
            y += header.DesiredSize.Height;

            var bodyHeight = Math.Min(body.DesiredSize.Height, Room(finalSize.Height));
            body.Arrange(new Rect(0, y, finalSize.Width, bodyHeight));
            y += bodyHeight;

            actions.Arrange(new Rect(0, y, finalSize.Width, actions.DesiredSize.Height));
            return finalSize;
        }

        private double Room(double height) =>
            double.IsInfinity(height)
                ? double.PositiveInfinity
                : Math.Max(0, height - Children[0].DesiredSize.Height - Children[2].DesiredSize.Height);
    }
}
