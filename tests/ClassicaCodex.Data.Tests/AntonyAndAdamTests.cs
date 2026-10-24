using System.Text;
using ClassicaCodex.Ingestion.Antony;
using ClassicaCodex.Ingestion.Dionysius;
using ClassicaCodex.Ingestion.Pseudepigrapha;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the Life of Antony out of the CCEL's ThML of the Nicene and
/// Post-Nicene Fathers, and the Latin Life of Adam and Eve out of the Online
/// Critical Pseudepigrapha. The samples keep each file's shape: the editor's
/// table of contents and a footnote in the one, a chapter divided as verse 0
/// and a lettered addition in the other.
/// </summary>
public class AntonyAndAdamTests
{
    private static string Life(string sections) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE ThML PUBLIC "-//CCEL/DTD Theological Markup Language//EN" "http://www.ccel.org/dtd/ThML10.dtd">
        <ThML><ThML.body>
        <div1 title="Life of Antony. (Vita Antoni.)">
        <div2 title="Introduction."><p>Antony was born about 251.</p></div2>
        <div2 title="Life of Antony. (Vita Antoni.)">
          <div3 title="Prologue.">
            <p>Life of Antony.</p>
            <p>§§1, 2. Birth and beginnings of
            Antony.</p>
            <p>§§8–10. His life in the tombs, and combats with demons there.</p>
          </div3>
          <div3 title="Preface.">
            <p>Life of Antony.</p>
            <p>Athanasius the bishop to the brethren in foreign parts.</p>
            <p>You have entered upon a noble rivalry with the monks of Egypt.</p>
          </div3>
          <div3 title="Sections 1-94">{sections}</div3>
        </div2>
        </div1>
        </ThML.body></ThML>
        """;

    private static string Sections(Func<int, string> text) =>
        string.Concat(Enumerable.Range(1, 94).Select(n => $"<p>{n}. {text(n)}</p>"));

    private static List<(string Citation, string Text, bool IsHead)> Parse(string thml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(thml));
        return AntonyLoader.Load(DionysiusLoader.Read(stream));
    }

    [Fact]
    public void TheLifeIsCitedByItsSectionsWithTheContentsAsHeadings()
    {
        var passages = Parse(Life(Sections(n => n == 9
            ? "And the place was on a sudden filled with the forms of lions, bears, leopards<note n=\"12\">A footnote.</note>.</p><p>But Antony was not alarmed."
            : $"Section {n}.")));

        Assert.Equal(new[] { "pr", "pr.2", "1.head", "1", "2" }, passages.Take(5).Select(p => p.Citation));
        Assert.Equal("Birth and beginnings of Antony.", passages[2].Text);
        Assert.Equal("His life in the tombs, and combats with demons there.", passages.Single(p => p.Citation == "8.head").Text);
        Assert.Equal("And the place was on a sudden filled with the forms of lions, bears, leopards.",
            passages.Single(p => p.Citation == "9").Text);
        Assert.Equal("But Antony was not alarmed.", passages.Single(p => p.Citation == "9.2").Text);
        Assert.Equal("Athanasius the bishop to the brethren in foreign parts.", passages[0].Text);
    }

    [Fact]
    public void ASectionOutOfOrderIsRefused() =>
        Assert.Throws<InvalidDataException>(() => Parse(Life(Sections(n => "x").Replace("<p>5. x</p>", "<p>6. x</p>"))));

    private const string Ocp = """
        <book filename="AdamEve" title="Life of Adam and Eve">
          <version title="Latin (Meyer)" author="" language="Latin">
            <manuscripts><ms abbrev="Meyer" language="Latin" show="yes"><name>Meyer Concordance</name>
              <bibliography>W. Meyer, "Vita Adae et Evae," Abhandlungen 14.3 (1878):185-220.</bibliography></ms></manuscripts>
            <text>
              <div number="14"><div number="0"><unit id="14" group="0" parallel="">
                <reading option="0" mss="Meyer ">Et egressus Michahel vocavit omnes angelos dicens: adorate imaginem domini dei.</reading>
              </unit></div></div>
              <div number="29"><div number="a"><unit id="29a" group="0" parallel="">
                <reading option="0" mss="Meyer ">Audi, fili mi Seth.</reading>
              </unit></div></div>
            </text>
          </version>
          <version title="Latin (Meyer) (English)" author="OCP" language="English">
            <text><div number="14"><div number="0"><unit id="14"><reading option="0" mss="OCP">Michael went out.</reading></unit></div></div></text>
          </version>
        </book>
        """;

    [Fact]
    public void TheLatinLifeIsCitedByMeyersChapters()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, Ocp);

        var (latin, bibliography) = OcpLoader.Load(path, "Latin (Meyer)", "Meyer");
        var cited = latin.Select(p => PseudepigraphaIngestService.ChapterOnly(p.Citation)).ToList();

        Assert.Equal(new[] { "14", "29.a" }, cited);
        Assert.StartsWith("Et egressus Michahel", latin[0].Text);
        Assert.StartsWith("W. Meyer", bibliography);
        Assert.DoesNotContain(latin, p => p.Text.Contains("Michael went out"));
    }
}
