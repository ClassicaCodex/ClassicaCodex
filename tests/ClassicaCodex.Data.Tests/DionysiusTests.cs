using System.Text;
using ClassicaCodex.Ingestion.Dionysius;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading Parker's Dionysius out of the CCEL's ThML. The sample keeps the
/// file's DOCTYPE, its footnotes and page breaks, and one of each kind of
/// chapter: sectioned, undivided, a letter whose first section label carries
/// its addressee, and a letter with Parker's own note after it.
/// </summary>
public class DionysiusTests
{
    private const string ThML = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE ThML PUBLIC "-//CCEL/DTD Theological Markup Language//EN" "http://www.ccel.org/dtd/ThML10.dtd">
        <ThML><ThML.body>
        <div2 title="On Divine Names">
          <p class="normal">THE Treatise on "Divine Names" was written by Dionysius, at the request of Timothy.</p>
          <div3 title="Caput I.">
            <h3>CAPUT I.</h3>
            <p class="subject">What is the purpose of the discourse.</p>
            <p class="normal">Now then, O blessed one, after the Theological Outlines.</p>
          </div3>
          <div3 title="Note"><p class="normal">A note by Parker.</p></div3>
        </div2>
        <div2 title="Mystic Theology">
          <div3 title="Caput I."><h3>CAPUT I.</h3><p class="normal">Triad supernal.</p></div3>
        </div2>
        <div2 title="Letters">
          <div3 title="Letter VII. To Polycarp—Hierarch.">
            <p class="letter">LETTER VII.</p>
            <p class="center">SECTION I. To Polycarp—Hierarch.</p>
            <p class="normal">I, at any rate, am not conscious.</p>
            <p class="center">SECTION II.</p>
            <p class="normal">But thou sayest that the sophist Apollophanes reviles me.</p>
          </div3>
          <div3 title="Letter VIII. To Demophilus.">
            <p class="letter">LETTER VIII. To Demophilus, Therapeutes.</p>
            <p class="normal">These are the things which I heard myself, and believe to be true.</p>
            <p class="center">TITUS.</p>
            <p class="normal">ZENAS, one of the seventy-two disciples, wrote a life of Titus.</p>
          </div3>
        </div2>
        <div2 title="On the Heavenly Hierarchy">
          <div3 title="Caput I.">
            <h3>CAPUT I.</h3>
            <p class="Centered">To my Fellow Presbyter Timothy.<note n="151"><scripRef>1 Pet. v. 1</scripRef>.</note> Dionysius the Presbyter.</p>
            <p class="subject">That every divine illumination remains simplex.</p>
            <p class="center">Section I.</p>
            <p class="normal">"Every good gift<note n="152">James i. 17.</note> and every perfect gift is from above."</p>
            <p class="normal">Further also, every procession of illuminating light.</p>
            <p class="center">Section II.</p>
            <p class="normal">Invoking then Jesus, accounting the visible<scripRef>Ps. xix.</scripRef> beauties as
        <pb n="2"/>reflections of the invisible comeliness.</p>
          </div3>
          <div3 title="Caput VI.">
            <h3>CAPUT VI.</h3>
            <p class="subject">Which is the first Order of the Heavenly Beings?</p>
            <p class="normal">The Word of God has designated the whole Heavenly Beings as nine.</p>
          </div3>
        </div2>
        <div2 title="Ecclesiastical Hierarchy">
          <div3 title="Caput Ii."><h3>CAPUT II.</h3><p class="normal">Concerning Illumination.</p></div3>
        </div2>
        </ThML.body></ThML>
        """;

    private static IReadOnlyList<DionysiusWork> Works()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ThML));
        return DionysiusLoader.Load(DionysiusLoader.Read(stream));
    }

    private static Dictionary<string, string> Passages(string title) =>
        Works().Single(w => w.Title == title).Passages.ToDictionary(p => p.Citation, p => p.Text);

    [Fact]
    public void TheFiveWorksAreReadInParkersOrder()
    {
        Assert.Equal(
            new[] { "On Divine Names", "Mystic Theology", "Letters", "On the Heavenly Hierarchy", "On the Ecclesiastical Hierarchy" },
            Works().Select(w => w.Title));
    }

    [Fact]
    public void AChapterIsCitedBySectionWithItsSubjectInTheHeading()
    {
        var hierarchy = Works().Single(w => w.Title == "On the Heavenly Hierarchy").Passages;

        Assert.Equal(
            new[] { "1.head", "1.0", "1.1", "1.1.2", "1.2", "6.head", "6.1" },
            hierarchy.Select(p => p.Citation));
        Assert.True(hierarchy[0].IsHead);
        Assert.Equal("Caput I. That every divine illumination remains simplex.", hierarchy[0].Text);
    }

    [Fact]
    public void FootnotesAndPageBreaksAreLeftOut()
    {
        var hierarchy = Passages("On the Heavenly Hierarchy");

        Assert.Equal("To my Fellow Presbyter Timothy. Dionysius the Presbyter.", hierarchy["1.0"]);
        Assert.Equal("\"Every good gift and every perfect gift is from above.\"", hierarchy["1.1"]);
        Assert.Equal(
            "Invoking then Jesus, accounting the visible [Ps. xix.] beauties as reflections of the invisible comeliness.",
            hierarchy["1.2"]);
    }

    [Fact]
    public void AnUndividedChapterIsItsOwnFirstSection() =>
        Assert.Equal("The Word of God has designated the whole Heavenly Beings as nine.",
            Passages("On the Heavenly Hierarchy")["6.1"]);

    [Fact]
    public void ALettersAddresseeAndParkersNotesAreHandled()
    {
        var letters = Passages("Letters");

        Assert.Equal("Letter VII.", letters["7.head"]);
        Assert.Equal("To Polycarp—Hierarch.", letters["7.1"]);
        Assert.Equal("I, at any rate, am not conscious.", letters["7.1.2"]);
        Assert.Equal("Letter VIII. To Demophilus, Therapeutes.", letters["8.head"]);
        Assert.DoesNotContain(letters.Values, t => t.Contains("ZENAS") || t == "TITUS.");
    }

    [Fact]
    public void ParkersIntroductionsAndNotesAreNotTheWork()
    {
        var names = Passages("On Divine Names");

        Assert.Equal(new[] { "1.head", "1.1" }, names.Keys);
        Assert.Equal("Concerning Illumination.", Passages("On the Ecclesiastical Hierarchy")["2.1"]);
    }
}
