using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Goetia;

/// <summary>One of the works the 1904 Goetia is divided into here.</summary>
public sealed record GoetiaWork(
    string Slug, string Title, bool ByCrowley, IReadOnlyList<(string Citation, string Text, bool IsHead)> Passages);

/// <summary>
/// The Goetia of Mathers and Crowley (1904) out of Project Gutenberg's HTML of
/// L. W. de Laurence's reprint of 1916 (ebook #72679), which the Distributed
/// Proofreaders checked against page images.
///
/// The book is cut into the five things it is: the seventy-two spirits, cited
/// by number as everyone cites them (Goetia 1 is Bael); the rest of
/// Mathers's Lemegeton - the definition of magic, the description of the five
/// books, the observations, the classified list, the circle and the triangle,
/// the conjurations, curses and licence to depart - cited by section and
/// paragraph in the book's order; and Crowley's three additions: the Greek
/// invocation, preface and Preliminary Invocation that open the book, his
/// essay "The Initiated Interpretation of Ceremonial Magic", and his
/// mock-archaic English of the conjurations at its end.
///
/// The conjurations have no headings of their own in the HTML: they follow
/// the line "Here endeth this First Book", under a centred title, and each
/// begins with a line in capitals ("YE FYRSTE CONJOURATIOUN."), which becomes
/// its heading.
///
/// Gutenberg links every footnote to its marker, so a note follows the
/// passage or heading its marker is in, as "Note: ...", and the marker comes
/// out of the text. Small capitals stay as Gutenberg types them ("Bael",
/// "Lucifer"); the printed book sets them in capitals. Left out: de
/// Laurence's title page and copyright notice, the transcriber's notes, and
/// the seals and figures, which are pictures.
/// </summary>
public static class GoetiaLoader
{
    private const string EndOfBook = "(Here endeth this First Book";
    private const string Closing = "These be the 72";

    // "(1.) Bael." - once, at Paimon, without the stop.
    private static readonly Regex SpiritLabel = new(@"^\((\d{1,2})\.?\)\s", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly string[] RunningTitles = { "Goetia", "The Lesser Key Of Solomon" };

    private enum Kind { Heading, Title, Paragraph }

    private sealed record Block(Kind Kind, string Text, List<string> Notes);

    private sealed class Section
    {
        public string? Title;
        public List<string> Notes = new();
        public bool Conjuration;
        public readonly List<(string Text, List<string> Notes)> Paragraphs = new();
    }

    public static IReadOnlyList<GoetiaWork> Parse(string html)
    {
        var body = GutenbergHtml.Body(html);
        var footnotes = Footnotes(body);
        var blocks = new List<Block>();
        Walk(body, blocks);

        var sections = Sections(blocks);
        var spirits = sections.SingleOrDefault(s => s.Title?.StartsWith("SHEMHAMPHORASH", StringComparison.Ordinal) == true)
            ?? throw new InvalidDataException("The list of the seventy-two spirits was not found");
        var essay = sections.Where(s => s.Title?.Contains("INITIATED INTERPRETATION", StringComparison.Ordinal) == true).ToList();
        var front = sections.TakeWhile(s => s.Title == null
            || s.Title.StartsWith("Preface", StringComparison.Ordinal)
            || s.Title.StartsWith("PRELIMINARY INVOCATION", StringComparison.Ordinal)).ToList();
        var conjurations = sections.Where(s => s.Conjuration).ToList();
        var art = sections.Except(front).Except(essay).Except(conjurations).Where(s => s != spirits).ToList();

        var used = new HashSet<string>();
        var works = new List<GoetiaWork>
        {
            new("spirits", "The Goetia: The Seventy-Two Spirits", false, SpiritPassages(spirits, footnotes, used)),
            new("art", "The Goetia: The Art and Its Conjurations", false, Sectioned(art, footnotes, used)),
            new("preface", "The Preface and the Preliminary Invocation", true, Sectioned(front, footnotes, used)),
            new("initiated-interpretation", "The Initiated Interpretation of Ceremonial Magic", true,
                Numbered(essay.SelectMany(s => s.Paragraphs), footnotes, used)),
            new("conjurations", "The Conjurations of the Goetia, in Crowley's English", true, Sectioned(conjurations, footnotes, used))
        };

        foreach (var work in works.Where(w => w.Passages.Count == 0))
        {
            throw new InvalidDataException($"\"{work.Title}\" has no text");
        }

        var lost = footnotes.Keys.Except(used).ToList();
        if (lost.Count > 0) throw new InvalidDataException($"Footnotes with no marker in the text: {string.Join(", ", lost)}");

        return works;
    }

    // ---- the works ------------------------------------------------------------

    private static List<(string, string, bool)> SpiritPassages(Section section, Dictionary<string, string> footnotes, HashSet<string> used)
    {
        var passages = new List<(string, string, bool)>();
        var number = 0;
        (string Text, List<string> Notes)? entry = null;
        var closing = new List<(string Text, List<string> Notes)>();

        void Flush()
        {
            if (entry is not { } e) return;
            passages.Add(($"{number}", e.Text, false));
            AddNotes(passages, $"{number}", e.Notes, footnotes, used);
            entry = null;
        }

        foreach (var paragraph in section.Paragraphs)
        {
            var label = SpiritLabel.Match(paragraph.Text);
            if (label.Success && int.Parse(label.Groups[1].Value) == number + 1)
            {
                Flush();
                number++;
                entry = (paragraph.Text, new List<string>(paragraph.Notes));
            }
            else if (number == 72 && paragraph.Text.StartsWith(Closing, StringComparison.OrdinalIgnoreCase) || closing.Count > 0)
            {
                // "These be the 72 Mighty Kings..." - the close of the list.
                closing.Add(paragraph);
            }
            else if (entry is { } e)
            {
                entry = ($"{e.Text} {paragraph.Text}", e.Notes.Concat(paragraph.Notes).ToList());
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
            AddNotes(passages, citation, closing[i].Notes, footnotes, used);
        }

        return passages;
    }

    /// <summary>
    /// Sections numbered in order, each with its heading, and paragraphs
    /// within them. The Greek invocation before the preface has no heading.
    /// </summary>
    private static List<(string, string, bool)> Sectioned(IEnumerable<Section> sections, Dictionary<string, string> footnotes, HashSet<string> used)
    {
        var passages = new List<(string, string, bool)>();
        var n = 0;

        foreach (var section in sections)
        {
            n++;
            if (section.Title != null)
            {
                passages.Add(($"{n}.head", section.Title, true));
                AddNotes(passages, $"{n}.head", section.Notes, footnotes, used);
            }

            for (var p = 0; p < section.Paragraphs.Count; p++)
            {
                var citation = $"{n}.{p + 1}";
                passages.Add((citation, section.Paragraphs[p].Text, false));
                AddNotes(passages, citation, section.Paragraphs[p].Notes, footnotes, used);
            }
        }

        return passages;
    }

    private static List<(string, string, bool)> Numbered(
        IEnumerable<(string Text, List<string> Notes)> paragraphs, Dictionary<string, string> footnotes, HashSet<string> used)
    {
        var passages = new List<(string, string, bool)>();
        var n = 0;

        foreach (var paragraph in paragraphs)
        {
            n++;
            passages.Add(($"{n}", paragraph.Text, false));
            AddNotes(passages, $"{n}", paragraph.Notes, footnotes, used);
        }

        return passages;
    }

    private static void AddNotes(
        List<(string, string, bool)> passages, string citation, List<string> notes, Dictionary<string, string> footnotes, HashSet<string> used)
    {
        for (var i = 0; i < notes.Count; i++)
        {
            if (!footnotes.TryGetValue(notes[i], out var text)) throw new InvalidDataException($"No footnote {notes[i]}");
            used.Add(notes[i]);
            passages.Add(($"{citation}.n{i + 1}", $"Note: {text}", false));
        }
    }

    // ---- reading the HTML -----------------------------------------------------

    /// <summary>
    /// The blocks in reading order, cut into sections: one with no title for
    /// the Greek invocation before the first heading, one for each heading,
    /// and, after the end of the first book, one for each of Crowley's
    /// conjurations.
    /// </summary>
    private static List<Section> Sections(List<Block> blocks)
    {
        var sections = new List<Section>();
        var front = new Section();
        Section? current = null;
        var ended = false;

        foreach (var block in blocks)
        {
            if (block.Kind == Kind.Heading)
            {
                current = new Section { Title = block.Text, Notes = block.Notes };
                sections.Add(current);
                continue;
            }

            // The centred blocks before the first heading are de Laurence's
            // title page; after the first book ends, one is the title of
            // Crowley's conjurations.
            if (block.Kind == Kind.Title)
            {
                if (!ended) continue;
                current = new Section { Title = block.Text, Notes = block.Notes, Conjuration = true };
                sections.Add(current);
                continue;
            }

            if (current == null)
            {
                front.Paragraphs.Add((block.Text, block.Notes));
                continue;
            }

            if (current.Conjuration && IsCapitals(block.Text))
            {
                current = new Section { Title = block.Text, Notes = block.Notes, Conjuration = true };
                sections.Add(current);
                continue;
            }

            current.Paragraphs.Add((block.Text, block.Notes));
            if (block.Text.StartsWith(EndOfBook, StringComparison.OrdinalIgnoreCase)) ended = true;
        }

        if (front.Paragraphs.Count > 0) sections.Insert(0, front);
        return sections;
    }

    private static void Walk(XElement element, List<Block> blocks)
    {
        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            var css = ((string?)child.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (css.Contains("footnote") || css.Contains("tnote") || css.Contains("tnotes") || css.Contains("figcenter")
                || css.Contains("pg-boilerplate")) continue;
            if (name is "h1" or "ul" or "ol" or "table" or "style" or "script" or "img") continue;

            if (name is "h2" or "h3" or "h4")
            {
                var notes = new List<string>();
                var title = Heading(child, notes);
                if (title.Length > 0) blocks.Add(new Block(Kind.Heading, title, notes));
                continue;
            }

            if (css.Contains("linegroup"))
            {
                foreach (var group in child.Descendants().Where(d => HasClass(d, "group")))
                {
                    var notes = new List<string>();
                    var lines = group.Elements().Where(d => HasClass(d, "line")).Select(l => Text(l, notes)).Where(t => t.Length > 0).ToList();
                    if (lines.Count > 0) blocks.Add(new Block(Kind.Paragraph, string.Join(" / ", lines), notes));
                }

                continue;
            }

            if (css.Contains("nf-center"))
            {
                var notes = new List<string>();
                var title = Whitespace.Replace(string.Join(" ", child.Elements().Select(l => Text(l, notes))), " ").Trim();
                if (title.Length > 0 && title != "Footnotes") blocks.Add(new Block(Kind.Title, title, notes));
                continue;
            }

            if (name == "p")
            {
                var notes = new List<string>();
                var text = Text(child, notes);
                if (text.Length > 0 && !text.StartsWith("[Transcriber", StringComparison.Ordinal)) blocks.Add(new Block(Kind.Paragraph, text, notes));
                continue;
            }

            // Gutenberg's own header and footer lines.
            if (name == "div" && !child.Elements().Any() && child.Value.TrimStart().StartsWith("***", StringComparison.Ordinal)) continue;

            Walk(child, blocks);
        }
    }

    /// <summary>
    /// A heading without the running titles the book prints above some of
    /// them ("Goetia / The Lesser Key Of Solomon / THE INITIATED ...").
    /// </summary>
    private static string Heading(XElement heading, List<string> notes)
    {
        var lines = new List<string>();
        var line = new XElement("line");
        foreach (var node in heading.Nodes())
        {
            if (node is XElement { Name.LocalName: "br" })
            {
                lines.Add(Text(line, notes));
                line = new XElement("line");
            }
            else
            {
                line.Add(node is XElement e ? new XElement(e) : node is XText t ? new XText(t.Value) : null);
            }
        }

        lines.Add(Text(line, notes));
        return string.Join(" ", lines.Where(l => l.Length > 0 && !RunningTitles.Contains(l, StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>The notes, by the id their markers link to, without their numbers.</summary>
    private static Dictionary<string, string> Footnotes(XElement body)
    {
        var notes = new Dictionary<string, string>();
        foreach (var note in body.Descendants().Where(d => HasClass(d, "footnote")))
        {
            var id = (string?)note.Attribute("id") ?? throw new InvalidDataException("A footnote has no id");
            foreach (var label in note.Descendants().Where(d => HasClass(d, "label")).ToList()) label.Remove();
            notes[id] = Whitespace.Replace(string.Join(" ", note.Elements().Select(p => Text(p, new List<string>()))), " ").Trim();
        }

        return notes;
    }

    /// <summary>An element's text, with each footnote marker taken out and its note's id kept.</summary>
    private static string Text(XElement element, List<string> notes)
    {
        var sb = new StringBuilder();
        Append(element, sb, notes);
        return Whitespace.Replace(sb.ToString(), " ").Trim();
    }

    private static void Append(XElement element, StringBuilder sb, List<string> notes)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;
                case XElement { Name.LocalName: "br" }:
                    sb.Append(' ');
                    break;
                case XElement { Name.LocalName: "img" }:
                    break;
                case XElement { Name.LocalName: "a" } link when ((string?)link.Attribute("href") ?? "").StartsWith("#f", StringComparison.Ordinal):
                    notes.Add(((string)link.Attribute("href")!)[1..]);
                    break;
                case XElement child:
                    Append(child, sb, notes);
                    break;
            }
        }
    }

    private static bool HasClass(XElement element, string css) =>
        ((string?)element.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(css);

    /// <summary>A line with letters and no small ones: "YE FYRSTE CONJOURATIOUN."</summary>
    private static bool IsCapitals(string text) =>
        text.Count(char.IsLetter) >= 2 && !text.Any(char.IsLower);
}
