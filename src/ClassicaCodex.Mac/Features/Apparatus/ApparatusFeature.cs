using ClassicaCodex.Core.Models;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Apparatus;

/// <summary>
/// Entry points for this feature, called from the main window's menus,
/// toolbar and context menus. A stub until the feature is ported: each
/// opens the coming-soon card.
/// </summary>
public static class ApparatusFeature
{
    public static void Open(IReaderHost host, PassageContext passage) =>
        ComingSoon.Show(host, "Editor's Notes", "collation", "EditorNotes");

    public static void ShowPreface(IReaderHost host, string title, string text) =>
        ComingSoon.Show(host, "Preface", "reader", "Preface");
}
