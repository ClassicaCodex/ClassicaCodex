namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// What the Settings window may change in the reader while it is open. Each
/// goes through the same path the toolbar and the menu bar use, so a choice
/// made in Settings shows in the reader, the toolbar and the menu at once.
/// </summary>
public partial class MainWindow
{
    public void SetPanesLinkedFromSettings(bool linked) => SetPanesLinked(linked);

    public void SetCitationMarginFromSettings(bool enabled) => SetCitationMargin(enabled);

    /// <summary>Refills both pages, after the kinds of line on show have changed.</summary>
    public Task RefreshPanesFromSettingsAsync() => RefreshReaderPanesAsync();
}
