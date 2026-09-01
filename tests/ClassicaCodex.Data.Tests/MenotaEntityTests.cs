using ClassicaCodex.Core;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// What happens when a Menota manuscript is read before its entity table is.
///
/// These files are written almost entirely in character entities: 1,780,562
/// references across the 91 manuscripts, of which menota-entities.txt defines
/// 1,779,204. Every thorn, eth and accented vowel is one. Read without that
/// file, the text is not degraded - it is gone, replaced a million times over
/// by U+FFFD.
///
/// That would be survivable if it stayed in memory. It does not. The title
/// goes into the .plan.json and the ingest reads it back, so a plan built in
/// that state stays wrong after the file arrives. It happened on a real
/// library: the plans were written at 20:47, menota-entities.txt was saved at
/// 22:26, and 106 of 219 work titles carried a replacement character into the
/// library - "Af Katli <FFFD>rym capitulum", "H<FFFD>r hefir upp Egils
/// s<FFFD>gu" - while the reading text, parsed fresh every time, was perfect.
/// Nothing on screen connected the two.
/// </summary>
public class MenotaEntityTests : IDisposable
{
    private readonly string _folder;

    public MenotaEntityTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "cc-menota-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* temp dir */ }
        GC.SuppressFinalize(this);
    }

    private string WriteManuscript(string body)
    {
        var path = Path.Combine(_folder, "AM-132-fol.xml");
        File.WriteAllText(path,
            "<!DOCTYPE TEI [ <!ENTITY % menota SYSTEM \"http://www.menota.org/entities.txt\"> ]>" +
            "<TEI xmlns=\"http://www.tei-c.org/ns/1.0\"><text><body>" + body + "</body></text></TEI>");
        return path;
    }

    private void WriteEntityFile(params (string Name, string Value)[] entities)
    {
        File.WriteAllText(Path.Combine(_folder, "menota-entities.txt"),
            string.Join("\n", entities.Select(e => $"<!ENTITY {e.Name} \"{e.Value}\">")));
    }

    // ------------------------------------------------ the entity table itself

    /// <summary>
    /// With the table, the thorn is a thorn.
    /// </summary>
    [Fact]
    public void TheEntityTableResolvesTheText()
    {
        var path = WriteManuscript("<head>Af Katli &THORN;rym capitulum</head>");
        WriteEntityFile(("THORN", "Þ"));

        var load = MenotaXmlLoader.Load(path, MenotaXmlLoader.LoadEntities(_folder));

        Assert.True(load.Ok);
        Assert.Contains("Af Katli Þrym capitulum", load.Document!.ToString());
        Assert.Equal(0, load.UnresolvedEntities);
    }

    /// <summary>
    /// Without it, the character is replaced and counted - not dropped, so the
    /// gap stays visible rather than closing up inside a word.
    /// </summary>
    [Fact]
    public void WithoutTheTableTheCharacterIsLostAndCounted()
    {
        var path = WriteManuscript("<head>Af Katli &qwertyPM; capitulum</head>");

        var load = MenotaXmlLoader.Load(path, new Dictionary<string, string>());

        Assert.True(load.Ok);
        Assert.Contains("�", load.Document!.ToString());
        Assert.Equal(1, load.UnresolvedEntities);
    }

    /// <summary>
    /// The standard named entities are a backstop for one the MUFI table
    /// happens not to define - measured over the whole corpus that is a single
    /// &amp;sbquo; in AM 242 fol, which is exactly the size of claim worth
    /// making for it.
    /// </summary>
    [Fact]
    public void AStandardEntityTheTableOmitsIsStillResolved()
    {
        var path = WriteManuscript("<head>hann sag&sbquo;i</head>");
        WriteEntityFile(("THORN", "Þ"));

        var load = MenotaXmlLoader.Load(path, MenotaXmlLoader.LoadEntities(_folder));

        Assert.Equal(0, load.UnresolvedEntities);
        Assert.Contains("‚", load.Document!.ToString());
    }

    /// <summary>
    /// The five XML built-ins are left for the parser. Resolving &amp;amp;
    /// before it is read produces a document that will not parse.
    /// </summary>
    [Fact]
    public void TheXmlBuiltinsAreLeftAlone()
    {
        var path = WriteManuscript("<head>Ketill &amp; Atli</head>");

        var load = MenotaXmlLoader.Load(path, new Dictionary<string, string>());

        Assert.True(load.Ok);
        Assert.Equal(0, load.UnresolvedEntities);
        Assert.Contains("Ketill & Atli", load.Document!.Descendants().First(e => e.Name.LocalName == "head").Value);
    }

    [Theory]
    [InlineData("thorn", "þ")]
    [InlineData("THORN", "Þ")]
    [InlineData("eth", "ð")]
    [InlineData("aacute", "á")]
    [InlineData("oacute", "ó")]
    public void TheSharedTableKnowsTheNorseLetters(string name, string expected)
    {
        Assert.True(XmlEntitySanitizer.TryResolve(name, out var actual));
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// And refuses the XML built-ins, so a caller resolving its own entities
    /// cannot be handed one that breaks its document.
    /// </summary>
    [Theory]
    [InlineData("amp")]
    [InlineData("lt")]
    [InlineData("gt")]
    [InlineData("quot")]
    [InlineData("apos")]
    public void TheSharedTableRefusesTheXmlBuiltins(string name) =>
        Assert.False(XmlEntitySanitizer.TryResolve(name, out _));

    // ------------------------------------------------- the plan that outlives it

    /// <summary>
    /// A plan carrying a replacement character in a title is refused by the
    /// ingest rather than used, because U+FFFD is not something a manuscript
    /// is written in - it is what this application substitutes for an entity
    /// it could not resolve, so it dates the plan rather than describing the
    /// text.
    ///
    /// Refused rather than silently rebuilt: the plan also holds merges,
    /// splits and renames somebody decided by hand.
    /// </summary>
    [Fact]
    public async Task AnIngestRefusesAPlanWhoseTitlesLostTheirEntities()
    {
        var path = WriteManuscript(
            "<div type=\"chapter\"><head>Af Katli &THORN;rym capitulum</head><p>Ketill</p></div>");
        WriteEntityFile(("THORN", "Þ"));

        var plan = new MenotaIngestPlan
        {
            ManuscriptId = "AM 132 fol",
            Confirmed = true,
            Works = { new MenotaWorkPlan { Title = "Af Katli �rym capitulum", DivPaths = { "0" } } }
        };
        plan.Save(MenotaIngestPlan.PlanPathFor(path));

        var service = new MenotaIngestService();
        await service.IngestAsync(_folder);

        Assert.Contains(service.FailedFiles,
            f => f.Error.Contains("menota-entities.txt", StringComparison.OrdinalIgnoreCase));
    }
}
