using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What keeps a repo's working directories out of the corpus.
///
/// First1KGreek ships more than its textgroups. Alongside tlg0018, ggm0001 and
/// the rest sit save/ (106 files), split/ (312) and volume_xml/ (85), and the
/// first two hold the SAME works as the textgroups they were derived from -
/// volume_xml has whole printed volumes, split/ the per-work extractions taken
/// out of them. Ingesting all three would put several editions into the corpus
/// two and three times over.
///
/// Nothing stops that by name. What stops it is that PerseusIngestService asks
/// CtsCatalogReader for a __cts__.xml at the top of every directory it finds
/// under the data path, and skips the directory when there is none - and
/// save/, split/ and volume_xml/ have none. (save/ has 53 of them further
/// down, under save/tlg0062/... , which is exactly why the check has to be at
/// the top level and not a search.)
///
/// That is a load-bearing accident. If First1KGreek ever adds a catalog file
/// to one of those folders, the corpus silently doubles - and duplicate texts
/// do not make a Burrows's Delta run fail, they make it confident and wrong.
/// These tests pin the behaviour the exclusion rests on.
/// </summary>
public class CorpusFolderExclusionTests
{
    private static string NewRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "cc-cts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteTextGroup(string dir, string urn, string name)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "__cts__.xml"),
            $@"<ti:textgroup xmlns:ti=""http://chs.harvard.edu/xmlns/cts"" urn=""{urn}"">
                 <ti:groupname xml:lang=""eng"">{name}</ti:groupname>
               </ti:textgroup>");
    }

    /// <summary>
    /// A real textgroup folder is read.
    /// </summary>
    [Fact]
    public void ATextGroupWithACatalogIsRead()
    {
        var root = NewRepo();
        try
        {
            var dir = Path.Combine(root, "tlg0018");
            WriteTextGroup(dir, "urn:cts:greekLit:tlg0018", "Philo Judaeus");

            var info = new CtsCatalogReader().ReadTextGroup(Path.Combine(dir, "__cts__.xml"));

            Assert.NotNull(info);
            Assert.Equal("Philo Judaeus", info!.GroupName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A catalog naming nobody is treated as no catalog.
    ///
    /// The Patrologia Latina repository carries thousands of placeholder textgroups
    /// whose groupname element is present and empty. Read literally, each became an
    /// author with no name: rows that cannot be read, searched for, or told apart, and
    /// which look to anyone opening the library like corruption rather than like a
    /// corpus that was passed over.
    /// </summary>
    [Fact]
    public void ATextGroupNamingNobodyIsSkipped()
    {
        var root = NewRepo();
        try
        {
            var dir = Path.Combine(root, "tmp26");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "__cts__.xml"),
                @"<ti:textgroup xmlns:ti=""http://chs.harvard.edu/xmlns/cts"" urn=""urn:cts:latinLit:tmp26"">
                    <ti:groupname xml:lang=""eng""></ti:groupname>
                  </ti:textgroup>");

            Assert.Null(new CtsCatalogReader().ReadTextGroup(Path.Combine(dir, "__cts__.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A working directory has no catalog of its own, so it is skipped -
    /// however many catalogs sit inside it. This is save/ exactly: 53
    /// __cts__.xml files below it and none at the top.
    /// </summary>
    [Fact]
    public void AWorkingDirectoryWithNoCatalogOfItsOwnIsSkipped()
    {
        var root = NewRepo();
        try
        {
            var save = Path.Combine(root, "save");
            Directory.CreateDirectory(save);

            // The catalogs that DO exist inside it, two levels down.
            WriteTextGroup(Path.Combine(save, "tlg0062"), "urn:cts:greekLit:tlg0062", "Lucian");

            var info = new CtsCatalogReader()
                .ReadTextGroup(Path.Combine(save, "__cts__.xml"));

            Assert.Null(info);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// split/ and volume_xml/ hold no catalog at any depth, so they are skipped
    /// for the same reason with nothing inside them to confuse it.
    /// </summary>
    [Theory]
    [InlineData("split")]
    [InlineData("volume_xml")]
    [InlineData("raw_files")]
    public void ADirectoryWithNoCatalogAnywhereIsSkipped(string folder)
    {
        var root = NewRepo();
        try
        {
            var dir = Path.Combine(root, folder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "commentaria_07.xml"), "<TEI/>");

            Assert.Null(new CtsCatalogReader().ReadTextGroup(Path.Combine(dir, "__cts__.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A catalog that is present but says nothing usable is also a skip rather
    /// than a half-built author with a null name.
    /// </summary>
    [Fact]
    public void ACatalogWithNoGroupNameIsSkipped()
    {
        var root = NewRepo();
        try
        {
            var dir = Path.Combine(root, "tlg9999");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "__cts__.xml"),
                @"<ti:textgroup xmlns:ti=""http://chs.harvard.edu/xmlns/cts""
                                urn=""urn:cts:greekLit:tlg9999"" />");

            Assert.Null(new CtsCatalogReader().ReadTextGroup(Path.Combine(dir, "__cts__.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
