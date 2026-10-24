using System.Globalization;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;

namespace ClassicaCodex.Mac.Features.Translation;

/// <summary>One word of the Whole Passage crib: the reading the context permits, and what else it could be.</summary>
internal sealed record CribLine(string Word, string? Headword, string Grammar, IReadOnlyList<string> Alternatives);

/// <summary>
/// The workbench's reading of a passage - its words, and what each one is -
/// as the Windows TranslationWorkbenchForm works it out, moved out of the
/// window so it can be tested without one. Nothing here is generated: the
/// headword comes from the lemma data and the parse from its morphology tag,
/// which is the same information a printed commentary would give.
/// </summary>
internal static class PassageCrib
{
    /// <summary>
    /// Every word of the passage, in the order it appears, with punctuation
    /// stripped but the original accents kept - accents are how a form is
    /// recognised, and stripping them here would show a word nobody wrote.
    ///
    /// Deliberately not deduplicated, and deliberately not filtered by
    /// length. This list is how someone walks a sentence, so it has to
    /// correspond to the sentence: collapsing a repeated word breaks the
    /// correspondence, and the one-letter words it used to drop are among
    /// the most grammatically loaded things in Greek - the article and the
    /// relative pronoun are single letters, and in a line like "to de peri
    /// HO dia ton phusikon" the whole clause turns on the one that was being
    /// discarded.
    ///
    /// The cost is a long list for a long passage, which is honest: the
    /// passage really does have that many words in it.
    /// </summary>
    public static IEnumerable<string> SplitWords(string text)
    {
        // Rejoined first, for the same reason Word Study does it: a word the
        // printed page broke across two lines would otherwise arrive here as
        // two fragments, and the lemma data would be asked about both and
        // find nothing for either.
        foreach (var raw in WordNormalizer.JoinSoftHyphenBreaks(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Elision leaves an apostrophe that isn't part of the word -
            // stripping non-letters turns "d'" into "d", which is the form
            // the lemma data actually holds.
            var word = new string(raw.Where(char.IsLetter).ToArray());
            if (word.Length > 0) yield return word;
        }
    }

    /// <summary>
    /// The part every citation reference in this work shares, so it can be
    /// left out of the passage picker. Some corpora store a full CTS URN per
    /// line, so 45 identical characters precede the part that differs and
    /// every entry in the picker reads the same.
    ///
    /// Backed off to the last separator rather than used raw: the longest
    /// common prefix of "1.10" and "1.11" is "1.1", and trimming that would
    /// leave "0" and "1". Cutting at the separator leaves "10" and "11".
    ///
    /// Empty when trimming would leave any entry with nothing at all, which
    /// is the case for a work whose references are already short.
    /// </summary>
    public static string CommonCitationPrefix(IReadOnlyList<TextNode> passages)
    {
        if (passages.Count < 2) return string.Empty;

        var prefix = passages[0].CitationRef;

        foreach (var passage in passages)
        {
            var i = 0;
            while (i < prefix.Length && i < passage.CitationRef.Length && prefix[i] == passage.CitationRef[i]) i++;

            prefix = prefix[..i];
            if (prefix.Length == 0) return string.Empty;
        }

        var cut = Math.Max(prefix.LastIndexOf('.'), prefix.LastIndexOf(':'));
        if (cut < 0) return string.Empty;

        prefix = prefix[..(cut + 1)];

        return passages.Any(p => p.CitationRef.Length <= prefix.Length) ? string.Empty : prefix;
    }

    /// <summary>
    /// The crib line for every word, in order, from every reading of every
    /// word. Every reading is needed first, because choosing between them
    /// needs the neighbours - and the neighbours are ambiguous too.
    /// </summary>
    public static List<CribLine> Build(
        IReadOnlyList<string> words, IReadOnlyDictionary<string, List<(string Headword, string? PartOfSpeech)>> lookups)
    {
        var lines = new List<CribLine>(words.Count);
        for (var i = 0; i < words.Count; i++)
        {
            var permitted = PermittedByContext(words, lookups, i);
            lines.Add(Describe(words[i], permitted, lookups[words[i]]));
        }

        return lines;
    }

    /// <summary>
    /// Narrows each word to the readings its neighbours permit.
    ///
    /// Not a choice between readings - a filter. Picking one and printing it
    /// was the mistake behind every version of this: agreement is symmetric,
    /// so two words can each justify the other's wrong reading and no amount
    /// of re-checking separates them. "ton kakon" sat at feminine because
    /// the article and kake agree with each other perfectly well.
    ///
    /// Keeping every permitted reading and reporting only what they share
    /// makes that impossible. Where the context genuinely narrows things,
    /// one reading survives and full detail is shown; where it doesn't, the
    /// undetermined feature simply isn't claimed. The article comes out as
    /// "genitive plural" with no gender, which is exactly what the form
    /// tells you and no more.
    ///
    /// A reading with no agreement features at all - a preposition, a
    /// particle - is never filtered out, having nothing to disagree with.
    /// </summary>
    public static List<(string Headword, string? PartOfSpeech)> PermittedByContext(
        IReadOnlyList<string> words,
        IReadOnlyDictionary<string, List<(string Headword, string? PartOfSpeech)>> lookups,
        int index)
    {
        var candidates = lookups[words[index]];
        if (candidates.Count < 2) return candidates;

        var neighbourFeatures = new List<MorphologyDecoder.Parse>();
        foreach (var offset in new[] { -1, 1 })
        {
            var at = index + offset;
            if (at < 0 || at >= words.Count) continue;

            neighbourFeatures.AddRange(lookups[words[at]]
                .Select(r => MorphologyDecoder.Decode(r.PartOfSpeech))
                .Where(HasAgreementFeatures));
        }

        if (neighbourFeatures.Count == 0) return candidates;

        var permitted = candidates
            .Where(c =>
            {
                var parse = MorphologyDecoder.Decode(c.PartOfSpeech);
                if (!HasAgreementFeatures(parse)) return true;
                return neighbourFeatures.Any(neighbour => Agrees(parse, neighbour));
            })
            .ToList();

        // Filtering must never promote an index artifact. A reading with no
        // agreement features is always permitted - right for a preposition,
        // but it also lets through a row that is just the surface form filed
        // under itself with no part of speech at all. If context has ruled
        // out every reading that actually parses, the context was misleading
        // rather than the artifact right.
        //
        // Tested on the parse rather than the headword: an artifact can look
        // like a perfectly good Greek word - "ta" for the article is
        // lower-case, accented and unpunctuated - and the thing that gives
        // it away is having no grammar behind it.
        if (!permitted.Any(HasRealParse) && candidates.Any(HasRealParse)) return candidates;

        if (permitted.Count == 0) return candidates;

        // Positively agreeing readings lead. Filtering alone isn't enough:
        // a reading with no agreement features is never excluded, so for
        // "ton Dia" the preposition dia survives beside Zeus and then wins
        // on resembling the form. Zeus is the one the article actually
        // agrees with, and that is stronger evidence than spelling.
        //
        // A stable ordering, so everything already decided about ranking
        // still applies within each group.
        return permitted
            .OrderBy(c =>
            {
                var parse = MorphologyDecoder.Decode(c.PartOfSpeech);
                if (!HasAgreementFeatures(parse)) return 1;
                return neighbourFeatures.Any(neighbour => Agrees(parse, neighbour)) ? 0 : 1;
            })
            .ToList();
    }

    private static CribLine Describe(
        string word,
        List<(string Headword, string? PartOfSpeech)> permitted,
        List<(string Headword, string? PartOfSpeech)> allReadings)
    {
        var reading = permitted.FirstOrDefault();
        if (string.IsNullOrEmpty(reading.Headword)) return new CribLine(word, null, string.Empty, Array.Empty<string>());

        // Alternatives come from everything the lemma data offered, not just
        // what context allowed - a reading the neighbours ruled out is still
        // worth seeing, because the ruling out is a guess too.
        var alternatives = allReadings
            .Select(r => r.Headword)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Where(h => !string.Equals(h, reading.Headword, StringComparison.CurrentCultureIgnoreCase))
            .Take(2)
            .ToList();

        return new CribLine(word, reading.Headword, DescribeSharedFeatures(reading, permitted), alternatives);
    }

    /// <summary>
    /// What every permitted reading of this headword agrees on.
    ///
    /// Includes tense, mood and voice as well as the nominal features: a
    /// participle whose readings differ only in case is still definitely a
    /// present active participle, and reporting merely "verb" would throw
    /// away the part that was never in doubt.
    /// </summary>
    private static string DescribeSharedFeatures(
        (string Headword, string? PartOfSpeech) reading,
        List<(string Headword, string? PartOfSpeech)> permitted)
    {
        var parse = MorphologyDecoder.Decode(reading.PartOfSpeech);
        if (!parse.IsDecoded || parse.Description.Length == 0) return string.Empty;
        if (!IsRealPartOfSpeech(parse)) return string.Empty;

        var sameHeadword = permitted
            .Where(r => string.Equals(r.Headword, reading.Headword, StringComparison.CurrentCultureIgnoreCase))
            .Select(r => MorphologyDecoder.Decode(r.PartOfSpeech))
            .Where(candidate => candidate.IsDecoded)
            .ToList();

        if (sameHeadword.Select(candidate => candidate.Description).Distinct().Count() <= 1) return parse.Description;

        var features = new[]
            {
                Unanimous(sameHeadword, c => c.Tense),
                Unanimous(sameHeadword, c => c.Voice),
                Unanimous(sameHeadword, c => c.Mood),
                Unanimous(sameHeadword, c => c.Person),
                Unanimous(sameHeadword, c => c.Gender),
                Unanimous(sameHeadword, c => c.Case),
                Unanimous(sameHeadword, c => c.Number)
            }
            .Where(feature => !string.IsNullOrEmpty(feature))
            .ToList();

        var pos = parse.PartOfSpeech ?? string.Empty;

        if (features.Count == 0) return pos;

        return pos.Length > 0 ? $"{pos}: {string.Join(" ", features)}" : string.Join(" ", features);
    }

    /// <summary>The value every reading agrees on, or null where they differ.</summary>
    private static string? Unanimous(List<MorphologyDecoder.Parse> readings, Func<MorphologyDecoder.Parse, string?> feature)
    {
        var values = readings.Select(feature).Distinct().ToList();
        return values.Count == 1 ? values[0] : null;
    }

    private static bool HasAgreementFeatures(MorphologyDecoder.Parse parse) =>
        parse.Gender != null && parse.Number != null && parse.Case != null;

    private static bool Agrees(MorphologyDecoder.Parse a, MorphologyDecoder.Parse b) =>
        HasAgreementFeatures(a) && HasAgreementFeatures(b)
        && a.Gender == b.Gender && a.Number == b.Number && a.Case == b.Case;

    /// <summary>
    /// Puts the candidate readings of a form in a sensible order.
    ///
    /// The lemma data returns every row matching a surface form, and the
    /// order it returns them in means nothing. Three signals decide, in
    /// order:
    ///
    /// Artifacts last. A headword carrying no accent or breathing at all, or
    /// written in capitals, is an index entry rather than a lexicon one -
    /// "των" and "KAI" alongside the real ὁ and καί.
    ///
    /// Then a headword that matches the form itself, ignoring accents. A
    /// word that is its own dictionary form usually is: εἰς beside εἰμί for
    /// the form εἰς, καί beside εἰ for Καὶ.
    ///
    /// Then the longest shared opening. ἀσθενής shares six letters with
    /// ἀσθενῆ where ἀσθενέω shares five, which is the difference between
    /// "being weak" and "he was weak".
    ///
    /// None of this understands the sentence, so a form whose reading truly
    /// depends on agreement - πάντα as πᾶς or πάντη - is still a coin toss.
    /// That is what the alternatives line is for.
    /// </summary>
    public static List<(string Headword, string? PartOfSpeech)> Rank(
        List<(string Headword, string? PartOfSpeech)> candidates, string surfaceForm)
    {
        var form = WordNormalizer.Normalize(surfaceForm);

        return candidates
            .OrderBy(c =>
            {
                var parse = MorphologyDecoder.Decode(c.PartOfSpeech);
                if (!parse.IsDecoded) return 1;
                return IsRealPartOfSpeech(parse) ? 0 : 2;
            })
            .ThenBy(c => LooksLikeALexiconHeadword(c.Headword, surfaceForm) ? 0 : 1)
            .ThenBy(c => WordNormalizer.Normalize(c.Headword) == form ? 0 : 1)
            .ThenByDescending(c => SharedOpening(form, WordNormalizer.Normalize(c.Headword)))
            .ThenBy(c => c.Headword.Length)
            .ToList();
    }

    /// <summary>Whether a reading carries a decodable, genuine part of speech.</summary>
    private static bool HasRealParse((string Headword, string? PartOfSpeech) reading)
    {
        var parse = MorphologyDecoder.Decode(reading.PartOfSpeech);
        return parse.IsDecoded && IsRealPartOfSpeech(parse);
    }

    /// <summary>
    /// Whether a headword looks like a lexicon entry rather than an index
    /// artifact - shouted in capitals, carrying stray punctuation, echoing
    /// the surface form with a capital, or wholly unaccented.
    /// </summary>
    private static bool LooksLikeALexiconHeadword(string headword, string surfaceForm)
    {
        if (headword.Length == 0) return false;

        // Shouted in capitals - an index key, not an entry.
        if (headword.ToUpperInvariant() == headword && headword.Any(char.IsLetter)) return false;

        var decomposed = headword.Normalize(System.Text.NormalizationForm.FormD);

        // Stray punctuation or spacing. A lexicon headword is letters and
        // the marks on them; "touto '" is an elided form filed under itself.
        if (decomposed.Any(c => !char.IsLetter(c) && c != '-'
                                && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            return false;
        }

        // The surface form echoed back with a capital, because the text
        // happened to have it at the start of a sentence. A genuine headword
        // is lower-case unless it is a name - and a name would not match the
        // inflected form letter for letter, which is why Zeus beside the
        // accusative Dia is unaffected.
        if (char.IsUpper(headword[0]) && WordNormalizer.Normalize(headword) == WordNormalizer.Normalize(surfaceForm))
        {
            return false;
        }

        // Polytonic Greek marks every word, so no diacritic at all means the
        // row came from somewhere other than the dictionary.
        return decomposed.Any(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark);
    }

    private static int SharedOpening(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        var shared = 0;
        while (shared < length && a[shared] == b[shared]) shared++;
        return shared;
    }

    private static bool IsRealPartOfSpeech(MorphologyDecoder.Parse parse) =>
        !parse.Description.StartsWith("punctuation", StringComparison.OrdinalIgnoreCase)
        && !parse.Description.StartsWith("unclassified", StringComparison.OrdinalIgnoreCase);
}
