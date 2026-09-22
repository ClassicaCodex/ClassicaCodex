using ClassicaCodex.Data.Repositories;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.UI.Tests;

/// <summary>
/// Invariants every setup step has to satisfy, checked over the whole
/// catalogue rather than over the one just added.
///
/// A setup step that is missing a piece does not fail loudly. It renders with
/// a blank description, or a button that does nothing, or a "already
/// installed" tick that is never earned - and it does that only on the screen
/// a new reader sees once, which is the screen a developer with a full
/// library never opens again.
///
/// Building the catalogue touches no database. Build captures the
/// repositories inside lambdas and calls none of them, so the checks below
/// examine the shape of every step without a library present.
/// </summary>
public class SetupDataSourceCatalogTests
{
    private static List<SetupDataSource> Build() => SetupDataSourceCatalog.Build(
        new AuthorRepository(), new LemmaRepository(), new DefinitionRepository(),
        new ArtifactRepository(), new EditionRepository());

    [Fact]
    public void EverySourceIsUsable()
    {
        Assert.All(Build(), source =>
        {
            Assert.False(string.IsNullOrWhiteSpace(source.Title));

            // GuidedSetupForm shows this instead of a repo URL, so a step
            // without one is a step with nothing on screen explaining it.
            Assert.False(string.IsNullOrWhiteSpace(source.PlainLanguageDescription),
                $"{source.Title} has no plain-language description");

            Assert.NotNull(source.RunIngest);
            Assert.NotNull(source.CheckComplete);
            Assert.False(string.IsNullOrWhiteSpace(source.DefaultDestination),
                $"{source.Title} has nowhere to put its files");
        });
    }

    /// <summary>
    /// A source that fetches one file has to say which file. DirectDownload
    /// with no name has nowhere to save to.
    /// </summary>
    [Fact]
    public void EveryDirectDownloadNamesItsFile()
    {
        Assert.All(
            Build().Where(s => s.FetchMode == SetupFetchMode.DirectDownload),
            source => Assert.False(string.IsNullOrWhiteSpace(source.DownloadFileName),
                $"{source.Title} downloads one file and does not name it"));
    }

    /// <summary>
    /// Every collection key must have a display name.
    ///
    /// DescribeCollection falls through to returning the raw key, so a
    /// missing one does not throw - the library just labels a collection
    /// "oracc" instead of "Cuneiform (Oracc)", in the tree, in search filters
    /// and in exports, and nobody notices until a screenshot.
    /// </summary>
    [Fact]
    public void EveryCollectionKeyHasADisplayName()
    {
        var keys = typeof(SetupDataSourceCatalog.CollectionKeys)
            .GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(keys);

        Assert.All(keys, key => Assert.NotEqual(
            key, SetupDataSourceCatalog.DescribeCollection(key)));
    }

    /// <summary>
    /// The cuneiform step specifically, because it is the first collection
    /// whose texts are not in an alphabet and the first that fetches several
    /// archives rather than one.
    /// </summary>
    [Fact]
    public void TheCuneiformStepIsPresentAndSelfFetching()
    {
        var oracc = Assert.Single(Build(), s => s.Title.Contains("Oracc"));

        // SelfManaged, because the wizard's own fetch step handles one file
        // and this needs eight archives extracted and imported in turn.
        Assert.Equal(SetupFetchMode.SelfManaged, oracc.FetchMode);

        // The reader is told there is no translation before they download,
        // not after they open a text and find only Akkadian.
        Assert.Contains("translation", oracc.PlainLanguageDescription);

        Assert.Equal("Cuneiform (Oracc)",
            SetupDataSourceCatalog.DescribeCollection(SetupDataSourceCatalog.CollectionKeys.Oracc));
    }
}
