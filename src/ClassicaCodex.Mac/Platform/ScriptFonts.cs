using Avalonia.Media;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Mac.Platform;

/// <summary>
/// Picks a font that can actually draw a passage - the Mac half of the
/// Windows class of the same name, which explains at length why the text
/// decides and not the language (an Oracc tablet and its transliteration
/// are both Akkadian; only one of them is cuneiform).
///
/// The detection is the same. The fonts are not: Windows reaches for Segoe UI
/// Historic, which a Mac does not have, while macOS ships Noto Sans Cuneiform
/// and Noto Sans Egyptian Hieroglyphs with the system, Geeza Pro for Arabic,
/// and a Times New Roman that carries Hebrew with its points. So on a Mac the
/// ancient scripts work out of the box, which on Windows they mostly do too.
/// </summary>
internal static class ScriptFonts
{
    private sealed record Script(
        string Name, (int First, int Last)[] Ranges, string[] Families, bool RightToLeft = false)
    {
        public bool Contains(int codepoint)
        {
            foreach (var (first, last) in Ranges)
            {
                if (codepoint >= first && codepoint <= last) return true;
            }

            return false;
        }
    }

    private static readonly Script Cuneiform = new(
        "cuneiform",
        new[] { (0x12000, 0x1254F) },
        new[] { "Noto Sans Cuneiform", "Segoe UI Historic", "Akkadian", "CuneiformComposite", "Santakku" });

    private static readonly Script EgyptianHieroglyphs = new(
        "Egyptian hieroglyphs",
        new[] { (0x13000, 0x1345F), (0x13460, 0x143FF) },
        new[] { "Noto Sans Egyptian Hieroglyphs", "NewGardiner", "Aegyptus", "JSeshFont", "Segoe UI Historic" });

    private static readonly Script Hebrew = new(
        "Hebrew",
        new[] { (0x0591, 0x05F4), (0xFB1D, 0xFB4F) },
        new[] { "SBL Hebrew", "Ezra SIL", "Taamey Frank CLM", "Times New Roman", "Arial Hebrew", "DejaVu Sans" },
        RightToLeft: true);

    private static readonly Script Arabic = new(
        "Arabic",
        new[] { (0x0600, 0x06FF), (0x0750, 0x077F), (0x08A0, 0x08FF), (0xFB50, 0xFDFF), (0xFE70, 0xFEFF) },
        new[] { "Amiri Quran", "Scheherazade New", "Noto Naskh Arabic", "Geeza Pro", "Times New Roman", "DejaVu Sans" },
        RightToLeft: true);

    private static readonly Script[] Scripts = { Cuneiform, EgyptianHieroglyphs, Hebrew, Arabic };

    /// <summary>How far into an edition to look before deciding it needs nothing special.</summary>
    private const int CharactersExamined = 20_000;

    /// <summary>The font to draw these passages in, or null to keep the reading font.</summary>
    internal static string? FamilyFor(IReadOnlyList<TextNode> nodes)
    {
        var script = ScriptOf(nodes);
        return script == null ? null : FirstInstalled(script.Families);
    }

    /// <summary>The same question for one string - a result row, a word.</summary>
    internal static string? FamilyFor(string text)
    {
        var script = ScriptOf(text);
        return script == null ? null : FirstInstalled(script.Families);
    }

    internal static bool IsRightToLeft(IReadOnlyList<TextNode> nodes) => ScriptOf(nodes)?.RightToLeft == true;

    internal static bool IsRightToLeft(string text) => ScriptOf(text)?.RightToLeft == true;

    /// <summary>
    /// The reading font with the script's own font put first, so the special
    /// face draws what it can and the reading font draws the rest - the
    /// numerals and punctuation an edition mixes in.
    /// </summary>
    internal static FontFamily Compose(FontFamily reading, string? scriptFamily) =>
        scriptFamily == null ? reading : new FontFamily($"{scriptFamily}, {reading}");

    private static Script? ScriptOf(IReadOnlyList<TextNode> nodes)
    {
        var examined = 0;

        foreach (var node in nodes)
        {
            var text = node.Text;
            if (string.IsNullOrEmpty(text)) continue;

            var script = ScriptOf(text);
            if (script != null) return script;

            examined += text.Length;
            if (examined >= CharactersExamined) return null;
        }

        return null;
    }

    private static Script? ScriptOf(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value < 0x0591 || (value > 0x08FF && value < 0xFB1D) || (value > 0xFEFF && value <= 0xFFFF)) continue;

            foreach (var script in Scripts)
            {
                if (script.Contains(value)) return script;
            }
        }

        return null;
    }

    private static string? FirstInstalled(IReadOnlyList<string> families)
    {
        foreach (var family in families)
        {
            if (Installed.Contains(family)) return family;
        }

        // Nothing named is installed: hand back the first anyway. Unlike GDI,
        // the text engine falls back glyph by glyph through the system's own
        // cascade, so naming a missing face costs nothing and CoreText will
        // still find the signs if anything on the machine has them.
        return families.Count > 0 ? families[0] : null;
    }

    private static readonly Lazy<HashSet<string>> InstalledFamilies = new(() =>
    {
        try
        {
            return FontManager.Current.SystemFonts
                .Select(f => f.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    });

    private static HashSet<string> Installed => InstalledFamilies.Value;
}
