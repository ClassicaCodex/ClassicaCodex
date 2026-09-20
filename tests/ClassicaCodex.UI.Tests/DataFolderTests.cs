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

    /// <summary>
    /// A folder inside OneDrive is recognised as one, whatever it is called
    /// underneath.
    ///
    /// This matters because of where the default came from. MyDocuments is a
    /// known folder, and on a Windows 11 install signed into a Microsoft
    /// account, OneDrive folder backup is on by default and redirects it - so
    /// the default download folder is inside OneDrive and the corpus steps put
    /// nine gigabytes of small files where they will be uploaded to an account
    /// whose free tier is five. The free-space check cannot see it: it asks
    /// DriveInfo about the local disk and answers "250 GB free" quite
    /// correctly.
    ///
    /// Nobody developing this would ever see it. The machines it was written
    /// on have a plain C:\Users\name\Documents, where every one of these
    /// assertions and the shipped behaviour are identical to what they were.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\someone\OneDrive\Documents\ClassicaCodexData")]
    [InlineData(@"C:\Users\someone\OneDrive - Contoso\Documents\ClassicaCodexData")]
    [InlineData(@"C:\Users\someone\onedrive\documents\ClassicaCodexData")]
    public void AFolderInsideOneDriveIsRecognised(string path)
    {
        Assert.Equal("OneDrive", DataFolderSettings.CloudSyncedBy(path));
    }

    [Theory]
    [InlineData(@"C:\Users\someone\Dropbox\ClassicaCodexData", "Dropbox")]
    [InlineData(@"C:\Users\someone\Google Drive\ClassicaCodexData", "Google Drive")]
    [InlineData(@"C:\Users\someone\iCloudDrive\ClassicaCodexData", "iCloud Drive")]
    public void TheOtherSyncedFoldersAreRecognisedToo(string path, string expected)
    {
        Assert.Equal(expected, DataFolderSettings.CloudSyncedBy(path));
    }

    /// <summary>
    /// And an ordinary folder is left alone. The last case is the one a
    /// prefix comparison gets wrong: a sibling whose name merely starts the
    /// same way is not inside anything.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\someone\Documents\ClassicaCodexData")]
    [InlineData(@"D:\ClassicaCodexData")]
    [InlineData(@"C:\Users\someone\OneDriveBackup\ClassicaCodexData")]
    [InlineData("")]
    [InlineData(null)]
    public void AnOrdinaryFolderIsNotReportedAsSynced(string? path)
    {
        Assert.Null(DataFolderSettings.CloudSyncedBy(path));
    }

    /// <summary>
    /// The suggestion only ever differs from the default when the default is
    /// both synced and empty, and on a machine whose Documents is not
    /// redirected - every machine this is developed on, and most machines
    /// generally - the two are the same string. Asserted rather than assumed,
    /// because the whole point of SuggestedRoot is that it changes nothing for
    /// an install that already has data where it always was.
    /// </summary>
    [Fact]
    public void TheSuggestionMatchesTheDefaultWhereDocumentsIsNotSynced()
    {
        if (DataFolderSettings.CloudSyncedBy(DataFolderSettings.DefaultRoot) != null)
        {
            // This machine's Documents IS redirected, so the interesting
            // assertion is the other one: the suggestion must lead somewhere
            // that is not synced.
            Assert.Null(DataFolderSettings.CloudSyncedBy(DataFolderSettings.SuggestedRoot));
            return;
        }

        Assert.Equal(DataFolderSettings.DefaultRoot, DataFolderSettings.SuggestedRoot);
    }
}
