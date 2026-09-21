using System.Reflection;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// The action button's label while a setup step runs, and the moment it stops
/// being Cancel.
///
/// <b>What was reported.</b> "The button still says Cancel after you hit
/// cancel." The visible half of a worse problem: the label was being restored
/// by the RenderStep at the far end of the runner's finally block, and between
/// enabling the button and reaching that RenderStep there is an await on
/// RefreshAllCompletionAsync - which counts distinct rows in the word index,
/// twenty-four seconds on a full library by the measurement recorded in this
/// form's own Load handler.
///
/// For all of that the button said "Cancel", was enabled, and _stepRunning was
/// already false - so the click handler's first branch no longer applied and
/// pressing the button labelled Cancel started the download again. On the
/// largest step that is several gigabytes, begun by pressing the control whose
/// entire job is to stop them.
///
/// <b>What these tests pin.</b> Not the wording, which can change: that ending
/// a step takes the button out of its Cancel state synchronously, with nothing
/// awaited in between, so there is no window in which the label and the
/// behaviour disagree. They drive the same three calls the runners make -
/// BeginCancellableStep at the start, SetNavEnabled(true) at the end - rather
/// than a download, because a download is the one thing a test cannot have.
///
/// Advanced Setup never had this: SetAllRowsEnabled restores each row's label
/// synchronously, before its own await. The last test here says so, so that the
/// two windows cannot drift apart on it.
/// </summary>
[Collection(SharedProcessStateCollection.Name)]
public class SetupCancelButtonTests : IClassFixture<EmptyLibraryFixture>
{
    private const int DatabaseStep = 1;

    private static void WithGuidedSetup(Action<GuidedSetupForm, Func<string, object?[], object?>, Button> check)
    {
        StaHarness.Run(_ =>
        {
            using var form = new GuidedSetupForm();
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();

            var type = typeof(GuidedSetupForm);

            object? Invoke(string name, params object?[] args) =>
                type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(form, args);

            var action = (Button)type
                .GetField("_actionButton", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(form)!;

            // A step with an action button, rendered the way arriving at it
            // would render it.
            type.GetField("_currentStep", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(form, DatabaseStep);
            Invoke("RenderStep");
            Application.DoEvents();

            check(form, (name, args) => Invoke(name, args), action);

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Starting a cancellable step turns its own button into Cancel, and leaves
    /// it usable - the whole point, since everything else on the window is off.
    /// </summary>
    [Fact]
    public void AStepInFlightTurnsItsOwnButtonIntoCancel()
    {
        WithGuidedSetup((_, invoke, action) =>
        {
            var idle = action.Text;
            Assert.NotEqual("Cancel", idle);

            invoke("BeginCancellableStep", Array.Empty<object?>());

            Assert.Equal("Cancel", action.Text);
            Assert.True(action.Enabled,
                "the Cancel button has to be the one enabled control while a step runs - every "
                + "other control on this window is disabled for the duration");
        });
    }

    /// <summary>
    /// The reported bug. Ending the step restores the label immediately, with
    /// no await in between - so the button cannot be sitting there saying
    /// Cancel while a click on it would start the step over.
    /// </summary>
    [Fact]
    public void EndingTheStepStopsSayingCancelBeforeAnythingIsAwaited()
    {
        WithGuidedSetup((_, invoke, action) =>
        {
            var idle = action.Text;

            invoke("BeginCancellableStep", Array.Empty<object?>());
            Assert.Equal("Cancel", action.Text);

            // Exactly what the runners' finally blocks do first, and nothing
            // that comes after it. If the label needs RenderStep or a completed
            // refresh to come back, this fails - which is the bug.
            invoke("SetNavEnabled", new object?[] { true });

            Assert.Equal(idle, action.Text);
        });
    }

    /// <summary>
    /// And the label and the behaviour agree at that instant: the click handler
    /// decides what to do from _stepRunning, so the flag must clear in the same
    /// synchronous step that restores the label. Either one moving without the
    /// other is a button that lies about what pressing it does.
    /// </summary>
    [Fact]
    public void TheLabelAndTheRunningFlagChangeTogether()
    {
        WithGuidedSetup((form, invoke, action) =>
        {
            bool Running() => (bool)typeof(GuidedSetupForm)
                .GetField("_stepRunning", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(form)!;

            Assert.False(Running());

            invoke("BeginCancellableStep", Array.Empty<object?>());
            Assert.True(Running());
            Assert.Equal("Cancel", action.Text);

            invoke("SetNavEnabled", new object?[] { true });
            Assert.False(Running());
            Assert.NotEqual("Cancel", action.Text);
        });
    }

    /// <summary>
    /// Cancelling takes the button out of service rather than leaving a second
    /// click to arrive while the step unwinds, and says what is happening.
    /// </summary>
    [Fact]
    public void PressingCancelDisablesItAndSaysSo()
    {
        WithGuidedSetup((form, invoke, action) =>
        {
            var status = (Label)typeof(GuidedSetupForm)
                .GetField("_statusLabel", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(form)!;

            invoke("BeginCancellableStep", Array.Empty<object?>());

            // The runners create the token before the button; CancelStep does
            // nothing without one, which is correct and not what this measures.
            typeof(GuidedSetupForm)
                .GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(form, new CancellationTokenSource());

            invoke("CancelStep", Array.Empty<object?>());

            Assert.False(action.Enabled);
            Assert.Contains("Stopping", status.Text);
        });
    }

    /// <summary>
    /// Advanced Setup restores its row labels synchronously too. It always did;
    /// this is here so that a change to one window cannot quietly leave the
    /// other behind.
    /// </summary>
    [Fact]
    public void AdvancedSetupRestoresItsRowLabelsBeforeItsOwnAwait()
    {
        var source = File.ReadAllText(Path.Combine(UiSourceDirectory(), "SetupWizardForm.cs"));

        var body = source[source.IndexOf("private void SetAllRowsEnabled", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n    }", StringComparison.Ordinal)];

        Assert.Contains("row.ActionButton.Text", body);
        Assert.DoesNotContain("await", body);
    }

    private static string UiSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ClassicaCodex.sln")))
            directory = directory.Parent;

        Assert.True(directory != null, "Could not find ClassicaCodex.sln above " + AppContext.BaseDirectory);
        return Path.Combine(directory!.FullName, "src", "ClassicaCodex.UI");
    }
}
