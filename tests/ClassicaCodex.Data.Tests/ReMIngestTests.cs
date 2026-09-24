using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.ReM;
using Xunit;

namespace ClassicaCodex.Data.Tests;

/// <summary>
/// Putting a ReM text into the library, end to end.
///
/// The loader's own tests say the XML is read correctly. These say the result is
/// a work a reader can open: filed under an author, with two editions that can
/// be told apart, and lines that carry the manuscript's own citation.
///
/// The two editions are the whole reason for ingesting a manuscript corpus
/// rather than an edition of one - the scribe's spelling and the normalised
/// reading, side by side. If they arrived as one edition, or as two that looked
/// identical in the dropdown, the feature would not be worth the download.
/// </summary>
[Collection("Database")]
public class ReMIngestTests
{
    private const string Shy = "­";

    /// <summary>
    /// A file shaped like a real ReM one. The header is the real header's shape,
    /// which is not the obvious one - see ReMTextLoaderTests.
    /// </summary>
    private static string File(string id, string title, string author, string body) =>
        $@"<TEI xmlns=""http://www.tei-c.org/ns/1.0"" version=""4.6.0"">
  <teiHeader>
    <fileDesc xml:id=""{id}"">
      <titleStmt><title>{title}</title></titleStmt>
      <sourceDesc><msDesc><msIdentifier>
        <repository>Wien, Österr. Nationalbibl.</repository><idno>Cod. 160</idno>
      </msIdentifier></msDesc></sourceDesc>
    </fileDesc>
    <encodingDesc><p>Primary line breaks: Hs.: Blatt (r/v), Zeile</p></encodingDesc>
    <profileDesc>
      <creation><persName>{author}</persName></creation>
      <langUsage><language ident=""gmh"">bairisch</language></langUsage>
    </profileDesc>
  </teiHeader>
  <text><body><ab>{body}</ab></body></text>
</TEI>";

    private static string Corpus(params (string Id, string Title, string Author, string Body)[] texts)
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ccx-rem-tests", Guid.NewGuid().ToString("N"));
        var tei = System.IO.Path.Combine(root, "ReM-v2.1_tei", "tei");
        Directory.CreateDirectory(tei);

        foreach (var (id, title, author, body) in texts)
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tei, id + ".xml"), File(id, title, author, body));
        }

        return root;
    }

    /// <summary>
    /// The one work in the library, reached the way the reader reaches it -
    /// author, then work, then that work's editions.
    /// </summary>
    private static async Task<(Work Work, List<Edition> Editions)> SingleWorkAsync()
    {
        var author = Assert.Single(await new AuthorRepository().GetAllAsync());
        var work = Assert.Single(await new WorkRepository().GetByAuthorAsync(author.AuthorId));
        return (work, await new EditionRepository().GetByWorkAsync(work.WorkId));
    }

    private const string TwoLines =
        @"<pb n=""100v"" ed=""1""/><lb n=""5"" ed=""1""/>
          <w norm=""welt"" lemma=""werelt"">welt</w>
          <w norm=""muozic"" lemma=""müezic"">muͦzic</w>
          <lb n=""6"" ed=""1""/>
          <w norm=""stet"" lemma=""stêt"">ſtet</w>
          <lb n=""7"" ed=""1""/>";

    [Fact]
    public async Task ATextArrivesAsOneWorkWithTwoReadableEditions()
    {
        using var db = await TempDatabase.CreateAsync();
        var root = Corpus(("M058", "Sangspruchstrophe", "-", TwoLines));

        var service = new ReMIngestService();
        var outcome = await service.IngestAsync(root);

        Assert.Empty(outcome.SkippedFiles);
        Assert.Equal(1, service.TextsInstalled);
        Assert.Equal(2, service.LinesInstalled);

        var (_, editions) = await SingleWorkAsync();
        Assert.Equal(2, editions.Count);

        // Both are originals in the same language - they are not a text and its
        // translation - and they differ by orthography, which is what the
        // dropdown shows.
        Assert.All(editions, e => Assert.Equal("gmh", e.Language));
        Assert.Equal(
            new[] { "diplomatic", "normalised" },
            editions.Select(e => e.Orthography).OrderBy(o => o, StringComparer.Ordinal));

        Assert.All(editions, e => Assert.Equal(ReMIngestService.CollectionKey, e.Collection));
    }

    /// <summary>
    /// The scribe's spelling and the reading text land in the right editions -
    /// the one mix-up that would make the whole feature quietly pointless, since
    /// two editions of the same text would look like a bug rather than a choice.
    /// </summary>
    [Fact]
    public async Task EachEditionCarriesItsOwnReading()
    {
        using var db = await TempDatabase.CreateAsync();
        await new ReMIngestService().IngestAsync(Corpus(("M058", "Sangspruch", "-", TwoLines)));

        var (_, editions) = await SingleWorkAsync();
        var nodes = new TextNodeRepository();

        var diplomatic = await nodes.GetByEditionAsync(
            editions.Single(e => e.Orthography == "diplomatic").EditionId);
        var normalised = await nodes.GetByEditionAsync(
            editions.Single(e => e.Orthography == "normalised").EditionId);

        Assert.Equal("welt muͦzic", diplomatic[0].Text);
        Assert.Equal("werelt müezic", normalised[0].Text);

        // Same passage, same citation, in both - which is what lets the two be
        // read against each other.
        Assert.Equal("100v.5", diplomatic[0].CitationRef);
        Assert.Equal("100v.5", normalised[0].CitationRef);
    }

    /// <summary>
    /// 345 of ReM's 406 texts name no author. They are filed as Anonymous rather
    /// than under ReM's "-" placeholder, which would put a library tree entry
    /// called "-" above four fifths of the collection.
    /// </summary>
    [Fact]
    public async Task AnonymousTextsAreFiledAsAnonymousAndNamedOnesUnderTheirAuthor()
    {
        using var db = await TempDatabase.CreateAsync();

        await new ReMIngestService().IngestAsync(Corpus(
            ("M058", "Sangspruchstrophe", "-", TwoLines),
            ("M004", "Johannes Baptista", "Priester Adelbrecht", TwoLines)));

        var authors = (await new AuthorRepository().GetAllAsync())
            .Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "Anonymous", "Priester Adelbrecht" }, authors);
    }

    /// <summary>
    /// The manuscript is part of the title. Two texts called "Predigten" from
    /// different libraries are otherwise the same row in the library tree.
    /// </summary>
    [Fact]
    public async Task TheWorkTitleNamesTheManuscript()
    {
        using var db = await TempDatabase.CreateAsync();
        await new ReMIngestService().IngestAsync(Corpus(("M058", "Predigten", "-", TwoLines)));

        var (work, _) = await SingleWorkAsync();
        Assert.Equal("Predigten (Wien, Österr. Nationalbibl., Cod. 160)", work.Title);
        Assert.Equal("Hs.: Blatt (r/v), Zeile", work.CitationScheme);
    }

    /// <summary>
    /// One unreadable file out of 406 must not throw away the other 405, and the
    /// one it could not read has to be named rather than quietly missing - the
    /// rule the whole IngestOutcome type exists for.
    /// </summary>
    [Fact]
    public async Task OneBadFileIsReportedAndTheRestStillArrive()
    {
        using var db = await TempDatabase.CreateAsync();
        var root = Corpus(("M058", "Good", "-", TwoLines));
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(root, "ReM-v2.1_tei", "tei", "M999.xml"), "<TEI><not-closed>");

        var service = new ReMIngestService();
        var outcome = await service.IngestAsync(root);

        Assert.Equal(1, service.TextsInstalled);
        Assert.Equal(2, outcome.FilesAttempted);
        Assert.Contains("M999", Assert.Single(outcome.SkippedFiles).FilePath);
    }

    /// <summary>
    /// Re-running replaces rather than doubles. Running a step again is the
    /// ordinary way to recover from a cancelled one, and this corpus is one
    /// archive that arrives whole.
    /// </summary>
    [Fact]
    public async Task RunningItTwiceLeavesOneCopy()
    {
        using var db = await TempDatabase.CreateAsync();
        var root = Corpus(("M058", "Sangspruch", "-", TwoLines));

        await new ReMIngestService().IngestAsync(root);
        await new ReMIngestService().IngestAsync(root);

        var (_, editions) = await SingleWorkAsync();
        Assert.Equal(2, editions.Count);

        var edition = editions.Single(e => e.Orthography == "normalised");
        Assert.Equal(2, (await new TextNodeRepository().GetByEditionAsync(edition.EditionId)).Count);
    }

    /// <summary>
    /// Nothing here writes a lemma. ReM's TEI @lemma holds the normalised form,
    /// not a headword, and a Lemmas table filled with inflected forms would make
    /// Word Study look right and be wrong - it gates itself on lemma rows
    /// existing for a language, so writing them would switch it on with bad
    /// data. The real annotation is in ReM's Tabular JSON, which nothing here
    /// reads.
    /// </summary>
    [Fact]
    public async Task NoLemmasAreWritten()
    {
        using var db = await TempDatabase.CreateAsync();
        await new ReMIngestService().IngestAsync(Corpus(("M058", "Sangspruch", "-", TwoLines)));

        Assert.Equal(0, await new LemmaRepository().CountByLanguageAsync(ReMIngestService.Language));
        Assert.Equal(0, await new LemmaRepository().CountAsync());
    }

    /// <summary>
    /// A soft hyphen is not what breaks a word here - ReM marks that with
    /// join - but the corpus is read through the same tokenizer as everything
    /// else, so a line that does carry one still indexes as one word. Cheap to
    /// assert, and it pins that ReM text goes through the shared path rather
    /// than around it.
    /// </summary>
    [Fact]
    public async Task ReMTextIsIndexedThroughTheSharedTokenizer()
    {
        using var db = await TempDatabase.CreateAsync();

        await new ReMIngestService().IngestAsync(Corpus(("M058", "Sangspruch", "-",
            $@"<lb n=""1"" ed=""1""/>
               <w norm=""gratiam"" lemma=""gra{Shy} tiam"">gra{Shy} tiam</w>
               <lb n=""2"" ed=""1""/>")));

        var (_, editions) = await SingleWorkAsync();
        var edition = editions.Single(e => e.Orthography == "normalised");
        var line = Assert.Single(await new TextNodeRepository().GetByEditionAsync(edition.EditionId));

        Assert.Equal(new[] { "gratiam" }, ClassicaCodex.Core.WordNormalizer.TokenizeLine(line.Text).ToArray());
    }
}
