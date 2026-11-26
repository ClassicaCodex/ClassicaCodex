using ClassicaCodex.Core;
using ClassicaCodex.Core.Quotations;
using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The shipped quotation links - who quotes what - and the pieces that turn
/// them into the reader's “ mark, its link back and the Quoted By window:
/// the table, its index, the word-by-word comparison and the lookup in a
/// library.
/// </summary>
[Collection("Database")]
public class QuotationLinksTests
{
    private static QuotationLink Link(
        string quotingUrn, string quotingRef, string quotingText, string quoted,
        string sourceUrn, string sourceRef, string sourceText, string matched,
        QuotationFinder finder = QuotationFinder.Testimonia, double score = 3)
    {
        var q = quotingText.IndexOf(quoted, StringComparison.Ordinal);
        var s = sourceText.IndexOf(matched, StringComparison.Ordinal);
        Assert.True(q >= 0 && s >= 0);
        return new QuotationLink(
            new PassageKey(quotingUrn, quotingRef), TextFingerprint.Of(quotingText), q, q + quoted.Length,
            new PassageKey(sourceUrn, sourceRef), TextFingerprint.Of(sourceText), s, s + matched.Length,
            finder, finder == QuotationFinder.Testimonia ? "text" : "both", score, string.Empty);
    }

    // ------------------------------------------------------------ the shipped table

    /// <summary>
    /// The table is embedded and reads whole: both searches are in it, and a
    /// line every reader of the Fathers knows is quoted by both. A table that
    /// failed to embed would load as no links at all, quietly - this is what
    /// turns that into a failed build.
    /// </summary>
    [Fact]
    public void TheShippedTableHoldsBothSearches()
    {
        var shipped = QuotationIndex.Shipped;

        Assert.True(shipped.Count > 20_000, $"{shipped.Count} links shipped");
        Assert.Contains(shipped.Links, l => l.Finder == QuotationFinder.Testimonia);
        Assert.Contains(shipped.Links, l => l.Finder == QuotationFinder.Afterlives);

        // Terence, Andria 68: obsequium amicos, veritas odium parit.
        var andria = shipped.QuotersOf("phi0134.phi001.perseus-lat2", "1.1.68");
        Assert.True(andria.Count >= 10, $"Andria 68 has {andria.Count} quoters");
        Assert.Contains(andria, l => l.Finder == QuotationFinder.Testimonia);
        Assert.Contains(andria, l => l.Finder == QuotationFinder.Afterlives);
        Assert.Contains("1.1.68", shipped.QuotedReferencesIn("phi0134.phi001.perseus-lat2"));
    }

    /// <summary>
    /// Afterlives is shipped at its high level only, and with its flags; a
    /// low row - right about a third of the time - must never become a mark.
    /// Every position fits inside a sane passage.
    /// </summary>
    [Fact]
    public void TheShippedTableIsWellFormed()
    {
        foreach (var link in QuotationIndex.Shipped.Links)
        {
            Assert.True(link.QuotingStart >= 0 && link.QuotingStart < link.QuotingEnd);
            Assert.True(link.SourceStart >= 0 && link.SourceStart < link.SourceEnd);
            Assert.Equal(8, link.QuotingFingerprint.Length);
            Assert.Equal(8, link.SourceFingerprint.Length);
            Assert.Contains(link.Kind, link.Finder == QuotationFinder.Testimonia ? new[] { "text", "fragment" } : new[] { "main", "short", "both" });
            Assert.Contains(link.Flag, new[] { string.Empty, "cento", "early-modern" });
        }
    }

    // ------------------------------------------------------------ the file

    [Fact]
    public void TheTableRoundTripsThroughItsFile()
    {
        var links = new[]
        {
            Link("tlg0007.tlg007.perseus-grc2", "urn:cts:greekLit:tlg0007.tlg007.perseus-grc2.10.1", "Σόλων δέ φησιν Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας", "Αἴας δ᾽ ἐκ Σαλαμῖνος",
                 "tlg0012.tlg001.perseus-grc2", "urn:cts:greekLit:tlg0012.tlg001.perseus-grc2.2.557", "Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας", "Αἴας δ᾽ ἐκ Σαλαμῖνος"),
            Link("stoa0040.stoa003.opp-lat3", "~odd", "spiritus intus alit", "spiritus intus alit",
                 "phi0690.phi003.perseus-lat2", "6.726", "principio caelum ac terras camposque liquentis", "principio caelum",
                 QuotationFinder.Afterlives, 55.5) with { Flag = "cento" }
        };
        var prefixes = new Dictionary<string, string>
        {
            ["tlg0007.tlg007.perseus-grc2"] = "urn:cts:greekLit:tlg0007.tlg007.perseus-grc2.",
            ["tlg0012.tlg001.perseus-grc2"] = "urn:cts:greekLit:tlg0012.tlg001.perseus-grc2."
        };

        var buffer = new MemoryStream();
        QuotationLinkFile.WriteCompressed(buffer, links, prefixes);
        buffer.Position = 0;
        var read = QuotationLinkFile.ReadCompressed(buffer);

        Assert.Equal(links, read);

        // The prefix is written once, and the reference that starts with "~"
        // is escaped rather than taken for a shortened one.
        var plain = new StringWriter();
        QuotationLinkFile.Write(plain, links, prefixes);
        Assert.Contains("\t~10.1\t", plain.ToString());
        Assert.Contains("\t=~odd\t", plain.ToString());
    }

    [Fact]
    public void AFileThatIsNotATableIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => QuotationLinkFile.Read(new StringReader("hello\n")));
        Assert.Throws<InvalidDataException>(() => QuotationLinkFile.Read(new StringReader("quotation-links\t2\n")));
        Assert.Throws<InvalidDataException>(() => QuotationLinkFile.Read(new StringReader("quotation-links\t1\nP\t0\t1.1\tabcdef01\n")));
    }

    [Fact]
    public void AFingerprintIsFnv1aOverTheExactText()
    {
        Assert.Equal("811c9dc5", TextFingerprint.Of(string.Empty));
        Assert.NotEqual(TextFingerprint.Of("arma uirumque"), TextFingerprint.Of("arma uirumque "));
    }

    // ------------------------------------------------------------ the index

    /// <summary>
    /// The library writes "tlg0012.tlg001.perseus-grc2"; the CTS standard and
    /// some libraries write the whole URN. Both are the same edition.
    /// </summary>
    [Fact]
    public void TheIndexFindsAnEditionWithOrWithoutItsUrnHead()
    {
        var link = Link("tlg0007.tlg007.perseus-grc2", "10.1", "Αἴας δ᾽ ἐκ Σαλαμῖνος", "Αἴας",
                        "tlg0012.tlg001.perseus-grc2", "2.557", "Αἴας δ᾽ ἐκ Σαλαμῖνος", "Αἴας");
        var index = new QuotationIndex(new[] { link });

        Assert.Single(index.QuotersOf("urn:cts:greekLit:tlg0012.tlg001.perseus-grc2", "2.557"));
        Assert.Single(index.QuotersOf("tlg0012.tlg001.perseus-grc2", "2.557"));
        Assert.Empty(index.QuotersOf("tlg0012.tlg001.perseus-grc2", "2.558"));
        Assert.Single(index.QuotedBy("urn:cts:greekLit:tlg0007.tlg007.perseus-grc2", "10.1"));
        Assert.Equal(new[] { "2.557" }, index.QuotedReferencesIn("tlg0012.tlg001.perseus-grc2"));
        Assert.Single(index.QuotationsIn("tlg0007.tlg007.perseus-grc2"));
        Assert.Empty(index.QuotationsIn("tlg0012.tlg001.perseus-grc2"));
    }

    [Fact]
    public void ATestCanPutItsOwnIndexInPlaceOfTheShippedOne()
    {
        var mine = new QuotationIndex(Array.Empty<QuotationLink>());
        using (QuotationIndex.UseInstead(mine))
            Assert.Same(mine, QuotationIndex.Current);

        Assert.Same(QuotationIndex.Shipped, QuotationIndex.Current);
    }

    // ------------------------------------------------------------ the wording

    /// <summary>
    /// First1KGreek prints the lunate sigma and Perseus the ordinary one, and
    /// the two houses accent and capitalise differently: none of that is the
    /// quoter changing Homer's words.
    /// </summary>
    [Fact]
    public void HousesConventionsAreNotDifferences()
    {
        var greek = QuotationWording.Compare("Ζεὺϲ γὰρ ἐϲ ὠκεανὸν μετ᾿ ἀμύμοναϲ Αἰθιοπῆαϲ", "Ζεὺς γὰρ ἐς Ὠκεανὸν μετʼ ἀμύμονας Αἰθιοπῆας", "grc");
        Assert.True(greek.SameWords);

        var latin = QuotationWording.Compare("Vt ait poeta, Arma virumque cano", "arma uirumque cano", "lat");
        Assert.Equal(3, latin.DifferingQuotingWords);
        Assert.Equal(0, latin.DifferingSourceWords);
    }

    /// <summary>
    /// A changed word is marked on both sides, an added one on the quoter's
    /// only, and the rest stay in step - which is what the alignment is for.
    /// </summary>
    [Fact]
    public void ChangedAndAddedWordsAreMarked()
    {
        var wording = QuotationWording.Compare(
            "et quoniam tanta sacrilegia a Patre Palladii nunc et semper ingeruntur",
            "et quoniam tanta sacrilegia a parte Palladii ingeruntur", "lat");

        Assert.Equal(4, wording.DifferingQuotingWords); // Patre, nunc, et, semper
        Assert.Equal(1, wording.DifferingSourceWords);  // parte
        Assert.Contains(wording.Source, p => p.Differs && p.Text == "parte");
        Assert.Contains(wording.Quoting, p => p.Differs && p.Text == "nunc et semper");
        Assert.Equal("et quoniam tanta sacrilegia a Patre Palladii nunc et semper ingeruntur", string.Concat(wording.Quoting.Select(p => p.Text)));
    }

    /// <summary>
    /// A passage whose text has changed since its link was made: the
    /// quotation is found again from its words, and a passage that does not
    /// hold enough of them is not given a place it does not have.
    /// </summary>
    [Fact]
    public void AQuotationIsFoundAgainInAChangedText()
    {
        const string text = "Unde et poeta ait: obsequium amicos, ueritas odium parit. Quod et apostolus dicit.";
        var place = QuotationWording.Locate(text, "obsequium amicos, veritas odium parit", "lat");

        Assert.NotNull(place);
        Assert.Equal("obsequium amicos, ueritas odium parit", text[place.Value.Start..place.Value.End]);
        Assert.Null(QuotationWording.Locate(text, "arma uirumque cano Troiae qui primus", "lat"));
    }

    [Fact]
    public void ContextIsCutWithAnEllipsis()
    {
        const string text = "one two three four five QUOTED six seven eight nine ten";
        var start = text.IndexOf("QUOTED", StringComparison.Ordinal);
        var (before, after) = QuotationWording.Context(text, start, start + 6, words: 2);

        Assert.Equal("… four five ", before);
        Assert.Equal(" six seven …", after);
    }

    // ------------------------------------------------------------ the marks

    [Fact]
    public void TheQuotedByMarkComesLast()
    {
        Assert.Equal("   ? # ★ “", PassageMarkSymbols.Suffix(PassageMarks.Inquiry | PassageMarks.Tag | PassageMarks.Bookmark | PassageMarks.QuotedBy));
        Assert.Equal("   “", PassageMarkSymbols.Suffix(PassageMarks.QuotedBy));
    }

    // ------------------------------------------------------------ the library

    /// <summary>
    /// A link names its passages by edition URN and reference, and an edition
    /// holding one reference twice is settled by the fingerprint of the text
    /// the link was found in.
    /// </summary>
    [Fact]
    public async Task APassageIsFoundByItsUrnAndReference()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Authors (CtsUrn, Name, Namespace, Language) VALUES ('urn:cts:greekLit:tlg0012', 'Homer', 'greekLit', 'grc');
            INSERT INTO Works (AuthorId, CtsUrn, Title) VALUES ((SELECT AuthorId FROM Authors), 'urn:cts:greekLit:tlg0012.tlg001', 'Iliad');
            INSERT INTO Editions (WorkId, CtsUrn, Kind, Language) VALUES ((SELECT WorkId FROM Works), 'urn:cts:greekLit:tlg0012.tlg001.perseus-grc2', 'Original', 'grc');");
        var edition = await db.ScalarAsync<int>("SELECT EditionId FROM Editions;");
        await db.InsertLinesAsync(edition, ("2.557", "Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας"), ("2.557", "στῆσε δ᾽ ἄγων"));

        var repository = new QuotationRepository();
        var found = await repository.GetEditionAsync("tlg0012.tlg001.perseus-grc2");
        Assert.NotNull(found);
        Assert.Equal("Homer", found.AuthorName);
        Assert.Equal("Iliad", found.WorkTitle);
        Assert.Null(await repository.GetEditionAsync("tlg0012.tlg002.perseus-grc2"));

        var second = await repository.GetPassageAsync(new PassageKey("tlg0012.tlg001.perseus-grc2", "2.557"), TextFingerprint.Of("στῆσε δ᾽ ἄγων"));
        Assert.Equal("στῆσε δ᾽ ἄγων", second!.Text);
        Assert.True(second.FingerprintMatches);

        var changed = await repository.GetPassageAsync(new PassageKey("tlg0012.tlg001.perseus-grc2", "2.557"), "00000000");
        Assert.Equal("Αἴας δ᾽ ἐκ Σαλαμῖνος ἄγεν δυοκαίδεκα νῆας", changed!.Text);
        Assert.False(changed.FingerprintMatches);

        Assert.Null(await repository.GetPassageAsync(new PassageKey("tlg0012.tlg001.perseus-grc2", "2.558"), "00000000"));
    }
}
