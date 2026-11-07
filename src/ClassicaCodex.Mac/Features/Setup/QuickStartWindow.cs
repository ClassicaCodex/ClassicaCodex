using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// The shortest way to a working library, for a device: the Greek and Latin
/// classics with their translations, the dictionaries, and the word index, one
/// after another with nothing to decide. The chooser that leads here says how
/// much it is, which Apple asks of an app that downloads what it needs to work.
///
/// Guided Setup is forty steps, and right for someone who wants to see them.
/// This is for someone who wants to read something.
/// </summary>
internal sealed class QuickStartWindow : FeatureWindow
{
    private static readonly string[] Collections = { "Ancient Greek Texts", "Ancient Latin Texts", "Dictionaries" };

    private readonly IReaderHost _host;
    private readonly List<(string Name, StateMark Mark, Func<CancellationToken, Task> Run)> _steps = new();
    private readonly TextBlock _status = SetupLook.Text("Starting...", "muted");
    private readonly ProgressBar _progress = new() { IsIndeterminate = true };
    private readonly Button _stop = new() { Content = "Stop", MinWidth = 96 };
    private readonly Button _again = new() { Content = "Try Again", MinWidth = 96, IsVisible = false };
    private readonly Button _done = new() { Content = "Start Reading", Classes = { "accent" }, MinWidth = 120, IsVisible = false };
    private CancellationTokenSource? _cts;
    private int _finished;

    public QuickStartWindow(IReaderHost host)
    {
        _host = host;
        Title = "Quick Start";
        Width = 520;
        Height = 430;

        var rows = new StackPanel { Spacing = 10 };
        var sources = SetupDataSourceCatalog.Build(
            new AuthorRepository(), new LemmaRepository(), new DefinitionRepository(), new ArtifactRepository(), new EditionRepository());

        foreach (var name in Collections)
        {
            var source = sources.First(s => s.Title.StartsWith(name, StringComparison.Ordinal));
            AddStep(rows, source.Title, cancel =>
            {
                Directory.CreateDirectory(source.DefaultDestination);
                return SetupRunner.RunAsync(
                    source, source.DefaultDestination, source.RunIngest, fetch: true,
                    new Progress<string>(_ => { }), text => _status.Text = text,
                    "Downloading...", "Installing...", cancel);
            });
        }

        AddStep(rows, "The word index, for search", cancel =>
        {
            var progress = new Progress<WordIndexProgress>(p => _status.Text = p.TotalNodes > 0 && p.Phase == "Indexing"
                ? $"Indexing... {p.NodesProcessed * 100 / p.TotalNodes}%"
                : p.Phase);
            return Task.Run(() => new WordIndexService().BuildAsync(progress, cancel), cancel);
        });

        _stop.Click += (_, _) => _cts?.Cancel();
        _again.Click += async (_, _) => await RunAsync();
        _done.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_stop);
        buttons.Children.Add(_again);
        buttons.Children.Add(_done);

        var layout = new StackPanel { Margin = new Thickness(26, 22), Spacing = 14 };
        layout.Children.Add(new TextBlock { Text = "Setting up your library", Classes = { "h1" } });
        layout.Children.Add(SetupLook.Text("Keep Classica Codex on screen until this finishes. iOS pauses an app that is switched away from.", "muted"));
        layout.Children.Add(rows);
        layout.Children.Add(_progress);
        layout.Children.Add(_status);
        layout.Children.Add(buttons);
        Content = layout;

        Opened += async (_, _) => await RunAsync();
    }

    private void AddStep(StackPanel rows, string name, Func<CancellationToken, Task> run)
    {
        var mark = new StateMark(16) { VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(mark);
        row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        rows.Children.Add(row);
        _steps.Add((name, mark, run));
    }

    /// <summary>Runs what is left, so trying again does not fetch what already arrived.</summary>
    private async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        _again.IsVisible = false;
        _stop.Content = "Stop";
        _stop.Click -= CloseThis;
        _progress.IsVisible = true;

        using var awake = SetupRunner.StayAwake?.Invoke();

        try
        {
            for (; _finished < _steps.Count; _finished++)
            {
                _steps[_finished].Mark.State = StepState.Running;
                await _steps[_finished].Run(_cts.Token);
                _steps[_finished].Mark.State = StepState.Complete;
                await _host.ReloadLibraryAsync();
            }

            _status.Text = "Your library is ready. There are more collections under Setup whenever you want them.";
            _stop.IsVisible = false;
            _done.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            Stopped("Stopped. Anything already installed is kept.");
        }
        catch (Exception ex)
        {
            var name = _steps[_finished].Name;
            CrashLog.Write(ex, $"quick start: {name}");
            _steps[_finished].Mark.State = StepState.Problem;
            Stopped(SetupRunner.DescribeError(ex, name).Split('\n')[0]);
        }
        finally
        {
            _progress.IsVisible = false;
        }
    }

    private void Stopped(string message)
    {
        _status.Text = message;
        _stop.Content = "Close";
        _stop.Click -= CloseThis;
        _stop.Click += CloseThis;
        _again.IsVisible = true;
    }

    private void CloseThis(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
