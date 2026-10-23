using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion.Pseudepigrapha;

namespace ClassicaCodex.Ingestion.Quran;

/// <summary>
/// The Quran in Arabic, in the two texts the Tanzil Project publishes, with
/// Marmaduke Pickthall's English of 1930 beside them.
///
/// The library had no Arabic beyond one translation of Aristotle, and for a
/// study of angels and demons the Quran is the missing third of the
/// tradition: Iblis who would not bow to Adam, Harut and Marut, the jinn of
/// Sura 72, the angels who take souls and record deeds.
///
/// <b>Two Arabic texts, both originals.</b> The Uthmani text is the
/// orthography of the Madina mushaf, the printed Quran most readers use; the
/// simple text spells the same words as modern Arabic would (Tanzil calls it
/// imla'i). They differ in spelling and not in a single word, so both are
/// filed as originals and told apart by their orthography - see
/// EditionLabels - in the way the Pyramid Texts' hieroglyphs and
/// transliteration are. A search typed in either spelling finds the verse in
/// at least one of them. Measured over the whole text, 89% of the words in
/// the simple text are indexed identically in the Uthmani once the marks are
/// set aside; the rest are where Uthmani spelling leaves a long a as a small
/// alef above the line - al-kitab, the book, as al-ktb - or writes the
/// vocative ya into its noun, and folding cannot reach those without
/// inventing spellings.
///
/// <b>The text is installed exactly as Tanzil publishes it.</b> Its terms
/// allow verbatim copies only, and require the copyright notice to go with
/// every copy, so the notice is stored as the editions' availability
/// statement word for word and nothing in the Arabic is touched. Search
/// folding happens in the index, not in the text.
///
/// One work per sura, as the Hebrew Bible has one per book: a sura is what a
/// reader opens, and the work list is then the table of contents. The names
/// are the conventional transliterations with an English gloss, written out
/// here rather than taken from any source, since none of the sources agrees
/// with another about them.
/// </summary>
public class QuranIngestService
{
    public const string CollectionKey = "quran";

    public const string Language = "ara";

    private const string Gutenberg = "https://gutenberg.pglaf.org/1/6/9/5/16955/16955.txt";

    private readonly AuthorRepository _authorRepo = new();
    private readonly WorkRepository _workRepo = new();
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly EditionHeaderRepository _headerRepo = new();
    private readonly FileDownloadService _downloader = new();

    public int SurasInstalled { get; private set; }
    public int VersesInstalled { get; private set; }

    public List<(string Text, string Error)> Failed { get; } = new();

    /// <summary>
    /// Tanzil's own download form, with its defaults: pause marks, sajdah
    /// signs, superscript alefs and the tatweel under them, and no
    /// rub el-hizb signs. The agreement is to its terms, which this
    /// application keeps by installing the text unaltered with its notice.
    /// </summary>
    public static string TanzilUrl(string type) =>
        "https://tanzil.net/pub/download/index.php?marks=true&sajdah=true&alef=true&tatweel=true"
        + $"&quranType={type}&outType=xml&agree=true";

    public async Task<IngestOutcome> IngestAsync(
        string root, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(root);

        progress.Report("The Quran: the Uthmani and simple texts from Tanzil...");
        var (uthmani, uthmaniNotice) = QuranLoader.ParseTanzil(await File.ReadAllTextAsync(
            await FetchAsync(root, "tanzil-uthmani.xml", TanzilUrl("uthmani"), cancellationToken), cancellationToken));
        var (simple, simpleNotice) = QuranLoader.ParseTanzil(await File.ReadAllTextAsync(
            await FetchAsync(root, "tanzil-simple.xml", TanzilUrl("simple"), cancellationToken), cancellationToken));

        if (uthmani.Count != Suras.Count || simple.Count != Suras.Count)
        {
            throw new InvalidDataException($"Tanzil's text has {uthmani.Count} and {simple.Count} suras, not {Suras.Count}");
        }

        // The translation is worth having and not worth failing the Arabic
        // over: a reader who cannot reach Gutenberg still gets the Quran.
        ILookup<int, (int Sura, int Verse, string Text)>? english = null;
        try
        {
            progress.Report("The Quran: Pickthall's translation from Project Gutenberg...");
            var path = await FetchAsync(root, "pickthall-16955.txt", Gutenberg, cancellationToken);
            english = QuranLoader.Pickthall(await File.ReadAllTextAsync(path, cancellationToken)).ToLookup(v => v.Sura);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Failed.Add(("Pickthall's translation", ex.GetBaseException().Message));
        }

        var authorId = await _authorRepo.UpsertAsync(new Author
        {
            CtsUrn = "urn:quran",
            Name = "The Quran",
            Namespace = "quran",
            Language = Language
        }, cancellationToken);

        for (var i = 0; i < Suras.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var number = i + 1;
            var (name, gloss) = Suras[i];
            if (number % 10 == 1) progress.Report($"The Quran: sura {number} of {Suras.Count}...");

            var workId = await _workRepo.UpsertAsync(new Work
            {
                AuthorId = authorId,
                CtsUrn = $"urn:quran:{number}",
                Title = $"Sura {number}: {name} ({gloss})",
                CitationScheme = "Sura.Verse"
            }, cancellationToken);

            await EditionAsync(workId, $"urn:quran:{number}:tanzil-uthmani", EditionKind.Original, Language, null,
                "uthmani", Arabic(uthmani[i], number), TanzilHeader("Uthmani", uthmaniNotice,
                    "the Uthmani script of the Madina mushaf (King Fahd Complex, Medina), in the Hafs reading"),
                cancellationToken);

            await EditionAsync(workId, $"urn:quran:{number}:tanzil-simple", EditionKind.Original, Language, null,
                "simple", Arabic(simple[i], number), TanzilHeader("Simple", simpleNotice,
                    "the same text in the simple, or imla'i, spelling of modern Arabic"),
                cancellationToken);

            var verses = english?[number].Select(v => ($"{number}.{v.Verse}", v.Text, false)).ToList();
            if (verses is { Count: > 0 })
            {
                await EditionAsync(workId, $"urn:quran:{number}:pickthall", EditionKind.Translation, "eng",
                    "Marmaduke Pickthall (1930)", null, verses, PickthallHeader, cancellationToken);
            }

            SurasInstalled++;
            VersesInstalled += uthmani[i].Verses.Count;
        }

        progress.Report($"{SurasInstalled} suras, {VersesInstalled:N0} verses.");
        return IngestOutcome.From(Failed);
    }

    /// <summary>
    /// A sura's passages: the basmala as the sura's heading where the mushaf
    /// prints one, then the verses, cited sura and verse as the Quran is
    /// cited everywhere (2.255).
    /// </summary>
    public static List<(string Citation, string Text, bool IsHead)> Arabic(TanzilSura sura, int number)
    {
        if (sura.Number != number) throw new InvalidDataException($"Sura {sura.Number} found where {number} belongs");

        var passages = new List<(string, string, bool)>();
        if (!string.IsNullOrEmpty(sura.Basmala)) passages.Add(($"{number}.head", sura.Basmala, true));
        passages.AddRange(sura.Verses.Select(v => ($"{number}.{v.Verse}", v.Text, false)));
        return passages;
    }

    private static EditionHeader TanzilHeader(string type, string notice, string what) => new()
    {
        Title = $"The Quran, Tanzil Quran Text ({type}, version 1.1)",
        Publisher = "Tanzil Project, tanzil.net",
        PublicationDate = "2021",
        SourceDescription = $"Tanzil Quran Text ({type}), version 1.1 (February 2021), from https://tanzil.net: "
            + $"{what}. Reproduced verbatim, with pause marks and sajdah signs; the basmala that heads a sura "
            + "is shown as its heading, as Tanzil's XML gives it.",
        Availability = notice
    };

    private static readonly EditionHeader PickthallHeader = new()
    {
        Title = "The Meaning of the Glorious Koran, translated by Marmaduke Pickthall",
        Publisher = "Alfred A. Knopf",
        PublicationPlace = "New York",
        PublicationDate = "1930",
        SourceDescription = "Marmaduke Pickthall, The Meaning of the Glorious Koran: An Explanatory Translation "
            + "(London and New York, 1930), from Project Gutenberg's \"Three Translations of The Koran\" (#16955, "
            + "2005), re-proofed against the printed translation. Only Pickthall's is taken of the three.",
        Availability = "Public domain (Pickthall died in 1936)."
    };

    private async Task<string> FetchAsync(string root, string relative, string url, CancellationToken ct)
    {
        var path = Path.Combine(root, relative);
        if (File.Exists(path)) return path;

        await _downloader.DownloadAsync(url, path, new Progress<string>(_ => { }), ct);
        return path;
    }

    private async Task EditionAsync(
        int workId, string urn, EditionKind kind, string language, string? translator, string? orthography,
        List<(string Citation, string Text, bool IsHead)> passages, EditionHeader header, CancellationToken ct)
    {
        if (passages.Count == 0) throw new InvalidDataException($"{urn}: no passages");

        var editionId = await _editionRepo.UpsertAsync(new Edition
        {
            WorkId = workId,
            CtsUrn = urn,
            Kind = kind,
            Language = language,
            Translator = translator,
            SourcePath = urn,
            Orthography = orthography,
            Collection = CollectionKey
        }, ct);

        await _editionRepo.ClearTextNodesAsync(editionId, ct);
        await _textNodeRepo.BulkInsertAsync(PseudepigraphaIngestService.Nodes(editionId, passages), ct);
        await _headerRepo.SaveAsync(editionId, header, ct);
    }

    /// <summary>The 114 suras in order: the name in transliteration, and what it means.</summary>
    public static readonly IReadOnlyList<(string Name, string Gloss)> Suras = new (string, string)[]
    {
        ("Al-Fatihah", "The Opening"), ("Al-Baqarah", "The Cow"), ("Al Imran", "The Family of Imran"),
        ("An-Nisa", "Women"), ("Al-Ma'idah", "The Table Spread"), ("Al-An'am", "Cattle"),
        ("Al-A'raf", "The Heights"), ("Al-Anfal", "The Spoils of War"), ("At-Tawbah", "Repentance"),
        ("Yunus", "Jonah"), ("Hud", "Hud"), ("Yusuf", "Joseph"),
        ("Ar-Ra'd", "The Thunder"), ("Ibrahim", "Abraham"), ("Al-Hijr", "The Rocky Tract"),
        ("An-Nahl", "The Bee"), ("Al-Isra", "The Night Journey"), ("Al-Kahf", "The Cave"),
        ("Maryam", "Mary"), ("Ta Ha", "Ta Ha"), ("Al-Anbiya", "The Prophets"),
        ("Al-Hajj", "The Pilgrimage"), ("Al-Mu'minun", "The Believers"), ("An-Nur", "The Light"),
        ("Al-Furqan", "The Criterion"), ("Ash-Shu'ara", "The Poets"), ("An-Naml", "The Ants"),
        ("Al-Qasas", "The Story"), ("Al-Ankabut", "The Spider"), ("Ar-Rum", "The Romans"),
        ("Luqman", "Luqman"), ("As-Sajdah", "The Prostration"), ("Al-Ahzab", "The Confederates"),
        ("Saba", "Sheba"), ("Fatir", "The Originator"), ("Ya Sin", "Ya Sin"),
        ("As-Saffat", "Those Ranged in Ranks"), ("Sad", "Sad"), ("Az-Zumar", "The Troops"),
        ("Ghafir", "The Forgiver"), ("Fussilat", "Explained in Detail"), ("Ash-Shura", "Consultation"),
        ("Az-Zukhruf", "Ornaments of Gold"), ("Ad-Dukhan", "Smoke"), ("Al-Jathiyah", "Crouching"),
        ("Al-Ahqaf", "The Sand Dunes"), ("Muhammad", "Muhammad"), ("Al-Fath", "Victory"),
        ("Al-Hujurat", "The Private Rooms"), ("Qaf", "Qaf"), ("Adh-Dhariyat", "The Winnowing Winds"),
        ("At-Tur", "The Mount"), ("An-Najm", "The Star"), ("Al-Qamar", "The Moon"),
        ("Ar-Rahman", "The Merciful"), ("Al-Waqi'ah", "The Event"), ("Al-Hadid", "Iron"),
        ("Al-Mujadilah", "The Pleading Woman"), ("Al-Hashr", "The Gathering"), ("Al-Mumtahanah", "The Woman Examined"),
        ("As-Saff", "The Ranks"), ("Al-Jumu'ah", "Friday"), ("Al-Munafiqun", "The Hypocrites"),
        ("At-Taghabun", "Mutual Loss"), ("At-Talaq", "Divorce"), ("At-Tahrim", "Prohibition"),
        ("Al-Mulk", "Sovereignty"), ("Al-Qalam", "The Pen"), ("Al-Haqqah", "The Reality"),
        ("Al-Ma'arij", "The Ways of Ascent"), ("Nuh", "Noah"), ("Al-Jinn", "The Jinn"),
        ("Al-Muzzammil", "The Wrapped One"), ("Al-Muddaththir", "The Cloaked One"), ("Al-Qiyamah", "The Resurrection"),
        ("Al-Insan", "Man"), ("Al-Mursalat", "Those Sent Forth"), ("An-Naba", "The Tidings"),
        ("An-Nazi'at", "Those Who Pull Out"), ("Abasa", "He Frowned"), ("At-Takwir", "The Folding Up"),
        ("Al-Infitar", "The Cleaving"), ("Al-Mutaffifin", "The Defrauders"), ("Al-Inshiqaq", "The Splitting Open"),
        ("Al-Buruj", "The Constellations"), ("At-Tariq", "The Night Visitor"), ("Al-A'la", "The Most High"),
        ("Al-Ghashiyah", "The Overwhelming"), ("Al-Fajr", "The Dawn"), ("Al-Balad", "The City"),
        ("Ash-Shams", "The Sun"), ("Al-Layl", "The Night"), ("Ad-Duha", "The Morning Brightness"),
        ("Ash-Sharh", "The Opening Up"), ("At-Tin", "The Fig"), ("Al-Alaq", "The Clot"),
        ("Al-Qadr", "Power"), ("Al-Bayyinah", "The Clear Proof"), ("Az-Zalzalah", "The Earthquake"),
        ("Al-Adiyat", "The Chargers"), ("Al-Qari'ah", "The Calamity"), ("At-Takathur", "Rivalry in Increase"),
        ("Al-Asr", "The Declining Day"), ("Al-Humazah", "The Slanderer"), ("Al-Fil", "The Elephant"),
        ("Quraysh", "Quraysh"), ("Al-Ma'un", "Small Kindnesses"), ("Al-Kawthar", "Abundance"),
        ("Al-Kafirun", "The Disbelievers"), ("An-Nasr", "Help"), ("Al-Masad", "Palm Fibre"),
        ("Al-Ikhlas", "Sincerity"), ("Al-Falaq", "The Daybreak"), ("An-Nas", "Mankind")
    };
}
