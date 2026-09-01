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
    /// <summary>
    /// A saved plan, or null if there isn't a usable one.
    ///
    /// A plan whose titles carry a replacement character is treated as absent
    /// rather than loaded, and that is the half of this that repairs a library
    /// already holding the damage. The check above stops a bad plan being
    /// written; this stops one already written from being believed.
    ///
    /// The reasoning is that U+FFFD cannot be a title. It is not a character
    /// any manuscript is written in - it is what this application substitutes
    /// for an entity it could not resolve - so its presence is proof the plan
    /// was made without the entity table, whatever it says about being
    /// confirmed. Discarding it costs a re-review; keeping it costs a library
    /// where half the Norse works are named "H&#xFFFD;r hefir upp Egils
    /// s&#xFFFD;gu" and no amount of re-importing will fix them.
    /// </summary>
    private static MenotaIngestPlan? SafeLoad(string path)
    {
        try
        {
            var plan = MenotaIngestPlan.Load(path);
            if (plan == null) return null;

            return plan.Works.Any(w => w.Title.Contains('�')) ? null : plan;
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

        // Without the entity table there is nothing worth planning, and
        // planning anyway is worse than not starting.
        //
        // A Menota manuscript is written almost entirely in character
        // entities - 1,780,562 references across the 91 files, of which
        // menota-entities.txt defines 1,779,204. Loaded without it, every one
        // of those becomes a replacement character, so a title reads
        // "Af Katli <?>rym capitulum" and "H<?>r hefir upp Egils s<?>gu".
        //
        // That would be recoverable if it stayed in memory. It does not: the
        // title is written into the .plan.json and the ingest reads it back,
        // so a plan built in this state stays wrong after the file arrives.
        // It happened - the plans went in at 20:47 and the entity file was
        // saved at 22:26, and 106 of 219 work titles carried a replacement
        // character into the library while the reading text, which is parsed
        // fresh every time, was perfect. Nothing on screen connected the two.
        if (entities.Count == 0)
        {
            MessageBox.Show(owner,
                "menota-entities.txt isn't in this folder yet.\n\n" +
                "These manuscripts are written almost entirely in character entities - the thorns, " +
                "eths and accented vowels are all references into that file - so without it the text " +
                "comes through as replacement characters, and the titles those plans record would be " +
                "wrong for good.\n\n" +
                "Download it from menota.org, save it into this folder, and import again.",
                "Menota - entity file missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

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
