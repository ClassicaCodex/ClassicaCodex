using ClassicaCodex.Ingestion.Egyptian;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Reading Egyptian texts out of AED-TEI, the TEI conversion of the Thesaurus
/// Linguae Aegyptiae's 2018 snapshot.
///
/// Every case here is taken from the real release and most were found by
/// running the loader over all of it: 3,416 texts and 59,127 sentences in the
/// subcorpora this step reads. The files are shortened but their shape is not
/// changed.
/// </summary>
public class AedTeiLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aedtei-" + Guid.NewGuid().ToString("N"));

    public AedTeiLoaderTests() => Directory.CreateDirectory(Path.Combine(_root, "files"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private const string Header =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<TEI xmlns=\"http://www.tei-c.org/ns/1.0\" xml:id=\"x\">\n"
        + "<teiHeader><fileDesc><titleStmt><title>T</title><respStmt><resp>edition by</resp><name>Burkhard Backes</name></respStmt></titleStmt></fileDesc></teiHeader>\n"
        + "<text><body><ab>\n";

    private const string Footer = "</ab></body></text></TEI>\n";

    private void Concordance(params string[] lines) =>
        File.WriteAllLines(Path.Combine(_root, AedTeiLoader.ConcordanceFileName), lines);

    private void File_(string name, string body) =>
        File.WriteAllText(Path.Combine(_root, "files", name), Header + body + Footer);

    private static string W(string id, string lemma, string inner) =>
        $"<w xml:id=\"{id}\" lemmaRef=\"tla:{lemma}\"><fs feats=\"verb\"/>{inner}</w>\n";

    /// <summary>
    /// <b>Three files make one text.</b> The transliteration is the base file's
    /// words; the German and the hieroglyphs come from two standoff files that
    /// point back at the base file's sentence ids. Taken from the Papyrus of
    /// Nu, spell 125, the Judgement of the Dead.
    /// </summary>
    [Fact]
    public void TransliterationGermanAndSignsComeFromThreeFiles()
    {
        Concordance("tb:pLondon BM EA 10477 (pNu)//Tb 125\tAAAAAAAAAAAAAAAAAAAAAAAAAA");
        File_("AAAAAAAAAAAAAAAAAAAAAAAAAA.xml",
            "<s xml:id=\"s1\">\n<lb n=\"[1]\"/>\n" + W("w1", "tla185810", "ḏd.t") + W("w2", "tla116761", "ḫft") + "</s>\n");
        File_("AAAAAAAAAAAAAAAAAAAAAAAAAA_st.xml",
            "<s xml:lang=\"de\" corresp=\"src:s1\">Was beim Erreichen dieser Halle zu sagen ist</s>\n");
        File_("AAAAAAAAAAAAAAAAAAAAAAAAAA_hiero.xml",
            "<s corresp=\"src:s1\"><w corresp=\"src:w1\">𓆓𓂧</w><w corresp=\"src:w2\">𓆼</w></s>\n");

        var text = Assert.Single(AedTeiLoader.Load(_root));
        var s = Assert.Single(text.Sentences);

        Assert.Equal("pLondon BM EA 10477 (pNu)", text.ObjectName);
        Assert.Equal("Tb 125", text.Title);
        Assert.Equal("ḏd.t ḫft", s.Transliteration);
        Assert.Equal("Was beim Erreichen dieser Halle zu sagen ist", s.German);
        Assert.Equal("𓆓𓂧 𓆼", s.Hieroglyphs);
        Assert.Equal("[1]", s.Line);
        Assert.Equal(new[] { "Burkhard Backes" }, text.Editors);
        Assert.Equal("tla185810", s.Words[0].LemmaId);
    }

    /// <summary>
    /// <b>The editorial marks are inside the words</b>, and print in the
    /// brackets Egyptologists use. The dictionary is given the bare letters.
    /// </summary>
    [Fact]
    public void EditorialMarksPrintAsBracketsAndTheDictionaryGetsBareLetters()
    {
        Concordance("sawlit:pWestcar//Die Erzählungen\tBBBBBBBBBBBBBBBBBBBBBBBBBB");
        File_("BBBBBBBBBBBBBBBBBBBBBBBBBB.xml",
            "<s xml:id=\"s1\">\n"
            + W("w1", "1", "<supplied reason=\"lost\">ꜥ=k</supplied>")
            + W("w2", "2", "<damage>šy</damage>")
            + W("w3", "3", "zẖꜣ,w<surplus>.pl</surplus>")
            + W("w4", "4", "dmḏ<add place=\"above\">.t</add>.n")
            + W("w5", "5", "<del rend=\"erasure\">jb</del>")
            + W("w6", "6", "<unclear>wꜥ</unclear>")
            + "<gap reason=\"lost\" />\n"
            + "</s>\n");

        var s = Assert.Single(Assert.Single(AedTeiLoader.Load(_root)).Sentences);

        Assert.Equal("[ꜥ=k] ⸢šy⸣ zẖꜣ,w{.pl} dmḏ⸌.t⸍.n ⟦jb⟧ wꜥ(?) […]", s.Transliteration);
        Assert.Equal(new[] { "ꜥ=k", "šy", "zẖꜣ,w.pl", "dmḏ.t.n", "jb", "wꜥ" }, s.Words.Select(w => w.Form));
    }

    /// <summary>
    /// <b>A sentence starts on the line it starts on.</b> A sentence with no
    /// line break of its own begins on the line the previous one ended on;
    /// a break after its first word does not move where it began.
    /// </summary>
    [Fact]
    public void ASentenceInheritsTheLineThePreviousOneEndedOn()
    {
        Concordance("tb:pNu//Tb 125\tCCCCCCCCCCCCCCCCCCCCCCCCCC");
        File_("CCCCCCCCCCCCCCCCCCCCCCCCCC.xml",
            "<s xml:id=\"s1\"><lb n=\"[2]\"/>" + W("a", "1", "ḏd-mdw") + "<lb n=\"[3]\"/></s>\n"
            + "<s xml:id=\"s2\">" + W("b", "2", "j⁝nḏ-ḥr") + "<lb n=\"[4]\"/>" + W("c", "3", "=k") + "</s>\n"
            + "<s xml:id=\"s3\">" + W("d", "4", "jy.n") + "</s>\n");

        var lines = Assert.Single(AedTeiLoader.Load(_root)).Sentences.Select(s => s.Line).ToList();

        Assert.Equal(new[] { "[2]", "[3]", "[4]" }, lines);
    }

    /// <summary>
    /// <b>A broken standoff file costs only itself.</b> Some hieroglyph files
    /// in the release close an <c>unclear</c> element in the wrong place. The
    /// text keeps its transliteration and German, loses its signs, and the
    /// problem is reported rather than stopping the load.
    /// </summary>
    [Fact]
    public void AMalformedHieroglyphFileLosesOnlyTheSigns()
    {
        Concordance("bbawarchive:pBM 10735 frame 12//2b\tDDDDDDDDDDDDDDDDDDDDDDDDDD");
        File_("DDDDDDDDDDDDDDDDDDDDDDDDDD.xml", "<s xml:id=\"s1\">" + W("a", "1", "jꜣw") + "</s>\n");
        File_("DDDDDDDDDDDDDDDDDDDDDDDDDD_st.xml", "<s corresp=\"src:s1\">Lobpreis</s>\n");
        File.WriteAllText(Path.Combine(_root, "files", "DDDDDDDDDDDDDDDDDDDDDDDDDD_hiero.xml"),
            Header + "<s corresp=\"src:s1\"><w corresp=\"src:a\"><unclear>𓇋</w></unclear></s>\n" + Footer);

        var problems = new List<(string Text, string Problem)>();
        var s = Assert.Single(Assert.Single(AedTeiLoader.Load(_root, null, problems)).Sentences);

        Assert.Equal("jꜣw", s.Transliteration);
        Assert.Equal("Lobpreis", s.German);
        Assert.Null(s.Hieroglyphs);
        Assert.Contains(problems, p => p.Problem.StartsWith("hieroglyphs not well-formed"));
    }

    /// <summary>
    /// <b>Where a sign has no Unicode, the TLA wrote a German sentence.</b>
    /// "Keine Entsprechung in Unicode für D58-X1-N37A" appears about 25,000
    /// times; it is shown as the Gardiner codes, or a single box where the TLA
    /// gave none.
    /// </summary>
    [Theory]
    [InlineData("𓆓 Keine Entsprechung in Unicode für D58-X1-N37A-D20 𓂧", "𓆓 ⟨D58-X1-N37A-D20⟩ 𓂧")]
    [InlineData("Keine Entsprechung in Unicode für diese Graphien", "□")]
    [InlineData("𓆓𓂧", "𓆓𓂧")]
    public void SignsWithoutUnicodeShowAsTheirCodes(string raw, string expected) =>
        Assert.Equal(expected, AedTeiLoader.Signs(raw));

    /// <summary>
    /// <b>Only whole documents become works.</b> A subcorpus filed wall by wall
    /// - the Old Kingdom tombs - is left out, because the conversion keeps
    /// "Westwand" and not whose west wall.
    /// </summary>
    [Fact]
    public void WallByWallSubcorporaAreLeftOut()
    {
        Concordance(
            "bbawgrabinschriften:Westwand//Beischrift\tEEEEEEEEEEEEEEEEEEEEEEEEEE",
            "sawmedizin:Papyrus Ebers//Eb 185\tFFFFFFFFFFFFFFFFFFFFFFFFFF");
        File_("EEEEEEEEEEEEEEEEEEEEEEEEEE.xml", "<s xml:id=\"s1\">" + W("a", "1", "ḥtp") + "</s>\n");
        File_("FFFFFFFFFFFFFFFFFFFFFFFFFF.xml", "<s xml:id=\"s1\">" + W("a", "1", "nfr") + "</s>\n");

        var text = Assert.Single(AedTeiLoader.Load(_root));

        Assert.Equal("Papyrus Ebers", text.ObjectName);
    }

    /// <summary>
    /// <b>The concordance's names need decoding.</b> They are stored with HTML
    /// entities, an object name can contain a colon, and a German title marks
    /// a transliterated word with dollar signs.
    /// </summary>
    [Fact]
    public void ConcordanceNamesAreDecoded()
    {
        Concordance(
            "bbawtotenlit:Papyrus &quot;Bremner Rhind&quot; (pBM 10188)//1. Gesänge\tGGGGGGGGGGGGGGGGGGGGGGGGGG",
            "sawlit:〈Liebeslied Nr. 31-47: 〉pChester Beatty I//Lied\tHHHHHHHHHHHHHHHHHHHHHHHHHH",
            "sawmedizin:Papyrus Ebers//$nfr$-Glosse zu Eb 185\tIIIIIIIIIIIIIIIIIIIIIIIIII");

        var entries = AedTeiLoader.ReadConcordance(Path.Combine(_root, AedTeiLoader.ConcordanceFileName));

        Assert.Equal("Papyrus \"Bremner Rhind\" (pBM 10188)", entries[0].ObjectName);
        Assert.Equal("〈Liebeslied Nr. 31-47: 〉pChester Beatty I", entries[1].ObjectName);
        Assert.Equal("nfr-Glosse zu Eb 185", entries[2].Title);
    }

    /// <summary>
    /// <b>A Book of the Dead papyrus is cited by spell.</b> In spell order, not
    /// title order - the Papyrus of Nu files spell 190 as "(Tb 190)", which
    /// sorts first by title. A second copy of a spell is "-2", a variant is
    /// "38A", and a hymn copied alongside is T1.
    /// </summary>
    [Fact]
    public void BookOfTheDeadPassagesAreCitedBySpellNumber()
    {
        static AedText Text(string title) =>
            new(title, "tb", "pNu", title, Array.Empty<string>(), Array.Empty<AedSentence>());

        var ordered = AedTeiIngestService.Order("tb", new List<AedText>
        {
            Text("(Tb 190)"), Text("Tb 125"), Text("Tb 017"), Text("Tb 038 A"),
            Text("Tb 017"), Text("Anbetung des Osiris")
        });

        Assert.Equal(new[] { "17", "17-2", "38A", "125", "190", "T1" }, ordered.Select(o => o.Number));
    }

    /// <summary>
    /// <b>Other documents are numbered in title order</b>, by the first number
    /// in the title, so a gloss on Ebers 185 sits beside Ebers 185.
    /// </summary>
    [Fact]
    public void OtherDocumentsAreNumberedByTheFirstNumberInTheTitle()
    {
        static AedText Text(string title) =>
            new(title, "sawmedizin", "Papyrus Ebers", title, Array.Empty<string>(), Array.Empty<AedSentence>());

        var ordered = AedTeiIngestService.Order("sawmedizin", new List<AedText>
        {
            Text("Eb 1000"), Text("nfr-Glosse zu Eb 185"), Text("Eb 185"), Text("Eb 2")
        });

        Assert.Equal(new[] { "Eb 2", "Eb 185", "nfr-Glosse zu Eb 185", "Eb 1000" }, ordered.Select(o => o.Text.Title));
        Assert.Equal(new[] { "1", "2", "3", "4" }, ordered.Select(o => o.Number));
    }

    [Fact]
    public void DocumentSlugsAreReadableAndUnique()
    {
        var used = new HashSet<string>();

        Assert.Equal("plondon-bm-ea-10477-pnu", AedTeiIngestService.UniqueSlug("tb", "pLondon BM EA 10477 (pNu)", used));
        Assert.Equal("plondon-bm-ea-10477-pnu-2", AedTeiIngestService.UniqueSlug("tb", "pLondon BM EA 10477 (pNu)!", used));
        Assert.Equal("plondon-bm-ea-10477-pnu", AedTeiIngestService.UniqueSlug("sawlit", "pLondon BM EA 10477 (pNu)", used));
    }
}
