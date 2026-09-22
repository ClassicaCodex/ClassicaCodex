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
        OraccProjectReader.ReadText(json, "fallback", catalogue: null);

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
    /// The two editions a text becomes must have the same number of rows.
    ///
    /// The reader synchronises linked panes by row index, so a transliteration
    /// pane and a sign pane that disagree on row count do not scroll together,
    /// they drift - and the drift grows down the page rather than announcing
    /// itself at the top.
    ///
    /// The case that breaks a naive importer is real: Q003622 is a monument
    /// inscribed in Akkadian and then in Aramaic, and the Aramaic lines are
    /// alphabetic, so Oracc records no cuneiform for them. Skipping the rows
    /// with no signs would have left those panes two lines apart.
    /// </summary>
    [Fact]
    public void BothEditionsOfABilingualKeepTheSameRows()
    {
        const string bilingual = """
        { "textid": "Q003622", "cdl": [{ "node": "c", "cdl": [
          { "node": "d", "type": "line-start", "n": "1", "label": "Akkadian 1" },
          { "node": "l", "frag": "E2.GAL", "f": { "form": "E2.GAL", "gdl": [
              { "group": [ { "utf8": "𒂍" } ] } ] } },
          { "node": "d", "type": "line-start", "n": "1", "label": "Aramaic 1" },
          { "node": "l", "frag": "zy", "f": { "form": "zy" } },
          { "node": "d", "type": "line-start", "n": "2", "label": "Aramaic 2" },
          { "node": "l", "frag": "br", "f": { "form": "br" } }
        ]}]}
        """;

        var (transliteration, cuneiform) = OraccIngestService.BuildNodes(Read(bilingual));

        Assert.Equal(3, transliteration.Count);
        Assert.Equal(transliteration.Count, cuneiform.Count);

        // Same citation on the same row in both, which is what makes a click
        // in one pane mean anything in the other.
        Assert.Equal(
            transliteration.Select(n => n.CitationRef),
            cuneiform.Select(n => n.CitationRef));

        // The Aramaic rows are present and blank rather than absent.
        Assert.NotEqual(string.Empty, cuneiform[0].Text);
        Assert.Equal(string.Empty, cuneiform[1].Text);
        Assert.Equal(string.Empty, cuneiform[2].Text);
    }

    /// <summary>
    /// A bilingual numbers each language from 1, so the plain counter is not
    /// unique within the text and the printed label is the only thing that
    /// tells two different lines apart.
    /// </summary>
    [Fact]
    public void CitationsAreUniqueInABilingual()
    {
        const string bilingual = """
        { "textid": "Q003622", "cdl": [{ "node": "c", "cdl": [
          { "node": "d", "type": "line-start", "n": "1", "label": "Akkadian 1" },
          { "node": "l", "frag": "E2.GAL", "f": { "form": "E2.GAL" } },
          { "node": "d", "type": "line-start", "n": "1", "label": "Aramaic 1" },
          { "node": "l", "frag": "zy", "f": { "form": "zy" } }
        ]}]}
        """;

        var refs = OraccIngestService.BuildNodes(Read(bilingual)).Transliteration
            .Select(n => n.CitationRef).ToList();

        Assert.Equal(new[] { "Akkadian 1", "Aramaic 1" }, refs);
        Assert.Equal(refs.Count, refs.Distinct().Count());
    }

    /// <summary>
    /// The depth of the project inside its archive is not constant, and
    /// guessing it wrong fails in the worst possible way.
    ///
    /// Measured against the real downloads: blms.zip extracts to blms/, one
    /// level, because blms is a top-level project. rinap-rinap1.zip extracts
    /// to rinap/rinap1/, two levels, because rinap1 is a subproject of rinap.
    /// Hardcoding either depth works for about half of Oracc's 208 projects -
    /// and the half it fails on does not throw, it finds no texts, so the
    /// setup step would report success over an empty import.
    /// </summary>
    [Theory]
    [InlineData("blms")]              // a top-level project, one level deep
    [InlineData("rinap/rinap1")]      // a subproject, one level deeper again
    [InlineData("a/b/c")]             // deeper than Oracc actually goes
    public void FindsTheProjectAtWhateverDepthTheArchiveNestsIt(string nesting)
    {
        var root = Path.Combine(Path.GetTempPath(), "oracc-depth-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(new[] { root }.Concat(nesting.Split('/')).ToArray());
        Directory.CreateDirectory(project);

        try
        {
            File.WriteAllText(Path.Combine(project, "metadata.json"), "{}");

            Assert.Equal(project, OraccDownloadService.FindProjectDirectory(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// No metadata.json anywhere means the caller must be told, not handed a
    /// folder that will import nothing.
    /// </summary>
    [Fact]
    public void FindsNothingWhenTheArchiveHasNoProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "oracc-nodepth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "junk", "deeper"));

        try
        {
            Assert.Null(OraccDownloadService.FindProjectDirectory(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The starter set is what a reader gets, and its size is stated on the
    /// setup step before they commit to the download. A project added without
    /// a size, or a size that drifts to zero, would make that promise false.
    /// </summary>
    [Fact]
    public void EveryStarterProjectIsNamedAndSized()
    {
        var starters = OraccDownloadService.StarterProjects;

        Assert.NotEmpty(starters);
        Assert.All(starters, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Id));
            Assert.False(string.IsNullOrWhiteSpace(p.Title));
            Assert.True(p.ApproxMegabytes > 0, $"{p.Id} has no size");
        });

        // Ids are the download filename, so a duplicate would silently
        // overwrite another project's archive.
        Assert.Equal(starters.Count, starters.Select(p => p.Id).Distinct().Count());

        Assert.True(OraccDownloadService.ApproxTotalMegabytes > 0);
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
