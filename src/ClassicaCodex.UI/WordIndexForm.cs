using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Checking and rebuilding the word index, on its own.
///
/// It used to sit inside the Setup Wizard, and the staleness check ran
/// whenever that dialog opened. That check counts distinct lines in the word
/// index against every line in the library - two aggregates over well past a
/// million rows - so simply looking at the data-source list paid for a scan
/// nobody had asked for. Moving it behind a button means the cost is only
/// paid by someone who actually wants to know.
/// </summary>
public class WordIndexForm : Form
{
    private readonly WordIndexRepository _wordIndexRepo = new();

    private readonly PictureBox _statusIcon;
    private readonly Label _statusLabel;
    private readonly Label _elapsedLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _buildButton;
    private readonly Button _closeButton;

    private CancellationTokenSource? _cts;
    private System.Windows.Forms.Timer? _heartbeat;
    private DateTime _operationStart;

    public WordIndexForm()
    {
        Text = "Word Index";
        AppIcons.ApplyWindowIcon(this, "Search");
        ClientSize = new Size(760, 250);
        MinimumSize = new Size(600, 250);
        StartPosition = FormStartPosition.CenterParent;

        _statusIcon = new PictureBox
        {
            Left = 12, Top = 13, Width = 18, Height = 18, SizeMode = PictureBoxSizeMode.Zoom
        };

        var title = new Label
        {
            Text = "Build Word Index",
            Left = 36,
            Top = 12,
            Width = 700,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(Font, FontStyle.Bold)
        };

        var description = new Label
        {
            Text = "This is what makes lemma-aware search fast - without it, each search scans the whole " +
                   "corpus once per word form. Run it after loading or adding a corpus. Safe to rerun " +
                   "any time; it always rebuilds from scratch.",
            Left = 12,
            Top = 36,
            Width = 736,
            Height = 36,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray
        };

        _progressBar = new ProgressBar
        {
            Left = 12, Top = 82, Width = 736, Height = 22,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _statusLabel = new Label
        {
            Left = 12, Top = 110, Width = 736, Height = 40, Text = "Checking...",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _elapsedLabel = new Label
        {
            Left = 12, Top = 154, Width = 736, Height = 20, ForeColor = Color.DimGray,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _buildButton = new Button
        {
            Text = "Build Word Index", Left = 12, Top = 186, Width = 160, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Enabled = false
        };
        _buildButton.Click += async (_, _) => await BuildAsync();

        _closeButton = new Button
        {
            Text = "Close", Left = 672, Top = 186, Width = 76, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK
        };
        CancelButton = _closeButton;

        Controls.AddRange(new Control[]
        {
            _statusIcon, title, description, _progressBar, _statusLabel, _elapsedLabel,
            _buildButton, _closeButton
        });

        Load += async (_, _) => await RefreshStatusAsync();
        ReadingTheme.AttachTo(this);
    }

    /// <summary>
    /// Whether a rebuild actually ran, so a caller showing its own summary
    /// of the library can refresh it.
    /// </summary>
    public bool IndexWasRebuilt { get; private set; }

    private async Task RefreshStatusAsync()
    {
        if (!DbConnectionFactory.IsConfigured)
        {
            _statusIcon.Image = AppIcons.Get("Error", 18);
            _statusLabel.Text = "No database configured yet - set one up first.";
            return;
        }

        _statusLabel.Text = "Checking...";
        UseWaitCursor = true;

        try
        {
            if (!await _wordIndexRepo.HasDataAsync())
            {
                _statusIcon.Image = AppIcons.Get("Error", 18);
                _statusLabel.Text = "Not built yet.";
            }
            else
            {
                // The index is pure derived data with no automatic refresh
                // hook - ingesting a new source doesn't touch it. A count
                // comparison is what catches that silently: "has data" alone
                // stayed true the whole time Shakespeare's lines sat
                // unindexed after a source was added post-build.
                var totalLines = await _wordIndexRepo.GetTextNodeCountAsync();
                var indexedLines = await _wordIndexRepo.GetIndexedTextNodeCountAsync();

                if (indexedLines >= totalLines)
                {
                    _statusIcon.Image = AppIcons.Get("Complete", 18);
                    _statusLabel.Text = $"Up to date - {indexedLines:N0} lines indexed.";
                }
                else
                {
                    _statusIcon.Image = AppIcons.Get("Warning", 18);
                    _statusLabel.Text =
                        $"Out of date - {indexedLines:N0} of {totalLines:N0} lines indexed. " +
                        $"{totalLines - indexedLines:N0} line(s) were added since the last build " +
                        "(likely a new source ingested afterward) and won't turn up in lemma-expansion " +
                        "searches like Auto-Tag's until this is rebuilt.";
                }
            }

            _buildButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _statusIcon.Image = AppIcons.Get("Error", 18);
            _statusLabel.Text = $"Couldn't check the index: {ex.Message}";
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task BuildAsync()
    {
        var confirm = MessageBox.Show(this,
            "Build the word index over every ingested line?\r\n\r\n" +
            "It takes a few minutes on a full corpus and can be rebuilt any time.",
            "Build Word Index", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK) return;

        _buildButton.Enabled = false;
        _closeButton.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _cts = new CancellationTokenSource();
        StartHeartbeat();

        var progress = new Progress<WordIndexProgress>(p =>
        {
            if (p.TotalNodes > 0 && p.Phase == "Indexing")
            {
                var percent = (int)Math.Min(100, p.NodesProcessed * 100 / p.TotalNodes);
                _progressBar.Style = ProgressBarStyle.Blocks;
                _progressBar.Value = percent;
                _statusLabel.Text =
                    $"Indexing... {p.NodesProcessed:N0}/{p.TotalNodes:N0} lines ({percent}%), " +
                    $"{p.EntriesWritten:N0} entries written.";
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

            IndexWasRebuilt = true;
            _statusLabel.Text = "Word index built.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Failed - see message.";
            MessageBox.Show(this, ex.Message, "Build failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            StopHeartbeat();
            _progressBar.Style = ProgressBarStyle.Blocks;
            _closeButton.Enabled = true;
            await RefreshStatusAsync();
        }
    }

    /// <summary>
    /// Ticks on its own regardless of what the build is doing - the actual
    /// diagnostic for telling "UI thread alive but nothing reported yet"
    /// apart from "UI thread genuinely blocked".
    /// </summary>
    private void StartHeartbeat()
    {
        _operationStart = DateTime.UtcNow;
        _heartbeat = new System.Windows.Forms.Timer { Interval = 500 };
        _heartbeat.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - _operationStart;
            _elapsedLabel.Text = $"Running for {elapsed:mm\\:ss}...";
        };
        _heartbeat.Start();
    }

    private void StopHeartbeat()
    {
        _heartbeat?.Stop();
        _heartbeat?.Dispose();
        _heartbeat = null;

        var elapsed = DateTime.UtcNow - _operationStart;
        _elapsedLabel.Text = $"Finished in {elapsed:mm\\:ss}.";
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        StopHeartbeat();
        base.OnFormClosed(e);
    }
}
