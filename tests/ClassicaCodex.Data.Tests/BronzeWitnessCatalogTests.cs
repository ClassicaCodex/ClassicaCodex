using ClassicaCodex.Core;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The bestiary's catalogue of ancient witnesses - the passage each creature
/// actually comes from, offered to a player who has defeated one.
///
/// The lookup that resolves these against a library is covered by
/// <see cref="BronzeWitnessTests"/>, which seeds a database and reads them
/// back. What that cannot tell anyone is whether the table it reads is any
/// good: a creature left out of it, or a citation with a typo, resolves to
/// nothing and is discovered by a player rather than by a test.
///
/// This is the optional reading rather than a quest gate, so nothing here can
/// strand a run. It can only offer somebody an empty page.
/// </summary>
public class BronzeWitnessCatalogTests
{
    /// <summary>
    /// A bestiary entry with nothing to read is a page that exists to say it
    /// is empty. Every creature the arena can spawn needs something.
    /// </summary>
    [Fact]
    public void EveryCreatureHasAWitness()
    {
        var covered = BronzeWitnesses.All.Select(w => w.Creature).Distinct().ToHashSet();

        Assert.All(Enum.GetValues<BronzeEnemyKind>(), kind =>
            Assert.True(covered.Contains(kind), $"{kind} has no ancient witness"));
    }

    [Fact]
    public void EveryWitnessIsCompletelyFilledIn() =>
        Assert.All(BronzeWitnesses.All, w =>
        {
            Assert.False(string.IsNullOrWhiteSpace(w.Title), $"{w.Creature}: no title");
            Assert.False(string.IsNullOrWhiteSpace(w.Note), $"{w.Creature}: no note");
            Assert.False(string.IsNullOrWhiteSpace(w.AuthorKey), $"{w.Creature}: no author");
            Assert.NotEmpty(w.TitleKeys);
            Assert.All(w.TitleKeys, k => Assert.False(string.IsNullOrWhiteSpace(k)));
            Assert.False(string.IsNullOrWhiteSpace(w.Citation), $"{w.Creature} {w.Title}: no citation");
        });

    /// <summary>
    /// A citation has to be the reference alone. Passing the stored form -
    /// which in this corpus usually carries the whole CTS URN - would never
    /// match, because the lookup compares against PassageCitation.Display.
    /// </summary>
    [Fact]
    public void NoCitationCarriesAUrn() =>
        Assert.All(BronzeWitnesses.All, w =>
            Assert.DoesNotContain("urn:", w.Citation, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The Cyclops has two witnesses on purpose - Homer's cave-dweller and
    /// Hesiod's thunderbolt-smiths, which are not the same creature - and that
    /// only teaches anything if they are genuinely two places to read.
    /// </summary>
    [Fact]
    public void NoCreatureOffersTheSamePassageTwice() =>
        Assert.All(BronzeWitnesses.All.GroupBy(w => w.Creature), group =>
        {
            var places = group.Select(w => $"{w.AuthorKey}|{w.TitleKeys[0]}|{w.Citation}").ToList();
            Assert.Equal(places.Count, places.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });

    /// <summary>The titles are what a player picks between, so two cannot read alike.</summary>
    [Fact]
    public void TitlesWithinACreatureAreDistinct() =>
        Assert.All(BronzeWitnesses.All.GroupBy(w => w.Creature), group =>
        {
            var titles = group.Select(w => w.Title).ToList();
            Assert.Equal(titles.Count, titles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });

    /// <summary>
    /// The note is the reason to go and read it. One that restates the title
    /// tells a player nothing they had not already read on the button.
    /// </summary>
    [Fact]
    public void EveryNoteSaysMoreThanItsTitle() =>
        Assert.All(BronzeWitnesses.All, w =>
        {
            Assert.True(w.Note.Length > w.Title.Length, $"{w.Creature} {w.Title}: the note adds nothing");
            Assert.NotEqual(w.Title, w.Note, StringComparer.OrdinalIgnoreCase);
        });

    /// <summary>
    /// A section citation is a prose address - Apollodorus 2.5.2 - which the
    /// lookup widens to that section's numbered children. Marking a bare line
    /// number as a section would widen it across a range no author wrote; see
    /// the section test in BronzeWitnessTests for what that widening does.
    /// </summary>
    [Fact]
    public void OnlyDividedCitationsAreMarkedAsSections() =>
        Assert.All(BronzeWitnesses.All.Where(w => w.Section), w =>
            Assert.Contains('.', w.Citation));
}
