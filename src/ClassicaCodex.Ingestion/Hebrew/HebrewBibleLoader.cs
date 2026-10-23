using System.Text;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Hebrew;

/// <summary>
/// One word of the Hebrew text as the Open Scriptures Hebrew Bible parses it:
/// the word as written, its Strong's number and its morphology code.
///
/// <b>The lemma is a number, not a word.</b> OSHB keys every word to Strong's
/// dictionary, with a letter after the number where Strong's runs two words
/// together ("1254 a"), and writes the prefixed particles before it with a
/// slash - "b/7225" is the preposition be- on reshit. <see cref="Headword"/>
/// is the number with the particles taken off, which is what a dictionary is
/// looked up by.
/// </summary>
public sealed record HebrewWord(string Form, string Lemma, string Morph)
{
    public string Headword => HebrewBibleLoader.HeadwordOf(Lemma);
}

/// <summary>One verse: its text as it reads on the page, and the words in it.</summary>
public sealed record HebrewVerse(int Chapter, int Verse, string Text, IReadOnlyList<HebrewWord> Words)
{
    public string Citation => $"{Chapter}.{Verse}";
}

/// <summary>One book of the Hebrew Bible, in Masoretic chapter and verse order.</summary>
public sealed record HebrewBook(string OsisId, IReadOnlyList<HebrewVerse> Verses);

/// <summary>
/// Reads one book of the Open Scriptures Hebrew Bible: the Westminster
/// Leningrad Codex, in OSIS XML, with every word lemmatised and parsed.
///
/// <b>The verse text is rebuilt, not copied.</b> OSIS keeps the words and the
/// marks between them as separate elements and leaves the spacing to whoever
/// reads it, so the rules for putting a verse back together live here:
///
///   - The slashes inside a word mark where OSHB divided its prefixes and
///     suffixes for parsing. The scribe wrote one word, and so does this.
///   - Maqaf binds two words into one accent unit and is written with no
///     space on either side. Sof pasuq ends the verse and is written against
///     the last word. Paseq is a vertical stroke with a space each side.
///   - Where the scribe's consonants (ketiv) and the reading the Masoretes
///     marked in the margin (qere) differ, both are kept, the way printed
///     Bibles keep them: the ketiv in parentheses and the qere in brackets
///     after it. The ketiv has no vowels of its own - the vowels written on it
///     in the manuscript belong to the qere - which is why it is unpointed.
///   - The open and closed paragraph marks (pe and samekh) are part of the
///     Masoretic text and stay, after the verse they close.
///   - Editorial notes are left out: where OSHB reads an accent differently
///     from BHS, or divides a word for parsing, it says so in a note, and
///     none of that is the text.
/// </summary>
public static class HebrewBibleLoader
{
    private const string Maqaf = "\u05BE";
    private const string SofPasuq = "\u05C3";
    private const string Paseq = "\u05C0";

    public static HebrewBook LoadBook(string path)
    {
        var document = XDocument.Load(path);
        var verses = new List<HebrewVerse>();
        string? osisId = null;

        foreach (var verse in document.Descendants().Where(e => e.Name.LocalName == "verse"))
        {
            var id = (string?)verse.Attribute("osisID");
            if (id == null) continue;

            var parts = id.Split('.');
            if (parts.Length != 3
                || !int.TryParse(parts[1], out var chapter)
                || !int.TryParse(parts[2], out var number))
            {
                throw new InvalidDataException($"{Path.GetFileName(path)}: unexpected verse id \"{id}\".");
            }

            osisId ??= parts[0];
            verses.Add(ReadVerse(chapter, number, verse));
        }

        return new HebrewBook(osisId ?? Path.GetFileNameWithoutExtension(path), verses);
    }

    /// <summary>
    /// Builds one verse from its OSIS children. Public so the spacing rules
    /// can be tested on verses written out by hand.
    /// </summary>
    public static HebrewVerse ReadVerse(int chapter, int number, XElement verse)
    {
        var text = new StringBuilder();
        var words = new List<HebrewWord>();
        var joinNext = false;

        void Append(string piece, bool spaceBefore)
        {
            if (piece.Length == 0) return;
            if (spaceBefore && !joinNext && text.Length > 0) text.Append(' ');
            text.Append(piece);
            joinNext = false;
        }

        foreach (var node in verse.Elements())
        {
            switch (node.Name.LocalName)
            {
                case "w":
                {
                    var word = WordOf(node);
                    words.Add(word);
                    var ketiv = (string?)node.Attribute("type") == "x-ketiv";
                    Append(ketiv ? $"({word.Form})" : word.Form, spaceBefore: true);
                    break;
                }

                case "seg":
                {
                    var type = (string?)node.Attribute("type");
                    if (type == "x-maqqef")
                    {
                        Append(Maqaf, spaceBefore: false);
                        joinNext = true;
                    }
                    else if (type == "x-sof-pasuq")
                    {
                        Append(SofPasuq, spaceBefore: false);
                    }
                    else
                    {
                        // Paseq, the paragraph marks and the inverted nuns of
                        // Numbers 10 stand apart from the words around them.
                        Append(node.Value.Trim(), spaceBefore: true);
                    }

                    break;
                }

                case "note":
                {
                    // Only a qere is text; every other note is OSHB talking
                    // about the text.
                    if ((string?)node.Attribute("type") != "variant") break;

                    var qere = node.Descendants()
                        .Where(e => e.Name.LocalName == "rdg" && (string?)e.Attribute("type") == "x-qere")
                        .SelectMany(r => r.Elements().Where(e => e.Name.LocalName is "w" or "seg"))
                        .ToList();
                    if (qere.Count == 0) break;

                    var reading = new StringBuilder();
                    var join = false;
                    foreach (var part in qere)
                    {
                        if (part.Name.LocalName == "seg")
                        {
                            if ((string?)part.Attribute("type") == "x-maqqef")
                            {
                                reading.Append(Maqaf);
                                join = true;
                            }

                            continue;
                        }

                        var word = WordOf(part);
                        words.Add(word);
                        if (reading.Length > 0 && !join) reading.Append(' ');
                        reading.Append(word.Form);
                        join = false;
                    }

                    Append($"[{reading}]", spaceBefore: true);
                    break;
                }
            }
        }

        return new HebrewVerse(chapter, number, text.ToString(), words);
    }

    private static HebrewWord WordOf(XElement w) =>
        new(
            // Value, not the first text node: a suspended or small letter is a
            // nested seg inside the word, and the word is all of it.
            w.Value.Replace("/", string.Empty).Trim(),
            (string?)w.Attribute("lemma") ?? string.Empty,
            (string?)w.Attribute("morph") ?? string.Empty);

    /// <summary>
    /// The Strong's entry a lemma attribute points at, without the particles
    /// attached to the word: "c/d/776" is 776, "1254 a" stays "1254 a", and a
    /// word that is nothing but particles has none.
    /// </summary>
    public static string HeadwordOf(string lemma)
    {
        foreach (var part in lemma.Split('/'))
        {
            var trimmed = part.Trim().TrimEnd('+');
            if (trimmed.Length > 0 && char.IsDigit(trimmed[0])) return trimmed;
        }

        return string.Empty;
    }
}
