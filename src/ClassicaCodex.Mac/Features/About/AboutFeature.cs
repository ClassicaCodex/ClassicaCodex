using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.About;

/// <summary>About Classica Codex: the version, every data source and its licence, and the privacy terms.</summary>
public static class AboutFeature
{
    public static void Open(IReaderHost host)
    {
        var window = new AboutWindow
        {
            OnNavigate = host.NavigateToPassageAsync,
            OnOpenWork = host.OpenWorkAsync
        };
        window.Show(host.Window);
    }
}
