using ClassicaCodex.Core;
using ClassicaCodex.Ingestion.Oracc;
using Xunit;

namespace ClassicaCodex.Core.Tests;

/// <summary>
/// Pins how an Oracc download is read.
///
/// The fixtures below are cut down from a real rinap/rinap1 download rather
/// than invented, because the shapes that matter here are the ones Oracc
/// actually emits - a grapheme description that nests, a line label that is
/// not a line number, a lemma whose part of speech says "this is a god".
/// Inventing them would pin what I assumed the format was.
///
/// Written as fixtures rather than against a downloaded corpus on purpose:
/// a test that needs 3 MB of Assyrian royal inscriptions to be on disk is a
/// test that does not run.
/// </summary>
public class OraccProjectReaderTests
{
    /// <summary>
    /// One word: the logogram NUNUZ, lemmatised to perʾu "offspring", with its
    /// cuneiform sign. Taken from Q003414 line 1.
    /// </summary>
    private const string OneWordText = """
    {
      "type": "cdl",
      "textid": "Q003414",
      "cdl": [{
        "node": "c", "type": "text",
        "cdl": [
          { "node": "d", "type": "line-start", "ref": "Q003414.2", "n": "1", "label": "1" },
          {
            "node": "l", "frag": "NUNUZ", "ref": "Q003414.2.1",
            "f": {
              "lang": "akk", "form": "NUNUZ",
              "gdl": [{ "gg": "logo", "group": [{ "s": "NUNUZ", "utf8": "𒉭" }] }],
              "cf": "perʾu", "gw": "bud", "sense": "offspring",
              "norm": "perʾi", "pos": "N"
            }
          }
        ]
      }]
    }
    """;

    private static OraccText Read(string json) =>
        OraccProjectReader.ReadText(json, "fallback", new Dictionary<string, string>());

    [Fact]
    public void ReadsTheWordAndItsLemmatisation()
    {
        var word = Assert.Single(Assert.Single(Read(OneWordText).Lines).Words);

        Assert.Equal("NUNUZ", word.Form);
        Assert.Equal("perʾu", word.CitationForm);
        Assert.Equal("bud", word.GuideWord);
        Assert.Equal("offspring", word.Sense);
        Assert.Equal("perʾi", word.Normalized);
        Assert.Equal("akk", word.Language);
    }

    /// <summary>
    /// The cuneiform comes out of the grapheme description, and it is astral -
    /// one sign, one rune, two chars. This is the join between the importer
    /// and the rune fix: if LetterRuns regressed, this text would index as
    /// nothing at all.
    /// </summary>
    [Fact]
    public void ReadsCuneiformSigns()
    {
        var word = Read(OneWordText).Lines[0].Words[0];

        Assert.Equal(char.ConvertFromUtf32(0x1226D), word.Cuneiform);
        Assert.Equal(2, word.Cuneiform.Length);
        Assert.Equal(1, LetterRuns.CountLetters(word.Cuneiform));
    }

    /// <summary>
    /// A compound sign nests a group inside a group, so reading two levels and
    /// stopping loses the inner signs. Three signs in, three signs out.
    /// </summary>
    [Fact]
    public void AssemblesNestedGraphemeGroups()
    {
        const string nested = """
        {
          "textid": "T1",
          "cdl": [{ "node": "c", "cdl": [
            { "node": "d", "type": "line-start", "label": "1" },
            { "node": "l", "frag": "bal-til{ki}", "f": { "form": "bal-til{ki}", "gdl": [
              { "group": [ { "utf8": "𒁄" },
                           { "group": [ { "utf8": "𒄀" }, { "utf8": "𒀚" } ] } ] }
            ] } }
          ]}]
        }
        """;

        var word = Read(nested).Lines[0].Words[0];

        Assert.Equal(3, LetterRuns.CountLetters(word.Cuneiform));
    }

    /// <summary>
    /// The printed label wins over the counter. Assyriological line references
    /// are "2'" or "i 14", and a citation points at those - replacing them with
    /// an ordinal would make every reference we display wrong.
    /// </summary>
    [Fact]
    public void PrefersThePrintedLabelOverTheCounter()
    {
        const string primed = """
        { "textid": "T1", "cdl": [{ "node": "c", "cdl": [
          { "node": "d", "type": "line-start", "n": "7", "label": "2'", "ref": "T1.9" }
        ]}]}
        """;

        var line = Assert.Single(Read(primed).Lines);

        Assert.Equal("2'", line.Label);
        Assert.Equal("T1.9", line.Reference);
    }

    /// <summary>
    /// Oracc types divine names rather than leaving them to be inferred from
    /// capitalisation, which is what makes "find every text that names this
    /// god" a lookup instead of a guess.
    /// </summary>
    [Fact]
    public void TypesDivineNames()
    {
        const string god = """
        { "textid": "T1", "cdl": [{ "node": "c", "cdl": [
          { "node": "d", "type": "line-start", "label": "1" },
          { "node": "l", "frag": "{d}a-šur", "f": { "form": "{d}a-šur", "cf": "Aššur", "pos": "DN" } },
          { "node": "l", "frag": "LUGAL", "f": { "form": "LUGAL", "cf": "šarru", "pos": "N" } }
        ]}]}
        """;

        var words = Read(god).Lines[0].Words;

        Assert.True(words[0].IsDivineName);
        Assert.False(words[1].IsDivineName);
    }

    /// <summary>
    /// A word arriving before any line-start must not vanish. Dropping it
    /// would lose text with nothing on screen to say so.
    /// </summary>
    [Fact]
    public void KeepsAWordThatArrivesBeforeAnyLine()
    {
        const string orphan = """
        { "textid": "T1", "cdl": [{ "node": "c", "cdl": [
          { "node": "l", "frag": "LUGAL", "f": { "form": "LUGAL" } }
        ]}]}
        """;

        var line = Assert.Single(Read(orphan).Lines);

        Assert.Equal(string.Empty, line.Label);
        Assert.Single(line.Words);
    }

    /// <summary>
    /// Three of rinap/rinap1's 96 corpusjson files are zero bytes. An importer
    /// that does not expect that dies partway through a download the reader
    /// cannot repair - and one that swallows it imports 93 of 96 texts and
    /// reports success, which is worse.
    /// </summary>
    [Fact]
    public void RecordsTextsTheDownloadNamesButDoesNotContain()
    {
        var root = Path.Combine(Path.GetTempPath(), "oracc-test-" + Guid.NewGuid().ToString("N"));
        var corpus = Path.Combine(root, "corpusjson");
        Directory.CreateDirectory(corpus);

        try
        {
            File.WriteAllText(Path.Combine(root, "metadata.json"), """
            {
              "project": "rinap/rinap1",
              "license": "This data is released under the CC0 license",
              "license-url": "https://creativecommons.org/publicdomain/zero/1.0/",
              "config": { "name": "Tiglath-pileser III and Shalmaneser V", "abbrev": "RINAP 1" }
            }
            """);

            File.WriteAllText(Path.Combine(corpus, "Q000001.json"), OneWordText);
            File.WriteAllText(Path.Combine(corpus, "Q003424.json"), string.Empty);

            var project = OraccProjectReader.Read(root);

            Assert.Single(project.Texts);
            var skipped = Assert.Single(project.Skipped);
            Assert.Equal("Q003424", skipped.TextId);

            // The licence is read from the data, not from our own catalogue.
            // RINAP 1 is CC0 where the Oracc default is CC BY-SA, and only the
            // download knows which one this is.
            Assert.Contains("CC0", project.License);
            Assert.Equal("RINAP 1", project.Abbreviation);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Pointing the reader at the top of an extracted zip rather than at the
    /// project folder inside it is the obvious first mistake, so it has to say
    /// so rather than return an empty project that looks like a finished
    /// import of nothing.
    /// </summary>
    [Fact]
    public void SaysSoWhenPointedAtTheWrongFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "oracc-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Assert.Throws<FileNotFoundException>(() => OraccProjectReader.Read(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
