using Avalonia.Controls;
using Avalonia.Threading;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Setup;

/// <summary>
/// Walks a folder of Menota manuscripts and puts each unconfirmed one in front
/// of the reader for review, before any import runs - the Mac counterpart of
/// the Windows MenotaPlanReview, and the same decisions in the same order.
///
/// <b>Why it is not reached through the catalogue's hook.</b> The catalogue
/// asks its question through SetupDataSource.PrepareSecondary, a synchronous
/// <c>Func&lt;IWin32Window, bool&gt;</c>, because a WinForms dialog blocks
/// until it is answered. An Avalonia window does not: ShowDialog returns a
/// task. The Mac's shim for MenotaPlanReview.Run (Shared/WindowsShims.cs)
/// therefore answers "proceed" without asking, and cannot call this without
/// that file changing. So both setup windows call
/// <see cref="PrepareSecondaryAsync"/> instead of the hook, and it runs this
/// review for the Menota step and the hook for any other. <see cref="Run"/>
/// is the same review in the hook's own synchronous shape, for the shim to
/// forward to.
/// </summary>
internal static class MenotaReview
{
    /// <summary>
    /// What a setup window calls where Windows calls PrepareSecondary: true to
    /// go on with the secondary action, false to leave it.
    /// </summary>
    /// <param name="folder">
    /// Where the step's files are, when that is not the catalogue's folder -
    /// Advanced Setup lets a row point anywhere.
    /// </param>
    public static async Task<bool> PrepareSecondaryAsync(SetupDataSource source, Window owner, string? folder = null)
    {
        if (source.PrepareSecondary is not { } prepare) return true;

        // The Menota step's hook is MenotaPlanReview.Run over its own download
        // folder, which is how the step is known: by the folder the catalogue
        // names for it rather than by its title, which is prose and may change.
        if (IsReviewStep(source)) return await RunAsync(owner, folder ?? source.DefaultDestination);

        return prepare(new MacWindowHandle(owner));
    }

    public static bool IsReviewStep(SetupDataSource source) =>
        source.PrepareSecondary != null
        && string.Equals(Path.GetFileName(source.DefaultDestination.TrimEnd(Path.DirectorySeparatorChar)), "menota", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The review in the synchronous shape the catalogue's hook has - what the
    /// shim's MenotaPlanReview.Run would forward to, making the hook itself
    /// ask the reader rather than answering "proceed".
    ///
    /// Waits for the review's windows in a nested dispatcher frame, the way a
    /// WinForms modal dialog runs its own message loop, so the windows stay
    /// live while the caller is blocked. Where the platform cannot run a
    /// nested loop it answers as the shim does today, rather than hanging.
    /// </summary>
    public static bool Run(System.Windows.Forms.IWin32Window owner, string folder)
    {
        var window = (owner as MacWindowHandle)?.Window ?? MainWindow.Current;
        if (window == null) return true;

        var review = RunAsync(window, folder);
        if (!review.IsCompleted)
        {
            if (!Dispatcher.UIThread.SupportsRunLoops) return true;

            var frame = new DispatcherFrame();
            review.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.UIThread.PushFrame(frame);
        }

        return review.GetAwaiter().GetResult();
    }

    /// <summary>
    /// A saved plan, or null if there isn't a usable one.
    ///
    /// A plan whose titles carry a replacement character is treated as absent
    /// rather than loaded, which is the half of this that repairs a library
    /// already holding the damage. U+FFFD cannot be a title - it is what this
    /// application substitutes for an entity it could not resolve - so its
    /// presence proves the plan was made without the entity table, whatever it
    /// says about being confirmed. Discarding it costs a re-review; keeping it
    /// costs a library where half the Norse works are named "H�r hefir upp
    /// Egils s�gu" and no amount of re-importing fixes them. A plan edited
    /// by hand into something that will not load is likewise simply absent.
    /// </summary>
    private static MenotaIngestPlan? SafeLoad(string path)
    {
        try
        {
            var plan = MenotaIngestPlan.Load(path);
            if (plan == null) return null;

            return plan.Works.Any(w => w.Title.Contains('\uFFFD')) ? null : plan;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns false if the reader backed out of the whole thing, in which case
    /// the import should not run at all. Skipping a single manuscript returns
    /// true: the others are still worth importing.
    /// </summary>
    public static async Task<bool> RunAsync(Window owner, string folder)
    {
        if (!Directory.Exists(folder)) return true;

        var files = Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories);
        if (files.Length == 0) return true;

        var entities = MenotaXmlLoader.LoadEntities(folder);

        // Without the entity table there is nothing worth planning, and
        // planning anyway is worse than not starting.
        //
        // A Menota manuscript is written almost entirely in character entities -
        // 1,780,562 references across the 91 files, of which menota-entities.txt
        // defines 1,779,204. Loaded without it, every one becomes a replacement
        // character, so a title reads "Af Katli �rym capitulum". That would
        // be recoverable if it stayed in memory; it does not, because the title
        // is written into the .plan.json and the import reads it back. It
        // happened: plans made at 20:47, the entity file saved at 22:26, and 106
        // of 219 work titles carried a replacement character into the library
        // while the reading text, parsed fresh each time, was perfect.
        if (entities.Count == 0)
        {
            await Alert.Warn(owner, "Menota - entity file missing",
                "menota-entities.txt isn't in this folder yet.\n\n"
                + "These manuscripts are written almost entirely in character entities - the thorns, eths and "
                + "accented vowels are all references into that file - so without it the text comes through as "
                + "replacement characters, and the titles those plans record would be wrong for good.\n\n"
                + "Download it from menota.org, save it into this folder, and import again.");
            return false;
        }

        var planner = new MenotaIngestPlanner();

        // A confirmed plan is normally taken as settled - that is the point of
        // confirming it. But when every plan is confirmed, Import Manuscripts
        // would silently reuse decisions that may be months old and show the
        // reader nothing, so a correction they came to make could not be made
        // without deleting the .plan.json by hand - exactly the file-editing
        // this review exists to replace. Asked once for the whole folder rather
        // than per manuscript, so re-importing ten of them is not ten questions.
        var confirmed = files
            .Select(MenotaIngestPlan.PlanPathFor)
            .Count(path => SafeLoad(path) is { Confirmed: true });

        var reopenConfirmed = false;

        if (confirmed > 0 && confirmed == files.Length)
        {
            reopenConfirmed = await Alert.Confirm(owner, "Menota",
                $"All {confirmed} manuscript(s) already have confirmed plans, so nothing would be reviewed.\n\n"
                + "Open them for review again?",
                confirm: "Review Again", cancel: "Import as Confirmed");
        }

        foreach (var path in files)
        {
            var planPath = MenotaIngestPlan.PlanPathFor(path);
            var existing = SafeLoad(planPath);

            // Already confirmed, and the reader did not ask to revisit these.
            if (existing is { Confirmed: true } && !reopenConfirmed) continue;

            var load = MenotaXmlLoader.Load(path, entities);

            // The import reports unreadable files properly, with the parser's
            // own message. Nothing useful to review here.
            if (!load.Ok) continue;

            MenotaIngestPlan plan;
            try
            {
                plan = existing ?? planner.Plan(path, load.Document!);
            }
            catch (Exception ex)
            {
                await Alert.Warn(owner, "Menota", $"Couldn't work out how to divide {Path.GetFileName(path)}:\n\n{ex.Message}");
                continue;
            }

            if (plan.Works.Count == 0)
            {
                await Alert.Info(owner, "Menota",
                    $"{Path.GetFileName(path)} has no divisions containing word markup - there is nothing in it to import.");
                continue;
            }

            var body = load.Document!
                .Descendants(MenotaXmlLoader.Tei + "body")
                .FirstOrDefault();

            var window = new MenotaPlanWindow(plan, body);
            if (!await window.ShowDialog<bool>(owner)) continue;

            try
            {
                window.Result.Save(planPath);
            }
            catch (Exception ex)
            {
                await Alert.Warn(owner, "Menota", $"Couldn't save the plan for {Path.GetFileName(path)}:\n\n{ex.Message}");
            }
        }

        return true;
    }
}
