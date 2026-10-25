using System.Text;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// Picks a font that can actually draw the text in a reader pane.
///
/// The reading fonts are chosen for reading Greek and Latin - Palatino
/// Linotype on the left, Georgia on the right - and neither contains a
/// single cuneiform sign or Egyptian hieroglyph. Measured on this machine,
/// by reading the fonts' own character maps rather than by guessing:
///
///   Segoe UI Historic    922 of the 1,024 cuneiform signs, and hieroglyphs
///   Palatino Linotype    0
///   Georgia              0
///   Segoe UI             0
///   Segoe UI Symbol      0
///
/// So a cuneiform edition rendered as a pane full of empty boxes, one per
/// sign, with nothing on screen to say the text was fine and the font was
/// not. GDI does have font linking, and it did not rescue this.
///
/// Segoe UI Historic ships with Windows 8.1 and later, so nothing has to be
/// bundled or downloaded - which matters, since this app dropped its
/// proprietary fonts on purpose. It is Microsoft's font FOR historic
/// scripts, so it also covers the ones this library has not met yet: Linear
/// B, Gothic, Old Italic, Ugaritic, and Egyptian when that arrives.
///
/// 922 of 1,024 is not all of them. A hundred-odd rare signs will still draw
/// as boxes, and that is worth knowing rather than claiming otherwise - but
/// it is the difference between a readable text with gaps and no text at
/// all.
/// </summary>
internal static class ReaderScriptFont
{
    /// <summary>
    /// Windows' own font for historic scripts. Present since Windows 8.1.
    /// </summary>
    internal const string HistoricScripts = "Segoe UI Historic";

    /// <summary>
    /// Whether this text needs the historic-script font.
    ///
    /// True when any character is a LETTER outside the Basic Multilingual
    /// Plane. Both halves of that matter:
    ///
    /// - Outside the BMP, because everything the ordinary reading fonts
    ///   cover is inside it. A text of Greek, Latin, Old Norse or English
    ///   never trips this, so nothing that reads correctly today changes.
    /// - A letter, because plenty of astral characters are not script -
    ///   emoji, symbols, musical notation - and swapping the whole pane to a
    ///   historic font because a stray symbol appeared would make the
    ///   surrounding prose worse to read for no gain.
    ///
    /// Returns on the first hit, so the common answer - a Greek edition,
    /// which has none - costs one pass and no allocation.
    /// </summary>
    internal static bool IsNeededFor(IEnumerable<TextNode>? nodes)
    {
        if (nodes == null) return false;

        foreach (var node in nodes)
        {
            foreach (var rune in node.Text.EnumerateRunes())
            {
                if (rune.Value > 0xFFFF && Rune.IsLetter(rune)) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The family a pane should use: its own, unless the text needs the
    /// historic one.
    ///
    /// Takes the pane's default rather than assuming it, because the two
    /// panes do not share a font - the original side reads in Palatino
    /// Linotype and the translation side in Georgia - and a pane that has
    /// shown cuneiform has to go back to its own font when it next shows
    /// something else.
    /// </summary>
    internal static string FamilyFor(string defaultFamily, IEnumerable<TextNode>? nodes) =>
        IsNeededFor(nodes) ? HistoricScripts : defaultFamily;
}
