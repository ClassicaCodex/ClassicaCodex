using System.Text;
using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.ReM;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// ReM's own annotation, read as lemma data.
///
/// This is the first collection in the library that brings its own. Greek,
/// Latin and English each need a separate lemma download from another project -
/// the Latin one is two gigabytes - and ReM annotated the same 406 texts the
/// reader is reading, so the mappings cover them completely rather than
/// approximately.
///
/// The figures quoted throughout were measured against the real v2.1 Tabular
/// JSON distribution: 2,293,069 annotated tokens across 406 files, producing
/// 334,427 deduplicated mappings in about a minute.
/// </summary>
public class ReMLemmaLoaderTests
{
    private static Stream Json(string tokens) =>
        new MemoryStream(Encoding.UTF8.GetBytes($"{{\"token\":[{tokens}]}}"));

    private static List<ReMAnnotation> Read(string tokens) =>
        ReMLemmaLoader.Read(Json(tokens)).ToList();

    /// <summary>
    /// The trap the TEI sets, and why this loader exists at all.
    ///
    /// ReM's TEI export has a @lemma attribute that holds the NORMALISED FORM -
    /// "stêt" for the word the manuscript spells "ſtet". The Tabular JSON's
    /// lemma field is the actual headword, "stân". Taking the TEI's would have
    /// filled the Lemmas table with inflected words and made Word Study group
    /// forms under normalised spellings, looking entirely convincing.
    /// </summary>
    [Fact]
    public void TheHeadwordIsTheHeadwordAndNotTheNormalisedForm()
    {
        var token = Read(
            @"{""form"":""ſtet"",""norm"":""stêt"",""lemma"":""stân"",
               ""pos_hits"":""VVFIN"",""infl"":""Ind.Pres.Sg.3"",""lemma_idmwb"":""157380000""}");

        var annotation = Assert.Single(token);
        Assert.Equal("stân", annotation.Headword);
        Assert.Equal("ſtet", annotation.DiplomaticForm);
        Assert.Equal("stêt", annotation.NormalisedForm);
        Assert.Equal("157380000", annotation.DictionaryId);
    }

    /// <summary>
    /// Part of speech and inflection are separate fields and become one parse,
    /// because there is one column. Left in ReM's own tagset: MorphologyDecoder
    /// has no HiTS branch and shows an undecoded tag raw, which its own comment
    /// argues is better than a wrong parse.
    /// </summary>
    [Theory]
    [InlineData(@"""pos_hits"":""VVFIN"",""infl"":""Ind.Pres.Sg.3""", "VVFIN Ind.Pres.Sg.3")]
    [InlineData(@"""pos_hits"":""AVD"",""infl"":""--""", "AVD")]
    [InlineData(@"""pos_hits"":""--"",""infl"":""Fem.Nom.Sg""", "Fem.Nom.Sg")]
    [InlineData(@"""pos_hits"":""--"",""infl"":""--""", null)]
    public void ThePartOfSpeechAndTheInflectionAreOneParse(string fields, string? expected)
    {
        var token = Assert.Single(Read($@"{{""form"":""al"",""norm"":""al"",""lemma"":""al"",{fields}}}"));
        Assert.Equal(expected, token.Tag);
    }

    /// <summary>
    /// "--" is ReM's absent-value marker, and punctuation carries it in the
    /// lemma field.
    /// </summary>
    [Fact]
    public void TokensWithNoHeadwordAreSkipped()
    {
        Assert.Empty(Read(@"{""form"":""."",""norm"":""."",""lemma"":""--""}"));
    }

    /// <summary>
    /// <b>The one that would have been most visible.</b> "--" is not the only
    /// way ReM says it could not give a lemma: it also writes [!!] and [!], on
    /// 110,616 and 20,975 tokens - 5.7% of the corpus. Kept, they would have
    /// been far and away the two commonest headwords in Middle High German, and
    /// a reader clicking a word would have been told its dictionary form was
    /// "[!!]".
    /// </summary>
    [Theory]
    [InlineData("[!!]")]
    [InlineData("[!]")]
    [InlineData("[?]")]
    public void ReMsCouldNotLemmatiseMarkersAreNotHeadwords(string marker)
    {
        Assert.Empty(Read($@"{{""form"":""x"",""norm"":""x"",""lemma"":""{marker}""}}"));
    }

    /// <summary>
    /// And a real headword with punctuation in it survives, because the rule is
    /// "contains a letter" rather than a list of forbidden characters. ReM's own
    /// headwords carry segmentation hyphens and parenthesised optional
    /// segments - "wër(e)lt", "hèr(e)-bërge" - and a rule that rejected brackets
    /// outright would throw away a large part of the inventory.
    /// </summary>
    [Theory]
    [InlineData("wër(e)lt")]
    [InlineData("hèr(e)-bërge")]
    [InlineData("un-ge-sèlle-schaft")]
    public void AHeadwordWithBracketsOrHyphensIsStillAHeadword(string headword)
    {
        var token = Assert.Single(Read(
            $@"{{""form"":""welt"",""norm"":""werelt"",""lemma"":""{headword}""}}"));

        Assert.Equal(headword, token.Headword);
    }
}

/// <summary>
/// The mappings as the library stores them.
/// </summary>
[Collection("Database")]
public class ReMLemmaIngestTests
{
    /// <summary>
    /// A ReM JSON archive with the given tokens, written where the ingest looks
    /// for it.
    /// </summary>
    private static string Archive(params string[] tokens)
    {
        var root = Path.Combine(Path.GetTempPath(), "ccx-rem-lemma-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var json = "{\"token\":[" + string.Join(",", tokens) + "]}";
        var zipPath = Path.Combine(root, ReMLemmaIngestService.ArchiveFileName);

        using (var zip = new System.IO.Compression.ZipArchive(
                   File.Create(zipPath), System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("ReM-v2.1_json/json/M058.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(json);
        }

        return root;
    }

    private const string Stet =
        @"{""form"":""ſtet"",""norm"":""stêt"",""lemma"":""stân"",""pos_hits"":""VVFIN"",""infl"":""Ind.Pres.Sg.3""}";

    /// <summary>
    /// The library holds every ReM text twice, as the scribe spelled it and
    /// normalised. A mapping known under only one of them would work in one
    /// pane and not the other.
    /// </summary>
    [Fact]
    public async Task BothSpellingsFindTheHeadword()
    {
        using var db = await TempDatabase.CreateAsync();
        await new ReMLemmaIngestService().IngestAsync(Archive(Stet));

        var repo = new LemmaRepository();

        Assert.Equal("stân", Assert.Single(await repo.GetHeadwordsForFormAsync("ſtet", "gmh")).Headword);
        Assert.Equal("stân", Assert.Single(await repo.GetHeadwordsForFormAsync("stêt", "gmh")).Headword);
    }

    /// <summary>
    /// And typing an ordinary s finds the manuscript's tall one, which is what
    /// folding U+017F in WordNormalizer is for - 431,099 occurrences across the
    /// corpus.
    /// </summary>
    [Fact]
    public async Task TypingAnOrdinarySFindsTheManuscriptSpelling()
    {
        using var db = await TempDatabase.CreateAsync();
        await new ReMLemmaIngestService().IngestAsync(Archive(Stet));

        Assert.Equal("stân",
            Assert.Single(await new LemmaRepository().GetHeadwordsForFormAsync("stet", "gmh")).Headword);
    }

    [Fact]
    public async Task TheParseIsStoredWithTheMapping()
    {
        using var db = await TempDatabase.CreateAsync();
        await new ReMLemmaIngestService().IngestAsync(Archive(Stet));

        var headword = Assert.Single(await new LemmaRepository().GetHeadwordsForFormAsync("ſtet", "gmh"));
        Assert.Equal("VVFIN Ind.Pres.Sg.3", headword.PartOfSpeech);
    }

    /// <summary>
    /// Where the two spellings normalise to the same string - 54.8% of the
    /// corpus - one row is stored, not two. That includes every word whose only
    /// difference was a long s, which is why folding it in WordNormalizer took
    /// the stored mappings down as well as making them findable.
    /// </summary>
    [Theory]
    [InlineData(@"""form"":""al"",""norm"":""al""")]
    [InlineData(@"""form"":""ſtet"",""norm"":""stet""")]
    public async Task SpellingsThatNormaliseAlikeAreStoredOnce(string forms)
    {
        using var db = await TempDatabase.CreateAsync();

        await new ReMLemmaIngestService().IngestAsync(Archive(
            $@"{{{forms},""lemma"":""al"",""pos_hits"":""AVD"",""infl"":""--""}}"));

        Assert.Equal(1, await new LemmaRepository().CountByLanguageAsync("gmh"));
    }

    /// <summary>
    /// And where they really differ, both are stored - 45.2% of the corpus,
    /// which is genuine variation between what the scribe wrote and what a
    /// modern edition prints, not a matter of letter shapes.
    /// </summary>
    [Fact]
    public async Task SpellingsThatDifferAreBothStored()
    {
        using var db = await TempDatabase.CreateAsync();

        await new ReMLemmaIngestService().IngestAsync(Archive(
            @"{""form"":""div"",""norm"":""diu"",""lemma"":""dër""}"));

        var repo = new LemmaRepository();
        Assert.Equal(2, await repo.CountByLanguageAsync("gmh"));
        Assert.Equal("dër", Assert.Single(await repo.GetHeadwordsForFormAsync("div", "gmh")).Headword);
        Assert.Equal("dër", Assert.Single(await repo.GetHeadwordsForFormAsync("diu", "gmh")).Headword);
    }

    /// <summary>
    /// Re-running replaces this language's mappings and leaves the others
    /// alone. The Greek and Latin sets are separate multi-minute downloads -
    /// the Latin one is two gigabytes - and a whole-table clear would take them
    /// with it.
    /// </summary>
    [Fact]
    public async Task ReRunningReplacesOnlyThisLanguage()
    {
        using var db = await TempDatabase.CreateAsync();

        await new LemmaRepository().BulkInsertAsync(new[]
        {
            new ClassicaCodex.Core.Models.Lemma
            {
                Form = "amavit", NormalizedForm = "amauit", Headword = "amo", Language = "lat"
            }
        });

        // A token whose two spellings really do normalise apart - "div" against
        // "diu", which is 45.2% of the corpus and the reason both are stored.
        // Using one that folds together would have made this pass whether the
        // re-run replaced or accumulated.
        var archive = Archive(
            @"{""form"":""div"",""norm"":""diu"",""lemma"":""dër"",""pos_hits"":""DDART"",""infl"":""Fem.Nom.Sg""}");

        await new ReMLemmaIngestService().IngestAsync(archive);
        await new ReMLemmaIngestService().IngestAsync(archive);

        var repo = new LemmaRepository();
        Assert.Equal(2, await repo.CountByLanguageAsync("gmh"));
        Assert.Equal(1, await repo.CountByLanguageAsync("lat"));
    }

    /// <summary>
    /// An archive of the wrong distribution says so rather than reporting that
    /// it loaded nothing. Someone who points this step at the TEI download has
    /// made an understandable mistake and should be told which one.
    /// </summary>
    [Fact]
    public async Task AnArchiveWithNoAnnotationInItIsReported()
    {
        using var db = await TempDatabase.CreateAsync();

        var root = Path.Combine(Path.GetTempPath(), "ccx-rem-lemma-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using (var zip = new System.IO.Compression.ZipArchive(
                   File.Create(Path.Combine(root, "ReM-v2.1_tei.zip")),
                   System.IO.Compression.ZipArchiveMode.Create))
        {
            zip.CreateEntry("ReM-v2.1_tei/tei/M058.xml");
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => new ReMLemmaIngestService().IngestAsync(root));

        Assert.Contains("Tabular JSON", error.Message);
    }

    /// <summary>
    /// The normalised form stored for matching is the one the word index will
    /// produce from the text, or a lookup finds nothing and the reason is
    /// invisible.
    /// </summary>
    [Fact]
    public async Task TheStoredFormMatchesWhatTheIndexWouldProduce()
    {
        using var db = await TempDatabase.CreateAsync();
        await new ReMLemmaIngestService().IngestAsync(Archive(Stet));

        var fromText = WordNormalizer.TokenizeLine("ſtet.").Single();

        Assert.Equal("stân",
            Assert.Single(await new LemmaRepository().GetHeadwordsForFormAsync(fromText, "gmh")).Headword);
    }
}
