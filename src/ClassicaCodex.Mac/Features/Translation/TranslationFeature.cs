using Avalonia.Controls;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Translation;

/// <summary>
/// Entry points for translating, called from the library's work menu, the
/// reader's line menu, the Tools and Help menus and the tool gallery. What
/// MainForm does before it opens each Windows translation form - finding
/// the edition to resume, inferring its source, ranking what to check
/// against - is done here, so the shell stays the only thing that knows how
/// to read and this stays the only thing that knows how to translate.
/// </summary>
public static class TranslationFeature
{
    /// <summary>
    /// One workbench per work: two would each hold their own copy of what is
    /// written and save over each other. A second request brings the open
    /// one forward.
    /// </summary>
    private static readonly Dictionary<int, TranslationWorkbenchWindow> OpenWorkbenches = new();

    /// <summary>One Create Translation per work, for the same reason: two runs would race to save one edition.</summary>
    private static readonly Dictionary<int, CreateTranslationWindow> OpenCreators = new();

    /// <summary>
    /// Opens the translation workbench for a work, creating or resuming a
    /// hand-written translation edition for it.
    ///
    /// A hand-written translation gets a name, unlike the AI ones - those are
    /// the same process run twice and a timestamp tells them apart perfectly
    /// well, whereas "my literal draft" is a thing someone chose to make and
    /// should be able to call something.
    /// </summary>
    public static async Task OpenWorkbenchAsync(IReaderHost host, Work work)
    {
        if (OpenWorkbenches.TryGetValue(work.WorkId, out var open))
        {
            open.Activate();
            return;
        }

        var editionRepo = new EditionRepository();
        var textNodeRepo = new TextNodeRepository();

        try
        {
            var editions = await editionRepo.GetByWorkAsync(work.WorkId);
            var originals = editions.Where(e => e.Kind == EditionKind.Original).ToList();

            if (originals.Count == 0)
            {
                await Alert.Info(host.Window, "Translate", "This work has no original-language text to translate from.");
                return;
            }

            // Resume an existing hand-written edition rather than starting a
            // second one - the workbench is for working through a text over
            // time, and silently beginning again would be the opposite.
            // Kind as well as the URN, not the URN alone.
            //
            // This edition is handed to the workbench, which deletes a passage
            // when its box is cleared - so a substring match was the only
            // thing standing between "clear this line" and deleting from an
            // ingested text. Kind is already in hand one statement above, for
            // the originals list; asking for it here costs nothing. The
            // repository refuses a non-translation too, and this is the half
            // that means the refusal is never reached.
            var mine = editions.FirstOrDefault(e =>
                e.Kind == EditionKind.Translation
                && e.CtsUrn.Contains(".mine-", StringComparison.Ordinal));

            var lineCounts = new Dictionary<int, int>();
            foreach (var edition in editions)
            {
                lineCounts[edition.EditionId] = await textNodeRepo.CountByEditionAsync(edition.EditionId);
            }

            Edition? source;

            if (mine != null)
            {
                // Which original this translation was built against isn't
                // recorded anywhere, so it's inferred from shared citation
                // references. Getting this wrong on resume would be worse
                // than asking - the passages already written would stop
                // lining up - so a failed inference falls back to asking.
                var inferred = await textNodeRepo.FindClosestEditionAsync(mine.EditionId, work.WorkId, EditionKind.Original);

                source = originals.FirstOrDefault(e => e.EditionId == inferred)
                         ?? (originals.Count == 1
                             ? originals[0]
                             : await EditionChoiceDialog.AskAsync(host.Window,
                                 "Which text is this translation being made from?", originals, lineCounts));

                if (source == null) return;
            }
            else if (originals.Count == 1)
            {
                source = originals[0];
            }
            else
            {
                source = await EditionChoiceDialog.AskAsync(host.Window,
                    "This work has more than one original-language edition. Which one are you " +
                    "translating from? Every passage you write is filed under that edition's " +
                    "citation references, so this can't be changed later.",
                    originals, lineCounts);

                if (source == null) return;
            }

            if (mine == null)
            {
                var name = await TextPrompt.Ask(host.Window, "Translate This Work",
                    "What should this translation be called?", "My translation", confirm: "Save");
                if (string.IsNullOrWhiteSpace(name)) return;

                var editionId = await editionRepo.UpsertAsync(new Edition
                {
                    WorkId = work.WorkId,
                    CtsUrn = $"{work.CtsUrn}.mine-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    Kind = EditionKind.Translation,
                    Language = "eng",
                    Translator = name.Trim(),
                    SourcePath = null
                });

                mine = (await editionRepo.GetByWorkAsync(work.WorkId)).First(e => e.EditionId == editionId);
            }

            var sourcePassages = await textNodeRepo.GetByEditionAsync(source.EditionId);
            var existing = (await textNodeRepo.GetByEditionAsync(mine.EditionId))
                .GroupBy(n => n.CitationRef)
                .ToDictionary(g => g.Key, g => g.First().Text);

            // Anything else that translates this work, yours excluded -
            // checking your work against itself would tell you nothing.
            var others = editions
                .Where(e => e.Kind == EditionKind.Translation && e.EditionId != mine.EditionId)
                .ToList();

            // The one whose citation references overlap the chosen source
            // most is the likeliest match, so it leads the list.
            var closest = await textNodeRepo.FindClosestEditionAsync(source.EditionId, work.WorkId, EditionKind.Translation);

            others = others
                .OrderBy(e => e.EditionId == closest ? 0 : 1)
                .ThenBy(e => e.Translator, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var comparisons = others
                .Select(e => (Edition: e, Label: DescribeComparison(e, lineCounts, e.EditionId == closest)))
                .ToList();

            var workbench = new TranslationWorkbenchWindow(
                host, work, host.AuthorNameFor(work), mine, source, sourcePassages, existing, comparisons,
                async editionId => new PassageAligner(await new TextNodeRepository().GetByEditionAsync(editionId)))
            {
                OnNavigate = host.NavigateToPassageAsync,
                OnOpenWork = host.OpenWorkAsync
            };

            OpenWorkbenches[work.WorkId] = workbench;
            workbench.Closed += async (_, _) =>
            {
                OpenWorkbenches.Remove(work.WorkId);

                // The reader's edition pickers learn of the new translation,
                // and its pane of what was written.
                await ReloadReaderAsync(host);
            };

            workbench.Show(host.Window);
        }
        catch (Exception ex)
        {
            await Alert.Error(host.Window, "Translate", $"Couldn't open the translation workbench: {ex.Message}");
        }
    }

    /// <summary>
    /// How a candidate comparison translation is labelled in the workbench's
    /// picker - translator, length, and whether its citation references
    /// actually line up with the text being translated.
    /// </summary>
    internal static string DescribeComparison(Edition edition, IReadOnlyDictionary<int, int> lineCounts, bool isClosest)
    {
        var who = string.IsNullOrWhiteSpace(edition.Translator)
            ? edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "translation"
            : edition.Translator;

        var lines = lineCounts.TryGetValue(edition.EditionId, out var n) ? $"{n:N0} lines" : "unknown length";

        return isClosest ? $"{who} · {lines} · lines up with this text" : $"{who} · {lines}";
    }

    /// <summary>
    /// A whole work translated by Gemini into a new edition. Needs the work's
    /// original-language edition specifically - there has to be something to
    /// translate from. A work with only translations ingested gets a clear
    /// message instead of a window with nothing to show.
    /// </summary>
    public static async Task CreateTranslationAsync(IReaderHost host, Work work)
    {
        if (OpenCreators.TryGetValue(work.WorkId, out var open))
        {
            open.Activate();
            return;
        }

        try
        {
            var editions = await new EditionRepository().GetByWorkAsync(work.WorkId);
            var originalEdition = editions.FirstOrDefault(e => e.Kind == EditionKind.Original);
            if (originalEdition == null)
            {
                await Alert.Info(host.Window, "Nothing to translate",
                    $"\"{work.Title}\" has no original-language edition ingested, so there's nothing to translate from.");
                return;
            }

            var author = await new AuthorRepository().GetByIdAsync(work.AuthorId);
            var authorName = author?.Name ?? "Unknown Author";

            var window = new CreateTranslationWindow(work, authorName, originalEdition.EditionId, originalEdition.Language, targetLanguage: "eng")
            {
                OnNavigate = host.NavigateToPassageAsync,
                OnOpenWork = host.OpenWorkAsync
            };

            OpenCreators[work.WorkId] = window;
            window.Closed += async (_, _) =>
            {
                OpenCreators.Remove(work.WorkId);

                // A run cancelled by the close may still be saving its last
                // batch; the reader reloads once that has landed.
                await window.Running;
                if (window.PersistedAnything) await ReloadReaderAsync(host);
            };

            window.Show(host.Window);
        }
        catch (Exception ex)
        {
            await Alert.Error(host.Window, "Create Translation", $"Couldn't open Create Translation: {ex.Message}");
        }
    }

    /// <summary>
    /// The right-clicked passage, in both directions: the counterpart is
    /// whichever pane wasn't clicked, so a click in the original looks toward
    /// the translation, and a click in the translation looks toward the
    /// original rather than uselessly matching against its own edition. The
    /// counterpart's language becomes the AI translation's target, so an
    /// English line is rendered into the work's original language rather
    /// than English into English.
    /// </summary>
    public static async Task TranslatePassageAsync(IReaderHost host, PassageContext passage)
    {
        var sourceInfo = await new TextNodeRepository().GetTextNodeSourceInfoAsync(passage.Node.TextNodeId);
        var authorName = sourceInfo?.AuthorName ?? "Unknown Author";
        var workTitle = sourceInfo?.WorkTitle ?? "Unknown Work";

        var font = Avalonia.Application.Current?.FindResource(passage.FromOriginal ? "Cc.SourceFont" : "Cc.TranslationFont") as FontFamily
                   ?? FontFamily.Default;

        var window = new TranslatePassageWindow(
            passage.Node, passage.Edition?.Language, passage.Counterpart?.Language, authorName, workTitle,
            passage.Counterpart?.EditionId, counterpartIsTranslation: passage.FromOriginal, font)
        {
            OnNavigate = host.NavigateToPassageAsync,
            OnOpenWork = host.OpenWorkAsync
        };

        window.Show(host.Window);
    }

    /// <summary>
    /// Offers a short list of works worth translating first, and opens the
    /// workbench on whichever the reader picks.
    ///
    /// Opens the work in the reader and then defers to OpenWorkbenchAsync
    /// rather than launching the workbench directly. That method carries a
    /// good deal besides - resuming an existing hand-written edition, ranking
    /// the comparison translations, the no-original guard - and a second
    /// launch path would have to keep pace with all of it.
    /// </summary>
    public static Task StartingPointsAsync(IReaderHost host)
    {
        var window = new StartingPointsWindow(async work =>
        {
            if (!await host.OpenWorkAsync(work.WorkId))
            {
                await Alert.Info(host.Window, "Where to start", "That work is no longer in the library.");
                return;
            }

            await OpenWorkbenchAsync(host, work);
        })
        {
            OnNavigate = host.NavigateToPassageAsync,
            OnOpenWork = host.OpenWorkAsync
        };

        window.Show(host.Window);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reloads the open work after a translation was written or changed.
    /// Runs from a window's Closed event, where an exception has nowhere to
    /// go but the app itself: the translation is already saved, and a reader
    /// that failed to refresh is not worth quitting over.
    /// </summary>
    private static async Task ReloadReaderAsync(IReaderHost host)
    {
        try
        {
            await host.ReloadOpenWorkAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "reloading the reader after a translation changed");
        }
    }

    /// <summary>The open workbench for a work, if there is one.</summary>
    internal static TranslationWorkbenchWindow? WorkbenchFor(int workId) =>
        OpenWorkbenches.TryGetValue(workId, out var window) ? window : null;
}
