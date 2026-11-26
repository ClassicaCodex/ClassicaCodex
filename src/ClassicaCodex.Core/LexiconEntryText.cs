using System.Text.RegularExpressions;

namespace ClassicaCodex.Core;

/// <summary>
/// A dictionary entry as the reader should see it, wherever it is shown.
///
/// One place for this because there are several panes that show an entry,
/// and each copy of the rule was a chance to forget it: Word Study decoded
/// LSJ's Greek, and the translation workbench, showing the same entries
/// from the same table, printed them as "polu/s , Att. pollh/".
/// </summary>
public static class LexiconEntryText
{
    /// <summary>
    /// The entry with its Greek in Greek letters. LSJ stores its entry bodies
    /// in Beta Code, same as its headwords, so undecoded a definition reads as
    /// "sunair-esiw/ths , ou , o(". Only the Greek is touched; the English
    /// glosses interleaved with it are left alone. Every other dictionary's
    /// bodies are already in their own script and pass through untouched.
    /// </summary>
    public static string Decode(string entry, string? language) =>
        string.Equals(language, "grc", StringComparison.OrdinalIgnoreCase)
            ? BetaCodeConverter.ConvertMixed(entry)
            : entry;

    /// <summary>The entry decoded and set as prose: <see cref="Decode"/>, then <see cref="Reflow"/>.</summary>
    public static string ForReading(string entry, string? language) => Reflow(Decode(entry, language));

    /// <summary>
    /// A lexicon entry as prose. The ingested bodies carry a line break at
    /// every element boundary of the TEI they came from - around each cited
    /// Greek phrase, each abbreviation, each sense label - so set as written
    /// "μῆνις , Dor. and Aeol." is four lines. The breaks are joined back
    /// into running text, and kept only where a sense begins ("A.", "II.",
    /// "2."), which is where a printed lexicon starts a new paragraph too.
    /// Nothing but whitespace changes. A lone lower-case letter is not taken
    /// for a sense: LSJ abbreviates the headword to its initial ("m. χθονίων"
    /// under μῆνις), and those are mid-sentence.
    /// </summary>
    public static string Reflow(string body)
    {
        var paragraphs = new List<List<string>> { new() };

        foreach (var raw in body.Split('\n'))
        {
            var fragment = raw.Trim();
            if (fragment.Length == 0) continue;

            if (SenseLabel.IsMatch(fragment) && paragraphs[^1].Count > 0) paragraphs.Add(new List<string>());
            paragraphs[^1].Add(fragment);
        }

        return string.Join("\n\n", paragraphs.Where(p => p.Count > 0).Select(p => string.Join(" ", p)));
    }

    private static readonly Regex SenseLabel =
        new(@"^(?:[IVX]+|[A-Z]|\d+)\.(?:\s|$)", RegexOptions.CultureInvariant);
}
