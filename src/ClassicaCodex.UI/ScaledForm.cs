namespace ClassicaCodex.UI;

/// <summary>
/// A Form that scales with the display's scaling setting.
///
/// Every window in this application derives from this rather than from Form
/// directly. The scaling has to be established before any control is created
/// or positioned, and a base constructor runs before the derived one's body -
/// which makes inheritance a more reliable place for it than a call each
/// constructor has to remember to make first.
///
/// It also has to be established with layout suspended, and that is the part
/// inheritance is not merely convenient for but necessary to. A form's
/// controls are added by its own constructor, after this one has finished, so
/// nothing in the derived class can wrap the assignment - and a
/// SuspendLayout with no matching resume would leave the window unable to lay
/// itself out at all. See <see cref="DpiScaling"/> for what goes wrong without
/// the suspension, and how long it went wrong for.
/// </summary>
public class ScaledForm : Form
{
    private bool _designScalingResumed;

    protected ScaledForm()
    {
        SuspendLayout();
        DpiScaling.UseDesignDpiScaling(this);
    }

    /// <summary>
    /// Resumed at the last possible moment: every control the derived
    /// constructor added is in place by the time a handle exists, and the
    /// scale is applied to all of them together.
    ///
    /// Guarded because a handle can be created more than once - a form whose
    /// handle is recreated would otherwise resume a layout it never
    /// suspended, and the unbalanced call throws.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        ResumeDesignScaling();
        base.OnHandleCreated(e);
    }

    /// <summary>
    /// Also on dispose, for a form built and thrown away without ever being
    /// shown. Nothing needs the layout at that point, but a suspend count
    /// left standing is the kind of thing that surfaces somewhere else
    /// entirely.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing) ResumeDesignScaling();
        base.Dispose(disposing);
    }

    private void ResumeDesignScaling()
    {
        if (_designScalingResumed) return;
        _designScalingResumed = true;

        ResumeLayout(false);
        PerformLayout();
    }
}
