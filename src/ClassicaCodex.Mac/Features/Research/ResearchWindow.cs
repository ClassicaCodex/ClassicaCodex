using Avalonia.Controls;
using Avalonia.Input;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Settings;
using ClassicaCodex.Mac.Platform;
using ClassicaCodex.Mac.Shell;

namespace ClassicaCodex.Mac.Features.Research;

/// <summary>
/// What the research windows have in common beyond <see cref="FeatureWindow"/>:
/// the reader to jump to, ⌘-key commands, a way to run a button's work that
/// reports a failure instead of crashing, and the one question every AI
/// surface asks first - is there a key yet.
/// </summary>
internal abstract class ResearchWindow : FeatureWindow
{
    protected ResearchWindow(IReaderHost? host)
    {
        Host = host;
        if (host != null)
        {
            OnNavigate = host.NavigateToPassageAsync;
            OnOpenWork = host.OpenWorkAsync;
        }

        KeyDown += (_, e) =>
        {
            if (!e.Handled && Ui.Command(e) && OnCommandKey(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
                e.Handled = true;
        };
    }

    /// <summary>The reader, for jumping to a passage and opening other tools. Null only in a dialog opened without one.</summary>
    protected IReaderHost? Host { get; }

    /// <summary>A ⌘-key chord; true if the window acted on it.</summary>
    protected virtual bool OnCommandKey(Key key, bool shift) => false;

    /// <summary>
    /// Runs a button's work. A failure is reported in a sheet rather than
    /// thrown out of an event handler, where it would end the application:
    /// the database a research window writes to can refuse a write for
    /// reasons no window can rule out.
    /// </summary>
    protected async void Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await Alert.Error(this, Title ?? "Research", ex.Message);
        }
    }

    /// <summary>Greys the window while a request is out, as the Windows forms do with Enabled.</summary>
    protected void SetBusy(bool busy)
    {
        if (Content is Control content) content.IsEnabled = !busy;
    }

    /// <summary>One line of text from the reader; null if cancelled or left empty, as on Windows.</summary>
    protected async Task<string?> PromptAsync(string title, string prompt, string initial = "")
    {
        var text = await TextPrompt.Ask(this, title, prompt, initial);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    protected Task<bool> ConfirmAsync(string title, string message, string confirm, AlertKind kind = AlertKind.Question) =>
        Alert.Confirm(this, title, message, confirm, "Cancel", kind);

    /// <summary>
    /// True once there is a Gemini key, opening the AI settings first if there
    /// is not. Carries on when a key was entered there - stopping regardless made
    /// the reader press the button a second time for no reason they could see.
    /// </summary>
    protected async Task<bool> EnsureGeminiKeyAsync()
    {
        if (!string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey)) return true;
        await SettingsFeature.OpenAiSettingsAsync(this);
        return !string.IsNullOrWhiteSpace(TranslationSettings.GeminiApiKey);
    }

    /// <summary>
    /// The confirmation before anything leaves the machine, unless the reader
    /// has switched it off in the AI settings.
    /// </summary>
    protected async Task<bool> ConfirmSendAsync(string title, string message)
    {
        if (!TranslationSettings.AlwaysConfirmBeforeSending) return true;
        return await Alert.Confirm(this, title, message, "Send", "Cancel", AlertKind.Warning);
    }
}
