using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ClassicaCodex.Ingestion;

/// <summary>
/// Writes an exported passage - a title, plus one or more chunks of text -
/// out to a file, in whichever of the three formats the export dialog asked
/// for. Each format is a self-contained method; none of them touch the
/// database, they just take already-fetched content and a font name (so the
/// export matches whichever pane - Greek serif or English serif - it was
/// pulled from) and produce a file.
///
/// Each chunk is a (Label, Text) pair. Label is either a citation marker
/// ("[1.1]" for a single line, "[1.1-1.15]" for a combined range) or an
/// empty string, which every method here treats as "draw no label for this
/// chunk" - that's how the export dialog's "show citation refs" toggle
/// actually takes effect, without this service needing to know about the
/// toggle itself.
/// </summary>
public static class PassageExportService
{
    public static void ExportText(string filePath, string title, string sourceUrl, IReadOnlyList<(string Label, string Text)> chunks)
    {
        using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        writer.WriteLine(title);
        writer.WriteLine(new string('-', title.Length));
        writer.WriteLine();

        foreach (var (label, text) in chunks)
        {
            writer.WriteLine(string.IsNullOrEmpty(label) ? text : $"{label} {text}");
        }

        writer.WriteLine();
        writer.WriteLine($"Source: {sourceUrl}");
    }

    public static void ExportDocx(string filePath, string title, string sourceUrl, IReadOnlyList<(string Label, string Text)> chunks, string fontName)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { After = "240" }),
            new Run(
                new RunProperties(
                    new RunFonts { Ascii = fontName, HighAnsi = fontName },
                    new Bold(),
                    new FontSize { Val = "28" }),
                new Text(title))));

        foreach (var (label, text) in chunks)
        {
            var paragraph = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "120" }));

            if (!string.IsNullOrEmpty(label))
            {
                paragraph.Append(new Run(
                    new RunProperties(
                        new RunFonts { Ascii = fontName, HighAnsi = fontName },
                        new Bold(),
                        new Color { Val = "666666" }),
                    new Text($"{label} ")));
            }

            paragraph.Append(new Run(
                new RunProperties(new RunFonts { Ascii = fontName, HighAnsi = fontName }),
                new Text(text)));

            body.AppendChild(paragraph);
        }

        body.AppendChild(new Paragraph(
            new ParagraphProperties(new SpacingBetweenLines { Before = "240" }),
            new Run(
                new RunProperties(
                    new RunFonts { Ascii = fontName, HighAnsi = fontName },
                    new Italic(),
                    new Color { Val = "666666" },
                    new FontSize { Val = "18" }),
                new Text($"Source: {sourceUrl}"))));

        mainPart.Document.Save();
    }

    private static bool _fontResolverRegistered;

    /// <summary>
    /// PdfSharp 6.x needs an explicit IFontResolver before it will use any
    /// font by name - see WindowsFontResolver's remarks. Registering more
    /// than once isn't safe, so this only ever runs the first time a PDF is
    /// exported in this session.
    /// </summary>
    private static void EnsureFontResolverRegistered()
    {
        if (_fontResolverRegistered) return;
        PdfSharp.Fonts.GlobalFontSettings.FontResolver = new WindowsFontResolver();
        _fontResolverRegistered = true;
    }

    /// <summary>
    /// Every line is laid out word-by-word with real measurements
    /// (gfx.MeasureString, font.GetHeight()) rather than estimated from
    /// character counts - the estimation approach this replaced could
    /// under-count a long single chunk's true height, which meant content
    /// that ran past the end of a page was silently dropped rather than
    /// continuing onto the next one (most visible with "combine into one
    /// continuous passage" on a whole work, where the entire body becomes
    /// one chunk). This also handles a single unbroken "word" wider than
    /// the page - a long citation URN, say - by force-breaking it into
    /// character-fitting fragments instead of letting it overflow the
    /// margin, which is what was happening to long titles.
    /// </summary>
    /// <summary>
    /// Substitutes characters an embedded font is unlikely to carry with the
    /// nearest one it certainly does.
    ///
    /// Only the PDF path needs this. On screen, Windows quietly falls back to
    /// another installed font for any character the chosen one lacks, so the
    /// text looks right; a PDF embeds one font and gets a .notdef box instead.
    /// That difference is why elided Greek - "ὅ τ'", "νῦν δ'" - read correctly
    /// in the reader and came out as "ὅ τ⊕" once exported.
    ///
    /// Greek elision is marked with several different code points depending on
    /// who prepared the text: the koronis, the modifier letter apostrophe, the
    /// typographic right quote, and the plain ASCII one all appear across
    /// Perseus and Menota. They are all the same mark to a reader, and U+2019
    /// is the one every Windows text font has.
    /// </summary>
    private static string SubstituteUnsupportedGlyphs(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new System.Text.StringBuilder(text.Length);

        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '\u1FBD' => '\u2019', // Greek koronis
                '\u02BC' => '\u2019', // modifier letter apostrophe
                '\u02BB' => '\u2018', // modifier letter turned comma
                '\u2032' => '\u2019', // prime
                '\u1FBF' => '\u2019', // Greek psili
                '\u1FFE' => '\u2018', // Greek dasia
                _ => c
            });
        }

        return sb.ToString();
    }

    public static void ExportPdf(string filePath, string title, string sourceUrl, IReadOnlyList<(string Label, string Text)> chunks, string fontName)
    {
        EnsureFontResolverRegistered();

        var document = new PdfDocument();
        var titleFont = new XFont(fontName, 18, XFontStyleEx.Bold);
        var citationFont = new XFont(fontName, 10, XFontStyleEx.Bold);
        var bodyFont = new XFont(fontName, 12, XFontStyleEx.Regular);
        var footerFont = new XFont(fontName, 9, XFontStyleEx.Italic);

        // Points throughout, said out loud. PdfSharp 6.1 deprecated the
        // implicit double-to-XUnit conversion these lines relied on, because
        // a bare number gives no clue which unit it is and readers guessed
        // wrong. Everything here is points already - XGraphics.MeasureString
        // returns them, and the page is 612x792pt - so taking .Point off the
        // page keeps the arithmetic in plain doubles and says which unit it
        // is in. Same numbers, checked: a nine-page export hashes identically
        // before and after.
        const double margin = 50;
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        var contentWidth = page.Width.Point - margin * 2;
        double y = margin;

        // PdfSharp buffers drawing operations on an XGraphics and only
        // flushes them into the page's content stream when it's disposed -
        // so each page's XGraphics has to be disposed before moving on, and
        // the last one before saving, or that page comes out blank.
        void NewPage()
        {
            gfx.Dispose();
            page = document.AddPage();
            gfx = XGraphics.FromPdfPage(page);
            y = margin;
        }

        void DrawFlowingText(string rawText, XFont font, XBrush brush)
        {
            if (string.IsNullOrEmpty(rawText)) return;

            // Once, here, rather than at each call site - every string that
            // reaches the page goes through this method.
            var text = SubstituteUnsupportedGlyphs(rawText);

            var lineHeight = font.GetHeight() * 1.15;
            var spaceWidth = gfx.MeasureString(" ", font).Width;
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            var currentLine = new System.Text.StringBuilder();
            var currentLineWidth = 0.0;

            void FlushLine()
            {
                if (currentLine.Length == 0) return;
                if (y + lineHeight > page.Height.Point - margin) NewPage();
                gfx.DrawString(currentLine.ToString(), font, brush, new XPoint(margin, y + font.GetHeight()));
                y += lineHeight;
                currentLine.Clear();
                currentLineWidth = 0;
            }

            foreach (var rawWord in words)
            {
                var word = rawWord;

                // A word wider than the whole page (a long citation URN
                // with no spaces to break on) can't sit on any line as-is -
                // split it at whatever length actually fits, one fragment
                // per line, rather than let it run off the margin.
                while (gfx.MeasureString(word, font).Width > contentWidth)
                {
                    var fitLength = word.Length;
                    while (fitLength > 1 && gfx.MeasureString(word[..fitLength], font).Width > contentWidth)
                    {
                        fitLength--;
                    }

                    if (currentLine.Length > 0) FlushLine();
                    currentLine.Append(word[..fitLength]);
                    FlushLine();
                    word = word[fitLength..];
                }

                var wordWidth = gfx.MeasureString(word, font).Width;
                if (currentLine.Length > 0 && currentLineWidth + spaceWidth + wordWidth > contentWidth)
                {
                    FlushLine();
                }

                if (currentLine.Length > 0)
                {
                    currentLine.Append(' ');
                    currentLineWidth += spaceWidth;
                }
                currentLine.Append(word);
                currentLineWidth += wordWidth;
            }

            FlushLine();
        }

        DrawFlowingText(title, titleFont, XBrushes.Black);
        y += 20;

        foreach (var (label, text) in chunks)
        {
            if (!string.IsNullOrEmpty(label))
            {
                DrawFlowingText(label, citationFont, XBrushes.DimGray);
            }

            DrawFlowingText(text, bodyFont, XBrushes.Black);
            y += 10;
        }

        y += 20;
        DrawFlowingText($"Source: {sourceUrl}", footerFont, XBrushes.DimGray);

        gfx.Dispose(); // flush the final page before writing the file
        document.Save(filePath);
    }
}
