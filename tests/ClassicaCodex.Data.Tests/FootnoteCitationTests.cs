using ClassicaCodex.Core.Citations;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Go to Citation with references as books print them: finding the
/// references in a footnote, reading which work each abbreviation names, and
/// opening them in the library.
/// </summary>
public class FootnoteCitationTests
{
    [Fact]
    public void A_footnote_is_read_for_every_reference_in_it()
    {
        var found = FootnoteReader.Read("Thuc. 2.40.1; cf. Hdt. 1.1, 5.3 and Pl. Resp. 514a–517a.");

        Assert.Equal(4, found.Count);
        Assert.Equal(new[] { "Thuc." }, found[0].Names);
        Assert.Equal("2.40.1", found[0].Reference);
        Assert.Equal(new[] { "Hdt." }, found[1].Names);
        Assert.Equal("1.1", found[1].Reference);
        Assert.Equal(FootnoteReferenceKind.SameWork, found[2].Kind);
        Assert.Equal("5.3", found[2].Reference);
        Assert.Equal(new[] { "Pl.", "Resp." }, found[3].Names);
        Assert.Equal("514a", found[3].Reference);
        Assert.Equal("Pl. Resp. 514a–517a", found[3].Written);
    }

    [Theory]
    [InlineData("Il. 4.106, 125", 1, "4.125")]
    [InlineData("Cic. Off. 1, 7, 22", 0, "1.7.22")]
    [InlineData("Tac. Ann. IV.32", 0, "4.32")]
    [InlineData("Hdt. I, 1", 0, "1.1")]
    [InlineData("Cic. Nat. D. 1.1", 0, "1.1")]
    [InlineData("Hor. C. 1, 28, 29 al.", 0, "1.28.29")]
    [InlineData("Arist. Pol. 1253a2–3", 0, "1253a2")]
    [InlineData("Od. 9.366 sq.; ibid. 9.410", 1, "9.410")]
    [InlineData("Plin. 2, 47, 48, § 128", 0, "2.47.48")]
    public void A_reference_is_read_as_books_write_it(string text, int index, string reference) =>
        Assert.Equal(reference, FootnoteReader.Read(text)[index].Reference);

    [Fact]
    public void Capital_letters_are_names_not_numerals_or_page_marks()
    {
        Assert.Equal(new[] { "Cic.", "Nat.", "D." }, FootnoteReader.Read("Cic. Nat. D. 1.1")[0].Names);
        Assert.Equal(new[] { "Hor.", "C." }, FootnoteReader.Read("Hor. C. 1, 28, 29")[0].Names);
        Assert.Equal(new[] { "Caes.", "B.", "C." }, FootnoteReader.Read("Caes. B. C. 1, 35")[0].Names);
        Assert.Equal("128", FootnoteReader.Read("Plin. 2, 47, 48, § 128")[0].Section);
    }

    [Fact]
    public void Id_and_ibid_carry_the_author_and_the_work()
    {
        var found = FootnoteReader.Read("Verg. Aen. 4.1–5; id. G. 1.1; ibid. 2.3.");

        Assert.Equal(FootnoteReferenceKind.SameAuthor, found[1].Kind);
        Assert.Equal(new[] { "G." }, found[1].Names);
        Assert.Equal(FootnoteReferenceKind.SameWork, found[2].Kind);
        Assert.Equal("2.3", found[2].Reference);
    }

    [Fact]
    public void Prose_between_references_ends_the_run()
    {
        var text = "As Thucydides (2.40.1) puts it, and as Herodotus says at 1.1, the Greeks were different.";
        var found = FootnoteReader.Read(text);

        Assert.Single(found);
        Assert.Equal("Thucydides 2.40.1", found[0].WrittenFrom(text, 1));
    }

    private static readonly LibraryWork[] Library =
    {
        new("tlg0003.tlg001", "Thucydides", "History of the Peloponnesian War"),
        new("tlg0059.tlg030", "Plato", "Republic"),
        new("tlg0059.tlg034", "Plato", "Laws"),
        new("tlg0086.tlg037", "Aristotle", "De respiratione"),
        new("tlg0086.tlg010", "Aristotle", "Nicomachean Ethics"),
        new("tlg0007.tlg038", "Plutarch", "Nicias"),
        new("phi0474.phi055", "Cicero, Marcus Tullius", "De Officiis"),
        new("tlg0627.tlg008", "Hippocrates", "De officina medici"),
        new("phi0631.phi001", "Sallust", "Bellum Catilinae"),
        new("phi0472.phi001", "Catullus, C. Valerius", "Carmina"),
        new("tlg0014.tlg018", "Demosthenes", "On the Crown"),
        new("tlg0014.tlg019", "Demosthenes", "On the False Embassy"),
        new("phi1348.abo012", "Suetonius", "Divus Augustus"),
        new("phi0978.phi001", "Pliny, the Elder", "Naturalis Historia"),
        new("phi1318.phi001", "Pliny, the Younger", "Epistulae"),
        new("phi0893.phi001", "Horace", "Odes"),
        new("phi0893.phi006", "Horace", "Ars Poetica"),
        new("tlg0001.tlg001", "Apollonius Rhodius", "Argonautica"),
        new("tlg0019.tlg001", "Aristophanes", "Acharnians"),
        new("tlg0074.tlg001", "Arrian", "Anabasis"),
    };

    private static readonly WorkAbbreviations Abbreviations = new(Library);

    private static string? Top(string names, params string[] passage) =>
        Abbreviations.Match(names.Split(' ', StringSplitOptions.RemoveEmptyEntries), passage.Length > 0 ? passage : new[] { "1" })
            .FirstOrDefault()?.Work;

    [Theory]
    [InlineData("Thuc.", "tlg0003.tlg001")]
    [InlineData("Thucydides", "tlg0003.tlg001")]
    [InlineData("Pl. Resp.", "tlg0059.tlg030")]
    [InlineData("Plato, Republic", "tlg0059.tlg030")]
    [InlineData("Pl. Leg.", "tlg0059.tlg034")]
    [InlineData("Arist. Eth. Nic.", "tlg0086.tlg010")]
    [InlineData("Arist. EN", "tlg0086.tlg010")]
    [InlineData("Cic. Off.", "phi0474.phi055")]
    [InlineData("Sall. Cat.", "phi0631.phi001")]
    [InlineData("Cat.", "phi0472.phi001")]
    [InlineData("Suet. Aug.", "phi1348.abo012")]
    [InlineData("Plin. HN", "phi0978.phi001")]
    [InlineData("Plin. Ep.", "phi1318.phi001")]
    [InlineData("Hor. C.", "phi0893.phi001")]
    [InlineData("Hor. Carm.", "phi0893.phi001")]
    [InlineData("Hor. Ars P.", "phi0893.phi006")]
    [InlineData("A.R.", "tlg0001.tlg001")]
    [InlineData("See Thuc.", "tlg0003.tlg001")]
    public void An_abbreviation_names_its_work(string names, string work) => Assert.Equal(work, Top(names));

    [Theory]
    [InlineData("In")]
    [InlineData("Smith")]
    [InlineData("Varr. R. R.")]
    public void Prose_and_unknown_authors_name_nothing(string names) => Assert.Null(Top(names));

    [Fact]
    public void An_orator_is_cited_by_speech()
    {
        var candidate = Abbreviations.Match(new[] { "Dem." }, new[] { "18", "169" })[0];

        Assert.Equal("tlg0014.tlg018", candidate.Work);
        Assert.Equal(new[] { "169" }, candidate.Passage);
        Assert.Equal("tlg0014.tlg019", Abbreviations.Continue("tlg0014.tlg018", new[] { "19", "1" })[0].Work);
    }

    [Fact]
    public void The_bundled_table_holds_the_dictionaries_abbreviations()
    {
        var table = CitationAbbreviations.Default;

        Assert.Contains(table.Uses, u => u.Abbreviation == "Pl. R." && u.Work == "tlg0059.tlg030");
        Assert.Contains(table.Uses, u => u.Abbreviation == "Hor. C." && u.Work == "phi0893.phi001");
        Assert.Contains(table.Speeches, s => s.Abbreviation == "Isoc." && s.Number == 4 && s.Work == "tlg0010.tlg011");
        Assert.True(table.Uses.Count > 2000);
    }

    [Collection("Database")]
    public class InTheLibrary
    {
        /// <summary>
        /// The Theogony stored under its edition's URN; the Iliad in Greek and
        /// English; Thucydides numbered a level deeper in Greek (2.40.1.1)
        /// than in a French translation (2.40.1); and two speeches of
        /// Demosthenes, numbered by section.
        /// </summary>
        private static async Task SeedAsync(TempDatabase db)
        {
            await db.ExecuteAsync(@"
                INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace) VALUES
                    (1, 'urn:cts:greekLit:tlg0020', 'Hesiod', 'greekLit'),
                    (2, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit'),
                    (3, 'urn:cts:greekLit:tlg0003', 'Thucydides', 'greekLit'),
                    (4, 'urn:cts:greekLit:tlg0014', 'Demosthenes', 'greekLit');
                INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES
                    (1, 1, 'urn:cts:greekLit:tlg0020.tlg001', 'Theogony'),
                    (2, 2, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad'),
                    (3, 3, 'urn:cts:greekLit:tlg0003.tlg001', 'History of the Peloponnesian War'),
                    (4, 4, 'urn:cts:greekLit:tlg0014.tlg018', 'On the Crown'),
                    (5, 4, 'urn:cts:greekLit:tlg0014.tlg019', 'On the False Embassy');
                INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language, Collection) VALUES
                    (1, 1, 'tlg0020.tlg001.perseus-grc2', 'Original', 'grc', 'perseus-greek'),
                    (2, 2, 'tlg0012.tlg001.perseus-grc2', 'Original', 'grc', 'perseus-greek'),
                    (3, 2, 'tlg0012.tlg001.perseus-eng3', 'Translation', 'eng', 'perseus-greek'),
                    (4, 3, 'tlg0003.tlg001.perseus-grc2', 'Original', 'grc', 'perseus-greek'),
                    (5, 3, 'tlg0003.tlg001.1st1K-fre1', 'Translation', 'fre', 'first1k-greek'),
                    (6, 4, 'tlg0014.tlg018.perseus-grc1', 'Original', 'grc', 'perseus-greek'),
                    (7, 5, 'tlg0014.tlg019.perseus-grc1', 'Original', 'grc', 'perseus-greek');
                INSERT INTO TextNodes (TextNodeId, EditionId, CitationRef, SortOrder, Text) VALUES
                    (10, 1, 'urn:cts:greekLit:tlg0020.tlg001.perseus-grc2.456', 0, 'line 456'),
                    (11, 1, 'urn:cts:greekLit:tlg0020.tlg001.perseus-grc2.457', 1, 'Ζῆνά τε μητιόεντα'),
                    (20, 2, '1.1', 2, 'μῆνιν ἄειδε θεὰ'),
                    (21, 2, '1.2', 3, 'οὐλομένην'),
                    (30, 3, '1.1', 4, 'Sing, goddess, the wrath'),
                    (40, 4, '2.40.1.1', 5, 'φιλοκαλοῦμέν τε γὰρ μετʼ εὐτελείας'),
                    (50, 5, '2.40.1', 6, 'Nous aimons le beau dans la simplicité'),
                    (60, 6, '169', 7, 'ἑσπέρα μὲν γὰρ ἦν'),
                    (70, 7, '1', 8, 'ὅση μέν, ὦ ἄνδρες Ἀθηναῖοι');");
        }

        [Fact]
        public async Task Each_reference_of_a_footnote_opens_its_line()
        {
            using var db = await TempDatabase.CreateAsync();
            await SeedAsync(db);

            var lookups = await new CopiedCitationResolver().LookUpFootnoteAsync(
                "See Hes. Th. 457; Il. 1.1, 2; Thuc. 2.40.1; Dem. 18.169; 19.1; Plut. Alex. 1.1; Smith 23.");

            Assert.Equal(new long?[] { 11, 20, 21, 40, 60, 70 }, lookups.Take(6).Select(l => l.Result?.TextNodeId).ToArray());
            Assert.All(lookups.Take(6), l => Assert.Equal(CitationLookupOutcome.Found, l.Result!.Outcome));
            Assert.Equal("Hes. Th. 457", lookups[0].Written);

            // The work is named, but this library does not have it.
            Assert.Equal(CitationLookupOutcome.NotInLibrary, lookups[6].Result!.Outcome);

            // Not a classical author at all.
            Assert.False(lookups[7].Recognised);
        }

        [Fact]
        public async Task A_footnote_opens_the_original_before_a_translation_numbered_like_it()
        {
            using var db = await TempDatabase.CreateAsync();
            await SeedAsync(db);

            var thucydides = (await new CopiedCitationResolver().LookUpFootnoteAsync("Thuc. 2.40.1"))[0].Result!;

            Assert.Equal(40, thucydides.TextNodeId);
            Assert.Equal("grc", thucydides.Edition!.Language);
        }
    }
}
