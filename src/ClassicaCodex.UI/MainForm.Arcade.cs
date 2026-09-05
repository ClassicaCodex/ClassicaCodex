using ClassicaCodex.Data;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

public partial class MainForm
{
    private BronzeArcadeForm? _arcadeForm;
    private SyncListView? _arcadeReaderPane;

    private void InitializeArcadeReaderTracking()
    {
        _originalPane.Enter += (_, _) => _arcadeReaderPane = _originalPane;
        _translationPane.Enter += (_, _) => _arcadeReaderPane = _translationPane;
        _originalPane.MouseDown += (_, _) => _arcadeReaderPane = _originalPane;
        _translationPane.MouseDown += (_, _) => _arcadeReaderPane = _translationPane;
        FormClosed += (_, _) => _arcadeForm?.Dispose();
    }

    private void OpenArcade()
    {
        if (_arcadeForm is { IsDisposed: false })
        {
            _arcadeForm.Show(); _arcadeForm.WindowState = FormWindowState.Normal; _arcadeForm.Activate();
            return;
        }
        _arcadeForm = new BronzeArcadeForm(DbConnectionFactory.DatabasePath,
            () =>
            {
                var pane = _arcadeReaderPane ?? _originalPane;
                if (pane.SelectedIndex < 0 || pane.SelectedIndex >= pane.Items.Count
                    || pane.Items[pane.SelectedIndex] is not TextNode node) return null;
                return node.TextNodeId;
            },
            () => DbConnectionFactory.DatabasePath,
            async workId => { await OpenWorkAsync(workId); }, openPassage: NavigateToArcadePassageAsync,
            activateLibrary: () =>
            {
                if (IsDisposed) return;
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show(); Activate();
            });
        // An owned window always stays above its owner. The arcade is an
        // independent top-level window so the reader can cover it on a small screen.
        _arcadeForm.StartPosition = FormStartPosition.Manual;
        var screen = Screen.FromControl(this).WorkingArea;
        _arcadeForm.Location = new Point(Math.Max(screen.Left, screen.Left + (screen.Width - _arcadeForm.Width) / 2),
            Math.Max(screen.Top, screen.Top + (screen.Height - _arcadeForm.Height) / 2));
        _arcadeForm.Show();
    }

    private async Task NavigateToArcadePassageAsync(int workId, long textNodeId)
    {
        await NavigateToPassageAsync(workId, textNodeId);
        foreach (var pane in new[] { _originalPane, _translationPane })
        {
            if (pane.SelectedItem is not TextNode node || node.TextNodeId != textNodeId) continue;

            // Put the clue at the top even with wrapped, variable-height rows.
            // Track this pane explicitly: the player may have last used the other one.
            pane.TopIndex = pane.SelectedIndex;
            _arcadeReaderPane = pane;
            pane.Focus();
            return;
        }
        throw new InvalidOperationException("The passage could not be highlighted in the reader.");
    }
}

