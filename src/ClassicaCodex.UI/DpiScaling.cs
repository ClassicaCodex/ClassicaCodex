namespace ClassicaCodex.UI;

/// <summary>
/// Makes a form scale with the system's text size.
///
/// Every form in this application positions its controls in absolute pixels, and none
/// of them set AutoScaleMode - which defaults to Inherit, and behaves as None on a
/// top-level window. The process is System DPI aware, so at 125% or 150% Windows hands
/// it a larger default font without stretching anything: the text grew, the boxes
/// holding it did not, and the last line of a wrapped label fell outside its label.
///
/// Naming the size the layouts were measured against lets WinForms scale those
/// coordinates by the ratio between it and the current one. At 100% that ratio is
/// exactly 1 and nothing moves.
/// </summary>
internal static class DpiScaling
{
    /// <summary>
    /// Segoe UI 9pt at 96 DPI, which is what SystemFonts.DefaultFont resolves to on the
    /// machines these forms were laid out on, and therefore the size every hard-coded
    /// coordinate in this project is implicitly relative to.
    /// </summary>
    private static readonly SizeF DesignFont = new(7F, 15F);

    /// <summary>
    /// Call as the first statement of a form's constructor, before any control is
    /// created or positioned - WinForms applies the scale factor as controls are added,
    /// so setting it afterwards leaves the layout at its design-time coordinates.
    /// </summary>
    public static void UseDesignFontScaling(ContainerControl form)
    {
        form.AutoScaleDimensions = DesignFont;
        form.AutoScaleMode = AutoScaleMode.Font;
    }
}
