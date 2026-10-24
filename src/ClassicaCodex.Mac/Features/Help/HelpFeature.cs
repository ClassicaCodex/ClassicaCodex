using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Help;

/// <summary>Classica Codex Help (⌘?), from the Help menu and the sidebar's Tools list.</summary>
public static class HelpFeature
{
    public static void Open(IReaderHost host)
    {
        var window = new HelpWindow
        {
            OnNavigate = host.NavigateToPassageAsync,
            OnOpenWork = host.OpenWorkAsync
        };
        window.Show(host.Window);
    }
}
