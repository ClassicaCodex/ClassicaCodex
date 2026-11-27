using System.Globalization;
using System.Text.RegularExpressions;

namespace ClassicaCodex.Core.Citations;

/// <summary>How a reference in a footnote names its work.</summary>
public enum FootnoteReferenceKind
{
    /// <summary>With an abbreviation or a name in front of it: "Thuc. 2.40.1", "Plato, Republic 514a".</summary>
    Named,

    /// <summary>In the same work as the reference before it: the "3.82.4" of "Thuc. 2.40.1; 3.82.4", or after "ibid.".</summary>
    SameWork,

    /// <summary>By the same author as the reference before it, after "id.": "id. Fam. 15.3".</summary>
    SameAuthor,
}

/// <summary>
/// One reference found in a footnote.
/// </summary>
/// <param name="Start">Where it starts in the text, at its abbreviation if it has one.</param>
/// <param name="Length">How much of the text it takes.</param>
/// <param name="Written">The reference as the footnote writes it: "Pl. Resp. 514a–517a".</param>
/// <param name="Kind">How it names its work.</param>
/// <param name="Names">The words in front of the numbers, as written: ["Pl.", "Resp."]. After "id.", only the work's.</param>
/// <param name="Passage">The reference, one level to an item: ["2", "40", "1"]; a range by its first passage.</param>
/// <param name="Section">A section given beside the reference with §, which some editions number by instead.</param>
/// <param name="NameStarts">Where each of <paramref name="Names"/> starts in the text.</param>
public sealed record FootnoteReference(
    int Start,
    int Length,
    string Written,
    FootnoteReferenceKind Kind,
    IReadOnlyList<string> Names,
    IReadOnlyList<string> Passage,
    string? Section = null,
    IReadOnlyList<int>? NameStarts = null)
{
    /// <summary>The reference as the library writes one: "2.40.1".</summary>
    public string Reference => string.Join('.', Passage);

    /// <summary>
    /// The reference as written from its last <paramref name="names"/> words
    /// on: "Thuc. 2.40.1" out of "See Thuc. 2.40.1", where only "Thuc." named
    /// the work.
    /// </summary>
    public string WrittenFrom(string text, int names)
    {
        var written = Written;
        if (NameStarts != null && names > 0 && names < Names.Count && Start + Length <= text.Length)
            written = text[NameStarts[Names.Count - names]..(Start + Length)];

        // "Thucydides (2.40.1": the bracket around the number is the prose's.
        return Regex.Replace(written.Replace("(", string.Empty).Replace("[", string.Empty), @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }
}

/// <summary>
/// Finds the references in a footnote, or any text that cites the classics
/// the way books do: "Thuc. 2.40.1; cf. Hdt. 1.1, 5.3 and Pl. Resp. 514a–b".
/// What each abbreviation names is not decided here, only where the
/// references are and how they are written; <see cref="WorkAbbreviations"/>
/// reads the names.
///
/// <list type="bullet">
/// <item>A reference is the words in front of a number - an abbreviation,
/// a name, a name and a title - and the number with its levels. Levels are
/// joined by points ("2.40.1") or, in the older way of Lewis and Short, by
/// commas ("1, 7, 22"). A Stephanus or Bekker page carries its letter, and
/// line: "514a", "1253a2".</item>
/// <item>After a reference with points in it, a comma starts another
/// reference in the same work: "Hdt. 1.1, 5.3". A number alone there keeps
/// the levels before its last: "Il. 4.106, 125" is 4.125.</item>
/// <item>After a semicolon, "and" or "cf.", a reference with no words in
/// front of it is in the same work again: "Thuc. 2.40.1; 3.82.4". Prose
/// between the two breaks that.</item>
/// <item>"id." is the same author, "ibid." and "ib." the same work.</item>
/// <item>A range is read by its first passage; "f.", "ff." and "sq." after a
/// reference are dropped.</item>
/// <item>A capital Roman numeral after a name is a book: "Tac. Ann. IV.32".</item>
/// </list>
///
/// Capitalised words that are not names - "See", the first word of a
/// sentence - are kept with the names in front of a reference, and it is
/// left to <see cref="WorkAbbreviations"/> to find the shortest run of them
/// that names a work.
/// </summary>
public static class FootnoteReader
{
    /// <summary>The most words in front of a number that are kept as its names.</summary>
    private const int MostNames = 6;

    private static readonly Regex TokenPattern = new(
        @"(?<number>\d+[a-z]{0,2}\d*)" +
        @"|(?<word>[\p{L}\p{M}]+(?:\.[\p{L}\p{M}]+)*(?:['’]s(?![\p{L}]))?\.?)" +
        @"|(?<mark>\S)",
        RegexOptions.CultureInvariant);

    private static readonly Regex RomanBook = new(@"^(?=[LXVI])(?:XL|L?X{0,3})(?:IX|IV|V?I{0,3})$", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Joiners = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "&", "or", "cf.", "cf", "see", "also", "e.g.", "eg.", "i.e.", "compare", "with", "esp.", "contra", "vs.", "vs",
    };

    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
    {
        "de", "in", "pro", "ad", "contra", "and", "of", "on", "the", "a", "an", "to", "et", "per", "post", "cum",
    };

    // Words that go in front of a number without naming anything: fragment,
    // page, line, verse, chapter, section. In small letters only, since in
    // capitals the same letters are names: "Hor. C." is the Odes, "Caes. B. C."
    // the Civil War.
    private static readonly HashSet<string> NumberWords = new(StringComparer.Ordinal)
    {
        "fr.", "Fr.", "frr.", "fr", "frag.", "Frag.", "fragm.", "p.", "pp.", "l.", "ll.", "v.", "vv.", "ch.", "chap.", "c.", "sect.", "no.", "No.", "nos.",
    };

    private static readonly HashSet<string> Following = new(StringComparer.OrdinalIgnoreCase)
    {
        "f.", "ff.", "f", "ff", "sq.", "sqq.", "seq.", "fin.", "init.", "al.", "extr.",
    };

    private static readonly HashSet<string> Dashes = new(StringComparer.Ordinal) { "–", "—", "-", "‒" };

    private enum TokenKind { Number, Word, Mark }

    private sealed record Token(TokenKind Kind, string Text, int Start, int End);

    /// <summary>The references in the text, in order. None where it holds none.</summary>
    public static IReadOnlyList<FootnoteReference> Read(string? text)
    {
        var found = new List<FootnoteReference>();
        if (string.IsNullOrWhiteSpace(text)) return found;

        var tokens = Tokens(text);
        var names = new List<Token>();
        var sameAuthor = false;
        var sameWork = false;
        var continuable = false;
        FootnoteReference? last = null;

        for (var i = 0; i < tokens.Count;)
        {
            var token = tokens[i];

            if (StartsReference(tokens, i, names.Count > 0 || sameAuthor) is { } first)
            {
                var (passage, section, end) = ReadPassage(tokens, first);
                var from = names.Count > 0 ? names[0].Start : (sameAuthor || sameWork) && i > 0 ? MarkStart(tokens, i) : tokens[i].Start;
                var to = tokens[end - 1].End;
                FootnoteReference? reference = null;

                if (sameAuthor && names.Count > 0)
                {
                    var (words, starts) = Words(names);
                    from = starts[0];
                    reference = new FootnoteReference(from, to - from, text[from..to], FootnoteReferenceKind.SameAuthor, words, passage, section, starts);
                }
                else if (names.Count > 0)
                {
                    var (words, starts) = Words(names);
                    from = starts.Count > 0 ? starts[0] : from;
                    reference = words.Count > 0
                        ? new FootnoteReference(from, to - from, text[from..to], FootnoteReferenceKind.Named, words, passage, section, starts)
                        : null;
                }
                else if ((sameAuthor || sameWork) && last != null)
                    reference = new FootnoteReference(from, to - from, text[from..to], FootnoteReferenceKind.SameWork, Array.Empty<string>(), passage, section);
                else if (continuable && last != null)
                {
                    // A number alone after a reference with more levels keeps
                    // the levels before its last: "Il. 4.106, 125".
                    if (passage.Count == 1 && last.Passage.Count > 1 && char.IsDigit(passage[0][0]) && last.Passage.All(p => p.All(char.IsDigit)))
                        passage = last.Passage.Take(last.Passage.Count - 1).Append(passage[0]).ToList();
                    reference = new FootnoteReference(from, to - from, text[from..to], FootnoteReferenceKind.SameWork, Array.Empty<string>(), passage, section);
                }

                if (reference != null)
                {
                    found.Add(reference);
                    last = reference;
                    continuable = true;
                }

                names.Clear();
                sameAuthor = sameWork = false;
                i = end;
                continue;
            }

            switch (token.Kind)
            {
                case TokenKind.Word:
                {
                    var lower = token.Text.ToLowerInvariant();
                    if (lower is "id." or "idem" or "eid." or "eadem")
                    {
                        names.Clear();
                        sameAuthor = true;
                        sameWork = false;
                    }
                    else if (lower is "ib." or "ibid." or "ibid" or "ib" or "ibidem")
                    {
                        names.Clear();
                        sameWork = true;
                        sameAuthor = false;
                    }
                    else if (Joiners.Contains(token.Text) && names.Count == 0)
                    {
                        // "and", "cf.": a reference after it may continue the work before.
                    }
                    else if (IsName(token, names.Count > 0 || sameAuthor))
                    {
                        names.Add(token);
                    }
                    else
                    {
                        names.Clear();
                        sameAuthor = sameWork = continuable = false;
                    }

                    break;
                }

                case TokenKind.Mark:
                    switch (token.Text)
                    {
                        case ",":
                            // "Plato, Republic 514a": the comma between a name
                            // and a title keeps them together.
                            break;
                        case ";":
                            names.Clear();
                            break;
                        case "(" or "[" when i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Number:
                            // "Thucydides (2.40.1)": the name goes with the number in brackets.
                            break;
                        case "(" or ")" or "[" or "]" or ":" or "–" or "—":
                            names.Clear();
                            break;
                        default:
                            names.Clear();
                            sameAuthor = sameWork = continuable = false;
                            break;
                    }

                    break;

                default:
                    // A number that starts nothing: a year, a count.
                    names.Clear();
                    continuable = false;
                    break;
            }

            i++;
        }

        return found;
    }

    private static List<Token> Tokens(string text)
    {
        var tokens = new List<Token>();
        foreach (Match m in TokenPattern.Matches(text))
        {
            var kind = m.Groups["number"].Success ? TokenKind.Number : m.Groups["word"].Success ? TokenKind.Word : TokenKind.Mark;
            tokens.Add(new Token(kind, m.Value, m.Index, m.Index + m.Length));
        }

        return tokens;
    }

    /// <summary>
    /// Where a reference starts at token i - the number itself, or a number
    /// word or section sign in front of it, or a Roman numeral after a name -
    /// as the index of its first number; null where none starts here.
    /// </summary>
    private static int? StartsReference(List<Token> tokens, int i, bool afterName)
    {
        var token = tokens[i];
        if (token.Kind == TokenKind.Number) return i;

        var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
        if (next?.Kind == TokenKind.Number && (NumberWords.Contains(token.Text) || token.Text == "§")) return i + 1;

        // "Tac. Ann. IV.32", "Hdt. I, 1": a book numbered in capitals. Only
        // I, V, X and L, as books run no higher, and a letter alone only
        // where it cannot be an abbreviation: "D." and "C." name authors.
        var numeral = token.Text.TrimEnd('.');
        if (afterName && token.Kind == TokenKind.Word && RomanBook.IsMatch(numeral) && numeral != "L" && next != null)
        {
            var single = numeral.Length == 1;
            if (next.Kind == TokenKind.Number && token.Text.EndsWith('.') && (!single || next.Start == token.End)) return i;
            if (next.Kind == TokenKind.Mark && next.Text is "." or "," && next.Start == token.End && i + 2 < tokens.Count && tokens[i + 2].Kind == TokenKind.Number) return i;
            if (next.Kind == TokenKind.Number && next.Start > token.End && !token.Text.EndsWith('.')) return i;
        }

        return null;
    }

    /// <summary>The reference that starts at token i: its levels, a § section beside it, and the index after it.</summary>
    private static (List<string> Passage, string? Section, int End) ReadPassage(List<Token> tokens, int i)
    {
        var levels = new List<string> { Level(tokens[i]) };
        var points = tokens[i].Text.EndsWith('.') && tokens[i].Kind == TokenKind.Word; // "IV.32" written as one word
        var lettered = tokens[i].Text.Any(char.IsLetter) && tokens[i].Kind == TokenKind.Number;
        string? section = null;
        var j = i + 1;

        bool IsNumber(int k) => k < tokens.Count && tokens[k].Kind == TokenKind.Number;
        bool Touching(int a, int b) => b < tokens.Count && tokens[a].End == tokens[b].Start;

        // Levels.
        while (j < tokens.Count)
        {
            var t = tokens[j];
            if (points && IsNumber(j) && Touching(j - 1, j))
            {
                // "IV.32": the point went with the numeral.
                levels.Add(tokens[j].Text);
                j++;
                points = true;
                continue;
            }

            if (t.Kind == TokenKind.Mark && t.Text is "." or ":" && IsNumber(j + 1) && Touching(j - 1, j) && Touching(j, j + 1))
            {
                levels.Add(tokens[j + 1].Text);
                points = true;
                j += 2;
                continue;
            }

            // Lewis and Short's "1, 7, 22": commas between levels, but only
            // between plain numbers and never after a point.
            if (t.Kind == TokenKind.Mark && t.Text == "," && IsNumber(j + 1) && !points && !lettered
                && tokens[j + 1].Text.All(char.IsDigit) && levels.All(l => l.All(char.IsDigit)))
            {
                levels.Add(tokens[j + 1].Text);
                j += 2;
                continue;
            }

            break;
        }

        // A range: read by its first passage, the rest passed over.
        if (j + 1 < tokens.Count && Dashes.Contains(tokens[j].Text)
            && (IsNumber(j + 1) || (tokens[j + 1].Kind == TokenKind.Word && tokens[j + 1].Text.Length == 1 && char.IsLower(tokens[j + 1].Text[0]))))
        {
            j += 2;
            while (j + 1 < tokens.Count && tokens[j].Kind == TokenKind.Mark && tokens[j].Text is "." or ":" && IsNumber(j + 1) && Touching(j - 1, j))
                j += 2;
        }

        // "ff.", "sq.": the passages following.
        while (j < tokens.Count && tokens[j].Kind == TokenKind.Word && Following.Contains(tokens[j].Text)) j++;

        // "2, 47, 48, § 128": a section beside the reference.
        var k = j;
        if (k < tokens.Count && tokens[k].Text == ",") k++;
        if (k + 1 < tokens.Count && tokens[k].Text == "§" && IsNumber(k + 1))
        {
            section = tokens[k + 1].Text;
            j = k + 2;
        }

        return (levels, section, j);
    }

    /// <summary>A level as the library writes it: a Roman numeral in figures.</summary>
    private static string Level(Token token)
    {
        if (token.Kind == TokenKind.Number) return token.Text;
        return Roman(token.Text.TrimEnd('.')).ToString(CultureInfo.InvariantCulture);
    }

    private static int Roman(string numeral)
    {
        var values = new Dictionary<char, int> { ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50, ['C'] = 100, ['D'] = 500, ['M'] = 1000 };
        var total = 0;
        for (var i = 0; i < numeral.Length; i++)
        {
            var value = values[numeral[i]];
            if (i + 1 < numeral.Length && values[numeral[i + 1]] > value) total -= value;
            else total += value;
        }

        return total;
    }

    /// <summary>
    /// Whether a word can be part of the names in front of a number: a
    /// capitalised word ("Thuc.", "Resp.", "OT"), LSJ's "h.Hom.", or a small
    /// word inside a title once a name has started ("Works and Days",
    /// "Cic. de Or.").
    /// </summary>
    private static bool IsName(Token token, bool inside)
    {
        var first = token.Text.FirstOrDefault(char.IsLetter);
        if (first == default) return false;
        if (char.IsUpper(first)) return true;
        if (token.Text.Any(char.IsUpper) && token.Text.Contains('.')) return true;
        if (!inside) return false;

        // "Ov. Ars am.", "Cic. Nat. deor.": an abbreviated word of a title in
        // small letters.
        return Connectors.Contains(token.Text.TrimEnd('.')) || (token.Text.EndsWith('.') && token.Text.Length <= 7);
    }

    /// <summary>The names as written, a possessive taken off ("Plato's" is "Plato"), and where each starts.</summary>
    private static (List<string> Words, List<int> Starts) Words(List<Token> names)
    {
        var kept = names.Skip(Math.Max(0, names.Count - MostNames)).ToList();

        // Small words at the front belong to the prose, not the title.
        while (kept.Count > 0 && !char.IsUpper(kept[0].Text.FirstOrDefault(char.IsLetter)) && !kept[0].Text.Contains('.')) kept.RemoveAt(0);

        return (kept.Select(n => Regex.Replace(n.Text, @"['’]s$", string.Empty, RegexOptions.CultureInvariant)).ToList(),
                kept.Select(n => n.Start).ToList());
    }

    /// <summary>Where "id." or "ibid." starts, before the number at i, so the reference shows it.</summary>
    private static int MarkStart(List<Token> tokens, int i)
    {
        for (var k = i - 1; k >= 0 && k >= i - 3; k--)
        {
            var lower = tokens[k].Text.ToLowerInvariant();
            if (lower is "id." or "idem" or "eid." or "eadem" or "ib." or "ibid." or "ibid" or "ib" or "ibidem") return tokens[k].Start;
        }

        return tokens[i].Start;
    }
}
