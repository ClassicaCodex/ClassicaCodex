using System.Xml.Linq;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading publication metadata out of a TEI header.
///
/// This runs against both TEI P4 and P5 because the corpus genuinely
/// contains both - the older Perseus files have a &lt;TEI.2&gt; root and no
/// namespace at all, the Open Greek and Latin files are namespaced P5 - and
/// a reader that silently handles only one would leave half the library
/// showing no publication details with nothing to say why.
///
/// No database needed: Read takes a parsed document, which is what makes
/// these plain unit tests rather than integration ones.
/// </summary>
public class TeiHeaderReaderTests
{
    private const string PerseusP4 = """
        <TEI.2><teiHeader>
          <fileDesc>
            <titleStmt>
              <title>The Iliad</title>
              <author>Homer</author>
              <sponsor>Perseus Project, Tufts University</sponsor>
              <principal>Gregory Crane</principal>
              <respStmt>
                <resp>Prepared under the supervision of</resp>
                <name>Lisa Cerrato</name>
                <name>William Merrill</name>
              </respStmt>
            </titleStmt>
            <publicationStmt>
              <publisher>Trustees of Tufts University</publisher>
              <pubPlace>Medford, MA</pubPlace>
              <date>Sept 10, 2010</date>
              <availability status="free">Available under a CC BY-SA 3.0 licence.</availability>
            </publicationStmt>
            <sourceDesc>
              <biblStruct><monogr>
                <title>Homeri Opera</title>
                <author>Homer</author>
                <editor role="editor">D. B. Monro</editor>
                <imprint>
                  <pubPlace>Oxford</pubPlace>
                  <publisher>Clarendon Press</publisher>
                  <date>1920</date>
                </imprint>
              </monogr></biblStruct>
            </sourceDesc>
          </fileDesc>
        </teiHeader><text><body><div/></body></text></TEI.2>
        """;

    private const string OglP5 = """
        <TEI xmlns="http://www.tei-c.org/ns/1.0"><teiHeader>
          <fileDesc>
            <titleStmt>
              <title>De Bello Gallico</title>
              <author>Julius Caesar</author>
              <editor>T. Rice Holmes</editor>
            </titleStmt>
            <editionStmt><edition>First digital edition</edition></editionStmt>
            <publicationStmt>
              <publisher>Open Greek and Latin</publisher>
              <date>2021</date>
              <availability><p>CC BY-SA 4.0</p></availability>
            </publicationStmt>
            <sourceDesc><p>Digitised from the Oxford Classical Text, 1914.</p></sourceDesc>
          </fileDesc>
        </teiHeader><text><body><div/></body></text></TEI>
        """;

    [Fact]
    public void ReadsAP4HeaderWithNoNamespace()
    {
        var header = TeiHeaderReader.Read(XDocument.Parse(PerseusP4));

        Assert.NotNull(header);
        Assert.Equal("The Iliad", header!.Title);
        Assert.Equal("Homer", header.Author);
        Assert.Equal("Trustees of Tufts University", header.Publisher);
        Assert.Equal("Sept 10, 2010", header.PublicationDate);
        Assert.Equal("Medford, MA", header.PublicationPlace);
    }

    [Fact]
    public void ReadsANamespacedP5Header()
    {
        var header = TeiHeaderReader.Read(XDocument.Parse(OglP5));

        Assert.NotNull(header);
        Assert.Equal("De Bello Gallico", header!.Title);
        Assert.Equal("Julius Caesar", header.Author);
        Assert.Equal("Open Greek and Latin", header.Publisher);
        Assert.Equal("First digital edition", header.EditionStatement);
    }

    /// <summary>
    /// The bug this reader shipped with, caught only because the parsing was
    /// tested before it went out.
    ///
    /// A biblStruct is built from nested elements with no whitespace between
    /// the tags, so taking its raw .Value concatenates them: "Homeri Opera"
    /// followed by "Homer" arrives as "Homeri OperaHomer", and the publisher
    /// runs into its year as "Clarendon Press1920". This is the single most
    /// useful line in the whole details view - it's what identifies which
    /// printed edition a text came from - so it has to be assembled rather
    /// than concatenated.
    /// </summary>
    [Fact]
    public void PrintedSourceIsAssembledNotConcatenated()
    {
        var header = TeiHeaderReader.Read(XDocument.Parse(PerseusP4));

        var source = header!.SourceDescription;

        Assert.NotNull(source);
        Assert.DoesNotContain("OperaHomer", source);
        Assert.DoesNotContain("Press1920", source);
        Assert.Contains("Homeri Opera", source);
        Assert.Contains("Clarendon Press", source);
        Assert.Contains("1920", source);
    }

    /// <summary>
    /// The other shape sourceDesc takes - a prose paragraph rather than a
    /// structured citation - has to come through as its own sentence.
    /// </summary>
    [Fact]
    public void PrintedSourceHandlesAProseParagraph()
    {
        var header = TeiHeaderReader.Read(XDocument.Parse(OglP5));

        Assert.Equal("Digitised from the Oxford Classical Text, 1914.", header!.SourceDescription);
    }

    /// <summary>
    /// Roles are the interesting half - a bare list of names without them
    /// reads as noise, so each is formatted "role: name".
    /// </summary>
    [Fact]
    public void ResponsibilitiesCarryTheirRoles()
    {
        var header = TeiHeaderReader.Read(XDocument.Parse(PerseusP4));

        Assert.Contains(header!.Responsibilities,
            r => r.StartsWith("Prepared under the supervision of:") && r.Contains("Lisa Cerrato"));
        Assert.Contains(header.Responsibilities, r => r == "Sponsor: Perseus Project, Tufts University");
        Assert.Contains(header.Responsibilities, r => r == "Principal: Gregory Crane");
    }

    /// <summary>
    /// Some files use a plain editor element instead of a respStmt. Both
    /// mean the same thing to a reader and both should show up.
    /// </summary>
    [Fact]
    public void PlainEditorElementIsPickedUpToo()
    {
        var header = TeiHeaderReader.Read(XDocument.Parse(OglP5));

        Assert.Contains(header!.Responsibilities, r => r == "Editor: T. Rice Holmes");
    }

    [Fact]
    public void MultipleNamesInOneRespStmtAreJoined()
    {
        var header = TeiHeaderReader.Read(XDocument.Parse(PerseusP4));

        var supervision = header!.Responsibilities.First(r => r.StartsWith("Prepared"));

        Assert.Contains("Lisa Cerrato", supervision);
        Assert.Contains("William Merrill", supervision);
    }

    [Fact]
    public void WhitespaceInsideAnElementIsCollapsed()
    {
        var xml = """
            <TEI.2><teiHeader><fileDesc><titleStmt>
              <title>
                  A title
                  wrapped over
                  several lines
              </title>
            </titleStmt></fileDesc></teiHeader></TEI.2>
            """;

        var header = TeiHeaderReader.Read(XDocument.Parse(xml));

        Assert.Equal("A title wrapped over several lines", header!.Title);
    }

    /// <summary>
    /// A file with a header that states nothing is reported as having none,
    /// rather than as an object full of nulls the caller has to inspect.
    /// </summary>
    [Fact]
    public void AHeaderWithNothingInItReadsAsNull()
    {
        var xml = "<TEI.2><teiHeader><fileDesc/></teiHeader><text><body/></text></TEI.2>";

        Assert.Null(TeiHeaderReader.Read(XDocument.Parse(xml)));
    }

    [Fact]
    public void ADocumentWithNoHeaderReadsAsNull()
    {
        var xml = "<TEI.2><text><body><div>text with no header at all</div></body></text></TEI.2>";

        Assert.Null(TeiHeaderReader.Read(XDocument.Parse(xml)));
    }

    [Fact]
    public void MissingSourceFileReadsAsNullRatherThanThrowing()
    {
        Assert.Null(TeiHeaderReader.TryRead(Path.Combine(Path.GetTempPath(), "definitely-not-here.xml")));
        Assert.Null(TeiHeaderReader.TryRead(null));
        Assert.Null(TeiHeaderReader.TryRead("   "));
    }
}
