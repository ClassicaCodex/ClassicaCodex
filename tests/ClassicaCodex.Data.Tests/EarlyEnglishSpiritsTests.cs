using ClassicaCodex.Ingestion;
using ClassicaCodex.Ingestion.EarlyEnglish;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Preparing the Text Creation Partnership's EEBO transcriptions for the
/// parser that reads Perseus.
///
/// The markup is TCP's own, from Daemonologie (A04243) and Saducismus
/// Triumphatus (A42824), cut down to the elements the rule is about.
/// </summary>
public class EarlyEnglishSpiritsTests
{
    private const string Tei = "http://www.tei-c.org/ns/1.0";

    private static string Document(string text) =>
        $"<TEI xmlns=\"{Tei}\"><teiHeader><fileDesc><titleStmt><title>t</title></titleStmt></fileDesc></teiHeader>{text}</TEI>";

    private static List<TeiParser.ParsedNode> Parse(string text) =>
        new TeiParser().ParseXml(EarlyEnglishSpiritsIngestService.Prepare(Document(text)));

    /// <summary>
    /// A word the printer broke at the end of a line is one word again, with
    /// the line break that followed the marker taken out too.
    /// </summary>
    [Fact]
    public void AWordBrokenAtTheEndOfAPrintedLineIsJoined()
    {
        var nodes = Parse(
            "<text><body><div type=\"book\" n=\"1\"><p>that <hi>disobedi<g ref=\"char:EOLhyphen\"/>\n"
            + "                ence is as the sinne of Witch-craft?</hi> Fourth<g ref=\"char:EOLunhyphen\"/>lie</p></div></body></text>");

        var text = Assert.Single(nodes, n => n.Text.Length > 0).Text;
        Assert.Contains("disobedience is as the sinne", text);
        Assert.Contains("Fourthlie", text);
    }

    /// <summary>
    /// Several texts bound in one volume are all read, each as a numbered part,
    /// rather than the first one alone - which is how Saducismus Triumphatus
    /// came out as its prefatory letter.
    /// </summary>
    [Fact]
    public void EveryTextInAVolumeIsRead()
    {
        var nodes = Parse(
            "<text><group>"
            + "<text><body><div type=\"letter\" n=\"1\"><p>SIR, When I was last at London</p></div></body></text>"
            + "<text><front><div type=\"to_the_reader\"><p>To the Reader.</p></div></front>"
            + "<body><div type=\"section\" n=\"1\"><p>The Daemon of Tedworth</p></div></body></text>"
            + "</group></text>");

        var texts = nodes.Select(n => n.Text).ToList();
        Assert.Contains(texts, t => t.Contains("When I was last at London"));
        Assert.Contains(texts, t => t.Contains("The Daemon of Tedworth"));
        Assert.Contains(texts, t => t.Contains("To the Reader."));
        Assert.Equal(nodes.Count, nodes.Select(n => n.CitationRef).Distinct().Count());
    }

    /// <summary>A volume of one text is passed through as it came.</summary>
    [Fact]
    public void ASingleTextIsLeftAsItIs()
    {
        var raw = Document("<text><body><div type=\"book\" n=\"1\"><p>OF Mans First Disobedience</p></div></body></text>");

        Assert.Equal(raw, EarlyEnglishSpiritsIngestService.Prepare(raw));
    }

    /// <summary>
    /// The two Marlowe plays join the Christopher Marlowe the Renaissance
    /// collection already files the Faust Book under, rather than a namesake.
    /// </summary>
    [Fact]
    public void FaustusJoinsTheMarloweAlreadyInTheLibrary() =>
        Assert.All(
            EarlyEnglishSpiritsIngestService.Books.Where(b => b.Title.StartsWith("Doctor Faustus")),
            b => Assert.Equal("engLit:renaissance:christopher_marlowe", b.AuthorUrn));
}
