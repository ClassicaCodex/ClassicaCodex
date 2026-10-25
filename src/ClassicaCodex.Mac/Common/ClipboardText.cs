using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace ClassicaCodex.Mac.Common;

/// <summary>Puts text on the pasteboard from anywhere that has a control in hand.</summary>
public static class ClipboardText
{
    public static async Task<bool> SetAsync(Control anywhere, string text)
    {
        var clipboard = TopLevel.GetTopLevel(anywhere)?.Clipboard;
        if (clipboard == null) return false;

        try
        {
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
