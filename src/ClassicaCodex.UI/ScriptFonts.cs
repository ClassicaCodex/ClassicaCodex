using System.Drawing.Text;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// Picks a font that can actually draw a passage.
///
/// <b>This exists because cuneiform came out as circles.</b> The reader draws
/// every edition in its reading font - Palatino Linotype for sources, Georgia
/// for translations - and neither has a glyph above U+FFFF. Cuneiform is at
/// U+12000 and up, so Windows substituted, and an Oracc tablet appeared as
/// rows of small rings: convincing enough to look like a rendering choice
/// rather than a missing font, and wrong on every line.
///
/// Nothing else was wrong. The text, the word index and search were all
/// correct the whole time; only the drawing was.
///
/// <b>Why the text decides and not the language.</b> Oracc publishes each work
/// twice, as a transliteration and as cuneiform, and both are recorded as
/// <c>akk</c> and <c>Original</c> - the language is identical and the script is
/// not. Asking the language which font to use would therefore give the same
/// answer for both and be wrong for one of them. Asking the characters cannot.
/// </summary>
internal static class ScriptFonts
{
    /// <summary>
    /// Fonts that carry the cuneiform block, best first.
    ///
    /// Segoe UI Historic ships with Windows 10 and 11, so on a normal machine
    /// the first entry is the answer and nothing has to be installed. The rest
    /// are what somebody who works with these texts is likely to have already.
    /// An installation with none of them keeps the reading font and the circles
    /// - which is no worse than before, and better than an exception.
    /// </summary>
    private static readonly string[] CuneiformFamilies =
    {
        "Segoe UI Historic",
        "Noto Sans Cuneiform",
        "Akkadian",
        "CuneiformComposite",
        "Santakku"
    };

    /// <summary>
    /// The cuneiform range: the signs themselves, their numbers and
    /// punctuation, and the Early Dynastic additions. One contiguous span, so
    /// one comparison.
    /// </summary>
    private const int CuneiformFirst = 0x12000;
    private const int CuneiformLast = 0x1254F;

    /// <summary>
    /// How much of an edition is examined before concluding it is not
    /// cuneiform.
    ///
    /// A cuneiform edition is cuneiform on every line, so the answer is
    /// settled by the first passage that has any text in it. The cap is there
    /// for the other case: Migne is 286,531 lines, and reading all of them to
    /// find out it is Latin would be a measurable pause on every work opened.
    ///
    /// The cost of the cap is a passage of cuneiform quoted inside a long work
    /// in another script, which would keep the reading font and the circles.
    /// No collection in this library is shaped that way; a per-row font would
    /// be the fix if one ever is, and it would mean making every row-height
    /// measurement font-specific too.
    /// </summary>
    private const int CharactersExamined = 20_000;

    /// <summary>
    /// The font family these passages need, or null to use the reading font.
    /// </summary>
    internal static string? FamilyFor(IReadOnlyList<TextNode> nodes)
    {
        return NeedsCuneiform(nodes) ? FirstInstalled(CuneiformFamilies) : null;
    }

    internal static bool NeedsCuneiform(IReadOnlyList<TextNode> nodes)
    {
        var examined = 0;

        foreach (var node in nodes)
        {
            var text = node.Text;
            if (string.IsNullOrEmpty(text)) continue;

            if (ContainsCuneiform(text)) return true;

            examined += text.Length;
            if (examined >= CharactersExamined) return false;
        }

        return false;
    }

    /// <summary>
    /// Whether a string has a cuneiform sign in it.
    ///
    /// Over runes rather than chars: every one of these codepoints is above
    /// U+FFFF and therefore arrives as a surrogate pair, and a loop over chars
    /// would compare two halves that are each in the D800 range and find
    /// nothing.
    /// </summary>
    internal static bool ContainsCuneiform(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value >= CuneiformFirst && rune.Value <= CuneiformLast) return true;
        }

        return false;
    }

    /// <summary>
    /// The first of these families this machine has, or null for none of them.
    ///
    /// The installed list is read once. It is read through a GDI+ collection
    /// that is cheap to ask and not cheap to build, and the answer does not
    /// change while the application is running.
    /// </summary>
    internal static string? FirstInstalled(IReadOnlyList<string> families)
    {
        foreach (var family in families)
        {
            if (Installed.Contains(family)) return family;
        }

        return null;
    }

    private static readonly Lazy<HashSet<string>> InstalledFamilies = new(() =>
    {
        try
        {
            using var collection = new InstalledFontCollection();
            return collection.Families
                .Select(f => f.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // A machine that cannot enumerate its fonts keeps the reading
            // font, which is what it had before any of this.
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    });

    private static HashSet<string> Installed => InstalledFamilies.Value;
}
