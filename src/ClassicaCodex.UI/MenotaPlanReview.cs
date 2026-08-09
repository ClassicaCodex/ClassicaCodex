using System.Xml.Linq;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Walks a folder of Menota manuscripts and puts each unconfirmed one in front
/// of the user for review, before any import runs.
///
/// Runs on the UI thread by necessity - it shows dialogs - which is why the
/// setup step invokes it through PrepareSecondary rather than from inside the
/// background action.
/// </summary>
public static class MenotaPlanReview
{
    /// <summary>
    /// Returns false if the user backed out of the whole thing, in which case
    /// the import should not run at all. Skipping a single manuscript returns
    /// true: the others are still worth importing.
    /// </summary>
    /// <summary>
    /// Loads a plan, treating one edited by hand into something that will not
    /// deserialise as simply absent rather than as a dead end.
    /// </summary>
    private static MenotaIngestPlan? SafeLoad(string path)
    {
        try
        {
            return MenotaIngestPlan.Load(path);
        }
        catch
        {
            return null;
        }
    }

    public static bool Run(IWin32Window owner, string folder)
    {
        if (!Directory.Exists(folder)) return true;

        var files = Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories);
        if (files.Length == 0) return true;

        var entities = MenotaXmlLoader.LoadEntities(folder);
        var planner = new MenotaIngestPlanner();
        var reviewed = 0;

        // A confirmed plan is normally taken as settled - that is the point of
        // confirming it. But when every plan is confirmed, "Import Manuscripts"
        // silently reuses decisions that may be months old and shows the user
        // nothing, so a correction they came here to make cannot be made. The
        // only way through was to delete the .plan.json by hand, which is
        // exactly the file-editing this dialog exists to replace.
        //
        // Asked once for the whole folder rather than per manuscript, so
        // re-importing ten of them is not ten questions.
        var confirmed = files
            .Select(MenotaIngestPlan.PlanPathFor)
            .Count(path => SafeLoad(path) is { Confirmed: true });

        var reopenConfirmed = false;

        if (confirmed > 0 && confirmed == files.Length)
        {
            reopenConfirmed = MessageBox.Show(owner,
                $"All {confirmed} manuscript(s) already have confirmed plans, so nothing would be " +
                "reviewed.\n\nOpen them for review again?",
                "Menota", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        foreach (var path in files)
        {
            var planPath = MenotaIngestPlan.PlanPathFor(path);
            var existing = SafeLoad(planPath);

            // Already confirmed, and the user did not ask to revisit these.
            if (existing is { Confirmed: true } && !reopenConfirmed) continue;

            var load = MenotaXmlLoader.Load(path, entities);
            if (!load.Ok)
            {
                // The import reports unreadable files properly, with the
                // parser's own message. Nothing useful to review here.
                continue;
            }

            MenotaIngestPlan plan;
            try
            {
                plan = existing ?? planner.Plan(path, load.Document!);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner,
                    $"Couldn't work out how to divide {Path.GetFileName(path)}:\n\n{ex.Message}",
                    "Menota", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                continue;
            }

            if (plan.Works.Count == 0)
            {
                MessageBox.Show(owner,
                    $"{Path.GetFileName(path)} has no divisions containing word markup - " +
                    "there is nothing in it to import.",
                    "Menota", MessageBoxButtons.OK, MessageBoxIcon.Information);
                continue;
            }

            var body = load.Document!
                .Descendants(MenotaXmlLoader.Tei + "body")
                .FirstOrDefault();

            using var form = new MenotaPlanForm(plan, body);
            var result = form.ShowDialog(owner);

            if (result == DialogResult.OK)
            {
                try
                {
                    form.Result.Save(planPath);
                    reviewed++;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner,
                        $"Couldn't save the plan for {Path.GetFileName(path)}:\n\n{ex.Message}",
                        "Menota", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        return true;
    }
}
