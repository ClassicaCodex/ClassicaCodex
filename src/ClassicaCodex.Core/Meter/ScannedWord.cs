using System.Text;

namespace ClassicaCodex.Core.Meter;

/// <summary>
/// One word of a scanned line, with what the metre made of each of its
/// syllables.
///
/// The point of this is not the scansion of the line, which a reader can see
/// for themselves, but the quantity of a particular word - because Perseus
/// prints no macrons and Latin spelling does not distinguish a great many
/// pairs that the metre does. "cano" is the first word of the Aeneid and its
/// final o is long, which makes it a verb; "puella" and "puella" are
/// nominative and ablative and differ in nothing but the length of a vowel no
/// edition marks.
///
/// Measured over Virgil, Ovid, Lucretius and Juvenal - 33,114 verse lines -
/// the metre settles 75.1% of the syllables the spelling cannot call, and
/// 74.8% of words in a scanned line come out with every syllable determined.
/// </summary>
public sealed record ScannedWord(
    int WordIndex,
    string Text,
    IReadOnlyList<ScannedSyllable> Syllables)
{
    /// <summary>
    /// The quantities, one mark per syllable, in order. See
    /// <see cref="ScannedSyllable.Mark"/> for why these three characters.
    ///
    /// Marks rather than a syllable-by-syllable division of the word, because
    /// a division is not available: a ProsodicSyllable carries its vowel
    /// nucleus and not the consonants around it, so "arma" comes back as two
    /// syllables reading "a" and "a". Printing that as "a-a" beside the word
    /// would look like a syllabification and be a wrong one. Two marks against
    /// a two-syllable word is less and true.
    /// </summary>
    public string Pattern => string.Join(" ", Syllables.Select(s => s.Mark));

    /// <summary>Whether the metre settled every syllable of this word.</summary>
    public bool FullyResolved =>
        Syllables.Count > 0 && Syllables.All(s => s.Quantity != Quantity.Unknown);

    /// <summary>
    /// Whether the metre says anything about this word at all - false for a
    /// word wholly elided, or one whose syllables all fell where the surviving
    /// readings disagree.
    /// </summary>
    public bool SaysAnything => Syllables.Any(s => s.Quantity != Quantity.Unknown);
}

/// <summary>One syllable of a word, and what the metre made of it.</summary>
public sealed record ScannedSyllable(string Text, Quantity Quantity, bool Elided)
{
    /// <summary>
    /// The conventional mark: macron for long, breve for short, multiplication
    /// sign for a syllable the metre does not settle.
    ///
    /// These three rather than the metrical symbols in the Unicode musical
    /// range, for the reason PassageMarkSymbols gives about the marks in the
    /// reader's margin: the panes are drawn in whatever face the reader
    /// chose, which may be a Greek or medieval font with narrow coverage, and
    /// a scansion that arrives as a row of missing-glyph boxes is worse than
    /// none. A macron, a breve and a multiplication sign are Latin-1 or next
    /// door to it and are in effectively every font that can show Latin at
    /// all.
    /// </summary>
    public string Mark => Quantity switch
    {
        Quantity.Long => "¯",   // ¯
        Quantity.Short => "˘",  // ˘
        _ => "×"                // ×
    };
}

/// <summary>
/// Reads a <see cref="Scansion"/> back as words rather than as a run of
/// syllables.
/// </summary>
public static class ScannedWords
{
    /// <summary>
    /// Every word of the line that has a syllable, in order.
    ///
    /// The word's letters come from <see cref="Scansion.Words"/> rather than
    /// from its syllables - a syllable carries its vowel nucleus and not the
    /// consonants around it, so "arma" would otherwise come back as "aa".
    /// </summary>
    public static IReadOnlyList<ScannedWord> From(Scansion scansion)
    {
        var words = new List<ScannedWord>();
        if (scansion.Syllables.Count == 0) return words;

        var current = new List<ScannedSyllable>();
        var currentIndex = scansion.Syllables[0].WordIndex;

        void Flush()
        {
            if (current.Count == 0) return;

            var text = currentIndex >= 0 && currentIndex < scansion.Words.Count
                ? scansion.Words[currentIndex]
                : string.Empty;

            words.Add(new ScannedWord(currentIndex, text, current.ToList()));
            current.Clear();
        }

        for (var i = 0; i < scansion.Syllables.Count; i++)
        {
            var syllable = scansion.Syllables[i];

            if (syllable.WordIndex != currentIndex)
            {
                Flush();
                currentIndex = syllable.WordIndex;
            }

            // MetricalQuantities is index-for-index with Syllables, but a
            // failed scan leaves it empty rather than full of Unknown, so the
            // length is checked rather than assumed.
            var quantity = i < scansion.MetricalQuantities.Count
                ? scansion.MetricalQuantities[i]
                : Quantity.Unknown;

            current.Add(new ScannedSyllable(syllable.Text, quantity, syllable.Elided));
        }

        Flush();
        return words;
    }

    /// <summary>
    /// The occurrences of one word in the line, matched on letters rather than
    /// on position.
    ///
    /// Position would be simpler and is not safe. The word list a reader picks
    /// from is built by splitting the line on whitespace and keeping letters,
    /// while the scanner splits on any non-letter - so a written elision like
    /// "mult'ille" is one word to the first and two to the second, and every
    /// index after it in that line would be off by one. Being off by one here
    /// means showing a reader the quantities of the wrong word, which is worse
    /// than showing none: it is wrong and it looks right.
    ///
    /// A word appearing twice in a line yields both, and they can differ - the
    /// same spelling can be long in one foot and short in another, which is
    /// exactly the kind of thing worth seeing.
    /// </summary>
    public static IReadOnlyList<ScannedWord> Matching(Scansion scansion, string word)
    {
        var target = WordNormalizer.NormalizeHeadword(word, "lat");
        if (target.Length == 0) return Array.Empty<ScannedWord>();

        return From(scansion)
            .Where(w => WordNormalizer.NormalizeHeadword(w.Text, "lat") == target)
            .ToList();
    }
}
