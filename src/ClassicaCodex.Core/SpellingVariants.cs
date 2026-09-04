using System.Text;

namespace ClassicaCodex.Core;

/// <summary>
/// The spellings of a word that differ only in u/v and i/j.
///
/// These were one letter each in antiquity. Which glyph an edition prints is
/// the editor's decision, not the author's, and editors disagree: Teubner and
/// the older Migne reprints print consonantal u as "v" and consonantal i as
/// "j", while most modern critical texts print "u" and "i" throughout. The
/// same word therefore sits in this corpus under two spellings, and a search
/// for one of them silently returns a fraction of the evidence.
///
/// Measured against a full library, counting lines in Latin editions:
///
///     typed        found      really there     hidden
///     iustitia     1,425      4,172            65.8%   (justitia 2,742)
///     iudicium     2,523      6,610            61.8%   (judicium 4,081)
///     eiusdem      1,953      5,867            66.7%   (ejusdem 3,906)
///     iam         24,046     43,149            44.3%   (jam 19,103)
///     adiuvare        58        293            80.2%   (adjuvare, adiuuare)
///     vel         33,214     44,117            24.7%   (uel 10,903)
///     across 22 ordinary query words: 31.8% of the evidence hidden.
///
/// The sting is which way round it falls. "iustitia" and "iudicium" are the
/// spellings of every modern critical edition and every Latin textbook, so
/// the reader typing what they were taught got the smaller half, and the
/// application quietly rewarded typing "justitia" instead.
///
/// Early-modern English has the same convention and the same problem, so
/// this is not gated on language: "haue" for have (7,280 lines), "vpon" for
/// upon (5,684), "iust", "iudge". Greek is unaffected for free, since a
/// normalized Greek word contains no Latin u, v, i or j and Of() then hands
/// back the single word it was given.
///
/// Conflating two genuinely different words is the risk worth checking, and
/// it does not appear to exist. Of the 464 keys in this corpus where more
/// than one frequent Latin spelling folds together - ut/vt, qui/qvi,
/// eius/ejus, iam/jam, cuius/cujus, huius/hujus - every one is the same word
/// twice. That is what "u and v were one letter" means in practice.
/// </summary>
public static class SpellingVariants
{
    /// <summary>
    /// Above this many ambiguous letters the word is not expanded in full.
    ///
    /// 2^6 is 64 spellings, and 64 is far more headroom than it sounds:
    /// measured across the 1,200,881 distinct Latin and English words in a
    /// full library's index, 97.4% have four or fewer of these letters and
    /// 99.66% have six or fewer. The words past the cap are compounds like
    /// "iniuriosissimus" - real, but rare enough that spending hundreds of
    /// index probes on them is the wrong trade.
    /// </summary>
    public const int MaxAmbiguousLetters = 6;

    /// <summary>
    /// Every spelling of <paramref name="normalizedWord"/> that differs only
    /// in u/v and i/j, the word itself first. Expects a word that has already
    /// been through <see cref="WordNormalizer.Normalize"/>, since that is
    /// what the word index stores and what these are compared against.
    ///
    /// A word with none of these letters comes back as a list of one, so a
    /// caller can run everything through this without checking first.
    ///
    /// Past the cap it returns the word alongside the two uniform spellings -
    /// everything folded to u and i, and everything to v and j - which are
    /// the two an edition following a house style consistently would use.
    /// That is a deliberate loss of the mixed spellings for 0.34% of the
    /// vocabulary, in exchange for a bounded query.
    /// </summary>
    public static List<string> Of(string? normalizedWord)
    {
        if (string.IsNullOrEmpty(normalizedWord)) return new List<string>();

        var word = normalizedWord;
        var positions = new List<int>();
        for (var i = 0; i < word.Length; i++)
        {
            if (word[i] is 'u' or 'v' or 'i' or 'j') positions.Add(i);
        }

        if (positions.Count == 0) return new List<string> { word };

        if (positions.Count > MaxAmbiguousLetters)
        {
            return new List<string>
            {
                word,
                word.Replace('v', 'u').Replace('j', 'i'),
                word.Replace('u', 'v').Replace('i', 'j')
            }.Distinct(StringComparer.Ordinal).ToList();
        }

        // Doubled one position at a time rather than counted out in binary,
        // so the word itself stays at the head of the list and the order is
        // the same on every run - a search that returns its rows in a
        // different order each time it is asked is not one a person can cite.
        var results = new List<string>(1 << positions.Count) { word };

        foreach (var position in positions)
        {
            var swapped = word[position] switch
            {
                'u' => 'v',
                'v' => 'u',
                'i' => 'j',
                _ => 'i'
            };

            var grown = new List<string>(results.Count * 2);
            foreach (var spelling in results)
            {
                grown.Add(spelling);
                var builder = new StringBuilder(spelling);
                builder[position] = swapped;
                grown.Add(builder.ToString());
            }

            results = grown;
        }

        return results.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The variants of every word given, flattened and de-duplicated, with
    /// each word's own spelling kept ahead of the ones invented for it.
    ///
    /// Capped at <paramref name="maxTargets"/> in total. The cap counts
    /// whole words rather than truncating mid-way through one, so a query
    /// that reaches it is short some words' variants but never has one word
    /// half-expanded - a partial expansion would be worse than none, since
    /// it would look like a complete answer.
    /// </summary>
    public static List<string> ExpandAll(IEnumerable<string> normalizedWords, int maxTargets = 512)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<string>();

        // Every word's own spelling first, so that hitting the cap costs
        // variants and never costs a word the reader actually typed.
        var words = normalizedWords.Where(w => !string.IsNullOrEmpty(w)).ToList();
        foreach (var word in words)
        {
            if (seen.Add(word)) results.Add(word);
        }

        foreach (var word in words)
        {
            var variants = Of(word);
            if (results.Count + variants.Count > maxTargets) continue;
            foreach (var variant in variants)
            {
                if (seen.Add(variant)) results.Add(variant);
            }
        }

        return results;
    }
}
