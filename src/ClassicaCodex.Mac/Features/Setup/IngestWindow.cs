using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Ingests Perseus repositories already on disk - the Windows IngestForm,
/// reached from Advanced Setup's "Already have the files?" for someone who
/// cloned canonical-greekLit and canonical-latinLit themselves, or keeps them
/// somewhere else, and does not want a second copy downloaded.
/// </summary>
public sealed class IngestWindow : FeatureWindow
{
    private readonly IReaderHost _host;
    private readonly TextBox _greekPath = PathBox();
    private readonly TextBox _latinPath = PathBox();
    private readonly Button _start = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Start Ingest", Classes = { "accent" }, MinWidth = 140 };
    private readonly Button _cancel = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Cancel", MinWidth = 96, IsEnabled = false };
    private readonly ProgressBar _progress = new() { Height = 6, MinHeight = 6 };
    private readonly TextBlock _status = new() { Text = "Idle.", TextWrapping = TextWrapping.Wrap };

    // Ticks on its own regardless of what the ingest is doing - the actual
    // diagnostic. If it keeps counting the window is alive and the gap is
    // somewhere not being reported; if it freezes too, the window is blocked.
    private readonly TextBlock _elapsed = new() { Classes = { "caption" } };
    private readonly Heartbeat _heartbeat;
    private CancellationTokenSource? _cts;

    public IngestWindow(IReaderHost host)
    {
        _host = host;
        Title = "Ingest Perseus Corpus";
        Width = 640;
        // Sized to what it holds; the explicit height FeatureWindow starts every
        // window with would otherwise win over SizeToContent.
        ClearValue(HeightProperty);
        SizeToContent = SizeToContent.Height;
        MinWidth = 560;
        MinHeight = 300;
        CanResize = false;

        _heartbeat = new Heartbeat(elapsed => _elapsed.Text = $"Elapsed: {elapsed:mm\\:ss}");

        var layout = new StackPanel { Margin = new Thickness(24, 20), Spacing = 8 };
        layout.Children.Add(new TextBlock { Text = "Ingest Perseus Corpus", Classes = { "h2" }, Margin = new Thickness(0, 0, 0, 6) });
        layout.Children.Add(new TextBlock { Text = "canonical-greekLit data folder:" });
        layout.Children.Add(PathRow(_greekPath));
        layout.Children.Add(new TextBlock { Text = "canonical-latinLit data folder:", Margin = new Thickness(0, 6, 0, 0) });
        layout.Children.Add(PathRow(_latinPath));
        layout.Children.Add(SetupLook.Text(
            "Clone the repos first (git clone https://github.com/PerseusDL/canonical-greekLit and .../canonical-latinLit), "
            + "then point each box at that repo's \"data\" folder.", "caption"));

        layout.Children.Add(new Border { Height = 8 });
        layout.Children.Add(_progress);
        layout.Children.Add(_status);
        layout.Children.Add(_elapsed);

        _start.Click += async (_, _) => await StartAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(_start);
        buttons.Children.Add(_cancel);
        layout.Children.Add(buttons);

        Content = layout;

        // The Windows form let its window go while the ingest carried on, with
        // nothing left on screen to show it or stop it. Closing stops it here.
        Closed += (_, _) => _cts?.Cancel();
    }

    internal TextBox GreekPath => _greekPath;

    internal string StatusText => _status.Text ?? string.Empty;

    private static TextBox PathBox() => new()
    {
        FontFamily = (FontFamily)Application.Current!.FindResource("Cc.MonoFont")!,
        FontSize = 12,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private Control PathRow(TextBox box)
    {
        var browse = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Choose…", MinWidth = 96, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var folder = await Files.FolderAsync(this, "Choose the repository's data folder");
            if (folder != null) box.Text = folder;
        };

        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(box);
        return row;
    }

    internal async Task StartAsync()
    {
        var repoPaths = new List<(string DataPath, string Namespace)>();
        if (!string.IsNullOrWhiteSpace(_greekPath.Text)) repoPaths.Add((_greekPath.Text.Trim(), "greekLit"));
        if (!string.IsNullOrWhiteSpace(_latinPath.Text)) repoPaths.Add((_latinPath.Text.Trim(), "latinLit"));

        if (repoPaths.Count == 0)
        {
            await Alert.Warn(this, "Nothing to ingest", "Point at least one repo's data folder first.");
            return;
        }

        _start.IsEnabled = false;
        _cancel.IsEnabled = true;
        _progress.IsIndeterminate = true;
        _cts = new CancellationTokenSource();
        _heartbeat.Start();

        var progress = new Progress<IngestProgress>(p =>
            _status.Text = $"{p.CurrentAuthor}: {p.CurrentWork}  ({p.WorksProcessed}/{p.TotalWorks} textgroups)");

        try
        {
            var service = new PerseusIngestService();
            var token = _cts.Token;
            await Task.Run(() => service.IngestAsync(repoPaths, progress, token), token);

            // Both lists, and through the same reporter the setup steps use,
            // rather than a second hand-built message that can drift from it.
            var outcome = IngestOutcome.From(service.FailedFiles, service.RecoveredWithoutCatalog, service.FilesAttempted);
            _status.Text = outcome.Describe("Ingest");

            // The skip report decides for itself whether the skips are worth
            // an alert. When they are not, this still confirms the run
            // finished, because a manual ingest was started by a button and
            // wants an answer.
            await SetupSkipReport.ShowIfAnyAsync(this, "Ingest", outcome);
            if (!outcome.SkipsAreWorthInterrupting) await Alert.Info(this, "Done", outcome.Describe("Ingest"));
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _status.Text = "Failed - see message.";
            CrashLog.Write(ex, "ingesting a Perseus corpus by hand");
            await Alert.Error(this, "Ingest failed", ex.Message);
        }
        finally
        {
            _heartbeat.Stop();
            _progress.IsIndeterminate = false;
            _start.IsEnabled = true;
            _cancel.IsEnabled = false;

            if (DbConnectionFactory.IsConfigured) await _host.ReloadLibraryAsync();
        }
    }
}
