using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using ClassicaCodex.Core;

namespace ClassicaCodex.Mac.Features.WordStudy;

/// <summary>
/// A passage as a result row: cut to what a row can sensibly hold, with the
/// words that matched set off in place. Shared by Word Study's occurrences
/// and the morphology search's results, which are read the same way - by
/// scanning for the form and the words either side of it.
/// </summary>
internal static class PassageText
{
    /// <summary>
    /// As much of a passage as one result row shows - the Windows
    /// ListResultHelpers.RowText, rule for rule.
    ///
    /// On Windows the cut is a crash fix: GDI+ fails measuring a single run of
    /// more than about 32,000 characters that holds a soft hyphen, and 82 of
    /// the corpus's passages are whole Migne and CSEL sections that long.
    /// Avalonia does not fail, but a row of 32,000 characters is still a row
    /// nobody reads and every scroll has to lay out. Nothing is lost by
    /// cutting: the copy, the export and the jump all read the untruncated
    /// passage the window holds.
    /// </summary>
    public static string RowText(string? text, int limit = 400)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= limit) return text;

        // Not through a surrogate pair, which would leave half a character.
        var cut = limit;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    /// <summary>
    /// Combining marks count as part of a word - otherwise a decomposed
    /// Greek word would split apart mid-token and never match.
    /// </summary>
    public static bool IsWordChar(char c) =>
        char.IsLetter(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

    /// <summary>
    /// Adds <paramref name="text"/> to a text block's inlines, with every
    /// word whose normalised form <paramref name="isMatch"/> accepts set off.
    ///
    /// Matching is per word against normalised forms rather than by
    /// substring: Greek accents and combining-versus-precomposed Unicode make
    /// raw substring comparison unreliable, and a substring match would also
    /// light up unrelated words that merely contain the form's letters. The
    /// line is walked in alternating word and non-word runs, so spacing and
    /// punctuation come out exactly as printed.
    /// </summary>
    public static void AddHighlighted(
        TextBlock block, string text, Func<string, bool> isMatch, FontFamily? font = null, double? size = null)
    {
        var plain = new System.Text.StringBuilder();

        void FlushPlain()
        {
            if (plain.Length == 0) return;
            var run = new Run(plain.ToString());
            if (font != null) run.FontFamily = font;
            if (size != null) run.FontSize = size.Value;
            block.Inlines!.Add(run);
            plain.Clear();
        }

        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            var inWord = IsWordChar(text[i]);
            while (i < text.Length && IsWordChar(text[i]) == inWord) i++;

            var chunk = text[start..i];
            if (inWord && isMatch(WordNormalizer.Normalize(chunk)))
            {
                FlushPlain();
                var highlight = Highlight(block, chunk, font);
                if (size != null) highlight.FontSize = size.Value;
                block.Inlines!.Add(highlight);
            }
            else
            {
                plain.Append(chunk);
            }
        }

        FlushPlain();
    }

    /// <summary>
    /// A matched word: the accent's ink on its soft wash, from the palette, so
    /// the mark reads on parchment and on lamplight alike. The Windows list
    /// had to darken its khaki by hand once it followed dark mode, because
    /// the theme's off-white text sat illegibly on bright yellow; resources
    /// that change with the appearance make that a non-question here.
    /// </summary>
    public static Run Highlight(TextBlock owner, string text, FontFamily? font = null)
    {
        var run = new Run(text) { FontWeight = FontWeight.SemiBold };
        if (font != null) run.FontFamily = font;
        run.Bind(TextElement.BackgroundProperty, owner.GetResourceObservable("Cc.AccentSoft"));
        run.Bind(TextElement.ForegroundProperty, owner.GetResourceObservable("Cc.AccentStrong"));
        return run;
    }

    /// <summary>A run in one of the palette's inks: the attribution before a passage, a caveat after it.</summary>
    public static Run Ink(TextBlock owner, string text, string brushKey, FontFamily? font = null, double? size = null)
    {
        var run = new Run(text);
        if (font != null) run.FontFamily = font;
        if (size != null) run.FontSize = size.Value;
        run.Bind(TextElement.ForegroundProperty, owner.GetResourceObservable(brushKey));
        return run;
    }
}
