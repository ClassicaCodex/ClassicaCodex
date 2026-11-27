using System.Globalization;
using System.Reflection;
using System.Text;

namespace ClassicaCodex.Core.Citations;

/// <summary>An abbreviation as a dictionary prints it, the work it names, and in how many of its citations.</summary>
public sealed record AbbreviationUse(string Abbreviation, string Work, int Citations);

/// <summary>An orator cited by speech: under this abbreviation, the speech with this number is this work.</summary>
public sealed record SpeechNumber(string Abbreviation, int Number, string Work);

/// <summary>
/// How LSJ and Lewis and Short abbreviate the works they cite - "Pl. R." for
/// Plato's Republic, "Verg. A." for the Aeneid, "Th." for Thucydides - and
/// which work each names, as CitationAbbreviations.tsv records it. Built by
/// tools/CitationAbbreviations from the citations Perseus gave a CTS URN, so
/// every abbreviation here was seen naming its work, most of them thousands
/// of times.
///
/// The dictionaries are where nearly every way of abbreviating a classical
/// author was fixed, and a modern footnote's "Thuc." or "Pl. Resp." is
/// usually theirs, or theirs lengthened; <see cref="WorkAbbreviations"/> reads
/// those.
/// </summary>
public sealed class CitationAbbreviations
{
    private const string ResourceName = "ClassicaCodex.Core.Citations.CitationAbbreviations.tsv";

    private static readonly Lazy<CitationAbbreviations> Bundled = new(Load);

    public CitationAbbreviations(IReadOnlyList<AbbreviationUse> uses, IReadOnlyList<SpeechNumber> speeches)
    {
        Uses = uses;
        Speeches = speeches;
    }

    /// <summary>The table that ships inside the app.</summary>
    public static CitationAbbreviations Default => Bundled.Value;

    public IReadOnlyList<AbbreviationUse> Uses { get; }

    public IReadOnlyList<SpeechNumber> Speeches { get; }

    /// <summary>
    /// An abbreviation reduced to what is compared: letters and digits only,
    /// in lower case and without accents, so that "Caes. B. G.", "Caes. BG"
    /// and "caes. b.g." are one, and LSJ's "Aët." is "aet". J is i and v is u,
    /// as Latin titles are spelled both ways: "Iug." and "Jug.", "Divus
    /// Iulius" and "Divus Julius".
    /// </summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(Fold(char.ToLowerInvariant(c)));
        }

        return sb.ToString();
    }

    /// <summary>
    /// The same, word by word, the words kept apart: "Ar. Ran." is "ar ran"
    /// and LSJ's "Arr. An." is "arr an", which <see cref="Normalize"/> would
    /// make one. A point ends a word even with no space after it, so "A.R."
    /// (Apollonius of Rhodes) is "a r", not the "ar" of Aristophanes.
    /// </summary>
    public static string NormalizeWords(string text) =>
        string.Join(' ', text.Split(new[] { ' ', ',', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(Normalize).Where(w => w.Length > 0));

    /// <summary>j as i and v as u.</summary>
    public static char Fold(char c) => c switch { 'j' => 'i', 'v' => 'u', _ => c };

    /// <summary>The table read from its lines: comments start with #, a row is tab-separated.</summary>
    public static CitationAbbreviations Parse(IEnumerable<string> lines)
    {
        var uses = new List<AbbreviationUse>();
        var speeches = new List<SpeechNumber>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var cells = line.Split('\t');
            if (cells.Length < 4) continue;
            if (cells[0] == "a" && int.TryParse(cells[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                uses.Add(new AbbreviationUse(cells[1], cells[2], count));
            else if (cells[0] == "s" && int.TryParse(cells[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                speeches.Add(new SpeechNumber(cells[1], number, cells[3]));
        }

        return new CitationAbbreviations(uses, speeches);
    }

    private static CitationAbbreviations Load()
    {
        using var stream = typeof(CitationAbbreviations).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"{ResourceName} is missing from {Assembly.GetExecutingAssembly().GetName().Name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return Parse(lines);
    }
}
