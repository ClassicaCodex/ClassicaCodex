using ClassicaCodex.Data.Repositories;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Which headword a reader is shown when they click a word.
///
/// Word Study selects the first headword automatically, so whichever leads is
/// the one they actually see. Alphabetical order put the wrong one there almost
/// every time: the Latin lemma data carries capitalised headwords that nothing
/// in Lewis and Short answers to - only 43,507 of its 139,190 headwords have an
/// entry behind them at all - and a capital sorts before a lowercase letter.
///
/// So clicking "regere" led with Reger and showed "no dictionary entry found",
/// with rego and its Lewis and Short entry sitting unnoticed on the next line.
/// Likewise amare with Amar over amo, and ferre with Ferres over fero. On the
/// feature this application is most for, at the first click.
/// </summary>
[Collection("Database")]
public class HeadwordOrderTests
{
    /// <summary>
    /// Two lemma rows for one form, only one of which the dictionary answers
    /// for - which is the shape the real data keeps producing.
    /// </summary>
    private static async Task SeedAsync(TempDatabase db)
    {
        await db.ExecuteAsync(@"
            INSERT INTO Lemmas (Language, Form, NormalizedForm, Headword, PartOfSpeech)
            VALUES ('lat', 'regere', 'regere', 'Reger', 'v'),
                   ('lat', 'regere', 'regere', 'rego',  'v');

            INSERT INTO Definitions (Language, Headword, NormalizedHeadword, Entry, Source)
            VALUES ('lat', 'rego', 'rego', 'to keep straight, guide, rule', 'Lewis & Short');");
    }

    [Fact]
    public async Task TheHeadwordTheDictionaryAnswersForComesFirst()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db);

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("regere", "lat");

        Assert.Equal("rego", heads[0].Headword);
        Assert.Equal(new[] { "rego", "Reger" }, heads.Select(h => h.Headword));
    }

    /// <summary>
    /// The whole point: the reader is shown a definition rather than an
    /// apology, without having to notice a second line and click it.
    /// </summary>
    [Fact]
    public async Task TheFirstHeadwordResolvesToAnEntry()
    {
        using var db = await TempDatabase.CreateAsync();
        await SeedAsync(db);

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("regere", "lat");
        var entries = await new DefinitionRepository().GetByHeadwordAsync(heads[0].Headword, "lat");

        Assert.NotEmpty(entries);
    }

    /// <summary>
    /// Latin normalising folds v to u and j to i, and trailing digits come off
    /// a numbered headword. The promotion has to use the same rule the
    /// definition lookup does, or it will promote the wrong one - which is why
    /// it calls WordNormalizer rather than reimplementing the fold in SQL.
    /// </summary>
    [Fact]
    public async Task PromotionUsesTheSameNormalisingAsTheLookup()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Lemmas (Language, Form, NormalizedForm, Headword, PartOfSpeech)
            VALUES ('lat', 'iuuenis', 'iuuenis', 'Aardvark', 'n'),
                   ('lat', 'iuuenis', 'iuuenis', 'juvenis2', 'n');

            INSERT INTO Definitions (Language, Headword, NormalizedHeadword, Entry, Source)
            VALUES ('lat', 'juvenis', 'iuuenis', 'young', 'Lewis & Short');");

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("iuuenis", "lat");

        Assert.Equal("juvenis2", heads[0].Headword);
    }

    /// <summary>
    /// Alphabetical order still decides between headwords the dictionary
    /// answers for equally - the promotion reorders the groups, not the
    /// contents of either.
    /// </summary>
    [Fact]
    public async Task OrderWithinEachGroupIsUnchanged()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Lemmas (Language, Form, NormalizedForm, Headword, PartOfSpeech)
            VALUES ('lat', 'x', 'x', 'aaa', 'n'), ('lat', 'x', 'x', 'bbb', 'n'),
                   ('lat', 'x', 'x', 'yyy', 'n'), ('lat', 'x', 'x', 'zzz', 'n');

            INSERT INTO Definitions (Language, Headword, NormalizedHeadword, Entry, Source)
            VALUES ('lat', 'bbb', 'bbb', 'b', 'L&S'), ('lat', 'zzz', 'zzz', 'z', 'L&S');");

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("x", "lat");

        Assert.Equal(new[] { "bbb", "zzz", "aaa", "yyy" }, heads.Select(h => h.Headword));
    }

    /// <summary>
    /// When every candidate is answerable, or none is, there is nothing to
    /// promote and the alphabetical order stands.
    /// </summary>
    [Fact]
    public async Task NothingMovesWhenThereIsNothingToPromote()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Lemmas (Language, Form, NormalizedForm, Headword, PartOfSpeech)
            VALUES ('lat', 'y', 'y', 'aaa', 'n'), ('lat', 'y', 'y', 'bbb', 'n');");

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("y", "lat");

        Assert.Equal(new[] { "aaa", "bbb" }, heads.Select(h => h.Headword));
    }

    /// <summary>
    /// A form with one candidate takes the cheap path and still comes back.
    /// </summary>
    [Fact]
    public async Task ASingleHeadwordIsReturnedUntouched()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Lemmas (Language, Form, NormalizedForm, Headword, PartOfSpeech)
            VALUES ('lat', 'uenit', 'uenit', 'uenio', 'v');");

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("uenit", "lat");

        Assert.Equal("uenio", Assert.Single(heads).Headword);
    }

    /// <summary>
    /// A language with no dictionary loaded at all must still list its
    /// headwords - Menota's Old Norse has lemma data and no lexicon.
    /// </summary>
    [Fact]
    public async Task HeadwordsStillListWhenNoDictionaryIsLoaded()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Lemmas (Language, Form, NormalizedForm, Headword, PartOfSpeech)
            VALUES ('non', 'konungr', 'konungr', 'konungr', 'n'),
                   ('non', 'konungr', 'konungr', 'Konung',  'n');");

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("konungr", "non");

        Assert.Equal(2, heads.Count);
    }

    /// <summary>
    /// The headword spelled exactly like the word clicked leads, even when
    /// another headword begins the same way and is therefore just as good a
    /// prefix match.
    ///
    /// Isolating this rule takes some care, and the first attempt at this
    /// test did not: alphabetical order already puts a word ahead of a longer
    /// word starting the same way, and so does the shared-opening rule, so
    /// with only those two seeded the test passed with this rule deleted.
    ///
    /// What it takes is the dictionary promotion pushing the exact spelling
    /// down - the one force above shared-opening. Only "amor" has an entry
    /// here, so promotion leads with it; both share the whole of "amo", so
    /// shared-opening ties; both are lower case, so capitalisation ties. The
    /// exact spelling is the only thing left that can bring "amo" back.
    /// </summary>
    [Fact]
    public async Task TheHeadwordSpelledLikeTheWordLeadsEvenOverOneTheDictionaryAnswersFor()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Lemmas (Language, Form, NormalizedForm, Headword, PartOfSpeech)
            VALUES ('lat', 'amo', 'amo', 'amor', 'n'),
                   ('lat', 'amo', 'amo', 'amo',  'v');

            INSERT INTO Definitions (Language, Headword, NormalizedHeadword, Entry, Source)
            VALUES ('lat', 'amor', 'amor', 'love, affection', 'Lewis & Short');");

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("amo", "lat");

        Assert.Equal("amo", heads[0].Headword);
    }

    /// <summary>
    /// A lower-case word does not lead with a capitalised headword.
    ///
    /// This is what put "Es" - a magistrate who superintended religious
    /// exhibitions - above sum1 for esse, "Bellius" above bellum for bello,
    /// and the place-name Πόλις above πόλις for πόλιν. A capital sorts before
    /// a lower-case letter, and whatever leads is what the reader is shown.
    ///
    /// Seeded so only the capitalisation rule can decide: neither headword is
    /// spelled like the form, and neither shares its opening character with
    /// it, so the exact and shared-opening rules both abstain.
    /// </summary>
    [Fact]
    public async Task ALowerCaseWordDoesNotLeadWithACapitalisedHeadword()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Lemmas (Language, Form, NormalizedForm, Headword, PartOfSpeech)
            VALUES ('lat', 'esse', 'esse', 'Es',   'n'),
                   ('lat', 'esse', 'esse', 'sum1', 'v');");

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("esse", "lat");

        Assert.Equal("sum1", heads[0].Headword);
    }

    /// <summary>
    /// Where neither headword is capitalised and neither is spelled like the
    /// form, the one that begins the same way leads.
    ///
    /// ἔχει is the case this exists for: its candidates are ἔχω and χέω, and
    /// ordered by headword the pouring one came first, so the commonest verb
    /// in Greek reported itself as "χέω, diffuse completely". Measured
    /// against the real library, this rule moved that one form and left the
    /// other thirty-two sampled forms exactly as they were.
    ///
    /// The other two rules are silent here by construction, which is what
    /// makes this test about this rule.
    /// </summary>
    [Fact]
    public async Task TheHeadwordThatBeginsLikeTheWordLeadsWhenNothingElseDecides()
    {
        using var db = await TempDatabase.CreateAsync();
        await db.ExecuteAsync(@"
            INSERT INTO Lemmas (Language, Form, NormalizedForm, Headword, PartOfSpeech)
            VALUES ('grc', 'ἔχει', 'εχει', 'χέω', 'v'),
                   ('grc', 'ἔχει', 'εχει', 'ἔχω', 'v');");

        var heads = await new LemmaRepository().GetHeadwordsForFormAsync("ἔχει", "grc");

        Assert.Equal("ἔχω", heads[0].Headword);
    }
}
