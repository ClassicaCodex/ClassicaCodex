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
    /// that takes. For a source like Art & Archaeology data, which needs
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

    public Func<string, IProgress<string>, CancellationToken, Task> RunIngest = null!;
    public Func<Task<bool>> CheckComplete = null!;
}
