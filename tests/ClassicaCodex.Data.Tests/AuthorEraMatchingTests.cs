using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Which author a set of dates belongs to.
///
/// The era filter includes or excludes a work on the strength of this table,
/// and it says nothing on screen about how it decided - so a wrong date is
/// not a small error, it is a search that quietly returns the wrong century.
///
/// Matching was a bare substring test in both directions, and it produced
/// exactly the failures that shape suggests. Measured against a full library:
///
///   "Anonymous"            269,429 lines, dated 560-580 CE, because
///                          "Anonymous pilgrim of Piacenza" contains it
///   "Scholia in Homerum"    37,374 lines, dated to Homer himself
///   "Elias Neoplatonicus"      579 lines, dated to Plato - a sixth-century
///                          CE commentator sent back a thousand years by the
///                          letters inside "Neoplatonicus"
///   "Appendix Vergiliana"       dated to Vergil, though it is precisely the
///                          collection that is not by him
///
/// Together, more than 300,000 lines in the wrong century.
/// </summary>
public class AuthorEraMatchingTests
{
    private static bool IsDated(string author) => AuthorEraData.Lookup(author) != null;

    // ---- a work about an author is not that author -----------------------

    [Theory]
    [InlineData("Scholia in Homerum")]
    [InlineData("Scholia in Pindarum")]
    [InlineData("Scholia in Euclidem")]
    [InlineData("Scholia in Euripidem")]
    [InlineData("Vitae Homeri")]
    [InlineData("Vitae Aesopi")]
    [InlineData("Certamen Homeri et Hesiodi")]
    [InlineData("Anonymi Exegesis in Hesiodi Theogoniam")]
    [InlineData("Solonis Epistulae")]
    public void CommentaryAndBiographyDoNotInheritTheirSubjectsDates(string author) =>
        Assert.False(IsDated(author), $"\"{author}\" should not carry its subject's dates");

    /// <summary>
    /// The Appendix Vergiliana is the collection of poems attributed to Vergil
    /// and generally held not to be his, of varied and mostly later date.
    /// Stamping it 70-19 BCE asserts the one thing it is defined by not being.
    /// </summary>
    [Fact]
    public void TheAppendixVergilianaIsNotVergil() => Assert.False(IsDated("Appendix Vergiliana"));

    [Fact]
    public void APseudonymousAuthorIsNotTheAuthorTheyAreNamedFor() =>
        Assert.False(IsDated("Pseudo-Arrianus"));

    /// <summary>The one that shows what plain substring matching costs.</summary>
    [Fact]
    public void PlatoIsNotFoundInsideNeoplatonicus() =>
        Assert.False(IsDated("Elias Neoplatonicus"));

    // ---- a name that identifies nobody -----------------------------------

    [Theory]
    [InlineData("Anonymous")]
    [InlineData("Anonymus")]
    [InlineData("Incertus")]
    [InlineData("Auctores Varii")]
    public void AGenericAnonymTakesNobodysDates(string author) =>
        Assert.False(IsDated(author), $"\"{author}\" matched a real person");

    // ---- and everything that must still work -----------------------------

    [Theory]
    [InlineData("Homer", -750)]
    [InlineData("Plato", -428)]
    [InlineData("Pindar", -518)]
    [InlineData("Euclid", -325)]
    [InlineData("Aesop", -620)]
    public void ThePeopleThemselvesKeepTheirDates(string author, int expectedStart) =>
        Assert.Equal(expectedStart, AuthorEraData.Lookup(author)!.Value.StartYear);

    /// <summary>
    /// The catalog spells authors out; the table is often keyed on the short
    /// name. That has to keep working, and it is the reason matching is by
    /// whole word rather than by exact string.
    /// </summary>
    [Theory]
    [InlineData("P. Vergilius Maro (Virgil)")]
    [InlineData("Titus Livius (Livy)")]
    [InlineData("Julius Caesar")]
    [InlineData("Ambrose, Saint, Bishop of Milan")]
    [InlineData("Silius Italicus, Tiberius Catius")]
    [InlineData("Valerius Flaccus, Gaius")]
    public void AFullCatalogNameStillFindsItsAuthor(string author) =>
        Assert.True(IsDated(author), $"\"{author}\" lost its dates");

    /// <summary>
    /// And the other direction: the catalog carries a short form of a name
    /// the table spells out.
    /// </summary>
    [Theory]
    [InlineData("Prudentius")]
    [InlineData("Zonaras")]
    [InlineData("Arnobius")]
    [InlineData("Orosius")]
    [InlineData("Alcuin")]
    public void AShortNameStillFindsItsAuthor(string author) =>
        Assert.True(IsDated(author), $"\"{author}\" lost its dates");

    // ---- the table's own consistency -------------------------------------

    [Fact]
    public void NoKeyIsListedTwice()
    {
        var keys = AuthorEraData.Keys.ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void NoRangeRunsBackwards()
    {
        foreach (var key in AuthorEraData.Keys)
        {
            var span = AuthorEraData.Lookup(key);
            Assert.NotNull(span);
            Assert.True(span!.Value.StartYear <= span.Value.EndYear,
                $"\"{key}\" runs from {span.Value.StartYear} to {span.Value.EndYear}");
        }
    }

    /// <summary>
    /// Every key has to find itself. A key that its own lookup cannot reach
    /// is dead weight, and the guards above are exactly the kind of change
    /// that could create one.
    /// </summary>
    [Fact]
    public void EveryKeyFindsItself()
    {
        foreach (var key in AuthorEraData.Keys)
            Assert.True(AuthorEraData.Lookup(key) != null, $"\"{key}\" cannot find itself");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingUsableIsUndated(string author) => Assert.False(IsDated(author));
}
