using System.Globalization;
using System.Text;

namespace ClassicaCodex.Core;

/// <summary>
/// How much of a difference between two printings of the same line has to be
/// ignored before what is left is a difference in the text.
///
/// Raw is the two strings as stored. Presentation folds away the things no
/// editor would call a reading - spacing, case, punctuation, brackets, the
/// Unicode composition the file happened to use. Orthography folds away the
/// spelling conventions editors genuinely differ on but which do not change
/// what word is printed - Greek accents and breathings, Latin u/v and i/j.
///
/// The levels are ordered and cumulative, so the first one at which two lines
/// agree says what kind of difference they have.
/// </summary>
public enum CollationLevel
{
    Raw,
    Presentation,
    Orthography
}

/// <summary>
/// Folds a line of text down to the level at which two editions can be
/// meaningfully compared.
///
/// This is the whole feature rather than a detail of it. Comparing two
/// editions byte for byte reports almost every line as different: measured
/// against this library, Perseus and First1KGreek agree on barely half the
/// Agamemnon, and on none of the Historia Ecclesiastica - which cannot be a
/// thousand textual variants and is obviously two houses' conventions for
/// accents and punctuation. A collation that says "these lines differ" without
/// saying how is a wall of noise, and worse than not collating at all, because
/// it looks like evidence.
///
/// So nothing here is a judgement about which reading is right. It only
/// separates the differences an editor would defend from the ones a typesetter
/// made.
/// </summary>
public static class CollationNormalizer
{
    /// <summary>
    /// The text folded to the given level. Raw returns it unchanged, so a
    /// caller can walk the levels without special-casing the first.
    /// </summary>
    public static string Normalize(string text, CollationLevel level, string? language = null)
    {
        if (level == CollationLevel.Raw) return text;

        var folded = FoldPresentation(text);
        if (level == CollationLevel.Presentation) return folded;

        return FoldOrthography(folded, language);
    }

    /// <summary>
    /// The first level at which two lines agree, or null when they still
    /// differ once everything foldable has been folded - which is the answer
    /// the collation exists to find.
    /// </summary>
    public static CollationLevel? FirstAgreement(string a, string b, string? language = null)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return CollationLevel.Raw;

        if (string.Equals(Normalize(a, CollationLevel.Presentation, language),
                Normalize(b, CollationLevel.Presentation, language), StringComparison.Ordinal))
        {
            return CollationLevel.Presentation;
        }

        if (string.Equals(Normalize(a, CollationLevel.Orthography, language),
                Normalize(b, CollationLevel.Orthography, language), StringComparison.Ordinal))
        {
            return CollationLevel.Orthography;
        }

        return null;
    }

    /// <summary>
    /// Spacing, case, punctuation and Unicode composition.
    ///
    /// Editorial brackets go with the punctuation, and that is deliberate: one
    /// editor bracketing a word another prints plainly is a disagreement about
    /// the word's standing, not about whether the word is there, and the word
    /// is in both. Marking that as a variant would bury the places where the
    /// editors actually print different words.
    ///
    /// Composed to NFC first so that a diacritic stored as its own code point
    /// in one file and baked into a precomposed character in the other is not
    /// read as a difference - which it is not; it is the same letter typed by
    /// two programs.
    /// </summary>
    private static string FoldPresentation(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var ch in text.Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            // Symbols as well as punctuation: the daggers editors put around a
            // corrupt passage are Unicode symbols rather than punctuation, and
            // a crux is a statement about the text, not a reading of it.
            if (char.IsPunctuation(ch) || char.IsSymbol(ch) || IsApostrophe(ch)) continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The mark that stands for an elided letter, in every character the
    /// digitisers reached for.
    ///
    /// Named explicitly because Unicode files these in three different
    /// categories and only two of them fall out of the punctuation and symbol
    /// tests above. U+02BC is a modifier LETTER, so it survived them - and
    /// measured against this library that one character accounted for most of
    /// the difference between Perseus and First1KGreek across the whole of
    /// Aeschylus. Perseus writes the koronis, First1KGreek the modifier letter,
    /// and every elided word in seven plays read as a variant.
    ///
    /// Stripping the mark is not the same as ignoring elision. An edition that
    /// prints the elided form and one that prints the full word still differ
    /// after this, because the letters differ - which is the reading, and is
    /// the editor's decision. Which glyph was used to mark it is not.
    /// </summary>
    private static bool IsApostrophe(char ch) =>
        // U+0027 apostrophe, U+2018 and U+2019 quotation marks, U+02BB and
        // U+02BC modifier letters, U+1FBD Greek koronis, U+1FBF Greek psili.
        // Written as escapes because six of the seven are indistinguishable
        // on screen, and the one that matters most is the one nobody would
        // guess is a letter.
        ch is '\u0027' or '\u2018' or '\u2019' or '\u02BB'
           or '\u02BC' or '\u1FBD' or '\u1FBF';

    /// <summary>
    /// The spelling conventions that differ between houses without changing
    /// which word is printed.
    ///
    /// Greek: accents, breathings, diaeresis and iota subscript are all
    /// editorial - the manuscripts largely do not have them - and final sigma
    /// is a positional variant of the same letter.
    ///
    /// Latin: u and v are one letter and i and j are one letter, split by
    /// printers long after the texts were written, and every edition picks a
    /// side. The e-caudata that medieval scribes wrote for ae is the same
    /// digraph, and the ae/oe ligatures are typography.
    ///
    /// Applied to both languages when the edition does not say which it is.
    /// Folding Greek rules over Latin, or the reverse, does nothing: neither
    /// alphabet contains the other's letters.
    /// </summary>
    private static string FoldOrthography(string presentationFolded, string? language)
    {
        // Before the diacritic strip below, which would otherwise reduce the
        // e-caudata to a bare "e" and lose half the digraph.
        var source = string.Equals(language, "lat", StringComparison.OrdinalIgnoreCase)
            ? ExpandECaudata(presentationFolded)
            : presentationFolded;

        var builder = new StringBuilder(source.Length);

        // Decomposed so that a diacritic baked into a precomposed character
        // becomes a combining mark this can drop. NFC alone would leave it
        // welded to its letter.
        foreach (var ch in source.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            switch (ch)
            {
                case 'ς':
                    builder.Append('σ');
                    break;

                case 'v':
                    builder.Append('u');
                    break;

                case 'j':
                    builder.Append('i');
                    break;

                // The ligatures, and the e-caudata that stands for the same
                // digraph. Written out rather than dropped, so "caelum" and
                // "cęlum" meet at the same string.
                case 'æ':
                    builder.Append("ae");
                    break;

                case 'œ':
                    builder.Append("oe");
                    break;

                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes the e-caudata out as the digraph it stands for, so a medieval
    /// "cęlum" and a classical "caelum" meet at the same string.
    ///
    /// One character has to become two, which the character-at-a-time fold
    /// above cannot do, so it happens here first. Decomposing means both
    /// spellings of the letter - the precomposed U+0119 and an "e" with a
    /// combining ogonek after it - arrive in the same shape.
    ///
    /// Only "e" is treated this way. Other letters take an ogonek in other
    /// languages, and none of them mean a digraph.
    /// </summary>
    private static string ExpandECaudata(string text)
    {
        const char ogonek = '̨';

        var decomposed = text.Normalize(NormalizationForm.FormD);
        if (!decomposed.Contains(ogonek)) return text;

        var builder = new StringBuilder(decomposed.Length + 4);
        for (var i = 0; i < decomposed.Length; i++)
        {
            if (decomposed[i] == 'e' && i + 1 < decomposed.Length && decomposed[i + 1] == ogonek)
            {
                builder.Append("ae");
                i++;
                continue;
            }

            builder.Append(decomposed[i]);
        }

        return builder.ToString();
    }
}
