// What the shared setup catalogue (ClassicaCodex.UI/SetupDataSourceCatalog.cs
// and SetupDataSource.cs, compiled into this project from the Windows
// folder) asks of WinForms, answered for the Mac.
//
// The catalogue is shared rather than copied so that a collection added on
// Windows reaches the Mac without anyone retyping it. It is plain logic apart
// from two hooks: a SetupDataSource can carry a step that runs before its
// import with a window to own any dialog it shows, typed as WinForms'
// IWin32Window; and the Menota step uses that hook to review each
// manuscript's division before importing. Those two are all this file
// supplies.

namespace System.Windows.Forms
{
    /// <summary>
    /// Stands in for WinForms' window handle in the shared catalogue's
    /// signatures. The Mac passes its own window through it.
    /// </summary>
    public interface IWin32Window
    {
    }
}

namespace ClassicaCodex.UI
{
    /// <summary>
    /// The Mac side of the Menota pre-import review, which on Windows is a
    /// dialog per manuscript. Here it proceeds with the division proposals as
    /// they stand: the .plan.json beside each manuscript is still written and
    /// still the record of what was imported, and still editable before a
    /// re-import, which is what the review existed to make easier rather than
    /// what made the import correct.
    /// </summary>
    public static class MenotaPlanReview
    {
        public static bool Run(System.Windows.Forms.IWin32Window owner, string folder) => true;
    }
}
