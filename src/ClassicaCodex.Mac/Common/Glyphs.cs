using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClassicaCodex.Mac.Common;

/// <summary>
/// The monochrome glyphs in Theme/Glyphs.axaml, by name, for code that
/// builds menus and buttons rather than declaring them.
/// </summary>
public static class Glyphs
{
    public static Geometry? Get(string name) =>
        Application.Current?.TryGetResource($"Glyph.{name}", null, out var value) == true ? value as Geometry : null;

    /// <summary>A PathIcon for <paramref name="name"/>, sized for a menu or a toolbar.</summary>
    public static PathIcon Icon(string name, double size = 16) =>
        new() { Data = Get(name), Width = size, Height = size };
}
