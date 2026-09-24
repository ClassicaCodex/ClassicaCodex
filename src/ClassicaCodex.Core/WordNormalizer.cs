using System.Globalization;
using System.Text;

namespace ClassicaCodex.Core;

/// <summary>
/// Normalizes a Greek or Latin word form for matching: strips accents and
/// breathings, folds final sigma, and lowercases.
///
/// This matters more than it might sound. Perseus texts (and the lemma data
/// published against them) aren't perfectly consistent about accentuation,
/// or about precomposed vs combining Unicode for the same character - so
/// ᾳ can be one codepoint in one file and two in another. Matching on the
/// bare letters sidesteps all of that. The cost is a small amount of real
/// ambiguity (a few Greek pairs are distinguished only by accent), which is
/// the right trade for a reading tool.
/// </summary>
public static class WordNormalizer
{
    public static string Normalize(string word)
    {
        if (string.IsNullOrEmpty(word)) return string.Empty;

        // NFD splits precomposed characters into base letter + combining
        // marks, so the marks can simply be dropped.
        var decomposed = word.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (!char.IsLetter(ch)) continue;

            var lower = char.ToLowerInvariant(ch);

            // Final sigma and medial sigma are the same letter positionally,
            // so fold them together or λόγος won't match λόγοσ-stemmed data.
            //
            // Lunate sigma is the same letter again, in the rounded shape
            // papyri and inscriptions use and which some editors keep in
            // print. 87 editions in this corpus are set in it throughout -
            // the Suda, Herodian, Apollonius Dyscolus, Philodemus, Porphyry -
            // and without this fold none of them could be reached by anyone
            // typing an ordinary sigma. That stranded 349,421 index entries
            // across 84,799 distinct words, and 22.2% of every line
            // containing πόλις. Herodian's Περὶ ὀρθογραφίας was among the
            // texts an orthographic gap made unsearchable.
            //
            // ToLowerInvariant has already turned capital lunate sigma
            // (U+03F9) into this one, the same way it turns Σ into σ, so
            // only the lowercase form needs naming here.
            if (lower is 'ς' or 'ϲ') lower = 'σ';

            // Long s, for the same reason and with the same force. U+017F is
            // the tall ſ that manuscripts and early print use everywhere except
            // at the end of a word - it is the letter s in a different shape,
            // not a different letter, and ToLowerInvariant leaves it alone
            // because it is already lowercase.
            //
            // Measured across the Middle High German corpus, which transcribes
            // the letter shapes the scribe actually wrote: 431,099 occurrences,
            // far and away the commonest character that survived this function
            // without being a-z. It is the whole difference between "iſt" and
            // "ist", "eſſet" and "esset", "deſponſata" and "desponsata" - and
            // so between a manuscript reading being findable by someone typing
            // an ordinary s and not being findable at all.
            //
            // Also right for everything already in the library: where an OCR'd
            // printed text carries a long s, it means s there too.
            if (lower is 'ſ') lower = 's';

            sb.Append(lower);
        }

        // No trailing Normalize(FormC) here. NFD decomposition splits a
        // precomposed character into a base letter plus combining marks -
        // never into more than one base letter - so once every combining
        // mark above has been dropped, what's left is already in its
        // simplest form; there is nothing left for FormC to recompose.
        // Verified across every Latin and Greek Extended codepoint this
        // corpus actually uses (589 letters, including the full range of
        // precomposed polytonic Greek forms): with or without the FormC
        // call, the output was identical in every case. This runs once per
        // word during a multi-million-line index build, so the call was
        // pure cost with no behavioral effect.
        return sb.ToString();
    }

    /// <summary>
    /// Normalizes a dictionary headword for lookup, on top of the standard
    /// normalization.
    ///
    /// Two extra problems show up when matching lemma headwords against
    /// lexicon keys:
    ///
    /// 1. Homograph numbering. Lemma data marks separate dictionary words
    ///    that share a spelling as liber1/liber2, but the lexicon numbers
    ///    them differently (or not at all), and there's no reliable mapping
    ///    between the two schemes. Stripping the digits means a lookup can
    ///    return several entries - which is the honest outcome, since we
    ///    genuinely can't tell which numbered sense was meant.
    ///
    /// 2. Latin u/v and i/j. These were one letter each in antiquity, and
    ///    editions differ: lemma data may say "uos" where the lexicon says
    ///    "vos". Folding both directions makes the two agree.
    /// </summary>
    public static string NormalizeHeadword(string headword, string language)
    {
        if (string.IsNullOrEmpty(headword)) return string.Empty;

        var trimmed = headword.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (trimmed.Length == 0) trimmed = headword;

        var normalized = Normalize(trimmed);

        if (string.Equals(language, "lat", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Replace('v', 'u').Replace('j', 'i');
        }

        return normalized;
    }

    /// <summary>
    /// U+00AD SOFT HYPHEN: the hyphen a printed page puts at the end of a
    /// line when a word runs over onto the next one.
    /// </summary>
    private const char SoftHyphen = (char)0x00AD;

    /// <summary>
    /// Rejoins words that a printed line break split in two.
    ///
    /// <b>This is a search-correctness fix, not a tidiness measure.</b> Migne
    /// was digitised with the line breaks of the printed page left in the
    /// text: "gratiam" set across two lines arrives as "gra&lt;SHY&gt; tiam".
    /// 85,026 of the 286,531 Latin lines in patrologia-latina carry a soft
    /// hyphen - 29.7% of the collection - and 99.9% of the 86,188 occurrences
    /// corpus-wide are a soft hyphen followed by a space, which is exactly
    /// this. Every other collection is clean: csel 120 nodes, perseus-latin 2,
    /// first1k-greek 2, perseus-greek and menota none.
    ///
    /// Left alone the damage runs both ways. TokenizeLine splits on
    /// whitespace, so the reader searching for the whole word misses every
    /// broken occurrence of it in the whole of Migne; and the halves become
    /// live index entries in its place. They are Latin word ENDINGS, and they
    /// were sitting in the index in their thousands - 'tur' 12,692 rows, 'rum'
    /// 12,284, 'bus' 8,198, 'runt' 3,672, 'tione' 3,365, 'tatem' 1,911,
    /// 'niam' 1,674 - which is the worst possible shape for anything
    /// frequency-based, the same way the detached punctuation fragments were
    /// that TeiParser.AppendText exists to prevent.
    ///
    /// Only the whitespace needs skipping here. A soft hyphen with no space
    /// after it (the other 0.1%) is already joined by Normalize, which keeps
    /// letters and drops everything else - so both spellings of the break end
    /// up as one word, which is the point.
    /// </summary>
    public static string JoinSoftHyphenBreaks(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Most lines have no soft hyphen at all, and this runs once per line
        // over 2.3 million of them on every index build. Nothing is allocated
        // for the 70% that come back here untouched.
        var first = text.IndexOf(SoftHyphen);
        if (first < 0) return text;

        var sb = new StringBuilder(text.Length);
        sb.Append(text, 0, first);

        for (var i = first; i < text.Length; i++)
        {
            if (text[i] != SoftHyphen)
            {
                sb.Append(text[i]);
                continue;
            }

            // Drop the hyphen and the line break it sat at the end of, so the
            // two halves close up. The loop's own i++ moves past the hyphen.
            while (i + 1 < text.Length && char.IsWhiteSpace(text[i + 1])) i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// The distinct indexable words in one line, exactly as the word index
    /// stores them.
    ///
    /// <b>This lives here so there is one of it.</b> It was private to
    /// WordIndexService, which is in the ingestion project, and the two places
    /// that need to REMOVE a line's index entries are repositories, which the
    /// ingestion project depends on rather than the other way round. Having
    /// them delete by line id instead cost a skip-scan of the whole index -
    /// see EditionRepository - and having them keep a second copy of this
    /// would be worse: a tokenizer that drifts from the one that did the
    /// inserting silently orphans rows instead of deleting them, and nothing
    /// would say so.
    ///
    /// The 200-character cap and the Distinct are both load-bearing: the cap
    /// is what keeps a run-on OCR artefact out of the index, and the pair
    /// (word, line) is the index's primary key, so a repeated word in one line
    /// is one row and must be offered for deletion once.
    ///
    /// The soft-hyphen pass has to come before the split rather than after
    /// it - see JoinSoftHyphenBreaks. Once the whitespace has done its work
    /// the two halves are separate tokens and nothing downstream can tell
    /// them from two real words.
    /// </summary>
    public static IEnumerable<string> TokenizeLine(string text) =>
        JoinSoftHyphenBreaks(text)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(w => w.Length > 0 && w.Length <= 200)
            .Distinct(StringComparer.Ordinal);
}
