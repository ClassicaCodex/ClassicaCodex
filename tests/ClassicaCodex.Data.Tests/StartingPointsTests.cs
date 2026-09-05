using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Which work the "Where should I start?" screen actually opens.
///
/// It exists so that someone who cannot yet read the script does not pick by
/// name recognition and land on choral lyric. Sending them somewhere wrong is
/// therefore worse here than anywhere else in the application: this is the one
/// audience with no way to tell that it happened.
///
/// It was sending them somewhere wrong. Suggestions match by author and title
/// text - deliberately, since the same work carries different URNs across the
/// corpora - and the matcher took the first author whose name contained the key
/// and stopped. So "caesar" found Pseudo-Caesar, whose De Bello Africo
/// satisfied the loose title key "bell", and the recommendation for the Gallic
/// War opened a spurious continuation by an unknown hand while Julius Caesar's
/// actual Gallic War sat in the library unlooked-at. "lucian" found
/// Pseudo-Lucian and offered the Amores as a first Greek reader.
/// </summary>
public class StartingPointsTests
{
    private static int _nextId = 1;

    /// <summary>Builds the two dictionaries AvailableIn takes.</summary>
    private sealed class Library
    {
        public Dictionary<int, List<Work>> WorksByAuthor { get; } = new();
        public Dictionary<int, string> AuthorNames { get; } = new();

        public Library With(string author, params string[] titles)
        {
            var id = _nextId++;
            AuthorNames[id] = author;
            WorksByAuthor[id] = titles
                .Select(t => new Work { WorkId = _nextId++, Title = t, CtsUrn = $"urn:cts:x:{t}" })
                .ToList();
            return this;
        }

        public string? Opens(string display)
        {
            var found = StartingPoints.AvailableIn(WorksByAuthor, AuthorNames);
            return found.FirstOrDefault(f => f.Suggestion.Display == display).Work?.Title;
        }
    }

    /// <summary>
    /// The exact shape the real library has: the genuine author and a
    /// pseudonymous one, both carrying the key.
    /// </summary>
    [Fact]
    public void TheGallicWarOpensTheGallicWar()
    {
        var library = new Library()
            .With("Pseudo-Caesar", "De Bello Africo", "De Bello Alexandrino")
            .With("Julius Caesar", "Civil War", "Gallic War");

        Assert.Equal("Gallic War", library.Opens("Caesar, Gallic War"));
    }

    /// <summary>
    /// And still does when the pseudonymous author is encountered second, so
    /// the result does not depend on dictionary ordering.
    /// </summary>
    [Fact]
    public void TheOrderTheAuthorsArriveInDoesNotDecideIt()
    {
        var library = new Library()
            .With("Julius Caesar", "Civil War", "Gallic War")
            .With("Pseudo-Caesar", "De Bello Africo");

        Assert.Equal("Gallic War", library.Opens("Caesar, Gallic War"));
    }

    [Fact]
    public void LucianOpensARealDialogueAndNotPseudoLucian()
    {
        var library = new Library()
            .With("Pseudo-Lucian", "Amores", "Asinus")
            .With("Lucian of Samosata", "Dialogi deorum", "Dialogi mortuorum");

        Assert.StartsWith("Dialogi", library.Opens("Lucian, Dialogues"));
    }

    /// <summary>
    /// "caesar" is the man in "Julius Caesar" and a syllable in "Caesarius
    /// Arelatensis Episcopus" and "Eusebius of Caesarea", both of which this
    /// library really does carry.
    /// </summary>
    [Fact]
    public void AWholeWordNameBeatsTheSameLettersInsideALongerOne()
    {
        var library = new Library()
            .With("Caesarius Arelatensis Episcopus", "De Gallico Sermone")
            .With("Julius Caesar", "Gallic War");

        Assert.Equal("Gallic War", library.Opens("Caesar, Gallic War"));
    }

    /// <summary>
    /// This test used to require the opposite, and was wrong to.
    ///
    /// The idea was that a reader with only part of a corpus installed should
    /// still get the recommendation if anyone could satisfy it. But "anyone"
    /// reached authors who merely share letters with the one meant: with the
    /// Gallic War absent, it walked past Julius Caesar and opened De Bello
    /// Gallico by Caesarius Arelatensis Episcopus, a sixth-century bishop of
    /// Arles. Substituting an unrelated author is the exact thing this screen
    /// exists to prevent, and offering nothing is the honest answer.
    /// </summary>
    [Fact]
    public void ARealAuthorWithNoMatchingWorkDoesNotFallThroughToASimilarName()
    {
        var library = new Library()
            .With("Julius Caesar", "Civil War")
            .With("Caesarius Arelatensis Episcopus", "De Bello Gallico");

        Assert.Null(library.Opens("Caesar, Gallic War"));
    }

    /// <summary>
    /// Falling through is still right among authors of the same quality - two
    /// genuine name forms of the same person, where the first happens not to
    /// carry the work.
    /// </summary>
    [Fact]
    public void FallingThroughStillHappensBetweenEquallyGoodNames()
    {
        var library = new Library()
            .With("Caesar, Julius", "Civil War")
            .With("Julius Caesar", "Gallic War");

        Assert.Equal("Gallic War", library.Opens("Caesar, Gallic War"));
    }

    /// <summary>
    /// And a name that only contains the key is still used when it is the
    /// only thing there. The key is "ovid", and a library naming him
    /// "Publius Ovidius Naso" carries it as a fragment rather than a word -
    /// which is a real match and the only one on offer.
    /// </summary>
    [Fact]
    public void ASubstringOnlyNameIsUsedWhenNothingBetterExists()
    {
        var library = new Library().With("Publius Ovidius Naso", "Metamorphoses");

        Assert.Equal("Metamorphoses", library.Opens("Ovid, Metamorphoses"));
    }

    /// <summary>
    /// A work that is not in the library is left out rather than substituted -
    /// which is the whole reason AvailableIn returns what it found instead of
    /// the full list with gaps.
    /// </summary>
    [Fact]
    public void AnAbsentWorkIsNotSubstitutedWithSomethingElse()
    {
        var library = new Library().With("Julius Caesar", "Civil War");

        Assert.Null(library.Opens("Caesar, Gallic War"));
    }

    /// <summary>
    /// Every recommendation needs an author key, a display name and a reason -
    /// a blank one would render as an empty row nobody could act on.
    /// </summary>
    [Fact]
    public void EverySuggestionIsCompletelyFilledIn()
    {
        Assert.NotEmpty(StartingPoints.All);

        foreach (var s in StartingPoints.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.AuthorKey), $"{s.Display} has no author key");
            Assert.False(string.IsNullOrWhiteSpace(s.Display), "a suggestion has no display name");
            Assert.False(string.IsNullOrWhiteSpace(s.Why), $"{s.Display} has no reason");
            Assert.NotEmpty(s.TitleKeys);
            Assert.Contains(s.Language, new[] { "grc", "lat" });
        }
    }

    /// <summary>
    /// No author key may name a pseudonymous author, since the matcher now
    /// demotes those - a suggestion that meant one would silently never match.
    /// </summary>
    [Fact]
    public void NoSuggestionAsksForAPseudonymousAuthor()
    {
        foreach (var s in StartingPoints.All)
            Assert.DoesNotContain("pseudo", s.AuthorKey, StringComparison.OrdinalIgnoreCase);
    }
}
