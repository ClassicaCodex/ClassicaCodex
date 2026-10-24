using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Checking and rebuilding the word index, on its own - the Windows
/// WordIndexForm.
///
/// It used to sit inside the setup wizard, and the staleness check ran
/// whenever that opened. The check counts distinct lines in the word index
/// against every line in the library - two aggregates over well past a
/// million rows - so simply looking at the data-source list paid for a scan
/// nobody had asked for. Behind a button, the cost is only paid by someone
/// who actually wants to know.
/// </summary>
public sealed class WordIndexWindow : FeatureWindow
{
    private readonly WordIndexRepository _wordIndexRepo = new();

    private readonly StateMark _mark = new(20);
    private readonly TextBlock _status = new() { Text = "Checking...", TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
    private readonly TextBlock _elapsed = new() { Classes = { "caption" } };
    private readonly ProgressBar _progress = new() { Height = 6, MinHeight = 6, Minimum = 0, Maximum = 100 };
    private readonly Button _build = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Build Word Index", Classes = { "accent" }, MinWidth = 160, IsEnabled = false };
    private readonly Button _close = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Close", MinWidth = 90 };

    private CancellationTokenSource? _cts;
    private readonly Heartbeat _heartbeat;

    /// <summary>
    /// Whether a rebuild actually ran, so a caller showing its own summary of
    /// the library can refresh it.
    /// </summary>
    public bool IndexWasRebuilt { get; private set; }

    public WordIndexWindow()
    {
        Title = "Word Index";
        Width = 760;
        Height = 330;
        MinWidth = 600;
        MinHeight = 300;

        _heartbeat = new Heartbeat(elapsed => _elapsed.Text = $"Running for {elapsed:mm\\:ss}...");

        var title = new DockPanel();
        _mark.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(_mark, Dock.Left);
        title.Children.Add(_mark);
        title.Children.Add(new TextBlock { Text = "Build Word Index", Classes = { "h2" }, VerticalAlignment = VerticalAlignment.Center });

        var tile = Art.Tile("search", "Search", 52);
        tile.Margin = new Thickness(0, 0, 18, 0);
        tile.VerticalAlignment = VerticalAlignment.Top;

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(title);
        body.Children.Add(SetupLook.Text(
            "This is what makes lemma-aware search fast - without it, each search scans the whole corpus once per word "
            + "form. Run it after loading or adding a corpus. Safe to rerun any time; it always rebuilds from scratch. "
            + "Takes about fifteen minutes.", "muted"));
        body.Children.Add(_progress);
        body.Children.Add(_status);
        body.Children.Add(_elapsed);

        var top = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        top.Children.Add(tile);
        top.Children.Add(body);

        _build.Click += async (_, _) => await BuildAsync();
        _close.Click += (_, _) => Close();

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(_build, Dock.Left);
        DockPanel.SetDock(_close, Dock.Right);
        buttons.Children.Add(_build);
        buttons.Children.Add(_close);
        buttons.LastChildFill = false;

        var layout = new DockPanel { Margin = new Thickness(24, 22, 24, 18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(top);
        Content = layout;

        Opened += async (_, _) => await RefreshStatusAsync();
        Closed += (_, _) =>
        {
            _cts?.Cancel();
            _heartbeat.Stop();
        };
    }

    internal string StatusText => _status.Text ?? string.Empty;

    internal StepState State => _mark.State;

    internal async Task RefreshStatusAsync()
    {
        if (!DbConnectionFactory.IsConfigured)
        {
            _mark.State = StepState.Problem;
            _status.Text = "No library is open yet - set one up first.";
            return;
        }

        _status.Text = "Checking...";

        try
        {
            if (!await Task.Run(() => _wordIndexRepo.HasDataAsync()))
            {
                _mark.State = StepState.Pending;
                _status.Text = "Not built yet.";
            }
            else
            {
                // The index is pure derived data with no automatic refresh -
                // ingesting a new source doesn't touch it. A count comparison is
                // what catches that silently: "has data" alone stayed true the
                // whole time Shakespeare's lines sat unindexed after a source was
                // added post-build.
                //
                // Both counts go to the thread pool together, because the second
                // one is expensive: Microsoft.Data.Sqlite's async methods run
                // synchronously, and counting distinct lines in an index keyed
                // (word, line) reads every one of its seventy million rows.
                var (totalLines, indexedLines) = await Task.Run(async () => (
                    await _wordIndexRepo.GetTextNodeCountAsync(),
                    await _wordIndexRepo.GetIndexedTextNodeCountAsync()));

                if (indexedLines >= totalLines)
                {
                    _mark.State = StepState.Complete;
                    _status.Text = $"Up to date - {indexedLines:N0} lines indexed.";
                }
                else
                {
                    _mark.State = StepState.Attention;
                    _status.Text =
                        $"Out of date - {indexedLines:N0} of {totalLines:N0} lines indexed. "
                        + $"{totalLines - indexedLines:N0} line(s) were added since the last build (likely a new source "
                        + "ingested afterward) and won't turn up in lemma-expansion searches like Auto-Tag's until this is rebuilt.";
                }
            }

            _build.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _mark.State = StepState.Problem;
            _status.Text = $"Couldn't check the index: {ex.Message}";
        }
    }

    private async Task BuildAsync()
    {
        var confirmed = await Alert.Confirm(this, "Build Word Index",
            "Build the word index over every ingested line?\n\nIt takes a few minutes on a full corpus and can be rebuilt any time.",
            confirm: "Build");
        if (!confirmed) return;

        _build.IsEnabled = false;
        _close.IsEnabled = false;
        _progress.IsIndeterminate = true;
        _mark.State = StepState.Running;
        _cts = new CancellationTokenSource();
        _heartbeat.Start();

        var progress = new Progress<WordIndexProgress>(p =>
        {
            if (p.TotalNodes > 0 && p.Phase == "Indexing")
            {
                var percent = (int)Math.Min(100, p.NodesProcessed * 100 / p.TotalNodes);
                _progress.IsIndeterminate = false;
                _progress.Value = percent;
                _status.Text = $"Indexing... {p.NodesProcessed:N0}/{p.TotalNodes:N0} lines ({percent}%), {p.EntriesWritten:N0} entries written.";
            }
            else
            {
                _progress.IsIndeterminate = true;
                _status.Text = p.Phase;
            }
        });

        try
        {
            var service = new WordIndexService();
            var token = _cts.Token;
            await Task.Run(() => service.BuildAsync(progress, token), token);

            IndexWasRebuilt = true;
            _status.Text = "Word index built.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _status.Text = "Failed - see message.";
            CrashLog.Write(ex, "building the word index");
            await Alert.Error(this, "Build failed", ex.Message);
        }
        finally
        {
            _heartbeat.Stop();
            _elapsed.Text = $"Finished in {_heartbeat.Elapsed:mm\\:ss}.";
            _progress.IsIndeterminate = false;
            _close.IsEnabled = true;
            await RefreshStatusAsync();
        }
    }
}
