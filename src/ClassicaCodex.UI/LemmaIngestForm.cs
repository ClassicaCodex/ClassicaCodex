using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

public class LemmaIngestForm : ScaledForm
{
    private readonly TextBox _pathBox;
    private readonly ComboBox _dataTypeBox;
    private readonly ComboBox _languageBox;
    private readonly CheckBox _clearFirstBox;
    private readonly Button _startButton;
    private readonly Button _cancelButton;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;

    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();
    private CancellationTokenSource? _cts;

    public LemmaIngestForm()
    {
        Text = "Load Reference Data";
        AppIcons.ApplyWindowIcon(this, "LoadLemmas");
        Width = 760;
        Height = 430;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var explainer = new Label
        {
            UseMnemonic = false,
            Left = 16,
            Top = 14,
            Width = 710,
            Height = 74,
            Text = "Lemma mappings link inflected forms to dictionary headwords (λόγου → λόγος). " +
                   "Dictionaries add what those headwords actually mean.\r\n\r\nClone the data first, e.g.:\r\n" +
                   "    git clone https://github.com/gcelano/LemmatizedAncientGreekXML   (Greek lemmas)\r\n" +
                   "    git clone https://github.com/PerseusDL/lexica                    (LSJ and Lewis & Short)",
            ForeColor = Color.DimGray
        };

        var pathLabel = new Label { Text = "Folder containing the XML:", Left = 16, Top = 96, Width = 300 };
        _pathBox = new TextBox { Left = 16, Top = 120, Width = 600 };
        var browseButton = new Button { Text = "Browse...", Left = 624, Top = 118, Width = 100 };
        browseButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == DialogResult.OK) _pathBox.Text = dialog.SelectedPath;
        };

        var dataTypeLabel = new Label { Text = "Data type:", Left = 16, Top = 156, Width = 80 };
        _dataTypeBox = new ComboBox
        {
            Left = 100,
            Top = 152,
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _dataTypeBox.Items.AddRange(new object[] { "Lemma mappings", "Dictionary (lexicon)" });
        _dataTypeBox.SelectedIndex = 0;

        var languageLabel = new Label { Text = "Language:", Left = 320, Top = 156, Width = 70 };
        _languageBox = new ComboBox
        {
            Left = 392,
            Top = 152,
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _languageBox.Items.AddRange(new object[] { "grc (Greek)", "lat (Latin)" });
        _languageBox.SelectedIndex = 0;

        _clearFirstBox = new CheckBox
        {
            Text = "Clear existing first",
            Left = 556,
            Top = 154,
            Width = 160,
            Checked = true
        };

        _progressBar = new ProgressBar { Left = 16, Top = 196, Width = 708, Height = 22 };
        _statusLabel = new Label { Left = 16, Top = 224, Width = 708, Height = 76, Text = "Idle." };

        _startButton = new Button { Text = "Load", Left = 16, Top = 312, Width = 150, Height = 34 };
        _startButton.Click += async (_, _) => await RunAsync();

        _cancelButton = new Button { Text = "Cancel", Left = 176, Top = 312, Width = 100, Height = 34, Enabled = false };
        _cancelButton.Click += (_, _) => _cts?.Cancel();

        Controls.Add(explainer);
        Controls.Add(pathLabel);
        Controls.Add(_pathBox);
        Controls.Add(browseButton);
        Controls.Add(dataTypeLabel);
        Controls.Add(_dataTypeBox);
        Controls.Add(languageLabel);
        Controls.Add(_languageBox);
        Controls.Add(_clearFirstBox);
        Controls.Add(_progressBar);
        Controls.Add(_statusLabel);
        Controls.Add(_startButton);
        Controls.Add(_cancelButton);

        Load += async (_, _) => await ShowExistingCountAsync();
    }

    private async Task ShowExistingCountAsync()
    {
        try
        {
            var lemmaCount = await _lemmaRepo.CountAsync();
            var definitionCount = await _definitionRepo.CountAsync();

            _statusLabel.Text =
                $"Currently loaded: {lemmaCount:N0} lemma mapping(s), {definitionCount:N0} dictionary entr(ies).";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't read existing counts: {ex.Message}";
        }
    }

    private async Task RunAsync()
    {
        var path = _pathBox.Text.Trim();
        if (path.Length == 0)
        {
            MessageBox.Show(this, "Point at the folder containing the XML first.", "Nothing to load",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var language = _languageBox.SelectedIndex == 1 ? "lat" : "grc";
        var loadingDictionary = _dataTypeBox.SelectedIndex == 1;

        _startButton.Enabled = false;
        _cancelButton.Enabled = true;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _cts = new CancellationTokenSource();

        try
        {
            if (loadingDictionary)
            {
                await LoadDictionaryAsync(path, language, _cts.Token);
            }
            else
            {
                await LoadLemmasAsync(path, language, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Failed - see message.";
            MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progressBar.Style = ProgressBarStyle.Blocks;
            _startButton.Enabled = true;
            _cancelButton.Enabled = false;
        }
    }

    private async Task LoadLemmasAsync(string path, string language, CancellationToken cancellationToken)
    {
        if (_clearFirstBox.Checked)
        {
            _statusLabel.Text = "Clearing existing lemma data...";
            await Task.Run(() => _lemmaRepo.ClearAsync(cancellationToken), cancellationToken);
        }

        var progress = new Progress<LemmaIngestProgress>(p =>
        {
            _statusLabel.Text = $"{p.CurrentFile}  ({p.FilesProcessed}/{p.TotalFiles} files, {p.LemmasLoaded:N0} mappings loaded)";
        });

        var service = new LemmaIngestService();
        await Task.Run(() => service.IngestAsync(path, language, progress, cancellationToken), cancellationToken);

        var finalCount = await _lemmaRepo.CountAsync(cancellationToken);
        _statusLabel.Text = $"Done. {finalCount:N0} lemma mapping(s) loaded.";

        if (finalCount == 0)
        {
            MessageBox.Show(this,
                "No mappings were extracted. That almost always means the XML uses element or attribute names " +
                "this parser doesn't recognize yet - open one of the files, check what the form and lemma " +
                "fields are actually called, and add them to the name lists in LemmaIngestService.cs.",
                "Nothing loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            MessageBox.Show(this, $"Loaded {finalCount:N0} lemma mappings.", "Done",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task LoadDictionaryAsync(string path, string language, CancellationToken cancellationToken)
    {
        if (_clearFirstBox.Checked)
        {
            _statusLabel.Text = "Clearing existing dictionary data...";
            await Task.Run(() => _definitionRepo.ClearAsync(cancellationToken), cancellationToken);
        }

        var sourceName = language == "lat" ? "Lewis & Short" : "LSJ";

        var progress = new Progress<LexiconIngestProgress>(p =>
        {
            _statusLabel.Text = $"{p.CurrentFile}  ({p.FilesProcessed}/{p.TotalFiles} files, {p.EntriesLoaded:N0} entries loaded)";
        });

        var service = new LexiconIngestService();
        await Task.Run(() => service.IngestAsync(path, language, sourceName, progress, cancellationToken), cancellationToken);

        var finalCount = await _definitionRepo.CountAsync(cancellationToken);
        var breakdown = FormatLanguageBreakdown(await _definitionRepo.CountByLanguageAsync(cancellationToken));
        _statusLabel.Text = $"Done. {finalCount:N0} dictionary entr(ies) loaded{breakdown}.";

        if (finalCount == 0)
        {
            MessageBox.Show(this,
                "No entries were extracted. The lexicon XML likely uses element names this parser doesn't " +
                "recognize yet - open one of the files, check what wraps a single dictionary entry, and add " +
                "it to EntryElementNames in LexiconIngestService.cs.",
                "Nothing loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            MessageBox.Show(this, $"Loaded {finalCount:N0} dictionary entries{breakdown}.", "Done",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// Renders a per-language count as " (241,000 Greek / 27,079 Latin)".
    /// Pointing at a folder with both dictionaries should show both here; a
    /// single language, or a zero, is the tell that something didn't load.
    /// </summary>
    private static string FormatLanguageBreakdown(IReadOnlyList<(string Language, int Count)> byLanguage)
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
