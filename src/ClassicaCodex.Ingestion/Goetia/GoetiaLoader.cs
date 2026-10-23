using System.Text.RegularExpressions;

namespace ClassicaCodex.Ingestion.Goetia;

/// <summary>One of the works the 1904 Goetia is divided into here.</summary>
public sealed record GoetiaWork(
    string Slug, string Title, bool ByCrowley, IReadOnlyList<(string Citation, string Text, bool IsHead)> Passages);

/// <summary>
/// The Goetia of Mathers and Crowley (1904) out of English Wikisource's
/// wikitext of "The Lesser Key of Solomon".
///
/// The book is cut into the four things it is: the seventy-two spirits, cited
/// by number as everyone cites them (Goetia 1 is Bael); the rest of
/// Mathers's Lemegeton - the definition of magic, the observations, the
/// classified list, the circle and the triangle, the conjurations, curses and
/// licence to depart - cited by section and paragraph; and Crowley's two
/// additions, his essay "The Initiated Interpretation of Ceremonial Magic"
/// and his mock-archaic English of the conjurations.
///
/// <b>The page is an unproofread copy of an earlier web transcription</b>, and
/// it shows: the footnotes stand in blocks headed "Footnotes" and numbered by
/// page ("41:3 Or 'with the Face of a Lion.'"), their markers are bare digits
/// in the running text ("in the Form of a Lion, 3 riding"), and paragraphs
/// broken at a page are sometimes rejoined with "[paragraph continues]" and
/// sometimes not. So:
///
/// - a block's notes are matched to the marker digits in the text before it
///   by longest common subsequence, in page order. The digit sequences say
///   which digits are markers - "6 Legions" is a number, and drops out - and
///   the match puts each note after the passage its marker is in, as
///   "Note: ...", with the marker taken out. A note whose marker the
///   transcription lost - four of the thirty-four - goes with the note
///   before it, or the paragraph before its block, and says that its place
///   is not known.
/// - a spirit's entry is one paragraph in the book, so every paragraph up to
///   the next spirit's number belongs to it.
/// - Crowley's own footnotes are wiki references, and become notes the same way.
///
/// The seals and figures are images in the book and are not here.
/// </summary>
public static class GoetiaLoader
{
    private const string Essay = "The Initiated Interpretation Of Ceremonial Magic";
    private const string Spirits = "Shemhamphorash";
    private const string Preamble = "Preamble";

    private static readonly Regex Heading = new(@"^==\s*(.*?)\s*==$", RegexOptions.Compiled);
    private static readonly Regex NoteLine = new(@"^(\d+):(\d+) (.*)$", RegexOptions.Compiled);
    private static readonly Regex Reference = new(@"<ref>(.*?)</ref>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex SpiritLabel = new(@"^\(?(\d{1,2})\.?\)\s", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// A digit standing alone after a word or a stop, before a word or a stop
    /// that does not start a number - a footnote marker, or a small number.
    /// </summary>
    private static readonly Regex Marker = new(
        @"(?<=[A-Za-z.,;:)'""\]!?]) ([1-9])(?=( [A-Za-z(""']| ?$|[.,;:](?!\d)))", RegexOptions.Compiled);

    private sealed class Paragraph
    {
        public string Text = "";
        public readonly List<(string Text, bool Placed)> Notes = new();
    }

    private sealed record Section(string Title, List<Paragraph> Paragraphs);

    public static IReadOnlyList<GoetiaWork> Parse(string wikitext)
    {
        var sections = Sections(wikitext);

        var essay = sections.Where(s => s.Title == Essay).ToList();
        var spirits = sections.SingleOrDefault(s => s.Title == Spirits)
            ?? throw new InvalidDataException("The list of the seventy-two spirits was not found");
        var preamble = sections.FindIndex(s => s.Title == Preamble);
        var lemegeton = sections.Take(preamble < 0 ? sections.Count : preamble)
            .Where(s => s.Title != Essay && s.Title != Spirits && s.Title != "References")
            .ToList();
        var conjurations = preamble < 0 ? new List<Section>() : sections.Skip(preamble).ToList();

        var works = new List<GoetiaWork>
        {
            new("spirits", "The Goetia: The Seventy-Two Spirits", false, SpiritPassages(spirits)),
            new("art", "The Goetia: The Art and Its Conjurations", false, Sectioned(lemegeton)),
            new("initiated-interpretation", "The Initiated Interpretation of Ceremonial Magic", true,
                Numbered(essay.SelectMany(s => s.Paragraphs))),
            new("conjurations", "The Conjurations of the Goetia, in Crowley's English", true, Sectioned(conjurations))
        };

        foreach (var work in works.Where(w => w.Passages.Count == 0))
        {
            throw new InvalidDataException($"\"{work.Title}\" has no text");
        }

        return works;
    }

    // ---- the four works -----------------------------------------------------

    private static List<(string, string, bool)> SpiritPassages(Section section)
    {
        var passages = new List<(string, string, bool)>();
        var number = 0;
        Paragraph? entry = null;
        var closing = new List<Paragraph>();

        void Flush()
        {
            if (entry == null) return;
            passages.Add(($"{number}", entry.Text, false));
            AddNotes(passages, $"{number}", entry.Notes);
            entry = null;
        }

        foreach (var paragraph in section.Paragraphs)
        {
            var label = SpiritLabel.Match(paragraph.Text);
            if (label.Success && int.Parse(label.Groups[1].Value) == number + 1)
            {
                Flush();
                number++;
                entry = new Paragraph { Text = paragraph.Text };
                entry.Notes.AddRange(paragraph.Notes);
            }
            else if (number == 72 && StartsInCapitals(paragraph.Text) || closing.Count > 0)
            {
                // "THESE be the 72 Mighty Kings..." - the close of the list.
                closing.Add(paragraph);
            }
            else if (entry != null)
            {
                entry.Text = $"{entry.Text} {paragraph.Text}";
                entry.Notes.AddRange(paragraph.Notes);
            }
            else
            {
                throw new InvalidDataException($"Text before the first spirit: \"{paragraph.Text[..Math.Min(40, paragraph.Text.Length)]}\"");
            }
        }

        Flush();
        if (number != 72) throw new InvalidDataException($"{number} spirits were found, not 72");

        for (var i = 0; i < closing.Count; i++)
        {
            var citation = i == 0 ? "end" : $"end.{i + 1}";
            passages.Add((citation, closing[i].Text, false));
            AddNotes(passages, citation, closing[i].Notes);
        }

        return passages;
    }

    /// <summary>Sections numbered in order, each with its heading, and paragraphs within them.</summary>
    private static List<(string, string, bool)> Sectioned(IEnumerable<Section> sections)
    {
        var passages = new List<(string, string, bool)>();
        var n = 0;

        foreach (var section in sections)
        {
            n++;
            passages.Add(($"{n}.head", section.Title, true));
            for (var p = 0; p < section.Paragraphs.Count; p++)
            {
                var citation = $"{n}.{p + 1}";
                passages.Add((citation, section.Paragraphs[p].Text, false));
                AddNotes(passages, citation, section.Paragraphs[p].Notes);
            }
        }

        return passages;
    }

    private static List<(string, string, bool)> Numbered(IEnumerable<Paragraph> paragraphs)
    {
        var passages = new List<(string, string, bool)>();
        var n = 0;

        foreach (var paragraph in paragraphs)
        {
            n++;
            passages.Add(($"{n}", paragraph.Text, false));
            AddNotes(passages, $"{n}", paragraph.Notes);
        }

        return passages;
    }

    private static void AddNotes(List<(string, string, bool)> passages, string citation, List<(string Text, bool Placed)> notes)
    {
        for (var i = 0; i < notes.Count; i++)
        {
            var (text, placed) = notes[i];
            passages.Add(($"{citation}.n{i + 1}", placed ? $"Note: {text}" : $"Note, its marker lost in this transcription: {text}", false));
        }
    }

    private static bool StartsInCapitals(string text)
    {
        var word = new string(text.TakeWhile(char.IsLetter).ToArray());
        return word.Length >= 2 && word.All(char.IsUpper);
    }

    // ---- reading the wikitext -----------------------------------------------

    /// <summary>
    /// The page's sections in order, their paragraphs rejoined and their
    /// footnotes and references attached to the paragraph they belong to.
    /// </summary>
    private static List<Section> Sections(string wikitext)
    {
        var sections = new List<Section>();
        Section? current = null;
        var lines = new List<string>();
        List<(int Page, int Number, string Text)>? notes = null;

        // The paragraphs since the last block of footnotes, which that block's
        // markers are in.
        var page = new List<Paragraph>();

        void Close()
        {
            if (lines.Count == 0) return;
            var text = Whitespace.Replace(string.Join(" ", lines), " ").Trim();
            lines.Clear();
            if (current == null || text.Length == 0) return;

            var paragraph = new Paragraph();
            foreach (Match reference in Reference.Matches(text)) paragraph.Notes.Add((Whitespace.Replace(reference.Groups[1].Value, " ").Trim(), true));
            text = Whitespace.Replace(Reference.Replace(text, ""), " ").Trim();

            const string Continues = "[paragraph continues]";
            if (text.StartsWith(Continues, StringComparison.Ordinal) && current.Paragraphs.Count > 0)
            {
                var previous = current.Paragraphs[^1];
                previous.Text = $"{previous.Text} {text[Continues.Length..].Trim()}";
                previous.Notes.AddRange(paragraph.Notes);

                // Its second half is on the next page, with that page's markers.
                if (!page.Contains(previous)) page.Add(previous);
                return;
            }

            paragraph.Text = text;
            current.Paragraphs.Add(paragraph);
            page.Add(paragraph);
        }

        void CloseNotes()
        {
            if (notes == null) return;
            Attach(page, notes);
            page.Clear();
            notes = null;
        }

        var started = false;
        foreach (var raw in wikitext.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            if (notes != null)
            {
                var note = NoteLine.Match(line.Trim());
                if (note.Success)
                {
                    notes.Add((int.Parse(note.Groups[1].Value), int.Parse(note.Groups[2].Value), note.Groups[3].Value.Trim()));
                    continue;
                }

                if (line.Length == 0) continue;
                CloseNotes();
            }

            var heading = Heading.Match(line);
            if (heading.Success)
            {
                Close();
                started = true;
                current = new Section(heading.Groups[1].Value, new List<Paragraph>());
                sections.Add(current);
                continue;
            }

            if (!started) continue;

            if (line.Trim() == "Footnotes")
            {
                Close();
                notes = new List<(int, int, string)>();
                continue;
            }

            // Links to subpages, the wiki's list of references, and the rules
            // between Crowley's conjurations are layout, not text.
            if (line.Length == 0 || line.StartsWith("*[[", StringComparison.Ordinal)
                || line.Trim() == "<References/>" || Regex.IsMatch(line, @"^-{5,}$"))
            {
                Close();
                continue;
            }

            lines.Add(line.Trim());
        }

        Close();
        CloseNotes();
        return sections;
    }

    /// <summary>
    /// Puts a block of footnotes with the paragraphs their markers are in,
    /// and takes the markers out of the text. See the class summary.
    /// </summary>
    private static void Attach(List<Paragraph> page, List<(int Page, int Number, string Text)> notes)
    {
        var candidates = new List<(int Paragraph, int Index, int Length, int Digit)>();
        for (var p = 0; p < page.Count; p++)
        {
            foreach (Match m in Marker.Matches(page[p].Text))
            {
                var next = page[p].Text[(m.Index + m.Length)..].TrimStart();
                if (next.StartsWith("Legions", StringComparison.OrdinalIgnoreCase)) continue;
                candidates.Add((p, m.Index, m.Length, int.Parse(m.Groups[1].Value)));
            }
        }

        // Longest common subsequence of the notes' numbers and the digits.
        var ordered = notes.OrderBy(n => n.Page).ThenBy(n => n.Number).ToList();
        var (a, b) = (ordered.Count, candidates.Count);
        var best = new int[a + 1, b + 1];
        for (var i = a - 1; i >= 0; i--)
        {
            for (var j = b - 1; j >= 0; j--)
            {
                best[i, j] = ordered[i].Number == candidates[j].Digit
                    ? best[i + 1, j + 1] + 1
                    : Math.Max(best[i + 1, j], best[i, j + 1]);
            }
        }

        var placed = new int?[a];
        for (int i = 0, j = 0; i < a && j < b;)
        {
            if (ordered[i].Number == candidates[j].Digit) placed[i++] = j++;
            else if (best[i + 1, j] > best[i, j + 1]) i++;
            else j++;
        }

        // Markers out, last first so the earlier positions still hold.
        foreach (var j in placed.Where(j => j.HasValue).Select(j => j!.Value).OrderByDescending(j => candidates[j].Index))
        {
            var (p, index, length, _) = candidates[j];
            page[p].Text = page[p].Text.Remove(index, length);
        }

        var lastParagraph = page.Count == 0 ? null : page[^1];
        Paragraph? previous = null;
        for (var i = 0; i < a; i++)
        {
            var home = placed[i] is { } j ? page[candidates[j].Paragraph] : previous ?? lastParagraph;
            home?.Notes.Add((ordered[i].Text, placed[i].HasValue));
            previous = home;
        }
    }
}
