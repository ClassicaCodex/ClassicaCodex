using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Hebrew;

/// <summary>One entry of Strong's Hebrew dictionary.</summary>
/// <param name="Number">The Strong's number without its H, as OSHB's lemmas carry it: "4397".</param>
/// <param name="Headword">The dictionary form in pointed Hebrew: מַלְאָךְ.</param>
/// <param name="Entry">The entry as a reader sees it: transliteration, part of speech, derivation, meaning and the King James renderings.</param>
public sealed record StrongsEntry(string Number, string Headword, string Language, string Entry);

/// <summary>
/// Reads Strong's Hebrew and Aramaic dictionary as the Open Scriptures
/// project has corrected and encoded it (HebrewStrong.xml).
///
/// <b>Why Strong's and not Brown-Driver-Briggs.</b> Both are in the public
/// domain and both are in the same repository, and BDB is the better
/// dictionary. But OSHB lemmatises by Strong's number, and its own readme says
/// the BDB file "remains a work in progress"; Strong's is complete, one entry
/// per number, and the number is all a word in the text carries. A lookup
/// that lands on the right short entry every time is worth more to a reader
/// than one that lands on a fuller entry some of the time.
///
/// Strong's ends each entry with the words the King James Version uses for the
/// headword. They are kept, labelled as what they are: for a reader of the
/// Hebrew they are the quickest guide to its range, and for this library in
/// particular they are how "angel" (מַלְאָךְ) and "devils" (שֵׁד) got into
/// English.
/// </summary>
public static class StrongsLexiconLoader
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static List<StrongsEntry> Load(string path)
    {
        var document = XDocument.Load(path);
        var entries = new List<StrongsEntry>();

        foreach (var entry in document.Descendants().Where(e => e.Name.LocalName == "entry"))
        {
            var id = (string?)entry.Attribute("id") ?? string.Empty;
            if (!id.StartsWith('H')) continue;

            var word = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "w");
            if (word == null) continue;

            var headword = word.Value.Trim();
            if (headword.Length == 0) continue;

            var language = word.Attributes().FirstOrDefault(a => a.Name.LocalName == "lang")?.Value ?? "heb";

            entries.Add(new StrongsEntry(id[1..], headword, language, Describe(id, word, entry)));
        }

        return entries;
    }

    private static string Describe(string id, XElement word, XElement entry)
    {
        var text = new StringBuilder();
        text.Append(id);

        var xlit = (string?)word.Attribute("xlit");
        if (!string.IsNullOrEmpty(xlit)) text.Append(' ').Append(xlit);

        var pos = (string?)word.Attribute("pos");
        if (!string.IsNullOrEmpty(pos)) text.Append(" (").Append(pos).Append(')');

        var source = Part(entry, "source");
        var meaning = Part(entry, "meaning");
        var usage = Part(entry, "usage");

        if (source.Length > 0) text.Append(". ").Append(source);
        if (meaning.Length > 0) text.Append(' ').Append(meaning);
        if (usage.Length > 0) text.Append(" King James Version: ").Append(usage);

        return Whitespace.Replace(text.ToString(), " ").Trim();
    }

    /// <summary>
    /// The text of one part of an entry. Cross-references are written as
    /// &lt;w src="H1"&gt;1&lt;/w&gt; - the number of the entry referred to -
    /// and stay as that number, which is how Strong's prints them.
    /// </summary>
    private static string Part(XElement entry, string name) =>
        entry.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;
}
