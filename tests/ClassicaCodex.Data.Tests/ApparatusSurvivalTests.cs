using ClassicaCodex.Core.Models;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Apparatus that has no line to hang on, and text that only looks deleted.
///
/// Both come from the same place: FlattenText excludes editorial matter from
/// the reading text, correctly, and two things downstream forgot that it does.
/// The apparatus extractor ran after the early return that skips a textless
/// element, so an element consisting only of its note lost the note. The
/// athetized test looked for a &lt;del&gt; anywhere in the subtree, including
/// inside the notes FlattenText had just excluded.
/// </summary>
public class ApparatusSurvivalTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""book"" n=""1"">{body}</div1></body></text></TEI.2>";

    // ------------------------------------------- apparatus with no text

    /// <summary>
    /// 106 elements across 16 editions flatten to nothing while carrying a
    /// note, and they are not throwaway - Polybius records an entire lost book
    /// this way, "Nihil huius libri superest", in a paragraph with no other
    /// text. Sophocles' Ichneutae has 8, the German Thucydides 24.
    /// </summary>
    [Fact]
    public void NoteOnATextlessLineReachesTheNextLine()
    {
        var parser = new TeiParser();
        var nodes = parser.ParseXml(Wrap(
            @"<l n=""10""><note>A leaf is missing here.</note></l>
              <l n=""11"">the text resumes</l>"));

        Assert.Equal("1.11", Assert.Single(nodes).CitationRef);

        var entry = Assert.Single(parser.LastApparatus);
        Assert.Equal("A leaf is missing here.", entry.Content);

        // Carried to a reference that exists, not to one invented for the
        // empty line: a citation with no node behind it resolves to nothing.
        Assert.Equal("1.11", entry.CitationRef);
    }

    [Fact]
    public void NoteOnATextlessHeadingReachesTheNextNode()
    {
        var parser = new TeiParser();
        parser.ParseXml(Wrap(
            @"<head><note resp=""Widmann"">Vgl. die Einleitung.</note></head>
              <p>the chapter begins</p>"));

        var entry = Assert.Single(parser.LastApparatus);
        Assert.Equal("1.1", entry.CitationRef);
        Assert.Equal("Widmann", entry.Witness);
    }

    /// <summary>
    /// Nothing follows, so the note attaches backwards rather than being
    /// dropped for want of a successor.
    /// </summary>
    [Fact]
    public void TrailingNoteAttachesToTheLastNode()
    {
        var parser = new TeiParser();
        parser.ParseXml(Wrap(
            @"<l n=""1"">the last surviving line</l>
              <l n=""2""><note>Nihil huius libri superest.</note></l>"));

        var entry = Assert.Single(parser.LastApparatus);
        Assert.Equal("1.1", entry.CitationRef);
        Assert.Contains("Nihil", entry.Content);
    }

    /// <summary>
    /// Several textless elements in a row all wait for the same node, and keep
    /// document order once they get there. SortOrder is what the Editor's
    /// Notes pane reads, and two extractions landing on one reference would
    /// otherwise both claim entry zero.
    /// </summary>
    [Fact]
    public void SeveralCarriedNotesKeepTheirOrderAndNumbering()
    {
        var parser = new TeiParser();
        parser.ParseXml(Wrap(
            @"<l n=""1""><note>first gap</note></l>
              <l n=""2""><note>second gap</note></l>
              <l n=""3"">resumes<note>on the line itself</note></l>"));

        Assert.Equal(new[] { "first gap", "second gap", "on the line itself" },
            parser.LastApparatus.Select(a => a.Content));
        Assert.Equal(new[] { 0, 1, 2 }, parser.LastApparatus.Select(a => a.SortOrder));
        Assert.All(parser.LastApparatus, a => Assert.Equal("1.3", a.CitationRef));
    }

    /// <summary>
    /// Nothing readable in the whole file, so there is no reference to attach
    /// to. Dropping is the honest outcome; minting a citation for a node that
    /// does not exist would leave a bookmark pointing at nothing.
    /// </summary>
    [Fact]
    public void ApparatusWithNoTextAnywhereIsDroppedRatherThanDangling()
    {
        var parser = new TeiParser();
        var nodes = parser.ParseXml(Wrap(@"<l n=""1""><note>A leaf is missing here.</note></l>"));

        Assert.Empty(nodes);
        Assert.Empty(parser.LastApparatus);
    }

    /// <summary>
    /// Pending apparatus is per-parse state. A second file must not inherit
    /// the first one's unattached notes and stitch them onto its opening line.
    /// </summary>
    [Fact]
    public void CarriedApparatusDoesNotLeakBetweenEditions()
    {
        var parser = new TeiParser();
        parser.ParseXml(Wrap(@"<l n=""1""><note>orphan</note></l>"));
        parser.ParseXml(Wrap(@"<l n=""1"">a different edition</l>"));

        Assert.Empty(parser.LastApparatus);
    }

    // ------------------------------------------------ athetized reading

    /// <summary>
    /// A Euripides line was marked athetized because a note beside it quoted
    /// the play's title, "Χορός Αἰχμαλωτίδων Γυναικών". The deletion has to be
    /// in the text FlattenText actually shows. 16 nodes across 10 editions.
    /// </summary>
    [Fact]
    public void DeletionInsideANoteDoesNotAthetizeTheLine()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<l n=""1"">accepted text <note>the editor discusses <del>foo</del></note></l>"));

        var line = Assert.Single(nodes);
        Assert.Equal("accepted text", line.Text);
        Assert.False(line.IsAthetized);
    }

    [Fact]
    public void DeletionInsideARejectedReadingDoesNotAthetizeTheLine()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<l n=""1"">accepted text<app><rdg wit=""#A"">variant <del>bar</del></rdg></app></l>"));

        Assert.False(Assert.Single(nodes).IsAthetized);
    }

    /// <summary>
    /// The other half. A deletion in the reading text still flags the line -
    /// 6,259 nodes across the corpora depend on this staying true.
    /// </summary>
    [Fact]
    public void DeletionInTheReadingTextStillAthetizesTheLine()
    {
        var nodes = new TeiParser().ParseXml(Wrap(
            @"<l n=""1"">accepted <del>athetized</del> text</l>"));

        Assert.True(Assert.Single(nodes).IsAthetized);
    }
}
