using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Where the reading text gets its spaces from.
///
/// FlattenElement used to append a space after every text node it read. That
/// was invisible wherever the source already had whitespace at the same point,
/// which is most places, and wrong wherever it did not - markup sitting inside
/// a word, or punctuation following a marked-up word. TokenizeLine is a bare
/// whitespace split, so every one of those went into the word index as two
/// tokens: a real word destroyed and a fragment invented.
///
/// Measured by re-flattening every leaf in canonical-greekLit,
/// canonical-latinLit, First1KGreek and the Renaissance English corpus:
/// 215,471 tokens created that the source does not contain, 19,264 of them
/// genuine letter-to-letter word splits and the rest punctuation detached from
/// the word it follows. The corresponding re-run with the fix in place creates
/// zero tokens the old flattening did not, which is the property the last test
/// here stands for.
///
/// The shapes below are all taken from the corpora rather than invented, and
/// each is the smallest form of a case that occurs at scale.
/// </summary>
public class FlattenSpacingTests
{
    private static string Wrap(string body) =>
        $@"<TEI.2><text><body><div1 type=""book"" n=""1"">{body}</div1></body></text></TEI.2>";

    private static string TextOf(string body) =>
        Assert.Single(new TeiParser().ParseXml(Wrap(body))).Text;

    // ------------------------------------------------- markup inside a word

    /// <summary>
    /// An editorially supplied ending. 1,170 of these in canonical-greekLit
    /// and 1,183 in canonical-latinLit, plus 1,154 and 898 respectively where
    /// the &lt;add&gt; opens the word instead of closing it.
    /// </summary>
    [Fact]
    public void SuppliedLetteringStaysInsideItsWord()
    {
        Assert.Equal(
            "πίνειν, Ἀγάθωνος δὲ",
            TextOf(@"<l>πίνειν, Ἀγάθων<add>ος</add> δὲ</l>"));
    }

    /// <summary>
    /// A deletion in the middle of a word. &lt;del&gt; is deliberately part of
    /// the reading text - see the comment on EditorialElements - so it has to
    /// rejoin the letters around it rather than being fenced off from them.
    /// </summary>
    [Fact]
    public void DeletionInsideAWordDoesNotSplitIt()
    {
        Assert.Equal(
            "ἀμφιγνοεῖν· οὐδὲ",
            TextOf(@"<l>ἀ<del>μφι</del>γνοεῖν· οὐδὲ</l>"));
    }

    /// <summary>
    /// Two in one line, which is how they usually arrive.
    /// </summary>
    [Fact]
    public void SeveralSuppliedLettersInOneLine()
    {
        Assert.Equal(
            "quicquam disque supatis.",
            TextOf(@"<l>qui<add>c</add>quam dis<add>que</add> supatis.</l>"));
    }

    /// <summary>
    /// Punctuation after a marked-up word. Euclid labels his figures with
    /// &lt;num&gt; and then punctuates the sentence: 13,359 in the Greek corpus
    /// alone, and the same shape with &lt;placeName&gt; another 15,183.
    /// </summary>
    [Fact]
    public void PunctuationStaysAttachedAfterInlineMarkup()
    {
        Assert.Equal(
            "εὐθεῖα πεπερασμένη ἡ ΑΒ. καὶ",
            TextOf(@"<l>εὐθεῖα πεπερασμένη ἡ <num>ΑΒ</num>. καὶ</l>"));
    }

    // ------------------------------------- elements that contribute nothing

    /// <summary>
    /// A footnote anchored between a word and the comma that follows it. The
    /// note is excluded from the text, as it must be, and the word and the
    /// comma were adjacent in the source - so they stay adjacent.
    /// </summary>
    [Fact]
    public void SkippedNoteDoesNotDetachTheFollowingPunctuation()
    {
        Assert.Equal(
            "Themistocles autem, ut venit",
            TextOf(@"<l>Themistocles autem<note>Nepos, Them. 4</note>, ut venit</l>"));
    }

    /// <summary>
    /// The counterpart, and the reason the rule is about what sits on either
    /// side rather than about which elements are markers. A line break between
    /// two words is a break; joining them would invent a word. 4,445
    /// &lt;lb/&gt; and 5,894 &lt;milestone/&gt; sit tight against text on both
    /// sides across the corpora, against 859 empty &lt;note&gt; anchors that
    /// must not break.
    /// </summary>
    [Fact]
    public void EmptyMarkersStillSeparateTwoWords()
    {
        Assert.Equal("word next", TextOf(@"<l>word<lb/>next</l>"));
        Assert.Equal("alpha beta", TextOf(@"<l>alpha<milestone unit=""section"" n=""2""/>beta</l>"));
        Assert.Equal("nunc quoniam", TextOf(@"<l>nunc<gap reason=""lost""/>quoniam</l>"));
    }

    /// <summary>
    /// Marlowe's Faustus regularises punctuation with an empty
    /// &lt;orig reg="," /&gt; between two words. It contributes no text, so it
    /// separates them exactly as a line break would.
    /// </summary>
    [Fact]
    public void EmptyRegularisationSeparatesTheWordsAroundIt()
    {
        Assert.Equal(
            "Go too sirra",
            TextOf(@"<p>Go <orig reg=""to"">too</orig><orig reg="","" /><orig reg=""sirrah"">sirra</orig></p>"));
    }

    /// <summary>
    /// An apparatus entry inline within a line. The lemma is the text at that
    /// point, so the comma after it belongs to it.
    /// </summary>
    [Fact]
    public void AdoptedReadingKeepsThePunctuationAfterIt()
    {
        Assert.Equal(
            "ἀστέρας, ὅταν",
            TextOf(@"<l><app><lem>ἀστέρας</lem><rdg wit=""M"">ἀστέρα</rdg></app>, ὅταν</l>"));
    }

    // ------------------------------------------------- boundaries that break

    /// <summary>
    /// This test used to assert "affinitatem Publii P. Sulpicii", pinning a
    /// weaker property: the expansion and the abbreviation were both being
    /// read, and the least this flattening owed them was to keep them two
    /// tokens rather than fusing them into "PubliiP.". Only the abbreviation
    /// is read now (see AbbreviationOverExpansionTests), so that property has
    /// nothing left to hold apart, and &lt;expan&gt; and &lt;ex&gt; have left
    /// BlockBoundaryElements with it.
    ///
    /// What is still worth pinning here is the spacing, which is this file's
    /// subject: the &lt;expan&gt; contributes nothing, and the words on either
    /// side of it must neither fuse nor gain a space that the source does not
    /// have. Nepos, verbatim.
    /// </summary>
    [Fact]
    public void ADroppedExpansionLeavesTheSpacingAroundItAlone()
    {
        Assert.Equal(
            "affinitatem P. Sulpicii",
            TextOf(@"<l>affinitatem <abbr><expan><ex>Publii</ex></expan>P.</abbr> Sulpicii</l>"));
    }

    /// <summary>
    /// A whole word inside inline markup, with the source's own spaces on both
    /// sides. The overwhelmingly common case, and the one that must not move:
    /// this is what every leaf in the corpora looked like before and after.
    /// </summary>
    [Fact]
    public void OrdinarySpacedMarkupIsUnchanged()
    {
        Assert.Equal(
            "and the word ἀρετή means excellence",
            TextOf(@"<l>and the word <foreign lang=""grc"">ἀρετή</foreign> means excellence</l>"));
    }

    /// <summary>
    /// Whitespace in the source is still collapsed, so a line broken across
    /// several source lines reads as one.
    /// </summary>
    [Fact]
    public void SourceWhitespaceIsStillCollapsed()
    {
        Assert.Equal(
            "μῆνιν ἄειδε θεά",
            TextOf("<l>μῆνιν\n              ἄειδε   θεά</l>"));
    }
}
