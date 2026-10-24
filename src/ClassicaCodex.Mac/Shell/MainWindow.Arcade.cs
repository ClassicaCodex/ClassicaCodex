using Avalonia.Input;
using Avalonia.Threading;
using ClassicaCodex.Mac.Features.Arcade;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// What the hidden arcade asks of the reader, as on Windows (MainForm.Arcade):
/// which passage the player has selected, and a jump that puts a clue's
/// passage at the top of its page. The arcade is opened with ⌃⇧F12, the
/// Windows chord, and a selection is submitted with ⌃⇧Return.
/// </summary>
public partial class MainWindow
{
    /// <summary>The page the player last used, so a submitted selection comes from the right side.</summary>
    private ReaderPane? _arcadePane;

    /// <summary>The passage selected in the page last used, or in the original if neither has been.</summary>
    public long? SelectedPassageId => (_arcadePane ?? _originalPane).SelectedNode?.TextNodeId
                                      ?? (_arcadePane == null ? _translationPane.SelectedNode?.TextNodeId : null);

    /// <summary>
    /// Jumps to a passage and puts it at the top of whichever page holds it,
    /// which becomes the page a submitted selection is read from.
    /// </summary>
    public async Task NavigateToPassageAtTopAsync(int workId, long textNodeId)
    {
        await NavigateToPassageAsync(workId, textNodeId);

        foreach (var pane in new[] { _originalPane, _translationPane })
        {
            if (pane.SelectedNode?.TextNodeId != textNodeId) continue;

            _arcadePane = pane;
            var row = pane.SelectedIndex;
            Dispatcher.UIThread.Post(() => pane.ScrollToTop(row), DispatcherPriority.Background);
            pane.List.Focus();
            return;
        }

        throw new InvalidOperationException("The passage could not be highlighted in the reader.");
    }

    private void TrackArcadePane()
    {
        foreach (var pane in new[] { _originalPane, _translationPane })
        {
            var captured = pane;
            pane.List.GotFocus += (_, _) => _arcadePane = captured;
            pane.UserSelected += (p, _) => _arcadePane = p;
        }
    }

    /// <summary>The arcade's two chords, on every platform: they are in no menu, being a secret.</summary>
    private bool HandleArcadeKeys(KeyEventArgs e)
    {
        var chord = e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!chord) return false;

        if (e.Key == Key.F12)
        {
            ArcadeFeature.Open(this);
            return true;
        }

        if (e.Key == Key.Enter && ArcadeFeature.IsOpen)
        {
            ArcadeFeature.SubmitReaderSelection();
            return true;
        }

        return false;
    }
}
