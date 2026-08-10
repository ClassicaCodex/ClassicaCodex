using ClassicaCodex.Core.Models;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the parts of a play that are not spoken lines.
///
/// A cast list, a stage direction and an editor's dramatis personae are each
/// neither a division nor a leaf, so each fell through TeiParser's walk into
/// the branch that descends looking for leaves, found none, and emitted
/// nothing at all. Nothing failed; the text was simply not there. King Lear's
/// dramatis personae showed two group headings and not one character, and
/// Hecuba's staging - every entrance and exit in the play - was absent.
///
/// These run on fragments rather than the real files so they say which rule
/// broke rather than that a number moved.
/// </summary>
public class DramaParsingTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""act"" n=""1"">{body}</div1></body></text></TEI.2>";

    [Fact]
    public void CastItemsAreRead()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <castList>
                <castItem type=""role""><role>LEAR</role><roleDesc>king of Britain</roleDesc></castItem>
                <castItem type=""role""><role>KING OF FRANCE</role></castItem>
            </castList>"));

        Assert.Equal(2, nodes.Count);
        Assert.Equal("LEAR king of Britain", nodes[0].Text);
        Assert.Equal("KING OF FRANCE", nodes[1].Text);
    }

    /// <summary>
    /// The group headings were the only part that used to survive, because
    /// they are &lt;head&gt; elements. They must keep working alongside the
    /// entries they label, and in the right order.
    /// </summary>
    [Fact]
    public void CastGroupHeadingsStayWithTheirEntries()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <castList>
                <castGroup>
                    <head rend=""braced"">Daughters to Lear</head>
                    <castItem type=""role""><role>GONERIL</role></castItem>
                    <castItem type=""role""><role>REGAN</role></castItem>
                </castGroup>
            </castList>"));

        Assert.Collection(nodes,
            n => Assert.Equal("Daughters to Lear", n.Text),
            n => Assert.Equal("GONERIL", n.Text),
            n => Assert.Equal("REGAN", n.Text));
    }

    [Fact]
    public void StageDirectionsBesideSpeechesAreRead()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <stage rend=""italic"">Before Agamemnon's tent.</stage>
            <sp><speaker>Ghost</speaker><l n=""1"">I have come from out the charnel-house</l></sp>
            <stage>The Ghost vanishes.</stage>"));

        // Four, not three: the speech carries its speaker. This test asserted
        // three while <speaker> was being dropped, which is exactly the shape
        // of loss it exists to catch - so it now names each part rather than
        // counting them, and a part going missing says which one.
        Assert.Collection(nodes,
            n => Assert.Equal("Before Agamemnon's tent.", n.Text),
            n => Assert.Equal("Ghost", n.Text),
            n => Assert.Equal("I have come from out the charnel-house", n.Text),
            n => Assert.Equal("The Ghost vanishes.", n.Text));

        Assert.Equal(
            new[] { TextNodeKinds.Stage, TextNodeKinds.Speaker, TextNodeKinds.Line, TextNodeKinds.Stage },
            nodes.Select(n => n.NodeKind));
    }

    /// <summary>
    /// Cast entries and stage directions are cited by name, not by number.
    ///
    /// Taking numbers from the leaf counter would renumber every line after
    /// them, and annotations resolve through (EditionId, CitationRef) - so a
    /// bookmark saved before this change would silently come back pointing at
    /// a different line.
    /// </summary>
    [Fact]
    public void NonSpokenPartsDoNotConsumeLineNumbers()
    {
        var nodes = new TeiParser().ParseXml(Wrap(@"
            <castList><castItem><role>LEAR</role></castItem></castList>
            <stage>Enter Lear.</stage>
            <l>Attend the lords of France and Burgundy.</l>
            <l>I shall, my liege.</l>"));

        var line = Assert.Single(nodes, n => n.Text.StartsWith("Attend"));
        Assert.EndsWith(".1", line.CitationRef);

        Assert.Contains(nodes, n => n.CitationRef.EndsWith(".cast1"));
        Assert.Contains(nodes, n => n.CitationRef.EndsWith(".stage1"));
    }

    /// <summary>
    /// Perseus does not use &lt;castList&gt; for its translations. Hecuba's
    /// cast is Coleridge's, wrapped in a note - editorial, and named as his.
    /// It goes to the apparatus rather than the text, because that is what it
    /// is.
    /// </summary>
    [Fact]
    public void ABlockLevelNoteBecomesApparatusRatherThanText()
    {
        var parser = new TeiParser();

        var nodes = parser.ParseXml(Wrap(@"
            <note resp=""Coleridge"" place=""inline"">
                <p rend=""center"">Dramatis Personae</p>
                <p>Ghost of Polydorus</p>
                <p>Hecuba</p>
            </note>
            <sp><speaker>Ghost</speaker><l>I have come from out the charnel-house</l></sp>"));

        // The note stays out of the text; the speech keeps its speaker. Both
        // halves matter together: this is the test that pins the boundary
        // between "editorial, goes to the apparatus" and "on the page, goes
        // to a node".
        Assert.Equal(new[] { "Ghost", "I have come from out the charnel-house" },
            nodes.Select(n => n.Text));
        Assert.DoesNotContain(nodes, n => n.Text.Contains("Dramatis Personae"));

        var entry = Assert.Single(parser.LastApparatus);
        Assert.Contains("Ghost of Polydorus", entry.Content);
        Assert.Contains("Hecuba", entry.Content);
        Assert.Equal("Coleridge", entry.Witness);
    }

    /// <summary>
    /// The narrowness of that rule is the whole point of it.
    ///
    /// Notes inside a line are apparatus of a different kind - Perseus carries
    /// its variant readings that way, and reading them as text put 17,000
    /// characters of editors' surnames and manuscript sigla into Agamemnon's
    /// word counts. Routing block-level notes to the apparatus must not
    /// disturb that.
    /// </summary>
    [Fact]
    public void NotesInsideALineStayOutOfTheText()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<l>μῆνιν ἄειδε<note resp=""Smyth"">seclusit Pauw</note> θεά</l>"));

        var line = Assert.Single(nodes);
        Assert.DoesNotContain("seclusit", line.Text);
        Assert.DoesNotContain("Pauw", line.Text);
    }
}
