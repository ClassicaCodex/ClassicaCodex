using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using ClassicaCodex.Mac.Features.MedievalHands;
using ClassicaCodex.Mac.Platform;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// The medieval characters that ordinary fonts leave empty.
///
/// A Menota library on an iPad showed boxes in one line in fourteen: MUFI's
/// abbreviation marks and letter forms, many of them in the Private Use Area,
/// which no font on a Mac or an iPad draws. Junicode now comes with the app
/// and is asked first for those blocks. See BundledFonts.
/// </summary>
public class MedievalFontTests
{
    /// <summary>
    /// Holm perg 30 4to, 48v.9, Magnus Lagabøte's Landslov, from Menota (CC
    /// BY-SA 4.0): "forlags fyri til. En æf þæir hafa", with two private-use
    /// characters, one of them twice, and four from Latin Extended-D.
    /// </summary>
    private const string Line =
        "ꝼoꝛlagſ yꝛi til. n æꝼ þæiꝛ haꝼa";

    [AvaloniaFact]
    public void Junicode_comes_with_the_app_and_has_Menotas_marks()
    {
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(BundledFonts.Junicode), out var junicode));
        Assert.Equal("Junicode", junicode!.FamilyName);

        // Three of the commonest private-use characters in Menota's text -
        // U+E7D3 alone is there 8,087 times - then the r rotunda and insular f
        // of Latin Extended-D.
        foreach (var codepoint in new[] { 0xE7D3, 0xE553, 0xEEE6, 0xA75B, 0xA77C })
        {
            Assert.True(junicode.CharacterToGlyphMap.TryGetGlyph(codepoint, out var glyph) && glyph != 0,
                $"Junicode has no glyph for U+{codepoint:X4}");
        }
    }

    [AvaloniaFact]
    public void A_Menota_line_in_the_reading_font_draws_every_mark()
    {
        var reading = (FontFamily)Application.Current!.FindResource("Cc.SourceFont")!;
        var layout = new TextLayout(Line, new Typeface(reading), 18, Brushes.Black);

        var runs = layout.TextLines.SelectMany(l => l.TextRuns).OfType<ShapedTextRun>().ToList();
        Assert.NotEmpty(runs);

        // Glyph 0 is the box.
        Assert.All(runs, run => Assert.DoesNotContain(run.GlyphRun.GlyphInfos, g => g.GlyphIndex == 0));

        var withMarks = runs.Where(r => r.Text.Span.ToString().Any(c => c is >= '' and <= '')).ToList();
        Assert.NotEmpty(withMarks);
        Assert.All(withMarks, run => Assert.Equal("Junicode", run.GlyphRun.GlyphTypeface.FamilyName));

        // The reading face still draws the ordinary letters around them.
        Assert.Contains(runs, run => run.GlyphRun.GlyphTypeface.FamilyName != "Junicode");
    }

    [AvaloniaFact]
    public void Medieval_Hands_sets_its_transcriptions_in_the_bundled_Junicode()
    {
        var font = MedievalHandsWindow.TranscriptionFont();

        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(font), out var face));
        Assert.Equal("Junicode", face!.FamilyName);
    }
}
