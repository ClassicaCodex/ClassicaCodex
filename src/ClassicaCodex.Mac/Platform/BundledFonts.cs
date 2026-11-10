using Avalonia.Media;

namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// The fonts that come with the app, and where the text engine looks for them.
///
/// Junicode is here for the medieval texts. Menota's manuscripts are written
/// with MUFI's characters: scribal abbreviation marks, ligatures and letter
/// forms that Unicode encodes in Latin Extended-D and the combining-mark
/// blocks, or that MUFI places in the Private Use Area. No font on a Mac or an
/// iPad draws the private-use ones, so on 9 November 2026 a Menota library on
/// an iPad showed boxes in 20,440 of its 281,474 lines - 25,165 characters, 84
/// different signs. Junicode is the font those characters were made for, and
/// it draws 81 of the 84; the three it lacks occur 89 times in all.
///
/// It is a fallback rather than the reading font, and only for the blocks it
/// is there for, so the reader's own face still draws the letters around the
/// marks, and nothing else in the app changes. Avalonia asks these fallbacks
/// before the system's own, which matters for the Private Use Area: Apple's
/// fonts put their own symbols there.
///
/// Junicode 2.226, Peter S. Baker, SIL Open Font License 1.1 - see
/// THIRD-PARTY-NOTICES-MAC.md.
/// </summary>
internal static class BundledFonts
{
    /// <summary>
    /// Junicode by its place in the app. Both the Mac and the iPad builds are
    /// called ClassicaCodex.Mac, and both carry Assets/Fonts.
    /// </summary>
    internal const string JunicodeSource = "avares://ClassicaCodex.Mac/Assets/Fonts#Junicode";

    /// <summary>
    /// Where Junicode is asked first for a character the font in use lacks: the
    /// Private Use Area, Latin Extended-D (ꝯ ꝑ ꝫ), Latin Extended-E, the
    /// combining marks supplements, the half marks that span two letters,
    /// Supplemental Punctuation (⸌ ⸍) and runes.
    /// </summary>
    internal const string MedievalRanges =
        "U+E000-F8FF, U+A720-A7FF, U+AB30-AB6F, U+1DC0-1DFF, U+FE20-FE2F, U+2E00-2E7F, U+16A0-16FF";

    internal static FontFamily Junicode { get; } = new(JunicodeSource);

    /// <summary>What both start-ups hand Avalonia: the Mac's Program and the iPad's AppDelegate.</summary>
    internal static FontManagerOptions Options() => new()
    {
        FontFallbacks = new[]
        {
            new FontFallback { FontFamily = Junicode, UnicodeRange = UnicodeRange.Parse(MedievalRanges) }
        }
    };
}
