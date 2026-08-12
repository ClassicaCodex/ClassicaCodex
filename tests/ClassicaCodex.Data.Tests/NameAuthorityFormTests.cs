using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// &lt;reg&gt; means two different things in the Perseus corpora, and only the
/// parent element tells them apart.
///
/// In canonical-latinLit it holds the regularised form of a word and IS the
/// text - Perseus lower-cases the first word of a sentence and marks it that
/// way, 34,160 times. In the English Herodotus it holds the Getty Thesaurus
/// record for a place, with Herodotus' own word beside it, so the text opened
/// "the inquiry of Herodotus of Bodrum [27.466,37.5] (inhabited place), Mugla
/// Ili, Ege kiyilari, Turkey, Asia Halicarnassus".
///
/// Re-flattening every leaf in the four corpora with the rule applied: 2,158
/// leaves change, all in tlg0016.tlg001.perseus-eng2, 19,746 tokens of
/// gazetteer removed, and not one token added anywhere. The 35,059 &lt;reg&gt;
/// elements outside a naming element are untouched.
/// </summary>
public class NameAuthorityFormTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""book"" n=""1"">{body}</div1></body></text></TEI.2>";

    private static string TextOf(string body) =>
        Assert.Single(new TeiParser().ParseXml(Wrap(body))).Text;

    /// <summary>
    /// Herodotus 1.1, verbatim. The reading text is the &lt;placeName&gt;
    /// sibling; the &lt;reg&gt; is the authority record.
    /// </summary>
    [Fact]
    public void GazetteerRecordBesideAPlaceNameIsNotText()
    {
        Assert.Equal(
            "the inquiry of Herodotus of Halicarnassus",
            TextOf(@"<p>the inquiry of Herodotus of <name key=""tgn,7016142"" type=""place""><reg>Bodrum [27.466,37.5] (inhabited place), Mugla Ili, Ege kiyilari, Turkey, Asia</reg><placeName key=""tgn,7016142"">Halicarnassus</placeName></name></p>"));
    }

    /// <summary>
    /// The other shape in the same file, and the reason a rule keyed on the
    /// presence of a &lt;placeName&gt; sibling would not have worked: here the
    /// reading text is the &lt;reg&gt;'s tail. 2,799 of the 4,305 are like
    /// this.
    /// </summary>
    [Fact]
    public void GazetteerRecordFollowedByBareTextIsNotText()
    {
        Assert.Equal(
            "they came to Tyrrhenia and settled",
            TextOf(@"<p>they came to <name key=""tgn,7008330"" type=""place""><reg>Etruria (region (general)), Italy, Europe</reg>Tyrrhenia</name> and settled</p>"));
    }

    /// <summary>
    /// The case that must not break. Cicero's sentences begin with a
    /// lower-cased &lt;reg&gt; in running text, and it carries the first word
    /// of the sentence - 32,529 of them with a &lt;p&gt; parent alone.
    /// </summary>
    [Fact]
    public void RegularisedWordInRunningTextIsStillRead()
    {
        Assert.Equal(
            "etsi non dubitabam quin hanc epistulam",
            TextOf(@"<p><reg>etsi</reg> non dubitabam quin hanc epistulam</p>"));
    }

    /// <summary>
    /// And inside the other containers it turns up in: &lt;q&gt;, &lt;said&gt;,
    /// &lt;quote&gt;, &lt;seg&gt;, &lt;l&gt;, &lt;hi&gt;.
    /// </summary>
    [Fact]
    public void RegularisedWordInsideAQuotationIsStillRead()
    {
        Assert.Equal(
            "nulli erant praedones.",
            TextOf(@"<p><q rend=""single""><reg>nulli</reg> erant praedones.</q></p>"));
    }

    /// <summary>
    /// A &lt;reg&gt; inside a &lt;choice&gt; is still the preferred reading and
    /// still chosen. &lt;choice&gt; is not a naming element, so the rule never
    /// sees it, but this is the pairing the parser has always resolved and it
    /// should fail here if that changes.
    /// </summary>
    [Fact]
    public void RegInsideAChoiceIsStillPreferred()
    {
        Assert.Equal(
            "the regularised form",
            TextOf(@"<p>the <choice><orig>regularysed</orig><reg>regularised</reg></choice> form</p>"));
    }

    /// <summary>
    /// The guard. Nothing in the corpora is a name holding only its
    /// &lt;reg&gt; - all 4,305 have the reading text beside it - but if one
    /// existed, dropping it would leave a place with no name at all, which is
    /// a worse outcome than a gazetteer string in the text.
    /// </summary>
    [Fact]
    public void ANameWithNothingButItsRegKeepsIt()
    {
        Assert.Equal(
            "they came to Etruria and settled",
            TextOf(@"<p>they came to <name type=""place""><reg>Etruria</reg></name> and settled</p>"));
    }

    /// <summary>
    /// The rule is about the parent, not about &lt;name&gt; specifically:
    /// &lt;persName&gt;, &lt;placeName&gt; and &lt;rs&gt; carry the same
    /// authority records elsewhere in Perseus even though this corpus puts
    /// them all under &lt;name&gt;.
    /// </summary>
    [Fact]
    public void TheRuleAppliesToOtherNamingElements()
    {
        Assert.Equal(
            "a letter from Atticus",
            TextOf(@"<p>a letter from <persName key=""perseus,Atticus""><reg>Pomponius Atticus, Titus, 110-32 BC</reg>Atticus</persName></p>"));
    }
}
