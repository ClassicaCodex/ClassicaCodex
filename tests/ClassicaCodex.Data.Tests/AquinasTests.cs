using ClassicaCodex.Ingestion.Aquinas;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading the First Part of the Summa out of Gutenberg's text.
///
/// The sample is the file's own shape, cut down, with each of its
/// irregularities in it once: a treatise heading, a question whose article
/// tag gives the wrong number, a translators' note before an objection, a
/// reply to two objections, a one-article question with no tag, and the
/// question that has lost its QUESTION heading.
/// </summary>
public class AquinasTests
{
    private const string Summa = """
        *** START OF THIS PROJECT GUTENBERG EBOOK SUMMA THEOLOGICA, PART I ***

        PROLOGUE

        Because the Master of Catholic truth ought not only to teach the
        proficient, but also to instruct beginners.
        _______________________

        SUMMA THEOLOGICA

        FIRST PART
        ["I," "Prima Pars"]
        _______________________

        QUESTION 1

        THE NATURE AND EXTENT OF SACRED DOCTRINE
        (in Two Articles)

        To place our purpose within proper limits, we first endeavor to
        investigate the nature and extent of this sacred doctrine.

        (1) Whether it is necessary?
        _______________________

        FIRST ARTICLE [I, Q. 1, Art. 1]

        Whether, Besides Philosophy, Any Further Doctrine Is Required?

        Objection 1: It seems that, besides philosophical science, we have no
        need of any further knowledge.

        Obj. 2: Further, knowledge can be concerned only with being.

        _On the contrary,_ It is written (2 Tim. 3:16).

        _I answer that,_ It was necessary for man's salvation.

        Even as regards those truths about God which human reason could have
        discovered.

        Reply Obj. 1 and 2: Although those things which are beyond man's
        knowledge may not be sought for by man through his reason.
        _______________________

        SECOND ARTICLE [I, Q. 1, Art. 1]

        Whether Sacred Doctrine Is a Science?

        [*Science here means knowledge by causes.]

        Objection 1: It seems that sacred doctrine is not a science.

        _I answer that,_ Sacred doctrine is a science.

        Reply Obj. 1: The principles of any science are either in themselves
        self-evident.
        _______________________

        TREATISE ON THE ANGELS (QQ. 2-3)
        _______________________

        QUESTION 2

        ON THE WORK OF THE FIFTH DAY
        (In One Article)

        We must next consider the work of the fifth day.

        Objection 1: It would seem that this work is not fittingly described.

        _On the contrary,_ Suffices the authority of Scripture.

        _I answer that,_ As said above, the order of the work of adornment
        corresponds to the order of the work of distinction.
        _______________________

        ON FATE
        (In One Article)

        We come now to the consideration of fate.
        _______________________

        FIRST ARTICLE [I, Q. 3, Art. 1]

        Whether There Be Such a Thing As Fate?

        Objection 1: It would seem that fate is nothing.

        _I answer that,_ In this world some things seem to happen by chance.
        _______________________

        End of the Project Gutenberg EBook of Summa Theologica, Part I

        *** END OF THIS PROJECT GUTENBERG EBOOK SUMMA THEOLOGICA, PART I ***
        """;

    private static Dictionary<string, string> Passages() =>
        SummaLoader.Parse(Summa).ToDictionary(p => p.Citation, p => p.Text);

    [Fact]
    public void AnArticleIsCitedByQuestionArticleAndPart()
    {
        var passages = SummaLoader.Parse(Summa);

        Assert.Equal(
            new[] { "1.1.head", "1.1.ob1", "1.1.ob2", "1.1.sc", "1.1.co", "1.1.co.2", "1.1.ad1" },
            passages.Select(p => p.Citation).Where(c => c.StartsWith("1.1.")));
        Assert.True(passages.Single(p => p.Citation == "1.1.head").IsHead);
    }

    [Fact]
    public void ItalicsAreTakenOutAndLinesRejoined()
    {
        var passages = Passages();

        Assert.Equal("On the contrary, It is written (2 Tim. 3:16).", passages["1.1.sc"]);
        Assert.Equal(
            "Reply Obj. 1 and 2: Although those things which are beyond man's knowledge may not be sought for by man through his reason.",
            passages["1.1.ad1"]);
    }

    [Fact]
    public void AQuestionHasATitleAndAnIntroduction()
    {
        var passages = Passages();

        Assert.Equal("Question 1. The Nature and Extent of Sacred Doctrine", passages["1.head"]);
        Assert.Equal("(1) Whether it is necessary?", passages["1.pr.2"]);
        Assert.StartsWith("Because the Master", passages["pr.1"]);
    }

    /// <summary>
    /// The second article's tag says Art. 1; the ordinal says SECOND, and
    /// the ordinal is the one that is right.
    /// </summary>
    [Fact]
    public void TheOrdinalPlacesAnArticleWhoseTagIsWrong()
    {
        var passages = Passages();

        Assert.Equal("Whether Sacred Doctrine Is a Science?", passages["1.2.head"]);
        Assert.Equal("[*Science here means knowledge by causes.]", passages["1.2.note"]);
    }

    [Fact]
    public void AOneArticleQuestionIsItsOwnArticle()
    {
        var passages = Passages();

        Assert.Equal("Treatise on the Angels (Questions 2-3)", passages["2.treatise"]);
        Assert.Equal("We must next consider the work of the fifth day.", passages["2.pr.1"]);
        Assert.StartsWith("Objection 1:", passages["2.1.ob1"]);
        Assert.StartsWith("I answer that,", passages["2.1.co"]);
    }

    [Fact]
    public void AQuestionWithoutItsHeadingIsNumberedFromTheOneBefore()
    {
        var passages = Passages();

        Assert.Equal("Question 3. On Fate", passages["3.head"]);
        Assert.Equal("Whether There Be Such a Thing As Fate?", passages["3.1.head"]);
        Assert.DoesNotContain(passages.Values, t => t.StartsWith("End of the Project Gutenberg"));
    }

    [Fact]
    public void AnArticleThatIsNotTheNextIsRefused()
    {
        var skipped = Summa.Replace("SECOND ARTICLE [I, Q. 1, Art. 1]", "THIRD ARTICLE [I, Q. 1, Art. 3]");

        Assert.Throws<InvalidDataException>(() => SummaLoader.Parse(skipped));
    }
}
