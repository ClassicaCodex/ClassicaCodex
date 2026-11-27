using System.Globalization;

namespace ClassicaCodex.Core.Citations;

/// <summary>A work in the library as the abbreviations are matched against it.</summary>
/// <param name="Key">Its identifier without a CTS head, as <c>CopiedCitationResolver.Key</c> writes it: "tlg0059.tlg030".</param>
/// <param name="Author">The author's name as the library stores it: "Plato", "Cicero, Marcus Tullius".</param>
/// <param name="Title">The work's title: "Republic", "De Officiis".</param>
public sealed record LibraryWork(string Key, string Author, string Title);

/// <summary>
/// A work a reference's names may mean, and the reference to look up in it.
/// </summary>
/// <param name="Work">The work's identifier: "tlg0014.tlg018".</param>
/// <param name="Passage">The reference within it. For an orator cited by speech, without the speech's number.</param>
/// <param name="Score">How closely the names match it: higher is closer.</param>
/// <param name="Citations">How often the dictionaries cite it, which settles ties.</param>
/// <param name="NamesUsed">How many of the names, counted from the last, named it.</param>
public sealed record WorkCandidate(string Work, IReadOnlyList<string> Passage, int Score, int Citations, int NamesUsed = 0);

/// <summary>
/// Which work the names in front of a reference mean: "Thuc.", "Pl. Resp.",
/// "Hom. Il.", "Plato, Republic", "Dem." with a speech number.
///
/// <b>Exactly as the dictionaries print them.</b> An abbreviation LSJ or
/// Lewis and Short print - "Pl. R.", "Verg. A.", "Il." - names the work they
/// use it for (<see cref="CitationAbbreviations"/>).
///
/// <b>As an author and a work.</b> A modern footnote mostly writes the
/// dictionaries' abbreviations, or lengthens them, or writes the names out.
/// So the names are also read as an author followed by a work:
/// <list type="bullet">
/// <item>The author is the dictionaries' abbreviation for one ("Pl.",
/// "Cic.", "Arist."), or the start of a word of the author's name in the
/// library ("Thuc" of Thucydides, "Verg" or "Virg" of "P. Vergilius Maro
/// (Virgil)").</item>
/// <item>The work is the dictionaries' abbreviation for one of that author's
/// works; or one that abbreviation begins ("Resp." of Plato's "R.", "Nub." of
/// Aristophanes' "Nu.", "Aen." of Vergil's "A."); or, word by word, the start
/// of the words of the library's title ("Nic. Eth." of "Nicomachean Ethics",
/// "OT" of "Oedipus Tyrannus"); or initials of the dictionaries' ("Bell.
/// Gall." of "B. G.").</item>
/// <item>No work at all is the author's only one ("Thuc. 2.40.1"), the one the
/// dictionaries cite under the author's name alone ("Hdt."), or - for an
/// orator cited by speech - the one the first number names ("Dem. 18.169" is
/// speech 18, section 169).</item>
/// </list>
///
/// The longest run of the names that matches anything is taken, so the
/// capitalised words prose puts in front of a reference ("See", the first word
/// of a sentence) fall away, and an author's abbreviation is never passed
/// over for a work's alone. A work's abbreviation written alone ("Off.",
/// "Resp.") counts for less than one with its author, since the dictionaries
/// print it alone only after naming the author, and the same letters name
/// other works under other authors (the Hippocratic "Off.", Aristotle's
/// "Resp.").
///
/// The dictionaries' work identifiers are Perseus's, and a few authors are
/// held in the library under others (Suetonius' lives as phi1348.abo012, not
/// phi1348.phi012): such a work is taken to be the library's work of the same
/// author with the same number.
/// </summary>
public sealed class WorkAbbreviations
{
    private const int Exact = 6;

    // A match that is likelier wrong than an author and a work matched apart:
    // a work's abbreviation alone, or words run together.
    private const int Loose = 4;

    private static readonly HashSet<string> NameStopWords = new(StringComparer.Ordinal) { "the", "of", "de", "von", "van", "der", "and", "et" };

    private static readonly HashSet<string> TitleStopWords = new(StringComparer.Ordinal)
    {
        "the", "of", "on", "and", "in", "a", "an", "de", "to", "for", "pro", "ad", "et", "against", "about",
    };

    private readonly Dictionary<string, List<(string Work, int Citations)>> _whole = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Work, int Citations)>> _wholeCompact = new(StringComparer.Ordinal);
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private readonly HashSet<string> _firstWords = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _authorAbbreviations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _workAbbreviations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _onlyWorkCited = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, string>> _speechesByAbbreviation = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, string>> _speechesByAuthor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _citations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<LibraryWork>> _libraryByAuthor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _authorWords = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _titleWords = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _worksByAuthor = new(StringComparer.Ordinal);

    public WorkAbbreviations(IEnumerable<LibraryWork> library, CitationAbbreviations? abbreviations = null)
    {
        abbreviations ??= CitationAbbreviations.Default;

        foreach (var work in library)
        {
            var key = work.Key.ToLowerInvariant();
            var author = Author(key);
            if (!_libraryByAuthor.TryGetValue(author, out var works)) _libraryByAuthor[author] = works = new();
            works.Add(work with { Key = key });
            _authorWords.TryAdd(author, Words(work.Author, NameStopWords));
            _titleWords[key] = Words(work.Title, TitleStopWords);
            _held.Add(key);
            Remember(author, key);
        }

        Learn(abbreviations);
    }

    /// <summary>
    /// The library's identifier for a work the dictionaries name: the same,
    /// or, where the library holds that author's works under another prefix,
    /// its work of the same author with the same number ("phi1348.phi012" is
    /// the library's "phi1348.abo012").
    /// </summary>
    private string Canonical(string work)
    {
        work = work.ToLowerInvariant();
        if (_held.Contains(work) || !_libraryByAuthor.TryGetValue(Author(work), out var held)) return work;
        var number = new string(work.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        if (number.Length == 0) return work;
        var same = held.Where(w => w.Key.EndsWith(number, StringComparison.Ordinal)
                                   && !char.IsDigit(w.Key[^(number.Length + 1)])).ToList();
        return same.Count == 1 ? same[0].Key : work;
    }

    /// <summary>
    /// The works the names may mean, closest first, each with the reference to
    /// look up in it. Empty where they name nothing this knows.
    /// </summary>
    public IReadOnlyList<WorkCandidate> Match(IReadOnlyList<string> names, IReadOnlyList<string> passage)
    {
        // The longest run that names anything: "Sall. Cat." is Sallust's
        // Catiline, though "Cat." alone is Catullus, and "Cic. Off." is not
        // the Hippocratic "Off.". A word prose puts in front - "See" - matches
        // nothing, and the run starts after it.
        for (var start = 0; start < names.Count; start++)
        {
            var run = names.Skip(start).ToList();
            var found = MatchRun(run, passage);
            if (found.Count > 0) return found.Select(c => c with { NamesUsed = run.Count }).ToList();

            // An author whose work is not recognised is not dropped to read
            // what follows as another author's: "Varr. R. R." is not Plato's
            // "R.", nor "Petron. Sat." Lucian's Saturnalia. An author is a
            // name in the library, or a word the dictionaries put first in an
            // abbreviation, written with its point.
            var word = names[start];
            if (Authors(new[] { word }, hasWork: false).Any()
                || (word.EndsWith('.') && _firstWords.Contains(CitationAbbreviations.Normalize(word)))) break;
        }

        return Array.Empty<WorkCandidate>();
    }

    /// <summary>
    /// The works by the author of <paramref name="previous"/> that the names
    /// mean - for "id. Fam. 15.3", where "id." is the author of the reference
    /// before it.
    /// </summary>
    public IReadOnlyList<WorkCandidate> MatchByAuthorOf(string previous, IReadOnlyList<string> names, IReadOnlyList<string> passage)
    {
        var author = Author(previous.ToLowerInvariant());
        var found = new Dictionary<string, WorkCandidate>(StringComparer.Ordinal);
        AddWorks(found, author, 3, names, passage);
        return Ranked(found);
    }

    /// <summary>
    /// Where a reference with no names of its own goes, after one in
    /// <paramref name="previous"/>: the same work - except for an orator cited
    /// by speech, where "D. 18.169; 19.1" is speech 19.
    /// </summary>
    public IReadOnlyList<WorkCandidate> Continue(string previous, IReadOnlyList<string> passage)
    {
        var work = previous.ToLowerInvariant();
        if (_speechesByAuthor.TryGetValue(Author(work), out var speeches) && speeches.ContainsValue(work) && Speech(speeches, passage) is { } speech)
            return new[] { new WorkCandidate(speech.Work, speech.Passage, Exact, Citations(speech.Work)) };
        return new[] { new WorkCandidate(work, passage, Exact, Citations(work)) };
    }

    private List<WorkCandidate> MatchRun(IReadOnlyList<string> run, IReadOnlyList<string> passage)
    {
        var found = new Dictionary<string, WorkCandidate>(StringComparer.Ordinal);
        var whole = CitationAbbreviations.NormalizeWords(string.Join(' ', run));
        var compact = CitationAbbreviations.Normalize(whole);
        if (compact.Length == 0) return new();

        // As the dictionaries print it, word for word; or with the words run
        // together ("Caes. BG" for "Caes. B. G."), which counts for less, as
        // it can join words that were apart ("Ar. Ran." and "Arr. An.").
        // A single word matched loosely must at least be written as an
        // abbreviation, with its point: "In 1999" is prose, though LSJ's "In."
        // is a book of the Septuagint.
        var abbreviated = run.Count > 1 || run[0].EndsWith('.');
        if (_whole.TryGetValue(whole, out var works))
        {
            foreach (var (work, citations) in works)
            {
                var score = ExactScore(run, work, compact);
                if (score == Exact || abbreviated) Add(found, work, passage, score, citations);
            }
        }
        else if (abbreviated && _wholeCompact.TryGetValue(compact, out works))
        {
            foreach (var (work, citations) in works)
                Add(found, work, passage, Loose, citations);
        }

        if (_speechesByAbbreviation.TryGetValue(compact, out var speeches) && Speech(speeches, passage) is { } speech)
            Add(found, speech.Work, speech.Passage, Exact, Citations(speech.Work));

        // As an author, then a work.
        for (var split = 1; split <= run.Count; split++)
        {
            var authorNames = run.Take(split).ToList();
            var workNames = run.Skip(split).ToList();
            foreach (var (author, score) in Authors(authorNames, workNames.Count > 0))
                AddWorks(found, author, score, workNames, passage);
        }

        return Ranked(found);
    }

    /// <summary>
    /// How much a match as printed counts: fully, unless it is a single word
    /// that is a work's abbreviation and not its author's ("Off.", "Resp.",
    /// "Nu."), which the dictionaries print alone only after the author.
    /// "Il." and "Od." count for less too, and are still found, as nothing
    /// else matches them.
    /// </summary>
    private int ExactScore(IReadOnlyList<string> run, string work, string compact)
    {
        if (run.Count > 1) return Exact;
        return _authorAbbreviations.TryGetValue(Author(work), out var authors) && authors.Contains(compact) ? Exact : Loose;
    }

    /// <summary>The works of one author the names mean, the author matched with this score.</summary>
    private void AddWorks(Dictionary<string, WorkCandidate> found, string author, int authorScore, IReadOnlyList<string> workNames, IReadOnlyList<string> passage)
    {
        if (workNames.Count == 0)
        {
            if (_speechesByAuthor.TryGetValue(author, out var speeches) && Speech(speeches, passage) is { } speech)
                Add(found, speech.Work, speech.Passage, authorScore + 3, Citations(speech.Work));
            else if (_onlyWorkCited.TryGetValue(author, out var cited))
                Add(found, cited, passage, authorScore + 2, Citations(cited));
            else if (_libraryByAuthor.TryGetValue(author, out var held) && held.Count == 1
                     && _worksByAuthor.TryGetValue(author, out var known) && known.Count == 1)
                Add(found, held[0].Key, passage, authorScore + 2, Citations(held[0].Key)); // the only work the author is known by
            return;
        }

        if (!_worksByAuthor.TryGetValue(author, out var works)) return;
        var normalized = CitationAbbreviations.Normalize(string.Join(' ', workNames));
        if (normalized.Length == 0) return;

        foreach (var work in works)
        {
            var score = WorkScore(work, normalized, workNames);
            if (score > 0) Add(found, work, passage, authorScore + score, Citations(work));
        }
    }

    private int WorkScore(string work, string normalized, IReadOnlyList<string> workNames)
    {
        var abbreviations = _workAbbreviations.GetValueOrDefault(work);
        if (abbreviations != null)
        {
            if (abbreviations.Contains(normalized)) return 3;

            // "Resp." for "R.", "Nub." for "Nu.", "Aen." for "A."; and the
            // other way, a shorter form of the dictionaries' own.
            if (abbreviations.Any(a => normalized.StartsWith(a, StringComparison.Ordinal)
                                       || (normalized.Length >= 2 && a.StartsWith(normalized, StringComparison.Ordinal)))) return 2;

            // "Leg." for LSJ's "Lg.", "Symp." for "Smp.": the dictionaries'
            // letters, in order, within a longer form of one word, the first
            // the same. Not of several words, where letters spread over them
            // match too much ("Ars am." holds the "Am." of the Amores).
            if (workNames.Count == 1
                && abbreviations.Any(a => a.Length >= 2 && a[0] == normalized[0] && normalized.Length <= a.Length + 3 && IsWithin(a, normalized))) return 2;

            // "Bell. Gall." for "B. G.".
            if (workNames.Count >= 2)
            {
                var initials = string.Concat(workNames.Select(n => CitationAbbreviations.Normalize(n)).Where(n => n.Length > 0).Select(n => n[0]));
                if (abbreviations.Contains(initials)) return 2;
            }
        }

        return _titleWords.TryGetValue(work, out var title) && TitleMatches(workNames, title) ? 2 : 0;
    }

    /// <summary>Whether the letters of <paramref name="shorter"/> appear in <paramref name="longer"/> in the same order.</summary>
    private static bool IsWithin(string shorter, string longer)
    {
        var at = 0;
        foreach (var c in longer)
            if (at < shorter.Length && shorter[at] == c) at++;
        return at == shorter.Length && longer.Length > shorter.Length;
    }

    /// <summary>
    /// Whether the names are the title, word by word: each the start of a
    /// different word of it, in any order ("Eth. Nic.", "Nicomachean Ethics");
    /// or, written in capitals, its initials ("OT", "HN").
    /// </summary>
    private static bool TitleMatches(IReadOnlyList<string> workNames, string[] title)
    {
        if (title.Length == 0) return false;

        List<string> parts;
        var capitals = workNames.Count == 1 && workNames[0].Trim('.').Length is >= 2 and <= 4 && workNames[0].Trim('.').All(char.IsUpper);
        if (capitals)
            parts = workNames[0].Trim('.').Select(c => char.ToLowerInvariant(c).ToString()).ToList();
        else
            parts = workNames.SelectMany(n => Words(n, TitleStopWords)).ToList();

        if (parts.Count == 0 || parts.Count > title.Length) return false;

        // A letter alone stands for a word only beside a longer one: "Ars P."
        // is Ars Poetica, but "P." by itself is too little to go on.
        if (!capitals && parts.All(p => p.Length < 2)) return false;

        var used = new bool[title.Length];
        foreach (var part in parts)
        {
            var at = -1;
            for (var i = 0; i < title.Length; i++)
                if (!used[i] && title[i].StartsWith(part, StringComparison.Ordinal))
                {
                    at = i;
                    break;
                }

            if (at < 0) return false;
            used[at] = true;
        }

        return true;
    }

    /// <summary>
    /// The authors the names may be, with how closely: the dictionaries'
    /// abbreviation for an author (3), or the start of words of the author's
    /// name, each name word three letters or more (2).
    /// </summary>
    private IEnumerable<(string Author, int Score)> Authors(IReadOnlyList<string> authorNames, bool hasWork)
    {
        var whole = CitationAbbreviations.Normalize(string.Join(' ', authorNames));
        if (whole.Length == 0) yield break;

        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (author, abbreviations) in _authorAbbreviations)
            if (abbreviations.Contains(whole) && named.Add(author)) yield return (author, 3);

        // Without a work named, the dictionaries' abbreviation for an author
        // settles who it is: "Hor." alone is Horace, not Lupold Hornburg, whose
        // name it also begins. With one, the work decides: "Plin. Ep." is the
        // younger Pliny's letters, though LSJ's "Plin." is the elder.
        if (named.Count > 0 && !hasWork) yield break;

        var parts = authorNames.SelectMany(n => Words(n, NameStopWords)).ToList();
        if (parts.Count == 0 || parts.Any(p => p.Length < 3)) yield break;

        foreach (var (author, words) in _authorWords)
        {
            if (named.Contains(author) || words.Length < parts.Count) continue;
            var used = new bool[words.Length];
            var all = true;
            foreach (var part in parts)
            {
                var at = Array.FindIndex(words, w => w.StartsWith(part, StringComparison.Ordinal));
                while (at >= 0 && used[at])
                {
                    var next = Array.FindIndex(words, at + 1, w => w.StartsWith(part, StringComparison.Ordinal));
                    at = next;
                }

                if (at < 0)
                {
                    all = false;
                    break;
                }

                used[at] = true;
            }

            if (all) yield return (author, 2);
        }
    }

    private static (string Work, IReadOnlyList<string> Passage)? Speech(Dictionary<int, string> speeches, IReadOnlyList<string> passage)
    {
        if (passage.Count == 0 || !int.TryParse(passage[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return null;
        return speeches.TryGetValue(number, out var work) ? (work, passage.Skip(1).ToList()) : null;
    }

    private static void Add(Dictionary<string, WorkCandidate> found, string work, IReadOnlyList<string> passage, int score, int citations)
    {
        if (!found.TryGetValue(work, out var known) || known.Score < score)
            found[work] = new WorkCandidate(work, passage, score, citations);
    }

    private static List<WorkCandidate> Ranked(Dictionary<string, WorkCandidate> found) =>
        found.Values
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Citations)
            .ThenBy(c => c.Work, StringComparer.Ordinal)
            .ToList();

    private int Citations(string work) => _citations.GetValueOrDefault(work);

    /// <summary>The author part of a work's identifier: "tlg0059" of "tlg0059.tlg030".</summary>
    private static string Author(string work)
    {
        var dot = work.IndexOf('.');
        return dot < 0 ? work : work[..dot];
    }

    private void Remember(string author, string work)
    {
        if (!_worksByAuthor.TryGetValue(author, out var works)) _worksByAuthor[author] = works = new(StringComparer.Ordinal);
        works.Add(work);
    }

    /// <summary>
    /// What the dictionaries' abbreviations say about authors and works. An
    /// abbreviation of several words is an author and a work ("Pl. R.") when
    /// its first word goes with more than one of that author's works, or is
    /// also used alone for the author; otherwise it is a work's name whole
    /// ("Val. Fl."). One of a single word is a work's name ("Il."), and an
    /// author's too when nearly all the author's citations are of one work
    /// ("Hdt.", "Th.").
    /// </summary>
    private void Learn(CitationAbbreviations abbreviations)
    {
        var firstWords = new Dictionary<(string Author, string First), HashSet<string>>();
        var alone = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var byAuthor = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var use in abbreviations.Uses)
        {
            var work = Canonical(use.Work);
            var author = Author(work);
            var whole = CitationAbbreviations.Normalize(use.Abbreviation);
            if (whole.Length == 0) continue;

            var spaced = CitationAbbreviations.NormalizeWords(use.Abbreviation);
            if (!_whole.TryGetValue(spaced, out var works)) _whole[spaced] = works = new();
            works.Add((work, use.Citations));
            if (!_wholeCompact.TryGetValue(whole, out var compact)) _wholeCompact[whole] = compact = new();
            compact.Add((work, use.Citations));
            _citations[work] = _citations.GetValueOrDefault(work) + use.Citations;
            byAuthor[author] = byAuthor.GetValueOrDefault(author) + use.Citations;
            Remember(author, work);

            var words = Split(use.Abbreviation);
            if (words.Count >= 2)
            {
                _firstWords.Add(CitationAbbreviations.Normalize(words[0]));
                var key = (author, CitationAbbreviations.Normalize(words[0]));
                if (!firstWords.TryGetValue(key, out var set)) firstWords[key] = set = new(StringComparer.Ordinal);
                set.Add(work);
            }
            else
            {
                if (!alone.TryGetValue(whole, out var counts)) alone[whole] = counts = new(StringComparer.Ordinal);
                counts[work] = counts.GetValueOrDefault(work) + use.Citations;
            }
        }

        foreach (var use in abbreviations.Uses)
        {
            var work = Canonical(use.Work);
            var author = Author(work);
            var words = Split(use.Abbreviation);
            var whole = CitationAbbreviations.Normalize(use.Abbreviation);
            if (whole.Length == 0) continue;

            if (words.Count >= 2)
            {
                var first = CitationAbbreviations.Normalize(words[0]);
                var isAuthor = firstWords[(author, first)].Count >= 2
                               || (alone.TryGetValue(first, out var counts) && counts.Keys.Any(w => Author(w) == author));
                if (isAuthor)
                {
                    AuthorAbbreviation(author, first);
                    WorkAbbreviation(work, CitationAbbreviations.Normalize(string.Join(' ', words.Skip(1))));
                }

                WorkAbbreviation(work, whole);
            }
            else
            {
                WorkAbbreviation(work, whole);
            }
        }

        // An abbreviation used alone for an author nearly all of whose
        // citations are of one work: "Hdt.", "Th.", "Liv.".
        foreach (var (abbreviation, counts) in alone)
        {
            var total = counts.Values.Sum();
            var (work, top) = counts.OrderByDescending(c => c.Value).First();
            var author = Author(work);
            if (top * 10 < total * 9 || top * 10 < byAuthor[author] * 9) continue;
            AuthorAbbreviation(author, abbreviation);
            _onlyWorkCited.TryAdd(author, work);
        }

        foreach (var speech in abbreviations.Speeches)
        {
            var whole = CitationAbbreviations.Normalize(speech.Abbreviation);
            var work = Canonical(speech.Work);
            var author = Author(work);
            if (!_speechesByAbbreviation.TryGetValue(whole, out var numbers)) _speechesByAbbreviation[whole] = numbers = new();
            numbers[speech.Number] = work;
            Remember(author, work);

            // Only an abbreviation of one word stands for the author alone:
            // "Isoc." is Isocrates' speeches, "Isoc. Ep." his letters.
            if (Split(speech.Abbreviation).Count == 1)
            {
                AuthorAbbreviation(author, whole);
                if (!_speechesByAuthor.TryGetValue(author, out var byNumber)) _speechesByAuthor[author] = byNumber = new();
                byNumber[speech.Number] = work;
            }
        }
    }

    private void AuthorAbbreviation(string author, string abbreviation)
    {
        if (!_authorAbbreviations.TryGetValue(author, out var set)) _authorAbbreviations[author] = set = new(StringComparer.Ordinal);
        set.Add(abbreviation);
    }

    private void WorkAbbreviation(string work, string abbreviation)
    {
        if (abbreviation.Length == 0) return;
        if (!_workAbbreviations.TryGetValue(work, out var set)) _workAbbreviations[work] = set = new(StringComparer.Ordinal);
        set.Add(abbreviation);
    }

    /// <summary>An abbreviation's words: "Caes. B. G." is three, "D.S." one; a comma is not a word.</summary>
    private static List<string> Split(string abbreviation) =>
        abbreviation.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>The words of a name or title, compared in lower case without accents, small words left out.</summary>
    private static string[] Words(string text, HashSet<string> stopWords)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in text.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetter(c))
            {
                current.Append(CitationAbbreviations.Fold(char.ToLowerInvariant(c)));
                continue;
            }

            Flush();
        }

        Flush();
        return words.ToArray();

        void Flush()
        {
            if (current.Length > 0 && !stopWords.Contains(current.ToString())) words.Add(current.ToString());
            current.Clear();
        }
    }
}
