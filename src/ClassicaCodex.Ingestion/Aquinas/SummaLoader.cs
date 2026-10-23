using System.Globalization;
using System.Text.RegularExpressions;

namespace ClassicaCodex.Ingestion.Aquinas;

/// <summary>
/// The First Part of the Summa Theologiae, out of Project Gutenberg's text of
/// the English Dominican translation (#17611), into passages cited as the
/// Summa is cited: question, article, and the part of the article.
///
/// Every article has the same shape, which is the reason the Summa is cited
/// the way it is: the objections, "On the contrary", "I answer that", and a
/// reply to each objection. So I, q. 50, a. 1, ad 1 is 50.1.ad1 here, the
/// objections are ob1, ob2..., the sed contra sc and the body co. A part that
/// runs to a second paragraph continues as 50.1.co.2.
///
/// <b>The file is regular, but not perfectly.</b> The question heading is
/// missing before question 116, so a question is numbered from the one before
/// it and a heading that is present has to agree. The article tags are
/// written five ways - [I, Q. 50, Art. 1], [I. Q. 6, Art. 3], [I, Q. 56, Art 1],
/// [I, Q. 58, A. 6], [Q. 98, Art. 1] - and eight of them give the wrong
/// number, so an article is placed by the ordinal before its tag (see Parse),
/// which must name the article that comes next. And a reply can answer two
/// objections at once ("Reply Obj. 2 and 3") or none in so many words
/// ("Reply Obj. 1 is clear from the foregoing"). Questions 71 and 72 have one
/// article each, with no tag and no title of its own.
/// </summary>
public static class SummaLoader
{
    private static readonly Regex Separator = new(@"^_{10,}$", RegexOptions.Compiled);
    private static readonly Regex QuestionLabel = new(@"^QUESTION (\d+)$", RegexOptions.Compiled);
    private static readonly Regex ArticleTag = new(
        @"^([A-Z]+(?:-[A-Z]+)?) ARTICLE \[(?:I[,.] )?Q\. (\d+), (?:Art\.?|A\.) (\d+)\]$", RegexOptions.Compiled);
    private static readonly Regex ArticleCount = new(@"\s*\((?:In )?[A-Za-z-]+ Articles?\)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Treatise = new(@"^(TREATISE ON .*?)(?: \(QQ\. (\d+)-(\d+)\))?$", RegexOptions.Compiled);
    private static readonly Regex Objection = new(@"^(?:Objection|Obj\.) (\d+):", RegexOptions.Compiled);
    private static readonly Regex Reply = new(@"^Reply Obj\. (\d+)", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static List<(string Citation, string Text, bool IsHead)> Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        // Gutenberg's closing line comes before its end marker, and would
        // otherwise be read as one more paragraph of the last reply.
        var end = Array.FindIndex(lines, l =>
            l.StartsWith("End of the Project Gutenberg", StringComparison.Ordinal)
            || l.StartsWith("*** END OF", StringComparison.Ordinal));
        if (end < 0) end = lines.Length;

        var firstQuestion = Array.FindIndex(lines, l => l.Trim() == "QUESTION 1");
        if (firstQuestion < 0) throw new InvalidDataException("Question 1 was not found");
        var start = Array.FindLastIndex(lines, firstQuestion, l => l.Trim() == "PROLOGUE");
        if (start < 0) start = firstQuestion;

        var passages = new List<(string, string, bool)>();
        var question = 0;
        var article = 0;
        string? treatise = null;

        foreach (var section in Sections(lines, start, end))
        {
            var first = section[0];

            if (first == "PROLOGUE")
            {
                passages.Add(("pr.head", "Prologue", true));
                for (var i = 1; i < section.Count; i++) passages.Add(($"pr.{i}", section[i], false));
                continue;
            }

            if (first.StartsWith("SUMMA THEOLOGICA", StringComparison.Ordinal)) continue;

            var treatiseMatch = Treatise.Match(first);
            if (treatiseMatch.Success)
            {
                treatise = TitleCase(treatiseMatch.Groups[1].Value)
                    + (treatiseMatch.Groups[2].Success
                        ? $" (Questions {treatiseMatch.Groups[2].Value}-{treatiseMatch.Groups[3].Value})"
                        : "");
                continue;
            }

            var tag = ArticleTag.Match(first);
            if (tag.Success)
            {
                // The ordinal and the brackets say the same thing twice, and
                // eight times they disagree. The ordinal is right every time:
                // "NINTH ARTICLE [I, Q. 19, Art. 8]" is the ninth, and "FOURTH
                // ARTICLE [I, Q. 4, Art. 4]" is in question 42. So the ordinal
                // decides, and the brackets only where it is not one of these.
                var a = article + 1;
                var numbered = int.Parse(tag.Groups[2].Value) == question && int.Parse(tag.Groups[3].Value) == a;
                if (Ordinal(tag.Groups[1].Value) != a && !numbered)
                {
                    throw new InvalidDataException($"Article tag \"{first}\" found after question {question}, article {article}");
                }

                article = a;
                if (section.Count < 2) throw new InvalidDataException($"Article {question}.{a} has no title");
                passages.Add(($"{question}.{a}.head", section[1], true));
                passages.AddRange(Parts(question, a, section.Skip(2)));
                continue;
            }

            // Anything else opens a question: its number (missing once), its
            // title in capitals with the count of articles, and the plan of
            // the question.
            var index = 0;
            var number = question + 1;
            var label = QuestionLabel.Match(first);
            if (label.Success)
            {
                if (int.Parse(label.Groups[1].Value) != number)
                {
                    throw new InvalidDataException($"{first} found after question {question}");
                }

                index = 1;
            }

            if (index >= section.Count || !IsCapitals(ArticleCount.Replace(section[index], "")))
            {
                throw new InvalidDataException($"Expected the title of question {number}, found \"{section[Math.Min(index, section.Count - 1)]}\"");
            }

            question = number;
            article = 0;

            if (treatise != null) passages.Add(($"{number}.treatise", treatise, true));
            treatise = null;

            passages.Add(($"{number}.head", $"Question {number}. {TitleCase(ArticleCount.Replace(section[index], ""))}", true));

            // A question of one article has no article tag: the objections
            // follow its introduction directly, and are its article 1.
            var body = section.FindIndex(index + 1, p => Objection.IsMatch(p));
            var plan = body < 0 ? section.Count : body;
            for (var i = index + 1; i < plan; i++) passages.Add(($"{number}.pr.{i - index}", section[i], false));

            if (body >= 0)
            {
                article = 1;
                passages.AddRange(Parts(number, 1, section.Skip(body)));
            }
        }

        return passages;
    }

    /// <summary>
    /// The paragraphs of an article after its title, each under the part it
    /// belongs to. A paragraph that opens no part continues the one before.
    /// </summary>
    private static IEnumerable<(string Citation, string Text, bool IsHead)> Parts(int q, int a, IEnumerable<string> paragraphs)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        string? part = null;

        foreach (var paragraph in paragraphs)
        {
            var objection = Objection.Match(paragraph);
            var reply = Reply.Match(paragraph);

            var opens = objection.Success ? $"ob{objection.Groups[1].Value}"
                : reply.Success ? $"ad{reply.Groups[1].Value}"
                : paragraph.StartsWith("On the contrary", StringComparison.Ordinal) ? "sc"
                : paragraph.StartsWith("I answer", StringComparison.Ordinal) ? "co"
                : null;

            // Before the first objection there is only ever the translators'
            // note on the title - once, at 48.5, on what "pain" means.
            part = opens ?? part ?? "note";

            // A part met again - a second reply to the same objection, or a
            // paragraph after it - continues it, so a citation is never used twice.
            var count = seen.GetValueOrDefault(part) + 1;
            seen[part] = count;

            yield return (count == 1 ? $"{q}.{a}.{part}" : $"{q}.{a}.{part}.{count}", paragraph, false);
        }
    }

    /// <summary>
    /// The body cut at the separator lines, each section a list of
    /// paragraphs with their lines rejoined and the underscores that mark
    /// italics taken out.
    /// </summary>
    private static IEnumerable<List<string>> Sections(string[] lines, int start, int end)
    {
        var section = new List<string>();
        var paragraph = new List<string>();

        for (var i = start; i <= end; i++)
        {
            var line = i < end ? lines[i].Trim() : "";

            if (line.Length > 0 && !Separator.IsMatch(line))
            {
                paragraph.Add(line);
                continue;
            }

            if (paragraph.Count > 0)
            {
                section.Add(Whitespace.Replace(Join(paragraph), " ").Replace("_", ""));
                paragraph.Clear();
            }

            if ((Separator.IsMatch(line) || i == end) && section.Count > 0)
            {
                yield return section;
                section = new List<string>();
            }
        }
    }

    private static readonly string[] Ordinals =
    {
        "FIRST", "SECOND", "THIRD", "FOURTH", "FIFTH", "SIXTH", "SEVENTH", "EIGHTH", "NINTH", "TENTH",
        "ELEVENTH", "TWELFTH", "THIRTEENTH", "FOURTEENTH", "FIFTEENTH", "SIXTEENTH"
    };

    private static int Ordinal(string word) => Array.IndexOf(Ordinals, word) + 1;

    /// <summary>
    /// A paragraph's lines as one. A line that ends in a hyphen breaks a
    /// compound or a dash, never a word - "Simultaneously-Whole", "man--that
    /// is" - so it closes up with the next.
    /// </summary>
    private static string Join(List<string> lines)
    {
        var joined = new System.Text.StringBuilder(lines[0]);
        for (var i = 1; i < lines.Count; i++)
        {
            if (!lines[i - 1].EndsWith('-')) joined.Append(' ');
            joined.Append(lines[i]);
        }

        return joined.ToString();
    }

    private static bool IsCapitals(string s) => s.Any(char.IsLetter) && !s.Any(char.IsLower);

    private static readonly HashSet<string> SmallWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "by", "for", "from", "in", "into", "nor", "of", "on", "or", "the", "to", "with"
    };

    /// <summary>A title in capitals, as it would be printed in a running head.</summary>
    internal static string TitleCase(string capitals)
    {
        var words = capitals.ToLower(CultureInfo.InvariantCulture).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            if (i > 0 && SmallWords.Contains(word)) continue;

            var letter = word.IndexOf(word.FirstOrDefault(char.IsLetter));
            if (letter >= 0) words[i] = word[..letter] + char.ToUpperInvariant(word[letter]) + word[(letter + 1)..];
        }

        return string.Join(" ", words);
    }
}
