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
    /// The Menota pre-import review the shared catalogue calls before an
    /// import: on Windows a dialog per manuscript, on the Mac the setup
    /// feature's review sheet. The catalogue's hook is synchronous, so the
    /// Mac review waits in a nested frame while the sheet is answered.
    /// </summary>
    public static class MenotaPlanReview
    {
        public static bool Run(System.Windows.Forms.IWin32Window owner, string folder) =>
            ClassicaCodex.Mac.Features.Setup.MenotaReview.Run(owner, folder);
    }
}
