using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Loads lemma mappings or a dictionary from a folder already on disk - the
/// Windows LemmaIngestForm, reached from Advanced Setup's "Already have the
/// files?".
/// </summary>
public sealed class LemmaIngestWindow : FeatureWindow
{
    private readonly TextBox _path = new()
    {
        FontFamily = (FontFamily)Application.Current!.FindResource("Cc.MonoFont")!,
        FontSize = 12,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private readonly ComboBox _dataType = new() { ItemsSource = new[] { "Lemma mappings", "Dictionary (lexicon)" }, SelectedIndex = 0, MinWidth = 200 };
    private readonly ComboBox _language = new() { ItemsSource = new[] { "grc (Greek)", "lat (Latin)" }, SelectedIndex = 0, MinWidth = 150 };
    private readonly CheckBox _clearFirst = new() { Content = "Clear existing first", IsChecked = true };
    private readonly Button _start = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Load", Classes = { "accent" }, MinWidth = 140 };
    private readonly Button _cancel = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Cancel", MinWidth = 96, IsEnabled = false };
    private readonly ProgressBar _progress = new() { Height = 6, MinHeight = 6 };
    private readonly TextBlock _status = new() { Text = "Idle.", TextWrapping = TextWrapping.Wrap, MinHeight = 54 };

    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();
    private CancellationTokenSource? _cts;

    public LemmaIngestWindow()
    {
        Title = "Load Reference Data";
        Width = 760;
        ClearValue(HeightProperty);
        SizeToContent = SizeToContent.Height;
        MinWidth = 640;
        MinHeight = 360;
        CanResize = false;

        var layout = new StackPanel { Margin = new Thickness(24, 20), Spacing = 8 };
        layout.Children.Add(new TextBlock { Text = "Load Reference Data", Classes = { "h2" }, Margin = new Thickness(0, 0, 0, 4) });
        layout.Children.Add(SetupLook.Text(
            "Lemma mappings link inflected forms to dictionary headwords (λόγου → λόγος). Dictionaries add what those "
            + "headwords actually mean.", "muted"));
        layout.Children.Add(SetupLook.Text("Clone the data first, e.g.:", "muted"));
        var commands = new TextBlock
        {
            Text = "git clone https://github.com/gcelano/LemmatizedAncientGreekXML   (Greek lemmas)\n"
                   + "git clone https://github.com/PerseusDL/lexica                    (LSJ and Lewis & Short)",
            Classes = { "mono", "muted" },
            Margin = new Thickness(16, 0, 0, 6)
        };
        layout.Children.Add(commands);

        layout.Children.Add(new TextBlock { Text = "Folder containing the XML:" });
        var browse = new Button { HorizontalContentAlignment = HorizontalAlignment.Center, Content = "Choose…", MinWidth = 96, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var folder = await Files.FolderAsync(this, "Choose the folder containing the XML");
            if (folder != null) _path.Text = folder;
        };
        var pathRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        pathRow.Children.Add(browse);
        pathRow.Children.Add(_path);
        layout.Children.Add(pathRow);

        var options = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        options.Children.Add(Labelled("Data type:", _dataType));
        options.Children.Add(Labelled("Language:", _language));
        _clearFirst.Margin = new Thickness(0, 0, 0, 6);
        options.Children.Add(_clearFirst);
        layout.Children.Add(options);

        layout.Children.Add(_progress);
        layout.Children.Add(_status);

        _start.Click += async (_, _) => await RunAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(_start);
        buttons.Children.Add(_cancel);
        layout.Children.Add(buttons);

        Content = layout;

        Opened += async (_, _) => await ShowExistingCountAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    internal string StatusText => _status.Text ?? string.Empty;

    private static Control Labelled(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 22, 6) };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }

    internal async Task ShowExistingCountAsync()
    {
        try
        {
            var (lemmaCount, definitionCount) = await Task.Run(async () =>
                (await _lemmaRepo.CountAsync(), await _definitionRepo.CountAsync()));

            _status.Text = $"Currently loaded: {lemmaCount:N0} lemma mapping(s), {definitionCount:N0} dictionary entr(ies).";
        }
        catch (Exception ex)
        {
            _status.Text = $"Couldn't read existing counts: {ex.Message}";
        }
    }

    private async Task RunAsync()
    {
        var path = (_path.Text ?? string.Empty).Trim();
        if (path.Length == 0)
        {
            await Alert.Warn(this, "Nothing to load", "Point at the folder containing the XML first.");
            return;
        }

        var language = _language.SelectedIndex == 1 ? "lat" : "grc";
        var loadingDictionary = _dataType.SelectedIndex == 1;

        _start.IsEnabled = false;
        _cancel.IsEnabled = true;
        _progress.IsIndeterminate = true;
        _cts = new CancellationTokenSource();

        try
        {
            if (loadingDictionary) await LoadDictionaryAsync(path, language, _cts.Token);
            else await LoadLemmasAsync(path, language, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _status.Text = "Failed - see message.";
            CrashLog.Write(ex, "loading reference data by hand");
            await Alert.Error(this, "Load failed", ex.Message);
        }
        finally
        {
            _progress.IsIndeterminate = false;
            _start.IsEnabled = true;
            _cancel.IsEnabled = false;
        }
    }

    private async Task LoadLemmasAsync(string path, string language, CancellationToken cancellationToken)
    {
        if (_clearFirst.IsChecked == true)
        {
            _status.Text = "Clearing existing lemma data...";
            await Task.Run(() => _lemmaRepo.ClearAsync(cancellationToken), cancellationToken);
        }

        var progress = new Progress<LemmaIngestProgress>(p =>
            _status.Text = $"{p.CurrentFile}  ({p.FilesProcessed}/{p.TotalFiles} files, {p.LemmasLoaded:N0} mappings loaded)");

        var service = new LemmaIngestService();
        await Task.Run(() => service.IngestAsync(path, language, progress, cancellationToken), cancellationToken);

        var finalCount = await Task.Run(() => _lemmaRepo.CountAsync(cancellationToken), cancellationToken);
        _status.Text = $"Done. {finalCount:N0} lemma mapping(s) loaded.";

        if (finalCount == 0)
        {
            await Alert.Warn(this, "Nothing loaded",
                "No mappings were extracted. That almost always means the XML uses element or attribute names this parser "
                + "doesn't recognize yet - open one of the files, check what the form and lemma fields are actually called, "
                + "and add them to the name lists in LemmaIngestService.cs.");
        }
        else
        {
            await Alert.Info(this, "Done", $"Loaded {finalCount:N0} lemma mappings.");
        }
    }

    private async Task LoadDictionaryAsync(string path, string language, CancellationToken cancellationToken)
    {
        if (_clearFirst.IsChecked == true)
        {
            _status.Text = "Clearing existing dictionary data...";
            await Task.Run(() => _definitionRepo.ClearAsync(cancellationToken), cancellationToken);
        }

        var sourceName = language == "lat" ? "Lewis & Short" : "LSJ";

        var progress = new Progress<LexiconIngestProgress>(p =>
            _status.Text = $"{p.CurrentFile}  ({p.FilesProcessed}/{p.TotalFiles} files, {p.EntriesLoaded:N0} entries loaded)");

        var service = new LexiconIngestService();
        await Task.Run(() => service.IngestAsync(path, language, sourceName, progress, cancellationToken), cancellationToken);

        var (finalCount, byLanguage) = await Task.Run(async () =>
            (await _definitionRepo.CountAsync(cancellationToken), await _definitionRepo.CountByLanguageAsync(cancellationToken)),
            cancellationToken);
        var breakdown = FormatLanguageBreakdown(byLanguage);
        _status.Text = $"Done. {finalCount:N0} dictionary entr(ies) loaded{breakdown}.";

        if (finalCount == 0)
        {
            await Alert.Warn(this, "Nothing loaded",
                "No entries were extracted. The lexicon XML likely uses element names this parser doesn't recognize yet - "
                + "open one of the files, check what wraps a single dictionary entry, and add it to EntryElementNames in "
                + "LexiconIngestService.cs.");
        }
        else
        {
            await Alert.Info(this, "Done", $"Loaded {finalCount:N0} dictionary entries{breakdown}.");
        }
    }

    /// <summary>
    /// A per-language count as " (241,000 Greek / 27,079 Latin)". Pointing at
    /// a folder with both dictionaries should show both; a single language, or
    /// a zero, is the tell that something didn't load.
    /// </summary>
    internal static string FormatLanguageBreakdown(IReadOnlyList<(string Language, int Count)> byLanguage)
    {
        if (byLanguage.Count == 0) return string.Empty;

        static string Name(string code) => code switch
        {
            "grc" => "Greek",
            "lat" => "Latin",
            _ => code
        };

        return " (" + string.Join(" / ", byLanguage.Select(b => $"{b.Count:N0} {Name(b.Language)}")) + ")";
    }
}
