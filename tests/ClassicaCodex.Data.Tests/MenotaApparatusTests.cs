using System.Xml.Linq;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Ingestion;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// The rules that decide what a Menota note is.
///
/// These were arrived at by running each candidate rule over all ten
/// manuscripts and counting, because every plausible-looking rule fails on
/// something: counting readings misfiles additions, a bare colon misfires on
/// "d:36", word markup alone misfires on "Ny text: Ivan Lejonriddaren". The
/// rule that survived classifies 4,157 notes in AM 63 fol and nothing else
/// anywhere in the corpus.
///
/// Each test below is one of those counter-examples, kept so that a future
/// simplification of the rule fails here rather than in a corpus count six
/// months later.
/// </summary>
public class MenotaApparatusTests
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";
    private static readonly XNamespace Me = "http://www.menota.org/ns/1.0";

    /// <summary>
    /// A note built the way Menota builds one: word-marked readings with the
    /// editor's punctuation as plain text between them.
    ///
    /// The interleaving matters. NoteText walks the note in document order, so
    /// a fixture that appended the words after the text would produce
    /// "Uphaf : Vphaf Sogo (Ms. AM 18 fol.) Uphaf Vphaf Sogo" and test
    /// something no manuscript contains.
    /// </summary>
    private static XElement Note(params object[] parts) =>
        new(Tei + "note", parts);

    /// <summary>One word-marked reading.</summary>
    private static XElement W(string text) =>
        new(Tei + "w", new XElement(Me + "dipl", text));

    private static ApparatusEntry Classify(XElement note)
    {
        var entries = new List<ApparatusEntry>();
        MenotaIngestService.AddApparatus(note, "dipl", "1.1", 0, entries);
        return Assert.Single(entries);
    }

    /// <summary>
    /// Möðruvallabók puts one of these inside every single word, holding the
    /// word's position on the page. There are 73,172 across the two AM 132
    /// files, and every one was being stored as an editor's note - so two of
    /// the ten manuscripts had an apparatus pane that was entirely noise.
    /// </summary>
    [Fact]
    public void WordPositionMarkersAreNotApparatus()
    {
        var location = new XElement(Tei + "note", new XAttribute("type", "location"), "114ra410");

        Assert.False(MenotaIngestService.IsApparatus(location));
        Assert.True(MenotaIngestService.IsApparatus(new XElement(Tei + "note", "a+r ligature.")));
    }

    [Fact]
    public void AVariantKeepsItsAdoptedReadingAndWitness()
    {
        var entry = Classify(Note(W("Uphaf"), " : ", W("Vphaf"), " ", W("Sogo"), " (Ms. AM 18 fol.)"));

        Assert.Equal("variant", entry.Kind);
        Assert.Equal("Uphaf", entry.Lemma);
        Assert.Equal("Ms. AM 18 fol.", entry.Witness);
    }

    /// <summary>
    /// The siglum is lifted into its own field, so leaving it in the content
    /// as well printed it twice in the apparatus pane.
    /// </summary>
    [Fact]
    public void TheWitnessIsRemovedFromTheTextItWasReadFrom()
    {
        var entry = Classify(Note(W("Magnús"), " : ", W("Magnusar"), " (Ms. AM 18 fol.)"));

        Assert.DoesNotContain("Ms. AM 18 fol.", entry.Content);
        Assert.Equal("Ms. AM 18 fol.", entry.Witness);
    }

    /// <summary>
    /// A note opening with the colon is an addition - text the other
    /// manuscript has and this one lacks. There is no adopted reading.
    ///
    /// This is the case that rules out counting readings: "two or more
    /// readings and a colon" would take the first word after the colon as the
    /// lemma, filing the added text itself as the reading it replaced. 273 of
    /// AM 63 fol's entries are this shape.
    /// </summary>
    [Fact]
    public void AnAdditionHasNoLemma()
    {
        var entry = Classify(Note(": ", W("Ferth"), " ", W("Magnusar"), " ", W("konongs"), " (Ms. AM 18 fol.)"));

        Assert.Equal("variant", entry.Kind);
        Assert.Null(entry.Lemma);
        Assert.Equal("Ms. AM 18 fol.", entry.Witness);
    }

    /// <summary>
    /// A variant may be one word against three. The previous rule required
    /// exactly two readings and so classified 2,883 of AM 63 fol's 4,157
    /// variants, filing the other 1,274 as prose.
    /// </summary>
    [Fact]
    public void AVariantMayHaveUnequalSides()
    {
        var entry = Classify(Note(
            W("helgi"), " ", W("Oláfr"), " ", W("konongr"), " : ",
            W("helga"), " ", W("Oláf"), " ", W("konong"), " (Ms. AM 18 fol.)"));

        Assert.Equal("variant", entry.Kind);
        Assert.Equal("helgi Oláfr konongr", entry.Lemma);
    }

    /// <summary>
    /// Holm D 4 and AM 619 write prose notes with colons in them, where the
    /// colon introduces a label rather than a reading. 379 of these.
    /// </summary>
    [Fact]
    public void AColonIntroducingALabelIsNotAVariant()
    {
        var entry = Classify(Note("Ny text: ", W("Ivan"), " ", W("Lejonriddaren")));

        Assert.Equal("note", entry.Kind);
        Assert.Null(entry.Lemma);
    }

    /// <summary>
    /// A colon tight against its neighbours is a reference, not a separator -
    /// "hafðr ð eller d:36", "utg. s. 106:1". Requiring space around it is
    /// what keeps these three notes out of the variant count.
    /// </summary>
    [Fact]
    public void ATightColonIsNotASeparator()
    {
        Assert.Equal("note", Classify(Note(W("hafðr"), " ð eller d:36")).Kind);
    }

    /// <summary>
    /// Parentheses in a prose note are the editor talking - "(adv)", "(kanske
    /// pga en lagning...)". Reading them as sigla put editorial asides in the
    /// witness column and deleted them from the note they belonged to.
    /// </summary>
    [Fact]
    public void AParenthesisInProseIsNotASiglum()
    {
        var entry = Classify(Note(W("orsak"), " (adv)"));

        Assert.Equal("note", entry.Kind);
        Assert.Null(entry.Witness);
        Assert.Contains("(adv)", entry.Content);
    }

    /// <summary>
    /// Where the file names the editor responsible, that is the witness -
    /// AM 619 4to's GI1931, Holm perg 4 fol's Bertelsen190511. 632 entries
    /// gained a witness they had never had.
    /// </summary>
    [Fact]
    public void TheRespAttributeNamesTheEditor()
    {
        var note = Note("a+r ligature.");
        note.Add(new XAttribute("resp", "GI1931"));

        var entry = Classify(note);

        Assert.Equal("note", entry.Kind);
        Assert.Equal("GI1931", entry.Witness);
    }
}
