using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Choosing where the downloads go.
///
/// The database has been movable for a long time; the downloads were not, and
/// they are much the larger of the two - about nine gigabytes for a full set,
/// five and a half of which is the Greek and Latin word-form data. They went to
/// a fixed folder under My Documents, which is on the system drive, which is
/// the drive a person with a small SSD does not have nine spare gigabytes on.
///
/// What is tested here is the part that can be tested without a window: whether
/// a folder will actually take a download. Everything expensive about getting
/// this wrong happens at the start of an hour-long fetch, so the check has to
/// be a real write rather than an inspection of attributes - a network share, a
/// signed-out cloud folder and a read-only mount all look fine until something
/// is written to them.
///
/// The stored preference itself is deliberately not exercised: writing it would
/// mean creating a real file in the user's own ClassicaCodex folder, which
/// tests have no business doing. Everything below works on temporary folders it
/// makes and removes.
/// </summary>
public class DataFolderTests : IDisposable
{
    private readonly string _scratch = Path.Combine(
        Path.GetTempPath(), "ccx-datafolder-tests", Guid.NewGuid().ToString("N"));

    public DataFolderTests() => Directory.CreateDirectory(_scratch);

    public void Dispose()
    {
        try { Directory.Delete(_scratch, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked temp file is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AFolderThatExistsAndTakesAWriteIsAccepted()
    {
        var ok = DataFolderSettings.TryPrepare(_scratch, out var resolved, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(_scratch, resolved);
    }

    /// <summary>
    /// The one someone hits with the browser dialog's New Folder button, or by
    /// typing a path onto a drive they have just plugged in. Creating it is
    /// part of accepting it - finding out at the first download that it was
    /// never there is finding out too late.
    /// </summary>
    [Fact]
    public void AFolderThatDoesNotExistYetIsCreatedRatherThanRefused()
    {
        var fresh = Path.Combine(_scratch, "not", "there", "yet");
        Assert.False(Directory.Exists(fresh));

        var ok = DataFolderSettings.TryPrepare(fresh, out var resolved, out _);

        Assert.True(ok);
        Assert.True(Directory.Exists(resolved));
    }

    /// <summary>
    /// The check writes a file to prove it can. It must not leave it there:
    /// this folder is about to be filled with a corpus someone will look at.
    /// </summary>
    [Fact]
    public void TheWriteTestLeavesNothingBehind()
    {
        var before = Directory.GetFileSystemEntries(_scratch);

        Assert.True(DataFolderSettings.TryPrepare(_scratch, out _, out _));

        Assert.Equal(before, Directory.GetFileSystemEntries(_scratch));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NothingTypedAsksForAFolderRatherThanFailing(string? nothing)
    {
        var ok = DataFolderSettings.TryPrepare(nothing, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.StartsWith("Enter ", error);
    }

    /// <summary>
    /// A path the operating system will not accept comes back as a refusal
    /// with a reason, not an exception - this runs while someone is looking at
    /// a wizard, and an unhandled throw there ends the setup.
    /// </summary>
    [Fact]
    public void AnImpossiblePathIsRefusedWithAReason()
    {
        var ok = DataFolderSettings.TryPrepare("::not a path::", out _, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// A relative path is resolved before it is stored. Storing one would make
    /// the download location depend on the working directory the program
    /// happened to be started from.
    /// </summary>
    [Fact]
    public void ARelativePathIsResolvedToAFullOne()
    {
        Assert.True(DataFolderSettings.TryPrepare("ccx-relative-probe", out var resolved, out _));

        Assert.True(Path.IsPathRooted(resolved));

        try { Directory.Delete(resolved); } catch (IOException) { }
    }

    /// <summary>
    /// The default is where every existing install already has its data, so
    /// that an upgrade asks nobody anything and moves nothing.
    /// </summary>
    [Fact]
    public void TheDefaultIsTheFolderDataAlreadyLivesIn()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClassicaCodexData");

        Assert.Equal(expected, DataFolderSettings.DefaultRoot);
    }

    /// <summary>
    /// What the wizard's tick mark asks on arrival, before anyone has chosen
    /// anything. It has to say yes to a folder that does not exist yet, because
    /// on a fresh install the default never does - and greeting a newcomer with
    /// a red cross beside a perfectly good default, on the step whose whole
    /// message is that the default is fine, is the wrong first impression.
    /// </summary>
    [Fact]
    public void AFolderThatDoesNotExistYetStillLooksUsableIfItCouldBeCreated()
    {
        Assert.True(DataFolderSettings.LooksUsable(Path.Combine(_scratch, "nothing", "here", "yet")));
    }

    [Fact]
    public void AFolderThatExistsLooksUsable()
    {
        Assert.True(DataFolderSettings.LooksUsable(_scratch));
    }

    /// <summary>
    /// The default has to pass this on a machine that has never run the
    /// program, which is every machine arriving from an announcement.
    /// </summary>
    [Fact]
    public void TheDefaultFolderLooksUsableEvenIfNobodyHasEverRunSetup()
    {
        Assert.True(DataFolderSettings.LooksUsable(DataFolderSettings.DefaultRoot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("::not a path::")]
    public void NonsenseDoesNotLookUsable(string? nonsense)
    {
        Assert.False(DataFolderSettings.LooksUsable(nonsense));
    }

    /// <summary>
    /// A drive letter nothing is mounted on has no existing ancestor at all,
    /// which is the case the upward walk has to terminate on rather than loop.
    /// </summary>
    [Fact]
    public void AFolderOnADriveThatIsNotThereDoesNotLookUsable()
    {
        var unused = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(c => $"{(char)c}:\\")
            .FirstOrDefault(root => !Directory.Exists(root));

        if (unused == null) return;   // every letter mounted; nothing to assert

        Assert.False(DataFolderSettings.LooksUsable(Path.Combine(unused, "ClassicaCodexData")));
    }

    [Fact]
    public void FreeSpaceIsReportedForARealDriveAndNotGuessedForANonsenseOne()
    {
        var free = DataFolderSettings.FreeGigabytesAt(_scratch);

        Assert.NotNull(free);
        Assert.True(free > 0);

        Assert.Null(DataFolderSettings.FreeGigabytesAt(string.Empty));
    }

    /// <summary>
    /// The threshold the wizard warns below has to be above the size of the
    /// thing being downloaded, not equal to it: a git clone needs room for a
    /// working copy as well as what it fetched.
    /// </summary>
    [Fact]
    public void TheComfortableThresholdLeavesRoomAboveAFullSet()
    {
        Assert.True(DataFolderSettings.ComfortableFreeGigabytes > DataFolderSettings.FullSetGigabytes);
    }
}
