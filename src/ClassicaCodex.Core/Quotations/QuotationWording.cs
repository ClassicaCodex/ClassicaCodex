using System.Text.RegularExpressions;

namespace ClassicaCodex.Core.Quotations;

/// <summary>A stretch of a quotation as shown: words that agree with the other side, or words that do not.</summary>
public sealed record WordingPiece(string Text, bool Differs);

/// <summary>
/// A quoter's wording set against the edition's, word by word.
/// </summary>
/// <param name="Quoting">The quoting passage's words, in pieces.</param>
/// <param name="Source">The edition's words, in pieces.</param>
/// <param name="QuotingWords">How many words the quoter's span holds.</param>
/// <param name="DifferingQuotingWords">How many of them have no counterpart in the edition's.</param>
/// <param name="SourceWords">How many words the edition's span holds.</param>
/// <param name="DifferingSourceWords">How many of them have no counterpart in the quoter's.</param>
public sealed record WordingComparison(
    IReadOnlyList<WordingPiece> Quoting, IReadOnlyList<WordingPiece> Source,
    int QuotingWords, int DifferingQuotingWords, int SourceWords, int DifferingSourceWords)
{
    /// <summary>The same words in the same order, once accents, case, punctuation and u/v, i/j are set aside.</summary>
    public bool SameWords => DifferingQuotingWords == 0 && DifferingSourceWords == 0;
}

/// <summary>
/// Compares a quotation with the line it quotes, and finds a quotation again
/// in a passage whose text has changed since the link was made.
///
/// <b>What counts as a different word.</b> Two words are the same when they
/// meet at the collation's orthography level - case, punctuation, accents and
/// breathings, final sigma, Latin u/v and i/j and the ae ligature all set
/// aside - and when the lunate sigma that First1KGreek prints meets the
/// ordinary one. Without those folds nearly every Greek quotation would show
/// every word as different, because the quoter's edition and the quoted
/// author's come from different houses with different conventions, and a
/// comparison that says everything differs says nothing. What is left is
/// what a reader of a quotation wants to see: the words the quoter changed,
/// added or left out.
///
/// The words are lined up by their longest common sequence, so an added word
/// shows as added rather than throwing every word after it out of step.
/// </summary>
public static class QuotationWording
{
    /// <summary>
    /// Letters, combining marks and digits. The soft hyphen inside a word -
    /// CSEL keeps a few - is part of the word and dropped from its key.
    /// </summary>
    private static readonly Regex Word = new(@"[\p{L}\p{M}\p{Nd}­]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Past this many cells the alignment table is not worth building; see Align.</summary>
    private const long MaxCells = 4_000_000;

    /// <summary>A word in a text: where it is, and the folded form it is compared by.</summary>
    public readonly record struct Token(int Start, int End, string Key);

    public static IReadOnlyList<Token> Tokens(string text, string? language)
    {
        var tokens = new List<Token>();
        foreach (Match match in Word.Matches(text))
        {
            var key = Key(match.Value, language);
            if (key.Length > 0) tokens.Add(new Token(match.Index, match.Index + match.Length, key));
        }

        return tokens;
    }

    /// <summary>The folded form of one word: see the class remarks.</summary>
    public static string Key(string word, string? language) =>
        CollationNormalizer.Normalize(word.Replace("­", string.Empty), CollationLevel.Orthography, language)
            .Replace('ϲ', 'σ')
            .Replace(" ", string.Empty);

    /// <summary>
    /// The quoter's words against the edition's. Either span may be the whole
    /// passage; the caller cuts them to the matched words where it knows them.
    /// </summary>
    public static WordingComparison Compare(string quoting, string source, string? language)
    {
        var q = Tokens(quoting, language);
        var s = Tokens(source, language);
        var (qMatched, sMatched) = Align(q, s);

        return new WordingComparison(
            Pieces(quoting, q, qMatched), Pieces(source, s, sMatched),
            q.Count, qMatched.Count(m => !m), s.Count, sMatched.Count(m => !m));
    }

    /// <summary>
    /// Where <paramref name="wanted"/>'s words fall in <paramref name="text"/>:
    /// the tightest stretch holding the most of them, or null when too few are
    /// there to say. For a passage that has changed since its link was made,
    /// so its recorded positions no longer fit.
    /// </summary>
    public static (int Start, int End)? Locate(string text, string wanted, string? language)
    {
        var tokens = Tokens(text, language);
        var keys = Tokens(wanted, language).Select(t => t.Key).ToList();
        var wantedSet = keys.ToHashSet(StringComparer.Ordinal);
        if (tokens.Count == 0 || wantedSet.Count == 0) return null;

        var hits = new List<int>();
        for (var i = 0; i < tokens.Count; i++)
            if (wantedSet.Contains(tokens[i].Key)) hits.Add(i);
        if (hits.Count == 0) return null;

        var reach = Math.Max(keys.Count * 2, keys.Count + 6);
        var best = (First: -1, Last: -1, Distinct: 0);
        for (var a = 0; a < hits.Count; a++)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var last = hits[a];
            for (var b = a; b < hits.Count && hits[b] - hits[a] <= reach; b++)
            {
                seen.Add(tokens[hits[b]].Key);
                last = hits[b];
            }

            if (seen.Count > best.Distinct) best = (hits[a], last, seen.Count);
        }

        // One shared word proves nothing in a long passage: "et" and καί are
        // everywhere. Half the quotation's words, and at least two, have to
        // be there - unless the quotation is itself a single word.
        var needed = wantedSet.Count == 1 ? 1 : Math.Max(2, (wantedSet.Count + 1) / 2);
        if (best.Distinct < needed) return null;

        return (tokens[best.First].Start, tokens[best.Last].End);
    }

    /// <summary>
    /// Up to <paramref name="words"/> words either side of a span, with an
    /// ellipsis where the passage goes on: enough to read the quotation in
    /// its sentence without printing a page of Augustine around it.
    /// </summary>
    public static (string Before, string After) Context(string text, int start, int end, int words = 24)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        var tokens = Word.Matches(text).Select(m => (m.Index, End: m.Index + m.Length)).ToList();

        var before = tokens.Where(t => t.End <= start).ToList();
        var from = before.Count > words ? before[^words].Index : 0;
        var after = tokens.Where(t => t.Index >= end).ToList();
        var to = after.Count > words ? after[words - 1].End : text.Length;

        var head = text[from..start];
        var tail = text[end..to];
        return ((from > 0 ? "… " : string.Empty) + head.TrimStart(), tail.TrimEnd() + (to < text.Length ? " …" : string.Empty));
    }

    /// <summary>
    /// Which words of each side belong to their longest common sequence.
    /// Quadratic, which is nothing for quotations of a few dozen words; two
    /// whole passages of thousands of words each - only ever compared when a
    /// changed text could not be placed - fall back to marking the words each
    /// side shares with the other at all.
    /// </summary>
    private static (bool[] Quoting, bool[] Source) Align(IReadOnlyList<Token> q, IReadOnlyList<Token> s)
    {
        var qMatched = new bool[q.Count];
        var sMatched = new bool[s.Count];
        if (q.Count == 0 || s.Count == 0) return (qMatched, sMatched);

        if ((long)q.Count * s.Count > MaxCells)
        {
            var sKeys = s.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
            var qKeys = q.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
            for (var i = 0; i < q.Count; i++) qMatched[i] = sKeys.Contains(q[i].Key);
            for (var j = 0; j < s.Count; j++) sMatched[j] = qKeys.Contains(s[j].Key);
            return (qMatched, sMatched);
        }

        var table = new int[q.Count + 1, s.Count + 1];
        for (var i = q.Count - 1; i >= 0; i--)
        for (var j = s.Count - 1; j >= 0; j--)
            table[i, j] = string.Equals(q[i].Key, s[j].Key, StringComparison.Ordinal)
                ? table[i + 1, j + 1] + 1
                : Math.Max(table[i + 1, j], table[i, j + 1]);

        for (int i = 0, j = 0; i < q.Count && j < s.Count;)
        {
            if (string.Equals(q[i].Key, s[j].Key, StringComparison.Ordinal))
            {
                qMatched[i++] = true;
                sMatched[j++] = true;
            }
            else if (table[i + 1, j] >= table[i, j + 1]) i++;
            else j++;
        }

        return (qMatched, sMatched);
    }

    /// <summary>
    /// The text cut into runs of agreeing and differing words. The space
    /// between two differing words goes with them, so "nunc et semper" added
    /// by a quoter reads as one marked phrase rather than three marked words.
    /// </summary>
    private static List<WordingPiece> Pieces(string text, IReadOnlyList<Token> tokens, bool[] matched)
    {
        var pieces = new List<WordingPiece>();

        void Add(string piece, bool differs)
        {
            if (piece.Length == 0) return;
            if (pieces.Count > 0 && pieces[^1].Differs == differs)
                pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + piece };
            else
                pieces.Add(new WordingPiece(piece, differs));
        }

        var at = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var differs = !matched[i];
            var gapDiffers = differs && i > 0 && !matched[i - 1];
            Add(text[at..tokens[i].Start], gapDiffers);
            Add(text[tokens[i].Start..tokens[i].End], differs);
            at = tokens[i].End;
        }

        Add(text[at..], false);
        return pieces;
    }
}
