using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace ClassicaCodex.Mac.Common;

/// <summary>Puts text on the pasteboard, and reads it, from anywhere that has a control in hand.</summary>
public static class ClipboardText
{
    /// <summary>The text on the pasteboard, or null where there is none or it cannot be read.</summary>
    public static async Task<string?> GetAsync(Control anywhere)
    {
        var clipboard = TopLevel.GetTopLevel(anywhere)?.Clipboard;
        if (clipboard == null) return null;

        try
        {
            return await clipboard.TryGetTextAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

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
