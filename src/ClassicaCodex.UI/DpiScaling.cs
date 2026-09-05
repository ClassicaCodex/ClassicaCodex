namespace ClassicaCodex.UI;

/// <summary>
/// Makes a form scale with the display's scaling setting.
///
/// Every form in this application positions its controls in absolute pixels,
/// measured on a 100% display. The process is System DPI aware, so at 125%
/// Windows hands it a larger default font - Segoe UI 9pt is 16 pixels tall at
/// 96 DPI and 20 at 120 - and stretches nothing else. Unless the form scales
/// its own coordinates to match, the text grows and the boxes holding it do
/// not.
///
/// <b>This did not work until 3.6.2, and the reason is worth recording,</b>
/// because it is invisible on the machine most likely to be testing it.
///
/// The old code set AutoScaleDimensions and then AutoScaleMode. Assigning
/// AutoScaleMode discards the dimensions just assigned and re-reads them from
/// the current font:
///
/// <code>
/// in ctor, after set    {Width=7, Height=15}
/// in ctor, after mode   {Width=8, Height=20}   &lt;- at 125%
/// </code>
///
/// Declared and current were therefore always identical, the scale factor was
/// always exactly 1, and every window was the same pixel size at 125% as at
/// 100% while its text was a quarter larger. At 100% that is indistinguishable
/// from working, which is why it survived a release whose notes claimed it had
/// been measured at 125%. It had been reasoned about, not run.
///
/// Two things fix it, and both are needed:
///
/// <b>Layout has to be suspended across the assignment.</b> That is what stops
/// the re-measure that discards the design value, and it is why the WinForms
/// designer has always emitted these two lines inside SuspendLayout. Since a
/// form's controls are added by its own constructor, after the base class has
/// run, only the base class can suspend and only it can resume - see
/// <see cref="ScaledForm"/>.
///
/// <b>The scale is taken from the DPI, not from the font.</b> Font mode
/// compares the font's average character width and height against a design
/// pair, and on this corpus of forms that goes wrong twice over. The width
/// grows more slowly than the height - 7 to 8 against 16 to 20 - so a form
/// scaled 1.25 tall comes out 1.14 wide and every button is squeezed. And the
/// design pair has to be what the machine that drew the layouts reported,
/// which is not knowable from the code: the value here was 15 where this
/// machine reports 16, a 7% error applied to every coordinate.
///
/// Measured at 125%, a 400x200 window with a 200x40 label:
///
/// <code>
/// Font (7,15)    client 457x267   label 229x53   1.335 tall, 1.14 wide
/// Font (7,16)    client 457x250   label 229x50   1.250 tall, 1.14 wide
/// Dpi  (96,96)   client 500x250   label 250x50   1.250 both
/// </code>
///
/// DPI is what actually changed, 96 is what the layouts were drawn at, and
/// neither number depends on which machine is running.
/// </summary>
internal static class DpiScaling
{
    /// <summary>
    /// The DPI every coordinate in this application was measured at. 100%
    /// scaling, by definition, so at 100% the factor is exactly 1 and nothing
    /// moves.
    /// </summary>
    private static readonly SizeF DesignDpi = new(96F, 96F);

    /// <summary>
    /// Call with layout already suspended, before any control is created or
    /// positioned - WinForms applies the factor as controls are added, so
    /// setting this afterwards leaves the layout at its design coordinates.
    /// <see cref="ScaledForm"/> does both, which is why it exists rather than
    /// each form remembering to.
    /// </summary>
    public static void UseDesignDpiScaling(ContainerControl form)
    {
        // Mode first, then dimensions. The other order is what was wrong here
        // for six versions: the mode setter overwrites the dimensions.
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoScaleDimensions = DesignDpi;
    }
}
