using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClassicaCodex.Ingestion.Sefaria;

/// <summary>One version of a text as Sefaria serves it, flattened to passages in reading order.</summary>
/// <param name="Passages">Each passage's citation ("1.1", "12.3") and its text, markup removed.</param>
public sealed record SefariaVersion(
    string VersionTitle, string License, string? VersionSource, string Language,
    IReadOnlyList<(string Citation, string Text)> Passages);

/// <summary>
/// Reads a text from Sefaria's texts API (v3), the way it answers when asked
/// for a whole book in one named version.
///
/// <b>One request per book, because that is what the API allows.</b> Asked for
/// "Genesis" rather than "Genesis 1", it returns every chapter in one jagged
/// array - fifty arrays of verses - and that shape is what this flattens.
/// Sefaria's bulk export on GitHub would be the alternative, but it is several
/// gigabytes in one repository and its file names follow version titles that
/// the project renames; the API takes the version title as Sefaria shows it.
///
/// <b>The licence is read from the response, not assumed.</b> Sefaria carries
/// texts under many terms, from public domain to non-commercial, and the
/// version is named in the request precisely so that the one fetched is the
/// one whose terms were checked. The caller is given the licence the server
/// reports and decides what to do with it.
/// </summary>
public static class SefariaTextLoader
{
    public const string ApiRoot = "https://www.sefaria.org/api/v3/texts/";

    /// <summary>The request for one book in one version.</summary>
    public static string UrlFor(string book, string language, string versionTitle) =>
        ApiRoot + Uri.EscapeDataString(book)
        + "?version=" + Uri.EscapeDataString($"{language}|{versionTitle}");

    public static SefariaVersion Load(string path) => Parse(File.ReadAllText(path));

    public static SefariaVersion Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("versions", out var versions) || versions.GetArrayLength() == 0)
        {
            throw new InvalidDataException("Sefaria returned no version of this text.");
        }

        var version = versions[0];
        var passages = new List<(string, string)>();

        if (version.TryGetProperty("text", out var text)) Flatten(text, prefix: null, passages);

        return new SefariaVersion(
            StringOrEmpty(version, "versionTitle"),
            StringOrEmpty(version, "license"),
            version.TryGetProperty("versionSource", out var source) && source.ValueKind == JsonValueKind.String
                ? source.GetString()
                : null,
            StringOrEmpty(version, "language"),
            passages);
    }

    /// <summary>
    /// Walks the jagged array depth first. Each level is numbered from 1, the
    /// way Sefaria numbers chapters and verses, and an empty string is a verse
    /// this version does not have - skipped, not renumbered, so that "3.7"
    /// stays 3.7 in every version of the book.
    /// </summary>
    private static void Flatten(JsonElement node, string? prefix, List<(string, string)> passages)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                var i = 0;
                foreach (var child in node.EnumerateArray())
                {
                    i++;
                    Flatten(child, prefix == null ? i.ToString() : $"{prefix}.{i}", passages);
                }

                break;

            case JsonValueKind.String:
                var cleaned = Clean(node.GetString() ?? string.Empty);
                if (cleaned.Length > 0 && prefix != null) passages.Add((prefix, cleaned));
                break;
        }
    }

    /// <summary>The curly quotes are Cohen's Kallah, inside the straight ones.</summary>
    private static readonly Regex FootnoteOpen = new(
        @"<i\b[^>]*class=""[“”]?footnote[“”]?""[^>]*>", RegexOptions.Compiled);

    private static readonly Regex ItalicTag = new(@"<(/?)i\b[^>]*>", RegexOptions.Compiled);

    /// <summary>The number a note hangs from, immediately before the note.</summary>
    private static readonly Regex MarkerBeforeNote = new(@"<sup\b[^>]*>[^<]*</sup>\s*\z", RegexOptions.Compiled);

    /// <summary>
    /// A marker whose note the source left out, a bare digit in the text
    /// otherwise, and Goldschmidt's Mishnah numbers, which have the same class.
    /// Guggenheimer's English has it on superscripts that are no markers at
    /// all, so an ordinal's ending, a transliterated vowel and the top of a
    /// fraction are left to stay in their words.
    /// </summary>
    private static readonly Regex OrphanMarker = new(
        @"<sup\b[^>]*class=""footnote-marker""[^>]*>(?!(?:st|nd|rd|th|e)</sup>)[^<]*</sup>(?!/)",
        RegexOptions.Compiled);

    private static readonly Regex Break = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Plain text from Sefaria's markup. Translators' footnotes go - they are
    /// notes about the text, and inline they would read as part of it - and
    /// every other tag is dropped with its text kept: italics, the small
    /// capitals of LORD, paragraph spans.
    /// </summary>
    public static string Clean(string html)
    {
        var text = OrphanMarker.Replace(RemoveFootnotes(html), string.Empty);
        text = Break.Replace(text, " ");
        text = Tag.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        return Whitespace.Replace(text, " ").Trim();
    }

    /// <summary>
    /// Takes out each footnote whole, by counting tags rather than stopping at
    /// the first close.
    ///
    /// <b>A note can hold italics of its own</b> - a book's title, a Greek word
    /// - and a pattern that ends at the first <c>&lt;/i&gt;</c> ends there
    /// too, leaving the rest of the note in the text as if the author had
    /// written it. Guggenheimer's Jerusalem Talmud measured half as long again
    /// that way: 2.02 million words of English where the translation is 1.37
    /// million.
    /// </summary>
    private static string RemoveFootnotes(string html)
    {
        var open = FootnoteOpen.Match(html);
        if (!open.Success) return html;

        var text = new StringBuilder(html.Length);
        var position = 0;

        while (open.Success)
        {
            var end = EndOfItalic(html, open.Index + open.Length);

            // Never closed, so there is no telling where the note stops. Its
            // words stay: a stray note is visible, and silently losing the
            // rest of the passage would not be. Notes after it can still go.
            if (end < 0)
            {
                open = FootnoteOpen.Match(html, open.Index + open.Length);
                continue;
            }

            text.Append(MarkerBeforeNote.Replace(html[position..open.Index], string.Empty));
            position = end;
            open = FootnoteOpen.Match(html, end);
        }

        return text.Append(html, position, html.Length - position).ToString();
    }

    /// <summary>Where the italic opened just before <paramref name="start"/> closes, or -1.</summary>
    private static int EndOfItalic(string html, int start)
    {
        var depth = 1;

        for (var tag = ItalicTag.Match(html, start); tag.Success; tag = tag.NextMatch())
        {
            depth += tag.Groups[1].Length > 0 ? -1 : 1;
            if (depth == 0) return tag.Index + tag.Length;
        }

        return -1;
    }

    private static string StringOrEmpty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
