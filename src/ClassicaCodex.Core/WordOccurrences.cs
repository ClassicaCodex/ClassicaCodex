namespace ClassicaCodex.Core;

/// <summary>
/// Where in a line the word a reader searched for actually sits.
///
/// Finding a line and pointing at the word in it had drifted apart. The
/// search matches through the word index, which folds accents, breathings,
/// both sigmas, and both halves of u/v and i/j - so a query typed without
/// accents finds the line that has them, and "iustitia" finds the edition
/// printing "justitia". Everything that then had to point at the word was
/// still doing a literal, case-insensitive IndexOf of what was typed.
///
/// So the search returned rows and the highlighter had nothing to highlight,
/// and the concordance - whose entire output is the word framed by its
/// context - fell back to printing the line with "(stemmed match)" where the
/// keyword column should be. From the reader's side that looks like the
/// application returning lines that do not contain the word: the single most
/// reportable-looking thing a search can do.
///
/// Matching here is deliberately the same shape the index build uses -
/// whitespace-delimited tokens, each run through WordNormalizer - because
/// anything else would point at a different set of words from the one that
/// selected the line.
/// </summary>
public static class WordOccurrences
{
    /// <summary>
    /// Every normalized spelling a query could have matched, which is what
    /// the index was asked for and therefore what may be sitting in the line.
    /// </summary>
    public static HashSet<string> TargetsFor(string? query)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(query)) return targets;

        foreach (var word in WordNormalizer.JoinSoftHyphenBreaks(query)
                     .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = WordNormalizer.Normalize(word);
            if (normalized.Length == 0) continue;

            foreach (var spelling in SpellingVariants.Of(normalized)) targets.Add(spelling);
        }

        return targets;
    }

    /// <summary>
    /// The spans of <paramref name="text"/> holding one of
    /// <paramref name="targets"/>, in order, without overlaps.
    ///
    /// A span covers the word as printed and not the punctuation around it:
    /// the token is split off by whitespace so that normalizing it agrees
    /// with the index, and the span is then pulled in to the letters, so
    /// highlighting "λόγος," marks the word and leaves the comma alone.
    ///
    /// A word broken across a printed line break - "gra&lt;SHY&gt; tiam",
    /// which is 29.7% of the lines in patrologia-latina - is one word to the
    /// index and gets one span per half here, so both halves highlight where
    /// they sit. It cannot be one span: the whitespace between them belongs
    /// to the page, not the word, and a single span would paint over it.
    /// Without this the soft-hyphen fix in the tokenizer would have handed
    /// the reader thousands of new Migne hits with nothing marked in any of
    /// them, which is the exact failure this file was written to end.
    /// </summary>
    public static List<(int Start, int Length)> Find(string? text, IReadOnlyCollection<string> targets)
    {
        var spans = new List<(int Start, int Length)>();
        if (string.IsNullOrEmpty(text) || targets.Count == 0) return spans;

        // The pieces of the token being looked at: one unless a line break
        // split it. Reused across the loop rather than allocated per word.
        var pieces = new List<(int Start, int End)>();

        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }

            pieces.Clear();

            while (true)
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                pieces.Add((start, i));

                // Ending in a soft hyphen means the rest of the word is on
                // the next line of the printed page. Step over the break and
                // keep reading the same word.
                if (text[i - 1] != SoftHyphen) break;

                var resumes = i;
                while (resumes < text.Length && char.IsWhiteSpace(text[resumes])) resumes++;
                if (resumes >= text.Length) break;

                i = resumes;
            }

            var token = pieces.Count == 1
                ? text[pieces[0].Start..pieces[0].End]
                : string.Concat(pieces.Select(p => text[p.Start..p.End]));

            // Normalize drops the soft hyphen itself along with every other
            // non-letter, so the rejoined halves arrive as one word without
            // anything further being done to them here.
            if (!targets.Contains(WordNormalizer.Normalize(token))) continue;

            foreach (var (pieceStart, pieceEnd) in pieces)
            {
                // In to the letters. A token that is entirely punctuation
                // cannot reach here, since it would have normalized to
                // nothing and no target is empty - but one PIECE of a broken
                // word can be (a stray soft hyphen standing alone), and that
                // piece simply contributes no span.
                var from = pieceStart;
                var to = pieceEnd - 1;
                while (from <= to && !char.IsLetter(text[from])) from++;
                while (to >= from && !char.IsLetter(text[to])) to--;

                if (from <= to) spans.Add((from, to - from + 1));
            }
        }

        return spans;
    }

    /// <summary>
    /// U+00AD SOFT HYPHEN - see WordNormalizer.JoinSoftHyphenBreaks, which is
    /// what put the rejoined word into the index this points back into.
    /// </summary>
    private const char SoftHyphen = (char)0x00AD;
}
