using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Go to Citation: reading a citation as the Atlas and the reports copy one,
/// and finding the passage it names in the library.
/// </summary>
public class CopiedCitationTests
{
    [Theory]
    [InlineData("tlg0020.tlg001.perseus-grc2 457", "tlg0020.tlg001.perseus-grc2", "457")]
    [InlineData("urn:cts:greekLit:tlg0020.tlg001.perseus-grc2:457", "urn:cts:greekLit:tlg0020.tlg001.perseus-grc2", "457")]
    [InlineData("urn:cts:greekLit:tlg0020.tlg001.perseus-grc2 457", "urn:cts:greekLit:tlg0020.tlg001.perseus-grc2", "457")]
    [InlineData("oracc:cams/gkab:P338345:transliteration o i 46'", "oracc:cams/gkab:P338345:transliteration", "o i 46'")]
    [InlineData("urn:menota:anonymous:vǫluspá:am-544-4to 1.3.7", "urn:menota:anonymous:vǫluspá:am-544-4to", "1.3.7")]
    [InlineData("  “tlg0012.tlg001.perseus-grc2   1.1”  ", "tlg0012.tlg001.perseus-grc2", "1.1")]
    [InlineData("tlg0012.tlg001.perseus-grc2 1.1;", "tlg0012.tlg001.perseus-grc2", "1.1")]
    [InlineData("\n\ntlg0012.tlg001 1.1\nsomething else", "tlg0012.tlg001", "1.1")]
    [InlineData("tlg0012.tlg001.perseus-grc2", "tlg0012.tlg001.perseus-grc2", "")]
    public void A_citation_is_an_identifier_then_a_reference(string text, string identifier, string reference)
    {
        var citation = CopiedCitation.Parse(text)!;
        Assert.Equal(identifier, citation.Identifier);
        Assert.Equal(reference, citation.Reference);
        Assert.Null(citation.ArtifactName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hesiod Theogony 457")]
    [InlineData("hello")]
    [InlineData("CatmusLines – Paris, BnF, lat. 9768 #120078")]
    public void Text_that_is_not_a_citation_is_not_read_as_one(string? text) => Assert.Null(CopiedCitation.Parse(text));

    [Theory]
    [InlineData("artifact:London E 163 London E 163", "London E 163")]
    [InlineData("artifact:Athens, NM 1841 Athens, NM 1841", "Athens, NM 1841")]
    [InlineData("artifact:Dewing 1418", "Dewing 1418")]
    public void A_museum_object_is_named_once_though_the_page_copies_it_twice(string text, string name)
    {
        var citation = CopiedCitation.Parse(text)!;
        Assert.Equal(name, citation.ArtifactName);
        Assert.Equal("artifact:" + name, citation.ToString());
    }

    [Fact]
    public void A_range_or_a_list_is_looked_for_by_its_first_passage_after_the_whole()
    {
        Assert.Equal(new[] { "10.235–10.240", "10.235" }, CopiedCitation.Parse("tlg0012.tlg002.perseus-grc2 10.235–10.240")!.References());
        Assert.Equal(new[] { "1.1-1.2", "1.1" }, CopiedCitation.Parse("urn:dante:commedia:inferno:petrocchi 1.1-1.2")!.References());
        Assert.Equal(new[] { "o 26'–o 29'", "o 26'" }, CopiedCitation.Parse("oracc:ccpo:P394923:transliteration o 26'–o 29'")!.References());
        Assert.Equal(new[] { "2.35; 2.40", "2.35" }, CopiedCitation.Parse("urn:magic:dclp:64011:grc 2.35; 2.40")!.References());

        // The hyphen in "64-2.53" is the reference's own, so the dash is split
        // on first, and "64-2.53" is tried before the "64" a hyphen would give.
        var egyptian = CopiedCitation.Parse("urn:egy:tla:tb:pkairo-cg-51189-pjuja:translit 64-2.53–64-2.57")!.References();
        Assert.Equal(new[] { "64-2.53–64-2.57", "64-2.53", "64" }, egyptian);

        Assert.Empty(CopiedCitation.Parse("tlg0012.tlg001.perseus-grc2")!.References());
    }

    [Fact]
    public void A_reference_written_with_its_edition_s_urn_is_read_without_it() =>
        Assert.Contains("457", CopiedCitation.Parse("tlg0020.tlg001.perseus-grc2 urn:cts:greekLit:tlg0020.tlg001.perseus-grc2.457")!.References());

    [Theory]
    [InlineData("urn:cts:greekLit:tlg0020.tlg001.perseus-grc2", "tlg0020.tlg001.perseus-grc2")]
    [InlineData("tlg0020.tlg001.perseus-grc2", "tlg0020.tlg001.perseus-grc2")]
    [InlineData("urn:menota:anonymous:gylfaginning:am-242-fol", "menota:anonymous:gylfaginning:am-242-fol")]
    [InlineData("Menota:Anonymous:Gylfaginning:AM-242-fol", "menota:anonymous:gylfaginning:am-242-fol")]
    public void Identifiers_compare_without_their_heads_or_case(string identifier, string key) =>
        Assert.Equal(key, CopiedCitationResolver.Key(identifier));

    [Collection("Database")]
    public class InTheLibrary
    {
        private static async Task<CitationLookupResult> LookUpAsync(string text) =>
            await new CopiedCitationResolver().LookUpAsync(CopiedCitation.Parse(text)!);

        /// <summary>
        /// Hesiod's Theogony, stored as Perseus stores it with the edition's
        /// URN in front of each line's number; the Iliad in Greek and English;
        /// and a Menota edition with a URN of its own.
        /// </summary>
        private static async Task SeedAsync(TempDatabase db)
        {
            await db.ExecuteAsync(@"
                INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace) VALUES
                    (1, 'urn:cts:greekLit:tlg0020', 'Hesiod', 'greekLit'),
                    (2, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit'),
                    (3, 'urn:menota:anonymous', 'Anonymous', 'menota');
                INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES
                    (1, 1, 'urn:cts:greekLit:tlg0020.tlg001', 'Theogony'),
                    (2, 2, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad'),
                    (3, 3, 'urn:menota:anonymous:gylfaginning', 'Gylfaginning');
                INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language, Collection) VALUES
                    (1, 1, 'tlg0020.tlg001.perseus-grc2', 'Original', 'grc', 'perseus-greek'),
                    (2, 2, 'tlg0012.tlg001.perseus-grc2', 'Original', 'grc', 'perseus-greek'),
                    (3, 2, 'tlg0012.tlg001.perseus-eng3', 'Translation', 'eng', 'perseus-greek'),
                    (4, 3, 'urn:menota:anonymous:gylfaginning:am-242-fol', 'Original', 'non', 'menota');
                INSERT INTO TextNodes (TextNodeId, EditionId, CitationRef, SortOrder, Text) VALUES
                    (10, 1, 'urn:cts:greekLit:tlg0020.tlg001.perseus-grc2.456', 0, 'line 456'),
                    (11, 1, 'urn:cts:greekLit:tlg0020.tlg001.perseus-grc2.457', 1, 'Ζῆνά τε μητιόεντα, θεῶν πατέρʼ ἠδὲ καὶ ἀνδρῶν,'),
                    (20, 2, '1.1', 2, 'μῆνιν ἄειδε θεὰ'),
                    (21, 2, '1.2', 3, 'οὐλομένην'),
                    (30, 3, '1.1', 4, 'Sing, goddess, the wrath'),
                    (40, 4, '10.22', 5, 'Þá mælti Gangleri');
                INSERT INTO Artifacts (ArtifactId, Type, Name, Location, Description) VALUES
                    ('aa_5227', 'Vase', 'London E 163', 'London, British Museum', 'A hydria.');");
        }

        [Fact]
        public async Task A_copied_citation_opens_the_line_though_the_library_stores_it_under_its_urn()
        {
            using var db = await TempDatabase.CreateAsync();
            await SeedAsync(db);

            var found = await LookUpAsync("tlg0020.tlg001.perseus-grc2 457");

            Assert.Equal(CitationLookupOutcome.Found, found.Outcome);
            Assert.Equal(11, found.TextNodeId);
            Assert.Equal(1, found.Edition!.EditionId);
            Assert.Equal("Hesiod", found.AuthorName);
            Assert.Equal("Theogony", found.WorkTitle);
            Assert.StartsWith("Ζῆνά", found.Text);
            Assert.False(found.Substituted);

            Assert.Equal(11, (await LookUpAsync("urn:cts:greekLit:tlg0020.tlg001.perseus-grc2:457")).TextNodeId);
            Assert.Equal(11, (await LookUpAsync("tlg0020.tlg001.perseus-grc2 457–460")).TextNodeId);
            Assert.Equal(40, (await LookUpAsync("urn:menota:anonymous:gylfaginning:am-242-fol 10.22")).TextNodeId);
        }

        [Fact]
        public async Task A_work_opens_in_its_original_and_a_missing_edition_in_another_of_the_same_language()
        {
            using var db = await TempDatabase.CreateAsync();
            await SeedAsync(db);

            var work = await LookUpAsync("tlg0012.tlg001 1.1");
            Assert.Equal(20, work.TextNodeId);
            Assert.False(work.Substituted);

            // The library has grc2, not grc1: the Greek, said to be another edition.
            var greek = await LookUpAsync("tlg0012.tlg001.perseus-grc1 1.2");
            Assert.Equal(21, greek.TextNodeId);
            Assert.True(greek.Substituted);

            // An English translation it lacks opens the English it has, not the Greek.
            var english = await LookUpAsync("tlg0012.tlg001.perseus-eng2 1.1");
            Assert.Equal(30, english.TextNodeId);
            Assert.True(english.Substituted);
        }

        [Fact]
        public async Task A_reference_the_edition_lacks_opens_it_at_the_start_and_an_unknown_identifier_nothing()
        {
            using var db = await TempDatabase.CreateAsync();
            await SeedAsync(db);

            var absent = await LookUpAsync("tlg0020.tlg001.perseus-grc2 9999");
            Assert.Equal(CitationLookupOutcome.ReferenceNotFound, absent.Outcome);
            Assert.Equal(10, absent.TextNodeId);
            Assert.Equal("Theogony", absent.WorkTitle);

            var start = await LookUpAsync("tlg0020.tlg001.perseus-grc2");
            Assert.Equal(CitationLookupOutcome.Found, start.Outcome);
            Assert.Equal(10, start.TextNodeId);

            var unknown = await LookUpAsync("tlg9999.tlg001.perseus-grc1 1.1");
            Assert.Equal(CitationLookupOutcome.NotInLibrary, unknown.Outcome);
            Assert.Null(unknown.TextNodeId);
        }

        [Fact]
        public async Task A_museum_object_is_found_by_its_name()
        {
            using var db = await TempDatabase.CreateAsync();
            await SeedAsync(db);

            var vase = await CopiedCitationResolver.LookUpArtifactAsync(CopiedCitation.Parse("artifact:London E 163 London E 163")!.ArtifactName!);
            Assert.Equal("aa_5227", vase!.ArtifactId);
            Assert.Equal("London, British Museum", vase.Location);

            Assert.Null(await CopiedCitationResolver.LookUpArtifactAsync("Munich 2044"));
        }
    }
}
