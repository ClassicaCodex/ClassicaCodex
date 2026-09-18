using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Test classes that reach for something the whole process shares, and so
/// cannot run at the same time as each other.
///
/// xUnit gives every test class its own collection by default and runs
/// collections in parallel, which is right for almost everything here -
/// these are mostly pure measurements over controls each test builds itself.
/// Two things in this project are not like that:
///
/// <b>DpiScaling.DpiForTests</b> is a static, because it has to be readable
/// from the STA thread a window actually lives on and the test that sets it
/// runs on a different thread - so a per-test or per-thread seam could not
/// reach it. GuidedSetupLayoutTests and ReaderAreaLayoutTests both write it,
/// and in parallel they take turns overwriting one another's value.
///
/// That is worse than a flake. If one class nulls the seam while the other is
/// mid-measurement, the scale function the second one is testing against
/// quietly becomes the identity - and an identity satisfies every scaled
/// assertion in the file, so a genuine regression to raw pixels would pass. A
/// test that cannot fail is the thing both those classes exist to prevent,
/// which makes this exactly the wrong place to have one. It went the other way
/// too: a scaling left behind by the other class made
/// AtOneHundredPercentNothingMoves expect 320 and get 325, failing for a
/// reason nobody could reproduce.
///
/// <b>The configured database.</b> EmptyLibraryFixture calls
/// DbConnectionFactory.Configure, which is also process-wide, so two classes
/// holding their own instance of that fixture at once point the application at
/// two different temp databases and the last one to run wins.
///
/// DisableParallelization keeps this collection from running beside the
/// others, and membership of one collection keeps its own classes in single
/// file. The cost is a few seconds of a forty-second run.
///
/// The Data tests solved the same problem the same way - see the "Database"
/// collection in TempDatabase.cs.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class SharedProcessStateCollection
{
    public const string Name = "SharedProcessState";
}
