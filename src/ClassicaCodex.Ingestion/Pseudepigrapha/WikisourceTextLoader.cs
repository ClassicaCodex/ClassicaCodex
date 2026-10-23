using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClassicaCodex.Ingestion.Pseudepigrapha;

/// <summary>
/// Reads public-domain translations from English Wikisource, where the
/// pseudepigrapha's standard old English translations are transcribed and
/// proofread against their scans.
///
/// <b>Verses are numbered in the running text, not marked up.</b> Charles
/// prints "1. And it came to pass ... 2. And the angels ...", and the
/// transcriptions keep it that way, so the numbers have to be found in the
/// prose. A digit and a full stop is not enough to go on - "Ps. 1. 2." and a
/// date both look like that - so a verse number is accepted only when it is
/// the next one expected, and a number that never comes (a page break can
/// swallow one) is stepped over rather than ending the chapter.
/// </summary>
public static class WikisourceTextLoader
{
    public const string UserAgent = FileDownloadService.UserAgent;

    /// <summary>The raw wikitext of a page.</summary>
    public static string RawUrl(string title) =>
        "https://en.wikisource.org/w/index.php?title=" + Uri.EscapeDataString(title.Replace(' ', '_')) + "&action=raw";

    /// <summary>The page as rendered, with every transcluded scan page in place.</summary>
    public static string ParsedUrl(string title) =>
        "https://en.wikisource.org/w/api.php?action=parse&prop=text&format=json&formatversion=2&page="
        + Uri.EscapeDataString(title.Replace(' ', '_'));

    private static readonly Regex Comment = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Ref = new(@"<ref[^>/]*/>|<ref[^>]*>.*?</ref>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex Link = new(@"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", RegexOptions.Compiled);
    private static readonly Regex Quotes = new(@"'{2,}", RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"[ \t\u00A0\u200B]+", RegexOptions.Compiled);

    /// <summary>
    /// Plain text from wikitext: templates, footnotes and comments out, links
    /// reduced to the words they show, bold and italic marks dropped. Line
    /// breaks are kept, because chapter and section headings are found by them.
    /// </summary>
    public static string CleanWikitext(string wikitext)
    {
        var text = Comment.Replace(wikitext, string.Empty);
        text = Ref.Replace(text, string.Empty);
        text = StripTemplates(text);
        text = Link.Replace(text, "$1");
        text = Quotes.Replace(text, string.Empty);
        text = Tag.Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        return Spaces.Replace(text, " ");
    }

    /// <summary>Removes {{templates}}, nested ones included, which a regex cannot count.</summary>
    private static string StripTemplates(string text)
    {
        var sb = new StringBuilder(text.Length);
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{') { depth++; i++; continue; }
            if (i + 1 < text.Length && text[i] == '}' && text[i + 1] == '}' && depth > 0) { depth--; i++; continue; }
            if (depth == 0) sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private static readonly Regex Heading = new(@"^\s*=+\s*(.*?)\s*=+\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ChapterLine = new(@"^\s*CHAPTER\s+[IVXLC]+\.?\s*$", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    /// One chapter of Charles's 1 Enoch, as Wikisource has it: a section
    /// heading where the chapter opens a new part of the book, the
    /// "CHAPTER VI." line, and then the verses run on with their numbers.
    /// </summary>
    public static List<(string Citation, string Text, bool IsHead)> EnochChapter(int chapter, string wikitext)
    {
        var text = CleanWikitext(wikitext);
        var result = new List<(string, string, bool)>();

        foreach (Match heading in Heading.Matches(text))
        {
            var title = heading.Groups[1].Value.Trim();
            if (title.Length > 0) result.Add(($"{chapter}.head", title, true));
        }

        text = Heading.Replace(text, " ");
        text = ChapterLine.Replace(text, " ");

        foreach (var (verse, body) in NumberedRuns(text, 1))
            result.Add(($"{chapter}.{verse}", body, false));

        return result;
    }

    /// <summary>
    /// Splits running text at verse numbers "1. ", "2. ", ... taken in order.
    /// A number is accepted only as the next one expected or, if that one never
    /// appears, the one after it - so a stray "3." inside verse 7 cannot start
    /// a verse, and one lost number does not swallow the rest of the text.
    /// </summary>
    public static List<(int Number, string Text)> NumberedRuns(string text, int first)
    {
        var runs = new List<(int, string)>();
        var expected = first;
        var start = FindNumber(text, expected, 0);
        if (start < 0) return runs;

        while (true)
        {
            var bodyStart = start + $"{expected}.".Length;
            var next = FindNumber(text, expected + 1, bodyStart);
            var skip = next < 0 ? FindNumber(text, expected + 2, bodyStart) : -1;

            var end = next >= 0 ? next : skip >= 0 ? skip : text.Length;
            var body = Squeeze(text[bodyStart..end]);
            if (body.Length > 0) runs.Add((expected, body));

            if (next >= 0) { expected++; start = next; }
            else if (skip >= 0) { expected += 2; start = skip; }
            else break;
        }

        return runs;
    }

    /// <remarks>
    /// At the start of a paragraph a comma is accepted for the full stop as
    /// well: Conybeare's section 12 is printed "12, And the angel", and a
    /// numbered paragraph is the one place a number and a comma cannot be
    /// anything else.
    /// </remarks>
    private static int FindNumber(string text, int number, int from)
    {
        var pattern = new Regex($@"(?<![\d\w.,])({number})\.\s|(?<=\n\s*)({number}),\s");
        var match = pattern.Match(text, from);
        return match.Success ? match.Index : -1;
    }

    private static string Squeeze(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static readonly Regex Wikiheading = new(@"^==\s*Chapter\s+(\d+)\s*==\s*$", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex NumberedParagraph = new(@"^(\d+)\s+(.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// A book set out as "==Chapter N==" sections with each verse its own
    /// paragraph beginning with its number, which is how Wikisource has the
    /// Forgotten Books of Eden reprint of 2 Enoch.
    /// </summary>
    public static List<(string Citation, string Text)> ChapterParagraphs(string wikitext)
    {
        var raw = Comment.Replace(wikitext, string.Empty);
        var result = new List<(string, string)>();
        var headings = Wikiheading.Matches(raw);

        for (var h = 0; h < headings.Count; h++)
        {
            var chapter = headings[h].Groups[1].Value;
            var from = headings[h].Index + headings[h].Length;
            var to = h + 1 < headings.Count ? headings[h + 1].Index : raw.Length;
            var body = CleanWikitext(raw[from..to]);

            foreach (Match paragraph in NumberedParagraph.Matches(body))
            {
                var text = Squeeze(paragraph.Groups[2].Value);
                if (text.Length > 0) result.Add(($"{chapter}.{paragraph.Groups[1].Value}", text));
            }
        }

        return result;
    }

    private static readonly Regex References = new(
        @"<ol class=""references"">.*?</ol>|<div class=""mw-references-wrap[^""]*"">.*?</div>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex PageNumber = new(
        @"<span[^>]*class=""[^""]*pagenum[^""]*""[^>]*>.*?</span>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex FootnoteMarker = new(
        @"<sup[^>]*class=""reference""[^>]*>.*?</sup>", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Plain text of a page rendered by the Wikisource API, with the footnotes,
    /// their markers and the scan page numbers taken out and paragraph breaks
    /// kept as blank lines.
    /// </summary>
    public static string RenderedText(string apiJson)
    {
        using var document = JsonDocument.Parse(apiJson);
        var html = document.RootElement.GetProperty("parse").GetProperty("text").GetString() ?? string.Empty;

        // Everything from Wikisource's licence box on is the site's, not the text's.
        var licence = html.IndexOf("licenseContainer", StringComparison.Ordinal);
        if (licence >= 0)
        {
            var box = html.LastIndexOf("<div", licence, StringComparison.Ordinal);
            html = html[..(box >= 0 ? box : licence)];
        }

        html = References.Replace(html, " ");
        html = FootnoteMarker.Replace(html, string.Empty);
        html = PageNumber.Replace(html, " ");
        html = Regex.Replace(html, @"<(style|script)[^>]*>.*?</\1>", " ", RegexOptions.Singleline);
        html = Regex.Replace(html, @"</p>|<br\s*/?>", "\n\n");

        return Spaces.Replace(WebUtility.HtmlDecode(Tag.Replace(html, string.Empty)), " ");
    }
}
