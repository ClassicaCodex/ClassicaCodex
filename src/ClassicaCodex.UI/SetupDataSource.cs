using ClassicaCodex.Ingestion;
using System.Windows.Forms;

namespace ClassicaCodex.UI;

/// <summary>
/// How a SetupDataSource's bytes actually arrive.
/// </summary>
public enum SetupFetchMode
{
    /// <summary>Clone the git repository at RepoUrl into the destination folder.</summary>
    GitClone,

    /// <summary>
    /// Plain HTTPS download of the single file at RepoUrl, saved into the
    /// destination folder as DownloadFileName. For sources where the repo
    /// is enormous but only one file matters - cloning Natural Earth's
    /// several-gigabyte repository for one 800KB GeoJSON would be absurd.
    /// </summary>
    DirectDownload,

    /// <summary>
    /// The wizard's fetch step does nothing at all - RunIngest is fully
    /// responsible for its own fetching, however many files or requests
    /// that takes. For a source like Art &amp; Archaeology data, which needs
    /// thirteen separate files rather than one.
    /// </summary>
    SelfManaged
}

/// <summary>
/// One external data source ClassicaCodex can fetch and ingest - Ancient
/// Greek Texts, the dictionaries, and so on. Defined once in
/// SetupDataSourceCatalog and consumed by both SetupWizardForm (the
/// all-at-once view) and GuidedSetupForm (the step-by-step one), so the two
/// never drift apart on what a given step actually does or how "is this
/// already loaded" gets decided.
/// </summary>
/// <summary>
/// Whether a supporting file a step depends on is in place.
///
/// Three states rather than two, because "not downloaded yet" and "downloaded
/// something that isn't it" need different words. A user who clicked a .txt
/// link and let the browser display it, then saved the page, has a file of
/// the right name and none of the right content - and a bare red cross would
/// send them to download it again and get the same result.
/// </summary>
public enum SetupReadinessState
{
    Missing,
    Problem,
    Ready
}

public sealed record SetupReadiness(SetupReadinessState State, string Message);

/// <summary>A labelled hyperlink on a setup step.</summary>
public sealed class SetupLink
{
    public string Text = "";
    public string Url = "";
}

public class SetupDataSource
{
    public string Title = string.Empty;
    public string RepoUrl = string.Empty;
    public string? DisplayNote;
    public string DefaultDestination = string.Empty;

    public SetupFetchMode FetchMode = SetupFetchMode.GitClone;

    /// <summary>File name to save as, for DirectDownload sources only.</summary>
    public string? DownloadFileName;

    /// <summary>
    /// A one- or two-sentence, jargon-free explanation of what this step is
    /// for - written for GuidedSetupForm, where there's no repo URL or file
    /// path on screen to give context the way SetupWizardForm's rows do.
    /// </summary>
    public string PlainLanguageDescription = string.Empty;

    /// <summary>
    /// Overrides the guided setup step's button label.
    ///
    /// The default reads "Download &amp; Install", which is accurate for every
    /// source that fetches its own files and a lie for one that opens a
    /// website and then reports on a folder. Null keeps the default.
    /// </summary>
    public string? ActionButtonText;

    /// <summary>
    /// Hyperlinks shown above the step's buttons, for sources whose files come
    /// from a website the user has to visit.
    ///
    /// Separated from the action button because they are different kinds of
    /// thing. "Open Menota &amp; Check Folder" was one button doing two
    /// unrelated jobs, and which of them it did depended on whether the folder
    /// happened to be empty - so the same button behaved differently on
    /// consecutive presses with nothing on screen explaining why. Visiting a
    /// website is not an operation with progress and an outcome; it is a link.
    ///
    /// A list because Menota needs two: the manuscript catalogue, and the
    /// character entity file that makes the manuscripts legible. Empty on
    /// every source that fetches its own files, which is most of them.
    /// </summary>
    public List<SetupLink> Links = new();

    /// <summary>
    /// Checks a supporting file the step depends on, and reports it beside the
    /// links. Given the destination folder; called whenever the step is shown
    /// and after every action, so it answers as of now rather than as of when
    /// the wizard opened.
    /// </summary>
    public Func<string, SetupReadiness>? CheckReadiness;

    /// <summary>
    /// Shows the destination folder on the step, read-only, and gives the step
    /// a log box for its output.
    ///
    /// For sources whose files the user puts there by hand, where the path is
    /// the single most useful thing on screen and burying it in a paragraph of
    /// description made it something to be read rather than copied.
    /// </summary>
    public bool ShowDestinationPath;

    /// <summary>
    /// An optional second action, shown as a button beside the first.
    ///
    /// For sources where inspecting what arrived and importing it are
    /// genuinely separate decisions - Menota, where the survey reports what a
    /// folder of manuscripts contains and the import is a further step the
    /// user takes once they have read it.
    /// </summary>
    public string? SecondaryButtonText;

    /// <summary>
    /// Runs the secondary action. Never preceded by a fetch: it operates on
    /// files that are already in the destination folder.
    /// </summary>
    public Func<string, IProgress<string>, CancellationToken, Task<IngestOutcome>>? RunSecondary;

    /// <summary>
    /// Runs on the UI thread immediately before RunSecondary, and can cancel
    /// it by returning false.
    ///
    /// For a step that has to ask the user something before it can do its
    /// work. RunSecondary itself runs on a background thread, where showing a
    /// dialog is not an option, so the asking has to happen here.
    /// </summary>
    public Func<IWin32Window, bool>? PrepareSecondary;

    /// <summary>
    /// Runs the step, and reports what it skipped. Returning IngestOutcome
    /// rather than a bare Task is the whole point: the ingest services
    /// already recorded which files they couldn't parse, and this delegate
    /// used to drop that on the floor along with the service instance that
    /// held it.
    /// </summary>
    public Func<string, IProgress<string>, CancellationToken, Task<IngestOutcome>> RunIngest = null!;

    public Func<Task<bool>> CheckComplete = null!;
}
