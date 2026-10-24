using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Compare;

/// <summary>
/// The two comparisons of one work with itself: its translations side by
/// side, and its original-language editions collated. Both open beside the
/// reader, from the Tools menu, the sidebar's Tools list and the gallery.
/// </summary>
public static class CompareFeature
{
    public static void CompareTranslations(IReaderHost host)
    {
        var window = new CompareTranslationsWindow((host.OpenWork ?? host.SelectedLibraryWork)?.WorkId)
        {
            OnNavigate = host.NavigateToPassageAsync
        };
        window.Show(host.Window);
    }

    public static void Collate(IReaderHost host)
    {
        var window = new CollationWindow();
        window.Show(host.Window);
    }
}
