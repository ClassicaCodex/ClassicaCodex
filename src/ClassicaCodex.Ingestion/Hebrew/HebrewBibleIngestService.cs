using System.Formats.Tar;
using System.IO.Compression;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Sefaria;

namespace ClassicaCodex.Ingestion.Hebrew;

/// <summary>
/// Puts the Hebrew Bible into the library: the Westminster Leningrad Codex
/// with every word parsed and keyed to Strong's dictionary, the 1917 Jewish
/// Publication Society translation beside it, and Strong's dictionary for
/// Word Study.
///
/// The first Hebrew in the library, and the text everything else in it
/// argues with or about: the Septuagint is a translation of it, the New
/// Testament quotes it, Augustine and Jerome comment on it, and the satan,
/// the seraphim, the cherubim and the "angel of the LORD" all start here.
///
/// <b>Three sources, each with its own terms, none restrictive.</b>
///   - The Leningrad Codex (1008 CE) as transcribed by the Westminster
///     Hebrew Institute is in the public domain; the Open Scriptures Hebrew
///     Bible's lemmas and morphology on top of it are CC BY 4.0.
///   - The JPS translation of 1917 is in the public domain, and Sefaria, which
///     serves it, says so on the version itself.
///   - Strong's dictionary (1890) is in the public domain; the Open Scriptures
///     project's corrected encoding of it is CC BY 4.0.
///
/// <b>The two texts share their verse numbers, which is why this translation
/// and not another.</b> JPS 1917 follows the Hebrew chapter and verse
/// divisions - the Psalm headings as verse 1, Malachi in three chapters, Joel
/// in four - so "Psalm 91.5" opens the same verse in both panes. The King
/// James Version numbers these differently, and the World English Bible
/// follows the KJV.
/// </summary>
public class HebrewBibleIngestService
{
    public const string CollectionKey = "hebrew-bible";
    public const string Namespace = "hbo";

    /// <summary>
    /// ISO 639-3 "hbo", Ancient Hebrew. The Aramaic chapters of Daniel and Ezra
    /// are filed under it too: they are in the same books, and splitting a
    /// book across two languages would split it across two editions.
    /// </summary>
    public const string Language = "hbo";

    /// <summary>Pinned to a release, so that a reader's citations are made against a known text.</summary>
    public const string Release = "v.2.2";

    public static string ArchiveUrl =>
        $"https://codeload.github.com/openscriptures/morphhb/tar.gz/refs/tags/{Release}";

    public const string ArchiveFileName = "morphhb.tar.gz";

    /// <summary>
    /// The dictionary, pinned to the commit that last changed it (September
    /// 2019) rather than to a branch, for the same reason as the text.
    /// </summary>
    public const string LexiconUrl =
        "https://raw.githubusercontent.com/openscriptures/HebrewLexicon/21c9add13bc727d3a951361778e97e3ff7afd1ce/HebrewStrong.xml";

    public const string LexiconFileName = "HebrewStrong.xml";

    public const string TranslationVersion = "The Holy Scriptures: A New Translation (JPS 1917)";

    public const string TranslationCredit = "Jewish Publication Society (1917)";

    public const string LexiconSource =
        "Strong's Hebrew and Chaldee Dictionary (1890), as corrected by the Open Scriptures Hebrew Bible project";

    /// <summary>
    /// The books in the order of the Hebrew canon - Torah, Prophets, Writings -
    /// with the OSIS name OSHB files them under and the name Sefaria uses.
    /// </summary>
    public static readonly IReadOnlyList<(string Osis, string Title, string Sefaria)> Books = new[]
    {
        ("Gen", "Genesis", "Genesis"), ("Exod", "Exodus", "Exodus"), ("Lev", "Leviticus", "Leviticus"),
        ("Num", "Numbers", "Numbers"), ("Deut", "Deuteronomy", "Deuteronomy"),
        ("Josh", "Joshua", "Joshua"), ("Judg", "Judges", "Judges"),
        ("1Sam", "1 Samuel", "I Samuel"), ("2Sam", "2 Samuel", "II Samuel"),
        ("1Kgs", "1 Kings", "I Kings"), ("2Kgs", "2 Kings", "II Kings"),
        ("Isa", "Isaiah", "Isaiah"), ("Jer", "Jeremiah", "Jeremiah"), ("Ezek", "Ezekiel", "Ezekiel"),
        ("Hos", "Hosea", "Hosea"), ("Joel", "Joel", "Joel"), ("Amos", "Amos", "Amos"),
        ("Obad", "Obadiah", "Obadiah"), ("Jonah", "Jonah", "Jonah"), ("Mic", "Micah", "Micah"),
        ("Nah", "Nahum", "Nahum"), ("Hab", "Habakkuk", "Habakkuk"), ("Zeph", "Zephaniah", "Zephaniah"),
        ("Hag", "Haggai", "Haggai"), ("Zech", "Zechariah", "Zechariah"), ("Mal", "Malachi", "Malachi"),
        ("Ps", "Psalms", "Psalms"), ("Prov", "Proverbs", "Proverbs"), ("Job", "Job", "Job"),
        ("Song", "Song of Songs", "Song of Songs"), ("Ruth", "Ruth", "Ruth"),
        ("Lam", "Lamentations", "Lamentations"), ("Eccl", "Ecclesiastes", "Ecclesiastes"),
        ("Esth", "Esther", "Esther"), ("Dan", "Daniel", "Daniel"), ("Ezra", "Ezra", "Ezra"),
        ("Neh", "Nehemiah", "Nehemiah"), ("1Chr", "1 Chronicles", "I Chronicles"),
        ("2Chr", "2 Chronicles", "II Chronicles"),
    };

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();
    private readonly DefinitionRepository _definitionRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly FileDownloadService _downloader = new();

    public int BooksInstalled { get; private set; }
    public int VersesInstalled { get; private set; }
    public int TranslatedVerses { get; private set; }
    public long MappingsLoaded { get; private set; }
    public int DefinitionsLoaded { get; private set; }

    /// <summary>Books whose translation could not be fetched or read, with the reason.</summary>
    public List<(string Book, string Error)> Failed { get; } = new();

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);

        var books = await EnsureTextAsync(root, progress, cancellationToken);
        var lexicon = await EnsureLexiconAsync(root, progress, cancellationToken);

        var headwords = lexicon.ToDictionary(e => e.Number, e => e.Headword, StringComparer.Ordinal);

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:hbo:hebrew-bible",
            Name = "Hebrew Bible",
            Namespace = Namespace,
            Language = Language
        }, cancellationToken);

        var lemmas = new List<Lemma>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (osis, title, sefaria) in Books)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!books.TryGetValue(osis, out var path))
            {
                Failed.Add((title, "not in the archive"));
                continue;
            }

            var book = HebrewBibleLoader.LoadBook(path);
            progress.Report($"{title}: {book.Verses.Count:N0} verses...");

            var translation = await FetchTranslationAsync(root, sefaria, cancellationToken);
            await InstallAsync(authorId, osis, title, book, translation, cancellationToken);

            BooksInstalled++;
            VersesInstalled += book.Verses.Count;

            foreach (var word in book.Verses.SelectMany(v => v.Words))
            {
                var normalized = WordNormalizer.Normalize(word.Form);
                if (normalized.Length == 0) continue;

                // A word that is nothing but particles has no entry of its own,
                // and a number the dictionary lacks has no headword to show.
                if (!headwords.TryGetValue(StrongsNumber(word.Headword), out var headword)) continue;

                var key = $"{normalized}\u0001{headword}\u0001{word.Morph}";
                if (!seen.Add(key)) continue;

                lemmas.Add(new Lemma
                {
                    Form = word.Form,
                    NormalizedForm = normalized,
                    Headword = headword,
                    Language = Language,
                    PartOfSpeech = word.Morph
                });
            }
        }

        if (lemmas.Count > 0)
        {
            await _lemmaRepo.ClearByLanguageAsync(Language, cancellationToken);
            await _lemmaRepo.BulkInsertAsync(lemmas, cancellationToken);
            MappingsLoaded = lemmas.Count;
        }

        if (lexicon.Count > 0)
        {
            await _definitionRepo.ClearByLanguageAsync(Language, cancellationToken);
            await _definitionRepo.BulkInsertAsync(lexicon.Select(e => new Definition
            {
                Headword = e.Headword,
                NormalizedHeadword = WordNormalizer.NormalizeHeadword(e.Headword, Language),
                Language = Language,
                Entry = e.Entry,
                Source = LexiconSource
            }).ToList(), cancellationToken);
            DefinitionsLoaded = lexicon.Count;
        }

        progress.Report(
            $"{BooksInstalled} books, {VersesInstalled:N0} verses ({TranslatedVerses:N0} with the JPS translation), "
            + $"{MappingsLoaded:N0} word-form mappings, {DefinitionsLoaded:N0} dictionary entries.");

        return IngestOutcome.From(Failed, attempted: Books.Count);
    }

    /// <summary>
    /// "1254 a" is entry 1254: OSHB's letters split homographs that Strong's
    /// prints as one entry.
    /// </summary>
    public static string StrongsNumber(string headword)
    {
        var space = headword.IndexOf(' ');
        return space < 0 ? headword : headword[..space];
    }

    private async Task InstallAsync(
        int authorId, string osis, string title, HebrewBook book, SefariaVersion? translation,
        CancellationToken cancellationToken)
    {
        var slug = osis.ToLowerInvariant();

        var workId = await _workRepo.UpsertAsync(new Work
        {
            AuthorId = authorId,
            CtsUrn = $"urn:hbo:hebrew-bible:{slug}",
            Title = title,
            CitationScheme = "Chapter.Verse"
        }, cancellationToken);

        var hebrewId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = $"urn:hbo:hebrew-bible:{slug}:wlc",
            Kind = EditionKind.Original,
            Language = Language,
            SourcePath = $"urn:hbo:hebrew-bible:{slug}:wlc",
            Collection = CollectionKey
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(hebrewId, cancellationToken);
        await _textNodeRepo.BulkInsertAsync(book.Verses
            .Select((verse, i) => new TextNode
            {
                EditionId = hebrewId,
                CitationRef = verse.Citation,
                SortOrder = i + 1,
                Text = verse.Text,

                // Psalms, Job and Proverbs are poetry and the Masoretes accented
                // them with a system of their own; but OSHB marks no lines,
                // and a verse of Genesis is set the same way as a verse of
                // Job. Nothing here would make the claim honestly.
                IsVerse = false
            })
            .Where(n => n.Text.Length > 0)
            .ToList(), cancellationToken);

        await _headerRepo.SaveAsync(hebrewId, new EditionHeader
        {
            Title = $"{title} (Westminster Leningrad Codex)",
            Responsibilities = new[] { "Morphology and lemmas: Open Scriptures Hebrew Bible (Daniel Owens, David Troidl)" },
            SourceDescription = "Westminster Leningrad Codex 4.20, a transcription of the Leningrad Codex (1008 CE), "
                + $"from the Open Scriptures Hebrew Bible, release {Release}.",
            Availability = "Text: public domain. Morphology and lemmas: CC BY 4.0, \"Original work of the Open "
                + "Scriptures Hebrew Bible available at https://github.com/openscriptures/morphhb\"."
        }, cancellationToken);

        if (translation == null) return;

        var englishId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = $"urn:hbo:hebrew-bible:{slug}:jps1917",
            Kind = EditionKind.Translation,
            Language = "eng",
            Translator = TranslationCredit,
            SourcePath = $"urn:hbo:hebrew-bible:{slug}:jps1917",
            Collection = CollectionKey
        }, cancellationToken);

        await _editionRepo.ClearTextNodesAsync(englishId, cancellationToken);
        var nodes = AlignToHebrew(osis, translation.Passages)
            .Select((p, i) => new TextNode
            {
                EditionId = englishId,
                CitationRef = p.Citation,
                SortOrder = i + 1,
                Text = p.Text,
                IsVerse = false
            })
            .ToList();
        await _textNodeRepo.BulkInsertAsync(nodes, cancellationToken);
        TranslatedVerses += nodes.Count;

        await _headerRepo.SaveAsync(englishId, new EditionHeader
        {
            Title = $"{title} (The Holy Scriptures according to the Masoretic Text, 1917)",
            Publisher = "Jewish Publication Society of America",
            PublicationDate = "1917",
            PublicationPlace = "Philadelphia",
            SourceDescription = $"Fetched from Sefaria, version \"{translation.VersionTitle}\""
                + (translation.VersionSource == null ? "." : $" (source: {translation.VersionSource})."),
            Availability = translation.License.Length > 0 ? translation.License : "Public Domain"
        }, cancellationToken);
    }

    /// <summary>
    /// Puts the three places where the 1917 translation numbers verses
    /// differently from the Leningrad Codex onto the Hebrew's numbers.
    ///
    /// <b>The Ten Commandments, twice.</b> The Masoretes accented the
    /// Decalogue in two ways, and the verse numbers follow the one that makes
    /// "You shall not murder", "You shall not commit adultery", "You shall not
    /// steal" and "You shall not bear false witness" four verses. JPS 1917
    /// prints the four as one verse - Exodus 20.13, Deuteronomy 5.17 - so
    /// without this every later verse of both chapters sits three verses away
    /// from its Hebrew, and the last three verses of each are missing from the
    /// English pane altogether. They are split back into four at the sentence
    /// ends, which is where the Hebrew divides them, and the rest of the
    /// chapter moves up by three. If the verse is ever not four sentences the
    /// chapter is left as Sefaria gave it: misaligned, but not mangled.
    ///
    /// <b>Numbers 25.19</b> is three Hebrew words, "and it came to pass after
    /// the plague", which JPS joins to the next chapter's first sentence.
    /// Divided at the comma that ends them.
    /// </summary>
    public static IReadOnlyList<(string Citation, string Text)> AlignToHebrew(
        string osis, IReadOnlyList<(string Citation, string Text)> passages)
    {
        if (osis == "Num")
        {
            const string Opening = "And it came to pass after the plague, ";
            var result = new List<(string, string)>(passages.Count + 1);
            foreach (var (citation, text) in passages)
            {
                if (citation == "26.1" && text.StartsWith(Opening, StringComparison.Ordinal))
                {
                    result.Add(("25.19", Opening.TrimEnd()));
                    result.Add(("26.1", text[Opening.Length..]));
                }
                else
                {
                    result.Add((citation, text));
                }
            }

            return result;
        }

        var (chapter, verse) = osis switch
        {
            "Exod" => (20, 13),
            "Deut" => (5, 17),
            _ => (0, 0)
        };
        if (chapter == 0) return passages;

        var joined = passages.FirstOrDefault(p => p.Citation == $"{chapter}.{verse}");
        if (joined.Text == null) return passages;

        var commandments = SentenceEnd.Split(joined.Text);
        if (commandments.Length != 4) return passages;

        var aligned = new List<(string, string)>(passages.Count + 3);
        foreach (var (citation, text) in passages)
        {
            var parts = citation.Split('.');
            var c = int.Parse(parts[0]);
            var v = int.Parse(parts[1]);

            if (c == chapter && v == verse)
            {
                for (var i = 0; i < 4; i++) aligned.Add(($"{c}.{v + i}", commandments[i]));
            }
            else if (c == chapter && v > verse)
            {
                aligned.Add(($"{c}.{v + 3}", text));
            }
            else
            {
                aligned.Add((citation, text));
            }
        }

        return aligned;
    }

    private static readonly System.Text.RegularExpressions.Regex SentenceEnd = new(@"(?<=\.)\s+");

    /// <summary>
    /// The JPS translation of one book, fetched once and kept. Null when it
    /// cannot be had - the Hebrew is installed regardless, and the book is
    /// reported as missing its translation rather than missing altogether.
    ///
    /// The licence is checked on every book, not assumed from the first: the
    /// version title names a public-domain translation, and if Sefaria ever
    /// served something under other terms under that title it should not be
    /// installed as if it were the same thing.
    /// </summary>
    private async Task<SefariaVersion?> FetchTranslationAsync(
        string root, string book, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(root, "jps1917");
        var path = Path.Combine(folder, book + ".json");

        try
        {
            if (!File.Exists(path))
            {
                await _downloader.DownloadAsync(
                    SefariaTextLoader.UrlFor(book, "english", TranslationVersion), path,
                    new Progress<string>(_ => { }), cancellationToken);
            }

            var version = SefariaTextLoader.Load(path);

            if (!string.Equals(version.VersionTitle, TranslationVersion, StringComparison.Ordinal))
            {
                Failed.Add((book, $"Sefaria returned \"{version.VersionTitle}\" instead of the JPS 1917 translation"));
                return null;
            }

            if (!version.License.Contains("Public Domain", StringComparison.OrdinalIgnoreCase))
            {
                Failed.Add((book, $"Sefaria lists this translation as \"{version.License}\", not public domain"));
                return null;
            }

            return version;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A bad download should not be kept and re-read on every run.
            try { File.Delete(path); } catch { /* best effort */ }
            Failed.Add((book, ex.GetBaseException().Message));
            return null;
        }
    }

    /// <summary>The OSIS file of every book, downloading and extracting the release once.</summary>
    private async Task<Dictionary<string, string>> EnsureTextAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var existing = BookFiles(root);
        if (existing.Count >= Books.Count) return existing;

        var archive = Path.Combine(root, ArchiveFileName);
        if (!File.Exists(archive))
        {
            progress.Report("Downloading the Hebrew text (about 20 MB)...");
            await _downloader.DownloadAsync(ArchiveUrl, archive, progress, cancellationToken);
        }

        progress.Report("Extracting...");
        await using (var file = File.OpenRead(archive))
        await using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, root, overwriteFiles: true, cancellationToken);
        }

        return BookFiles(root);
    }

    /// <summary>
    /// The book files under the release's wlc folder, by OSIS name. VerseMap.xml
    /// sits beside them and is not a book.
    /// </summary>
    public static Dictionary<string, string> BookFiles(string root)
    {
        var names = Books.Select(b => b.Osis).ToHashSet(StringComparer.Ordinal);

        return Directory.Exists(root)
            ? Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories)
                .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "wlc", StringComparison.OrdinalIgnoreCase))
                .Where(f => names.Contains(Path.GetFileNameWithoutExtension(f)))
                .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private async Task<List<StrongsEntry>> EnsureLexiconAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, LexiconFileName);
        if (!File.Exists(path))
        {
            progress.Report("Downloading Strong's Hebrew dictionary (about 3 MB)...");
            await _downloader.DownloadAsync(LexiconUrl, path, progress, cancellationToken);
        }

        return StrongsLexiconLoader.Load(path);
    }
}
