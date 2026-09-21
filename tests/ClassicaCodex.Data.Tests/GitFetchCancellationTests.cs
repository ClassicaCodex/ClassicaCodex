using System.Text.RegularExpressions;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Cancelling a corpus download must not start a bigger one.
///
/// <b>What happened.</b> The setup wizards gained a Cancel button in 3.10.1.
/// The first person to press it saw "Stopping..." and then watched the machine
/// carry on downloading. The token had always been threaded through correctly
/// and the code that received it had always been right; what was wrong was the
/// seam between them, and it could not exist until something called Cancel.
///
/// A progress callback returning false is how libgit2 is told to abort a
/// transfer, and LibGit2Sharp surfaces that as UserCancelledException - which
/// derives from LibGit2SharpException. GitCorpusFetchService clones shallow
/// first and catches LibGit2SharpException to fall back to a full clone,
/// because shallow needs a server that supports it. So Cancel aborted the
/// shallow clone, was read as "this server cannot do shallow", and was
/// answered by deleting the partial download and cloning the whole repository
/// instead - history and every past revision of every file, several times
/// larger than the download just cancelled. The status line said "fetching in
/// full instead", which was the honest report of a decision made for the wrong
/// reason.
///
/// <b>Why this is a lint and not a run.</b> Reproducing it needs a real remote
/// and a repository big enough that a transfer callback fires before the clone
/// finishes - a network test measured in gigabytes. And it cannot be reached
/// from the other end either: FetchAsync wraps its body in Task.Run(body,
/// token), so a token cancelled before the call means the body never runs at
/// all and the interesting code is never entered.
///
/// What can be pinned exactly is the structure that made it safe, and the
/// structure is the whole fix: every clone goes through one guarded helper. A
/// second Repository.Clone call written anywhere else in this file is the bug
/// again, whatever its author intended.
/// </summary>
public class GitFetchCancellationTests
{
    private static string Source()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ClassicaCodex.sln")))
            directory = directory.Parent;

        Assert.True(directory != null, "Could not find ClassicaCodex.sln above " + AppContext.BaseDirectory);

        var path = Path.Combine(
            directory!.FullName, "src", "ClassicaCodex.Ingestion", "GitCorpusFetchService.cs");

        Assert.True(File.Exists(path), path + " is not there any more");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// One clone call, inside the helper that knows a cancellation when it sees
    /// one. Any other is unguarded by construction.
    /// </summary>
    [Fact]
    public void EveryCloneGoesThroughTheGuardedHelper()
    {
        var clones = Regex.Matches(Source(), @"Repository\.Clone\s*\(").Count;

        Assert.True(clones == 1,
            $"GitCorpusFetchService calls Repository.Clone {clones} times. It must call it exactly "
            + "once, inside CloneWith - the helper that converts libgit2's own cancellation "
            + "exception into an OperationCanceledException. A clone anywhere else throws "
            + "UserCancelledException, which is a LibGit2SharpException, which the shallow-clone "
            + "fallback catches and answers by starting a full clone of the same repository. See "
            + "this class's summary.");
    }

    /// <summary>
    /// And that the helper still tells the two apart. Without the filter the
    /// conversion is either absent or unconditional, and unconditional would be
    /// worse: a genuine shallow-unsupported failure would be reported as though
    /// the user had cancelled, and the fallback that makes those servers work at
    /// all would never run.
    /// </summary>
    [Fact]
    public void TheHelperTreatsACancelledCloneAsACancellation()
    {
        var source = Source();

        Assert.Matches(
            @"catch\s*\(\s*LibGit2SharpException\s*\)\s*when\s*\(\s*cancellationToken\.IsCancellationRequested\s*\)",
            source);

        Assert.Contains("throw new OperationCanceledException(cancellationToken);", source);
    }

    /// <summary>
    /// The fallback itself has to survive. It is not scaffolding: shallow clone
    /// needs libgit2 1.7+ and a server that cooperates, and the full clone is
    /// what makes a repository without that support work at all.
    /// </summary>
    [Fact]
    public void TheShallowCloneFallbackIsStillThere()
    {
        var source = Source();

        Assert.Contains("Shallow download unavailable", source);
        Assert.Contains("CloneWith(depth: null)", source);
        Assert.Contains("CloneWith(depth: 1)", source);
    }

    /// <summary>
    /// Cancel is noticed during the wait as well as during the transfer.
    ///
    /// OnTransferProgress only fires once objects start arriving, and on a
    /// corpus this size the server can spend a long time counting them first.
    /// With only that callback, Cancel did nothing visible until the first
    /// packet landed - which on a slow connection is exactly the stretch
    /// somebody changes their mind in.
    /// </summary>
    [Fact]
    public void BothProgressCallbacksReportTheCancellation()
    {
        var source = Source();

        Assert.Contains("OnTransferProgress", source);
        Assert.Contains("OnProgress", source);

        var reporting = Regex.Matches(source, @"!cancellationToken\.IsCancellationRequested").Count;

        Assert.True(reporting >= 2,
            $"only {reporting} progress callback(s) return the cancellation state to libgit2. Both "
            + "OnTransferProgress and OnProgress have to, or Cancel is not noticed until the "
            + "transfer itself begins.");
    }
}
