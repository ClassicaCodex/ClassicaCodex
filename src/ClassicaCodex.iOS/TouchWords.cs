using Avalonia;
using Avalonia.Controls;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.iOS;

/// <summary>
/// Rewords the interface for a finger as it is shown. Most instructions are
/// written straight into the windows, so a table can only reach them here,
/// where every line of text passes on its way to the screen.
/// </summary>
internal static class TouchWords
{
    public static void Install() => TextBlock.TextProperty.Changed.AddClassHandler<TextBlock, string?>((block, e) =>
    {
        if (e.NewValue.GetValueOrDefault() is not { } text) return;

        var said = SystemWords.ForTouch(text);
        if (said != text) block.SetCurrentValue(TextBlock.TextProperty, said);
    });
}
