namespace ClassicaCodex.Core;

/// <summary>
/// Which words a text is actually made of, ranked by how much of it they
/// account for.
///
/// This is the standard apparatus of learning a classical language and the
/// thing a beginner needs before they need anything else: a text is hard not
/// because its grammar is exotic but because every third word is unknown, and
/// a few hundred headwords carry most of any Greek or Latin work. Knowing
/// which few hundred turns an impossible text into a slow one.
///
/// Counted from the text itself rather than from WordIndex. WordIndex records
/// each word once per line - it answers "which lines contain this word", not
/// how many times - so a frequency list built on it would be reporting line
/// counts as word counts. Every percentage here would have been wrong in a
/// way nothing on screen would reveal. Counting the text directly also means
/// this is unaffected by the word index going stale.
/// </summary>
public static class VocabularyProfile
{
    /// <summary>
    /// One headword and what it accounts for.
    ///
    /// CumulativeCoverage is the share of all running words covered by this
    /// entry and everything above it - the number that answers "how far do I
    /// get if I learn down to here".
    /// </summary>
    public sealed record Entry(
        int Rank,
        string Headword,
        int Occurrences,
        double CumulativeCoverage,
        bool Ambiguous);

    public sealed record Result(
        IReadOnlyList<Entry> Entries,
        int TotalTokens,
        int UnknownTokens)
    {
        /// <summary>
        /// Share of running words whose form has no lemma data at all. These
        /// can never be covered by learning headwords from this list, so a
        /// coverage figure that ignored them would be promising more than it
        /// can deliver on a work with thin lemma coverage.
        /// </summary>
        public double UnknownShare => TotalTokens == 0 ? 0 : (double)UnknownTokens / TotalTokens;
    }

    /// <summary>
    /// Builds the profile from already-tokenised form counts and a form to
    /// headword map.
    ///
    /// Ambiguity is real in this data - one form genuinely maps to several
    /// headwords and nothing in the form separates them - so a form's
    /// occurrences count towards every candidate rather than being split
    /// fractionally between them or assigned to a guess. That inflates
    /// individual counts, which is why ranking and coverage are computed
    /// differently: a token is counted as covered the first time any of its
    /// candidate headwords is reached, so cumulative coverage stays a true
    /// share of the text and cannot exceed it.
    ///
    /// Entries touched by an ambiguous form are marked, because a reader
    /// deciding what to memorise should know when a count is partly
    /// borrowed from forms that may belong to another word.
    /// </summary>
    public static Result Build(
        IReadOnlyDictionary<string, int> formCounts,
        IReadOnlyDictionary<string, List<string>> headwordsByForm)
    {
        var totalTokens = formCounts.Values.Sum();

        var unknownTokens = formCounts
            .Where(f => !headwordsByForm.TryGetValue(f.Key, out var h) || h.Count == 0)
            .Sum(f => f.Value);

        // Ranking metric: every candidate headword gets the form's full
        // count. See the remarks above for why this is not the same number
        // as the coverage below.
        var headwordTotals = new Dictionary<string, int>(StringComparer.Ordinal);
        var headwordAmbiguous = new Dictionary<string, bool>(StringComparer.Ordinal);
        var formsByHeadword = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (form, count) in formCounts)
        {
            if (!headwordsByForm.TryGetValue(form, out var headwords) || headwords.Count == 0) continue;

            var ambiguous = headwords.Count > 1;

            foreach (var headword in headwords)
            {
                headwordTotals[headword] = headwordTotals.GetValueOrDefault(headword) + count;
                if (ambiguous) headwordAmbiguous[headword] = true;

                if (!formsByHeadword.TryGetValue(headword, out var forms))
                {
                    forms = new List<string>();
                    formsByHeadword[headword] = forms;
                }

                forms.Add(form);
            }
        }

        var ranked = headwordTotals
            .OrderByDescending(h => h.Value)
            .ThenBy(h => h.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var entries = new List<Entry>(ranked.Count);
        var countedForms = new HashSet<string>(StringComparer.Ordinal);
        var coveredTokens = 0;
        var rank = 0;

        foreach (var (headword, occurrences) in ranked)
        {
            rank++;

            // A form is counted once, at the first headword that claims it.
            // Without this an ambiguous form would be added to the running
            // total twice and coverage would climb past 100%.
            foreach (var form in formsByHeadword[headword])
            {
                if (countedForms.Add(form)) coveredTokens += formCounts[form];
            }

            entries.Add(new Entry(
                rank,
                headword,
                occurrences,
                totalTokens == 0 ? 0 : (double)coveredTokens / totalTokens,
                headwordAmbiguous.GetValueOrDefault(headword)));
        }

        return new Result(entries, totalTokens, unknownTokens);
    }

    /// <summary>
    /// How many headwords are needed to reach a given share of the text -
    /// the "learn these 250 words and you can read 80% of it" number.
    ///
    /// Returns the whole list's length when the target is beyond what the
    /// lemma data can reach, which is the honest answer for a work whose
    /// forms are largely unlemmatised: the target is not attainable from
    /// this list at all.
    /// </summary>
    public static int HeadwordsToReach(IReadOnlyList<Entry> entries, double targetCoverage)
    {
        foreach (var entry in entries)
        {
            if (entry.CumulativeCoverage >= targetCoverage) return entry.Rank;
        }

        return entries.Count;
    }

    /// <summary>
    /// Counts word forms in the way the lemma data is keyed, so the two can
    /// be matched. Splits on whitespace and normalises exactly as the word
    /// index does - but without its per-line Distinct, because this needs
    /// how many times, which is precisely what the index does not record.
    /// </summary>
    public static Dictionary<string, int> CountForms(IEnumerable<string> lines)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            foreach (var raw in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var word = WordNormalizer.Normalize(raw);
                if (word.Length == 0 || word.Length > 200) continue;

                counts[word] = counts.GetValueOrDefault(word) + 1;
            }
        }

        return counts;
    }
}
