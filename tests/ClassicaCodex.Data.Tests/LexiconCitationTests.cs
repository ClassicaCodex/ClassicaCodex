using System.Xml.Linq;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Lexicon;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The dictionaries' citations: read out of LSJ's and Lewis and Short's TEI,
/// kept in the entry and in the citations table, and matched to a passage of
/// the library the way the measurements found works.
/// </summary>
public class LexiconCitationTests
{
    private const string Greek = "urn:cts:greekLit:";

    private static LexiconCitationTarget Target(string urn) => LexiconCitationTarget.Parse(urn)!;

    [Fact]
    public void A_citation_URN_reads_in_either_dictionary_s_shape()
    {
        var iliad = Target("urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:1.441");
        Assert.Equal("greekLit", iliad.Namespace);
        Assert.Equal("tlg0012.tlg001", iliad.WorkUrn);
        Assert.Equal("tlg0012.tlg001.perseus-grc1", iliad.EditionUrn);
        Assert.Equal(new[] { "1", "441" }, iliad.Passage);
        Assert.Equal("urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:1.441", iliad.ToString());

        // Lewis and Short's colons, and a work with no edition named.
        var quintilian = Target("urn:cts:latinLit:phi1002.phi001:1:5:61");
        Assert.Null(quintilian.EditionUrn);
        Assert.Equal("1.5.61", quintilian.PassageRef);

        // "and the lines following", and a label no edition numbers by.
        Assert.Equal("24.263", Target("urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:24:263s").PassageRef);
        Assert.Equal("1.1.2", Target("urn:cts:latinLit:phi0474.phi037.perseus-lat1:Orat::1:1:2").PassageRef);

        // Nothing to open: a work alone, LSJ's unnumbered 000, a papyrus address.
        Assert.Null(LexiconCitationTarget.Parse("urn:cts:latinLit:phi1236.phi001"));
        Assert.Null(LexiconCitationTarget.Parse("urn:cts:greekLit:tlg0018.tlg001:000"));
        Assert.Null(LexiconCitationTarget.Parse("Perseus:abo:pap,P.Oxy.:1:2"));
        Assert.Null(LexiconCitationTarget.Parse(null));
    }

    [Fact]
    public void An_entry_keeps_its_citations_in_its_text_and_reads_plain_without_them()
    {
        var iliad = Target("urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:1.1");
        var entry = "wrath, " + LexiconCitationMarkup.Write(iliad, "Il.\n   1.1") + "; " + LexiconCitationMarkup.Write(null, "Hsch.") + ".";

        var pieces = LexiconCitationMarkup.Parse(entry);
        Assert.Equal(new[] { "wrath, ", "Il. 1.1", "; ", "Hsch.", "." }, pieces.Select(p => p.Text));
        Assert.Equal(iliad, pieces[1].Citation!.Target);
        Assert.Equal(0, pieces[1].Citation!.Ordinal);
        Assert.Null(pieces[3].Citation!.Target);
        Assert.Equal(1, pieces[3].Citation!.Ordinal);
        Assert.Equal("wrath, Il. 1.1; Hsch..", LexiconCitationMarkup.Plain(entry));

        // An entry from before citations were kept is its own plain text.
        Assert.False(LexiconCitationMarkup.HasCitations("wrath, Il. 1.1"));
        Assert.Equal("wrath", LexiconCitationMarkup.Plain("wrath"));

        // Cut short inside a marker, the label is still what a reader sees.
        var broken = "wrath, " + LexiconCitationMarkup.Open + iliad + LexiconCitationMarkup.Separator + "Il. 1";
        Assert.Equal("wrath, Il. 1", LexiconCitationMarkup.Plain(broken));
    }

    [Fact]
    public void Truncating_an_entry_never_cuts_a_citation_and_says_how_many_it_kept()
    {
        var iliad = Target("urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:1.1");
        var entry = "abcdefghij" + LexiconCitationMarkup.Write(iliad, "Il. 1.1") + "klmnopqrst" + LexiconCitationMarkup.Write(iliad, "Il. 1.2");

        // The passages the citations name do not count against the limit.
        Assert.Equal((entry, 2), LexiconCitationMarkup.Truncate(entry, 34));

        var (cut, kept) = LexiconCitationMarkup.Truncate(entry, 20);
        Assert.Equal(1, kept);
        Assert.Equal("abcdefghijIl. 1.1klm...", LexiconCitationMarkup.Plain(cut));

        (cut, kept) = LexiconCitationMarkup.Truncate(entry, 13);
        Assert.Equal(0, kept);
        Assert.Equal("abcdefghij...", cut);
    }

    [Fact]
    public void LSJ_s_Beta_Code_is_decoded_around_its_citations_and_not_inside_them()
    {
        var entry = "mh=nis, " + LexiconCitationMarkup.Write(Target("urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:1.1"), "Il. 1.1") + "\nA. wrath";

        Assert.Equal("μῆνις, Il. 1.1\n\nA. wrath", LexiconEntryText.ForReading(entry, "grc"));

        var pieces = LexiconEntryText.PiecesForReading(entry, "grc");
        var citation = Assert.Single(pieces, p => p.IsCitation);
        Assert.Equal("Il. 1.1", citation.Text);
        Assert.Equal("tlg0012.tlg001", citation.Citation!.Target!.WorkUrn);
    }

    [Fact]
    public void The_reader_fills_in_a_bare_reference_from_the_one_before_it()
    {
        var reader = new LexiconCitationReader("grc");
        Assert.Equal("4.106", reader.Read(Greek + "tlg0012.tlg001.perseus-grc1:4.106", "Il. 4.106")!.PassageRef);
        Assert.Equal("4.125", reader.Read(Greek + "tlg0012.tlg001.perseus-grc1:125", "125")!.PassageRef);

        // Another work's bare number is that work's own; a labelled one is taken as given.
        Assert.Equal("7", reader.Read(Greek + "tlg0011.tlg003.perseus-grc1:7", "7")!.PassageRef);
        Assert.Equal("200", reader.Read(Greek + "tlg0012.tlg001.perseus-grc1:200", "Od. 200")!.PassageRef);
    }

    [Fact]
    public void The_reader_leaves_unlinked_what_was_measured_to_open_the_wrong_passage()
    {
        var latin = new LexiconCitationReader("lat");

        // The Vulgate under the Septuagint's and the Greek New Testament's URNs.
        Assert.Null(latin.Read(Greek + "tlg0527.tlg002:4:14", "Vulg. Exod. 4, 14"));
        Assert.Null(latin.Read(Greek + "tlg0031.tlg006.perseus-grc1:12:2", "id. Rom. 12, 2"));

        // Pliny by Hardouin's chapters, and a section sign the URN does not end in.
        Assert.Null(latin.Read("urn:cts:latinLit:phi0978.phi001:2:48", "Plin. 2, 47, 48, § 128"));
        Assert.Null(latin.Read("urn:cts:latinLit:phi0474.phi058:fr::3:1:2", "Cic. Q. Fr. 3, 1, 2, § 4"));
        Assert.NotNull(latin.Read("urn:cts:latinLit:phi0474.phi005.perseus-lat1:2:5:172", "Cic. Verr. 2, 5, 67, § 172"));

        // Plautus by act, scene and line within the scene; a plain line number is kept.
        Assert.Null(latin.Read("urn:cts:latinLit:phi0119.phi019.perseus-lat1:2:4:129", "Plaut. Trin. 2, 4, 129"));
        Assert.NotNull(latin.Read("urn:cts:latinLit:phi0119.phi019.perseus-lat1:400", "Plaut. Trin. 400"));

        // The Odes moved off the Carmen Saeculare; the Carmen itself left where it is.
        var ode = latin.Read("urn:cts:latinLit:phi0893.phi002:1:28:29", "Hor. C. 1, 28, 29 al.")!;
        Assert.Equal("phi0893.phi001", ode.WorkUrn);
        Assert.Null(ode.EditionUrn);
        Assert.Equal("phi0893.phi002", latin.Read("urn:cts:latinLit:phi0893.phi002:5", "Hor. C. S. 5")!.WorkUrn);

        // LSJ's Ichneutae, numbered as the library's edition is not.
        Assert.Null(new LexiconCitationReader("grc").Read(Greek + "tlg0011.tlg008.perseus-grc1:88", "S. Ichn. 88"));
    }

    private static PassageIndex Index(bool sectionsRunThrough, params (string Ref, string? Milestone)[] passages) =>
        new(passages.Select((p, i) => ((long)i + 1, p.Ref, p.Milestone)), sectionsRunThrough);

    private static PassageIndex Index(params string[] refs) => Index(false, refs.Select(r => (r, (string?)null)).ToArray());

    private static (long, CitationMatch)? Find(PassageIndex index, string reference, bool mayStopAtChapter = false) =>
        index.Find(reference.Split('.'), mayStopAtChapter);

    [Fact]
    public void A_reference_is_found_as_it_stands_or_with_the_paragraph_the_library_adds_to_prose()
    {
        var iliad = Index(Greek + "tlg0012.tlg001.perseus-grc2.1.1", Greek + "tlg0012.tlg001.perseus-grc2.1.2");
        Assert.Equal((2L, CitationMatch.Exact), Find(iliad, "1.2"));
        Assert.Null(Find(iliad, "1.3"));

        var plutarch = Index("1.1.1", "1.2.1", "1.2.2");
        Assert.Equal((2L, CitationMatch.Paragraph), Find(plutarch, "1.2"));

        // A chapter the edition divides into sections opens at its first.
        var herodotus = Index("4.24.1.1", "4.25.0.1", "4.25.1.1");
        Assert.Equal((2L, CitationMatch.Section), Find(herodotus, "4.25"));
    }

    [Fact]
    public void A_Stephanus_reference_is_found_in_the_range_the_edition_records_and_never_on_a_speaker()
    {
        var republic = Index(false,
            ("1.327.1", "327a–c"),
            ("3.389.speaker1", "389e"),
            ("3.389.1", "389e–390e"),
            ("3.391.1", "390e–391e"));

        Assert.Equal((1L, CitationMatch.Milestone), Find(republic, "327b"));
        Assert.Equal((3L, CitationMatch.Milestone), Find(republic, "390c"));
        Assert.Equal((3L, CitationMatch.Milestone), Find(republic, "389e"));
        Assert.Null(Find(republic, "400a"));
    }

    [Fact]
    public void Chapter_and_section_read_as_the_section_only_where_sections_run_through()
    {
        var brutus = Index(true, ("92.1", null), ("93.1", null));
        Assert.Equal((2L, CitationMatch.ContinuousSection), Find(brutus, "24.93"));
        Assert.Null(Find(Index(false, ("92.1", null), ("93.1", null)), "24.93"));

        // De Oratore, cited by book and chapter: the edition's "3.45" is a
        // section, so a reference that may stop at the chapter is not taken
        // for one.
        var deOratore = Index(true, ("3.45.1", null), ("3.178.1", null));
        Assert.Equal((1L, CitationMatch.Paragraph), Find(deOratore, "3.45"));
        Assert.Null(Find(deOratore, "3.45", mayStopAtChapter: true));
        Assert.Equal((2L, CitationMatch.ContinuousSection), Find(deOratore, "3.45.178"));

        Assert.True(CitationConventions.MayStopAtChapter("phi0474.phi037", new[] { "3", "45" }));
        Assert.False(CitationConventions.MayStopAtChapter("phi0474.phi037", new[] { "3", "45", "178" }));
        Assert.False(CitationConventions.MayStopAtChapter("phi0474.phi057", new[] { "7", "13" }), "Cicero's letters are cited by letter");
        Assert.False(CitationConventions.SectionsRunThrough("phi0631.phi002"), "nor are Sallust's sections continuous");
    }

    [Fact]
    public void An_edition_with_a_level_above_the_dictionary_s_is_searched_one_level_down_first()
    {
        // Juvenal: books above the satires, which run straight through them.
        var juvenal = Index("1.1.170", "1.3.7", "3.7.1", "3.8.1");
        Assert.True(juvenal.HasLevelAbove);
        Assert.Equal((2L, CitationMatch.Suffix), Find(juvenal, "3.7"));
        Assert.Equal((1L, CitationMatch.Suffix), Find(juvenal, "1.170"));

        // Livy in a volume edition with an index of sigla numbered under one volume.
        var livy = Index("2.head", "2.24", "1.2.24.1.1", "1.2.24.2.1", "2.6.1.1.1", "2.7.1.1.1");
        Assert.True(livy.HasLevelAbove);
        Assert.Equal((3L, CitationMatch.Section), Find(livy, "2.24"));

        // One chapter divided further than the rest is not a level above them.
        var protrepticus = Index("20.1", "21.praef.1", "21.19.1", "21.21.1");
        Assert.False(protrepticus.HasLevelAbove);
        Assert.Equal((2L, CitationMatch.Section), Find(protrepticus, "21"));

        // Chapters that restart in every book are not one either.
        Assert.False(Index("1.1.1.1", "1.2.1.1", "2.1.1.1", "2.2.1.1").HasLevelAbove);
    }

    [Fact]
    public void One_level_down_is_tried_last_and_only_beneath_a_numbered_level()
    {
        // Aeschylus with the lyric passages a level down.
        Assert.Equal((2L, CitationMatch.Suffix), Find(Index("760", "3.761", "762"), "761"));

        // An appendix of parallels, not a part of the text.
        Assert.Null(Find(Index("frag128.1", "similes.5"), "5"));

        // Two passages fit: neither is guessed at.
        Assert.Null(Find(Index("1.5", "2.5"), "5"));
    }

    private const string Entry = @"<entryFree key=""mh=nis"" type=""main""><orth>mh=nis</orth>, <gen>h(</gen>,
        <sense n=""A"" level=""1""><tr>wrath</tr>, <cit><quote lang=""greek"">mh=nin a)/eide qea/</quote>
            <bibl n=""urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:1.1""><author>Il.</author> <biblScope>1.1</biblScope></bibl></cit>;
            <bibl n=""urn:cts:greekLit:tlg0012.tlg001.perseus-grc1:3""><biblScope>3</biblScope></bibl>;</sense>
        <sense n=""II"" level=""2"">of the gods, <bibl n=""urn:cts:greekLit:tlg0059.tlg030.perseus-grc1:390e""><author>Pl.</author> <title>R.</title> 390e</bibl>;
            <bibl><author>Hsch.</author></bibl></sense></entryFree>";

    [Fact]
    public void An_entry_is_read_with_its_citations_marked_and_listed_by_sense()
    {
        var (text, citations) = LexiconIngestService.FlattenEntry(XElement.Parse(Entry), "grc");

        var shown = LexiconEntryText.ForReading(text, "grc");
        Assert.Contains("μῆνιν ἄειδε θεά Il. 1.1", shown);
        Assert.Contains("Pl. R. 390e", shown);
        Assert.Contains("Hsch.", shown);
        Assert.DoesNotContain("urn:cts", shown);

        Assert.Equal(new[] { "Il. 1.1", "3", "Pl. R. 390e", "Hsch." }, LexiconCitationMarkup.Citations(text).Select(c => c.Label));

        // Hesychius names no passage, so three of the four are kept for the table.
        Assert.Equal(new[] { 0, 1, 2 }, citations.Select(c => c.Ordinal));
        Assert.Equal(new[] { "1.1", "1.3", "390e" }, citations.Select(c => c.Target.PassageRef));
        Assert.Equal(new[] { "A", "A", "A.II" }, citations.Select(c => c.Sense));
    }

    [Collection("Database")]
    public class InTheLibrary
    {
        private static Definition Definition(string headword, string source, string language, params LexiconCitation[] citations) =>
            new()
            {
                Headword = headword,
                NormalizedHeadword = WordNormalizer.NormalizeHeadword(headword, language),
                Language = language,
                Entry = headword,
                Source = source,
                Citations = citations
            };

        private static LexiconCitation Citation(int ordinal, string urn, string? sense = null) => new(ordinal, Target(urn), sense);

        [Fact]
        public async Task Citations_are_stored_against_the_entry_they_are_in_and_cleared_with_it()
        {
            using var db = await TempDatabase.CreateAsync();
            var repo = new DefinitionRepository();

            var first = new[]
            {
                Definition("μῆνις", "LSJ", "grc", Citation(0, Greek + "tlg0012.tlg001.perseus-grc1:1.1", "A"), Citation(2, Greek + "tlg0059.tlg030:390e", "A.II")),
                Definition("μῆνιν", "LSJ", "grc")
            };
            await repo.BulkInsertAsync(first);
            var second = new[] { Definition("cano", "Lewis & Short", "lat", Citation(0, "urn:cts:latinLit:phi0690.phi003.perseus-lat1:1.1")) };
            await repo.BulkInsertAsync(second);

            Assert.Equal(new long[] { 1, 2 }, first.Select(d => d.DefinitionId));
            Assert.Equal(3, second[0].DefinitionId);
            Assert.Equal(3, await db.CountAsync("LexiconCitations"));
            Assert.Equal("tlg0012.tlg001|tlg0012.tlg001.perseus-grc1|1.1|A|1|0", await db.ScalarStringAsync(
                "SELECT WorkUrn || '|' || EditionUrn || '|' || Passage || '|' || Sense || '|' || DefinitionId || '|' || Ordinal FROM LexiconCitations ORDER BY CitationId LIMIT 1;"));
            Assert.Equal(1, await db.ScalarAsync<long>("SELECT COUNT(*) FROM LexiconCitations WHERE EditionUrn IS NULL AND Passage = '390e';"));

            await repo.ClearBySourceAsync("LSJ");
            Assert.Equal(1, await db.CountAsync("LexiconCitations"));

            await repo.ClearAsync();
            Assert.Equal(0, await db.CountAsync("LexiconCitations"));
        }

        [Fact]
        public async Task Migration_42_adds_the_citations_table_to_an_existing_library()
        {
            using var db = await TempDatabase.CreateAsync();
            await db.ExecuteAsync("DROP TABLE LexiconCitations; PRAGMA user_version = 41;");

            await SchemaInitializer.EnsureSchemaAsync();

            Assert.Equal(SchemaInitializer.TargetSchemaVersion, await db.ScalarAsync<int>("PRAGMA user_version;"));
            Assert.True(await db.TableExistsAsync("LexiconCitations"));
            Assert.Equal(new[] { "CitationId", "DefinitionId", "Ordinal", "WorkUrn", "EditionUrn", "Passage", "Sense" },
                await db.ColumnNamesAsync("LexiconCitations"));
        }

        /// <summary>The Iliad in a Perseus and a First1K edition and an English translation, lines 1.1 to 1.3.</summary>
        private static async Task<Dictionary<string, int>> IliadAsync(TempDatabase db)
        {
            await db.ExecuteAsync(@"
                INSERT INTO Authors (AuthorId, CtsUrn, Name, Namespace) VALUES (1, 'urn:cts:greekLit:tlg0012', 'Homer', 'greekLit');
                INSERT INTO Works (WorkId, AuthorId, CtsUrn, Title) VALUES (1, 1, 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad');
                INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language, Collection) VALUES
                    (1, 1, 'tlg0012.tlg001.perseus-grc2', 'Original', 'grc', 'perseus-greek'),
                    (2, 1, 'tlg0012.tlg001.1st1K-grc1', 'Original', 'grc', 'first1k-greek'),
                    (3, 1, 'tlg0012.tlg001.perseus-eng3', 'Translation', 'eng', 'perseus-greek');");

            var order = 0;
            foreach (var (edition, lines) in new[] { (1, 3), (2, 2), (3, 4) })
            for (var line = 1; line <= lines; line++)
                await db.ExecuteAsync($"INSERT INTO TextNodes (EditionId, CitationRef, SortOrder, Text) VALUES ({edition}, '1.{line}', {order++}, 'line {line} of edition {edition}');");

            return new Dictionary<string, int> { ["perseus"] = 1, ["first1k"] = 2, ["english"] = 3 };
        }

        [Fact]
        public async Task A_citation_opens_the_edition_the_reader_would_and_never_a_translation()
        {
            using var db = await TempDatabase.CreateAsync();
            await IliadAsync(db);

            // By identifier, First1K's comes first; the reader's own order is the caller's to give.
            var byIdentifier = new LexiconCitationResolver();
            var line2 = Target(Greek + "tlg0012.tlg001.perseus-grc1:1.2");
            var opened = (await byIdentifier.ResolveAsync(new[] { line2 }))[0]!;
            Assert.Equal(2, opened.Edition.EditionId);
            Assert.Equal("Homer", opened.AuthorName);
            Assert.Equal("Iliad", opened.WorkTitle);
            Assert.Equal(CitationMatch.Exact, opened.How);

            var perseusFirst = new LexiconCitationResolver(e => e.OrderByDescending(x => x.Collection == "perseus-greek").ToList());
            Assert.Equal(1, (await perseusFirst.ResolveAsync(new[] { line2 }))[0]!.Edition.EditionId);

            // Line 3 is only in the Perseus edition, so it is found there whatever comes first.
            Assert.Equal("line 3 of edition 1", (await byIdentifier.ResolveAsync(new[] { Target(Greek + "tlg0012.tlg001:1.3") }))[0]!.Text);

            // Line 4 is only in the translation: a dictionary cites the Greek, so nothing opens.
            var results = await byIdentifier.ResolveAsync(new LexiconCitationTarget?[] { Target(Greek + "tlg0012.tlg001:1.4"), null, Target(Greek + "tlg9999.tlg001:1") });
            Assert.Equal(new CitedPassage?[] { null, null, null }, results);
        }

        [Fact]
        public async Task The_edition_the_dictionary_names_is_tried_after_the_reader_s_own()
        {
            using var db = await TempDatabase.CreateAsync();
            await IliadAsync(db);
            await db.ExecuteAsync("INSERT INTO Editions (EditionId, WorkId, CtsUrn, Kind, Language) VALUES (4, 1, 'tlg0012.tlg001.perseus-grc1', 'Original', 'grc');");

            var resolver = new LexiconCitationResolver(e => e.OrderBy(x => x.EditionId).ToList());
            await using var conn = await DbConnectionFactory.OpenConnectionAsync();
            var order = await resolver.CandidatesAsync(conn, Target(Greek + "tlg0012.tlg001.perseus-grc1:1.1"), default);
            Assert.Equal(new[] { 1, 4, 2 }, order.Select(e => e.EditionId));

            order = await resolver.CandidatesAsync(conn, Target(Greek + "tlg0012.tlg001:1.1"), default);
            Assert.Equal(new[] { 1, 2, 4 }, order.Select(e => e.EditionId));
        }

        [Fact]
        public async Task The_reader_finds_the_citations_of_an_edition_s_lines_but_not_of_a_translation_s()
        {
            using var db = await TempDatabase.CreateAsync();
            await IliadAsync(db);
            await new DefinitionRepository().BulkInsertAsync(new[]
            {
                Definition("μῆνις", "LSJ", "grc", Citation(0, Greek + "tlg0012.tlg001.perseus-grc1:1.1", "A"), Citation(1, Greek + "tlg0012.tlg001.perseus-grc1:1.3")),
                Definition("ἄειδω", "LSJ", "grc", Citation(0, Greek + "tlg0012.tlg001:1.1"))
            });

            var editions = await new EditionRepository().GetByWorkAsync(1);
            var repo = new LexiconCitationRepository();
            async Task<IReadOnlyDictionary<long, IReadOnlyList<long>>> For(int editionId)
            {
                var nodes = await new TextNodeRepository().GetByEditionAsync(editionId);
                return await repo.CitationsByPassageAsync(editions.Single(e => e.EditionId == editionId), nodes.Select(n => (n.TextNodeId, n.CitationRef, n.Milestone)));
            }

            var perseus = await For(1);
            Assert.Equal(2, perseus.Count);
            Assert.Equal(2, perseus[await db.TextNodeIdAsync(1, "1.1")].Count);
            Assert.Single(perseus[await db.TextNodeIdAsync(1, "1.3")]);

            // First1K has no line 3; the translation is not cited at all.
            Assert.Single(await For(2));
            Assert.Empty(await For(3));

            var entries = await repo.GetCitingEntriesAsync(perseus[await db.TextNodeIdAsync(1, "1.1")]);
            Assert.Equal(new[] { "μῆνις", "ἄειδω" }, entries.OrderBy(e => e.DefinitionId).Select(e => e.Headword));
            Assert.Equal("A", entries.Single(e => e.Headword == "μῆνις").Sense);
            Assert.True(await repo.AnyAsync());
        }

        [Fact]
        public async Task The_dictionary_step_reads_a_lexicon_file_with_its_citations()
        {
            using var db = await TempDatabase.CreateAsync();
            var folder = Path.Combine(Path.GetTempPath(), "classicacodex-lexicon-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                await File.WriteAllTextAsync(Path.Combine(folder, "grc.lsj.perseus-eng1.xml"),
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE TEI.2 PUBLIC \"-//TEI P4//DTD Main Document Type//EN\" \"tei2.dtd\" [\n<!ENTITY mdash \"&#x2014;\">\n]>\n" +
                    "<TEI.2><text><body><div0>" + Entry.Replace("of the gods,", "of the gods &mdash;") + "</div0></body></text></TEI.2>");

                await new LexiconIngestService().IngestAsync(folder, "grc", "LSJ");

                var entries = await new DefinitionRepository().GetByHeadwordAsync("μῆνις", "grc");
                var (headword, entry, source) = Assert.Single(entries);
                Assert.Equal("LSJ", source);
                Assert.Contains("of the gods — Pl. R. 390e", LexiconEntryText.ForReading(entry, "grc"));
                Assert.Equal(3, await db.CountAsync("LexiconCitations"));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
