namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Runs a piece of asynchronous work on a thread a Windows Forms control can
/// actually live on.
///
/// Needed because the reader pane is a real control and the things worth
/// testing about it are asynchronous. A test runner's thread is neither: it is
/// MTA, and it pumps no messages, so a control created there has no message
/// loop and an await that resumes on the UI context never resumes at all - the
/// test hangs rather than fails, which is the worst way for a test to be wrong.
///
/// Two details are load-bearing. The thread is STA, because that is what
/// creating a window requires. And the work is posted into a real message loop
/// rather than waited on, because the code under test resumes on the
/// synchronization context and something has to be pumping it.
///
/// A control that is never shown also measures nothing - it has no handle, and
/// the drawing and measuring paths quietly do nothing - so tests here force
/// handles into existence. That trap cost a release's worth of measurements
/// once already.
/// </summary>
internal static class StaHarness
{
    internal static void Run(Func<Form, Task> work, int timeoutSeconds = 60)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

                var loop = new ApplicationContext();
                var host = new Form { ClientSize = new Size(460, 420) };
                host.CreateControl();
                _ = host.Handle;

                SynchronizationContext.Current!.Post(async _ =>
                {
                    try
                    {
                        await work(host);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        host.Dispose();
                        loop.ExitThread();
                    }
                }, null);

                Application.Run(loop);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            throw new TimeoutException(
                $"the pane did not finish within {timeoutSeconds}s - a hang here usually means an await " +
                "resuming on a context nothing is pumping");
        }

        if (failure != null) throw failure;
    }
}
