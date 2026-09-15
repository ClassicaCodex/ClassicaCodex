using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// That the Latin parse in the source files actually reaches the library.
///
/// It did not, for the life of the project. Every token in the Latin lemma
/// data carries two attributes - a coarse category in @pos and the full
/// analysis in @msd, "Case=Abl|Numb=Sing|Gend=Fem" - and the ingester read
/// whichever candidate attribute name it found first, which was always @pos.
/// So the whole parse was read out of the file and dropped on the way past:
/// every Latin word in the library became a bare "verb" or "common noun", a
/// Latin morphology search could never match a case or a tense, and the
/// promise that an ambiguous form shows all its candidates was true of Greek
/// only.
///
/// Decoded separately in MorphologyDecoderTests. What this covers is the
/// step between the file and the database, because that is where it was lost
/// - and a decoder that can read a tag nothing ever stores would be no use.
/// </summary>
[Collection("Database")]
public class LatinLemmaFeatureIngestTests
{
    /// <summary>
    /// The shape the real files have, reduced to three tokens. nostra is the
    /// textbook ambiguity and carries both its readings here exactly as the
    /// corpus records them.
    /// </summary>
    private const string Sample = """
        <TEI xmlns="http://www.tei-c.org/ns/1.0">
          <text><body>
            <w rend="verse" n="1.1" pos="NOMcom" msd="Case=Gen|Numb=Sing" lemma="generatio">generationis</w>
            <w rend="verse" n="1.2" pos="VER" msd="Mood=Ind|Tense=Pres|Voice=Act|Pers=3|Numb=Sing" lemma="dico2">dicit</w>
            <w rend="verse" n="1.3" pos="ADJqua" msd="Case=Abl|Numb=Sing|Gend=Fem" lemma="noster">nostra</w>
            <w rend="verse" n="1.4" pos="ADJqua" msd="Case=Nom|Numb=Plur|Gend=Neut" lemma="noster">nostra</w>
          </body></text>
        </TEI>
        """;

    private static async Task<string> IngestSampleAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ccx-latin-lemma-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "sample.xml"), Sample);

        await new LemmaIngestService().IngestAsync(folder, "lat");
        return folder;
    }

    [Fact]
    public async Task TheFeatureStringReachesTheLibrary()
    {
        using var db = await TempDatabase.CreateAsync();
        var folder = await IngestSampleAsync();

        try
        {
            var rows = await new LemmaRepository().GetHeadwordsForFormAsync("generationis", "lat");

            var row = Assert.Single(rows);
            Assert.Equal("generatio", row.Headword);

            // The category alone is what used to be stored, and is not enough.
            Assert.NotEqual("NOMcom", row.PartOfSpeech);
            Assert.Contains("Case=Gen", row.PartOfSpeech);

            var parse = MorphologyDecoder.Decode(row.PartOfSpeech);
            Assert.True(parse.IsDecoded);
            Assert.Equal("common noun", parse.PartOfSpeech);
            Assert.Contains("genitive", parse.Description);
            Assert.Contains("singular", parse.Description);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The claim the README makes, tested on the word a Latinist would try
    /// first: nostra is nominative/ablative feminine singular and also
    /// nominative/accusative neuter plural, and the app must say so rather
    /// than listing the same answer twice.
    /// </summary>
    [Fact]
    public async Task NostrasTwoReadingsArriveAsTwoDifferentParses()
    {
        using var db = await TempDatabase.CreateAsync();
        var folder = await IngestSampleAsync();

        try
        {
            var rows = await new LemmaRepository().GetHeadwordsForFormAsync("nostra", "lat");

            var described = rows
                .Select(r => MorphologyDecoder.Decode(r.PartOfSpeech).Description)
                .Distinct()
                .ToList();

            Assert.Equal(2, described.Count);
            Assert.Contains(described, d => d.Contains("ablative") && d.Contains("feminine"));
            Assert.Contains(described, d => d.Contains("neuter") && d.Contains("plural"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A finite verb keeps its tense, voice, mood, person and number - none
    /// of which survived before, and all of which a learner is looking for.
    /// </summary>
    [Fact]
    public async Task AVerbKeepsItsTenseVoiceAndMood()
    {
        using var db = await TempDatabase.CreateAsync();
        var folder = await IngestSampleAsync();

        try
        {
            var rows = await new LemmaRepository().GetHeadwordsForFormAsync("dicit", "lat");
            var parse = MorphologyDecoder.Decode(Assert.Single(rows).PartOfSpeech);

            Assert.Equal("verb: present active indicative 3rd person singular", parse.Description);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
