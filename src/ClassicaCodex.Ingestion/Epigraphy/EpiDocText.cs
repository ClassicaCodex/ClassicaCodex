using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClassicaCodex.Ingestion.Epigraphy;

/// <summary>One line of an inscription or papyrus: where it is, and what it says.</summary>
/// <param name="Part">The text part it belongs to (a face, a fragment, a column), 1-based, in document order.</param>
/// <param name="PartLabel">That part's own label, if it has one ("a", "sin.", "recto").</param>
/// <param name="Line">The line number as the edition gives it ("1", "5a").</param>
/// <param name="PartType">What kind of part it is, where the edition says ("column", "fragment").</param>
public sealed record EpiDocLine(int Part, string? PartLabel, string Line, string Text, string? PartType = null);

/// <summary>
/// Turns an EpiDoc edition into lines of text in the Leiden conventions
/// epigraphers and papyrologists print: [restored], [- - -] for a loss,
/// letters with a dot under them where the reading is uncertain, a(bbreviation)
/// expanded in round brackets, ⟦erased⟧, {superfluous}, ⟨omitted⟩ and (!) for
/// a reading the editor confirms is what the stone or papyrus has.
///
/// EpiDoc is the TEI dialect that the epigraphic and papyrological databases
/// share, so this one renderer reads the Heidelberg database, I.Sicily, the
/// Inscriptiones Graecae and papyri.info alike.
///
/// <b>A line is a line of the object.</b> Each <c>lb</c> starts one, numbered
/// as the edition numbers it. Where a word runs on to the next line - the
/// edition says <c>break="no"</c> - the first line ends with a hyphen so the
/// reader can see the word continue.
/// </summary>
public static class EpiDocText
{
    private static readonly XNamespace Tei = "http://www.tei-c.org/ns/1.0";

    /// <summary>
    /// The lines of an edition (or translation) div, in order. Text parts - the
    /// faces of a tablet, the fragments of a stone, the columns of a papyrus -
    /// are numbered from 1 so that a caller can tell two "line 1"s apart.
    /// </summary>
    public static List<EpiDocLine> Lines(XElement div)
    {
        var state = new State { PartHasBreaks = div.Descendants(Tei + "lb").Any() };
        Walk(div, state);
        state.Flush();
        return state.Lines;
    }

    /// <summary>
    /// A div's text as one string, lines joined with " / ", for a translation
    /// or a short text that is not divided into lines.
    /// </summary>
    public static string Flat(XElement div) =>
        string.Join(" / ", Lines(div).Select(l => l.Text).Where(t => t.Length > 0));

    private sealed class State
    {
        public readonly List<EpiDocLine> Lines = new();
        public readonly StringBuilder Current = new();
        public int Part;
        public string? PartLabel;
        public string? PartType;
        public string? Line;
        public bool Started;

        /// <summary>
        /// Whether the current part numbers its lines. Text before a part's
        /// first line break - an editor's "vacat", a heading - is line 0 there,
        /// so that it does not take the number of the real line 1; a part with
        /// no line breaks at all is one line, line 1.
        /// </summary>
        public bool PartHasBreaks;

        public void Flush()
        {
            var text = Collapse(Current.ToString());
            if (text.Length > 0) Lines.Add(new EpiDocLine(Math.Max(Part, 1), PartLabel, Line ?? (PartHasBreaks ? "0" : "1"), text, PartType));
            Current.Clear();
        }
    }

    private static void Walk(XElement element, State state)
    {
        foreach (var node in element.Nodes())
        {
            if (node is XText t)
            {
                state.Current.Append(t.Value);
                continue;
            }

            if (node is not XElement e) continue;
            var name = e.Name.LocalName;

            switch (name)
            {
                case "lb":
                {
                    var noBreak = (string?)e.Attribute("break") == "no";
                    if (noBreak) TrimEndAndHyphen(state.Current);
                    state.Flush();
                    state.Line = ((string?)e.Attribute("n"))?.Trim();
                    state.Started = true;
                    break;
                }
                case "div" when (string?)e.Attribute("type") == "textpart":
                {
                    state.Flush();
                    state.Part++;
                    state.PartLabel = ((string?)e.Attribute("n"))?.Trim();
                    state.PartType = ((string?)e.Attribute("subtype"))?.Trim();
                    state.Line = null;
                    state.PartHasBreaks = e.Descendants(Tei + "lb").Any();
                    Walk(e, state);
                    state.Flush();
                    break;
                }
                case "head":
                    break;
                case "note":
                    // Inline notes are editorial asides; the one that belongs in
                    // the text is "sic", which Leiden prints as (!).
                    if (e.Value.Trim().Equals("sic", StringComparison.OrdinalIgnoreCase)) state.Current.Append("(!)");
                    break;
                case "app":
                {
                    // An apparatus inside the text, as the papyri have it: the
                    // lemma is the reading the editor prints, and the rival
                    // readings beside it (Preisendanz's, Wünsch's) are notes.
                    // Skipping the whole element lost the printed words too.
                    var lemma = e.Element(Tei + "lem") ?? e.Element(Tei + "rdg");
                    if (lemma != null) Walk(lemma, state);
                    break;
                }
                case "certainty" or "milestone" or "figure" or "desc" or "bibl":
                    break;
                case "choice" when e.Descendants(Tei + "lb").Any():
                {
                    var pick = e.Element(Tei + "corr") ?? e.Element(Tei + "reg") ?? e.Element(Tei + "expan")
                        ?? e.Elements().FirstOrDefault();
                    if (pick != null) Walk(pick, state);

                    // The break can sit in the reading not printed: the papyrus
                    // writes χαρχαρό- | στομα across two lines, and the
                    // regularised καρχαρόστομε has no break in it. The line
                    // still ends there, or the next line's words are run into
                    // this one and its number is lost - Suppl. Mag. 42 lost
                    // two lines that way. The whole word stays on the first
                    // line, so no hyphen.
                    if (pick != null && !pick.Descendants(Tei + "lb").Any())
                    {
                        foreach (var lb in e.Descendants(Tei + "lb"))
                        {
                            state.Flush();
                            state.Line = ((string?)lb.Attribute("n"))?.Trim();
                            state.Started = true;
                        }
                    }

                    break;
                }
                default:
                    if (e.Descendants(Tei + "lb").Any())
                    {
                        // A container (ab, p, a word or name broken across lines,
                        // a restoration that runs on to the next line) is walked
                        // into, so each line break inside it still starts a line;
                        // a bracket opened on one line closes on the next, as
                        // Leiden prints it.
                        var (open, close) = Brackets(e);
                        state.Current.Append(open);
                        Walk(e, state);
                        state.Current.Append(close);
                    }
                    else
                    {
                        state.Current.Append(Render(e));
                    }

                    break;
            }
        }
    }

    /// <summary>One element's text in Leiden form. Line breaks inside it are handled by the caller.</summary>
    public static string Render(XElement e)
    {
        var name = e.Name.LocalName;
        switch (name)
        {
            case "supplied":
            {
                var inner = Inner(e);
                return (string?)e.Attribute("reason") switch
                {
                    "omitted" => $"⟨{inner}⟩",
                    "subaudible" => $"({inner})",
                    _ => $"[{inner}]"
                };
            }
            case "gap":
                return Gap(e);
            case "unclear":
                return Underdot(Inner(e));
            case "del":
                return $"⟦{Inner(e)}⟧";
            case "surplus":
                return $"{{{Inner(e)}}}";
            case "ex":
                return $"({Inner(e)})";
            case "am":
                return string.Empty;
            case "choice":
            {
                var pick = e.Element(Tei + "corr") ?? e.Element(Tei + "reg") ?? e.Element(Tei + "expan")
                    ?? e.Elements().FirstOrDefault();
                return pick == null ? string.Empty : Inner(pick);
            }
            case "space":
                return " ";
            case "app":
            {
                var lemma = e.Element(Tei + "lem") ?? e.Element(Tei + "rdg");
                return lemma == null ? string.Empty : Inner(lemma);
            }
            case "note" or "certainty" or "figure" or "milestone":
                return string.Empty;
            case "hi" when IsEditorial((string?)e.Attribute("rend")):
                // The IG prints an editor's remarks among the letters in
                // italics, small italics or superscript - vestigia, "traces";
                // perierunt vv. 5, "five lines lost"; vacat; characteres; c.6,
                // "about six letters". They are not text of the tablet, and
                // printed plain they were read as it: a Greek tablet said
                // "defixio in lingua Dorica scripta". Bracketed, they read as
                // the asides they are. Underlining is not here: in Leiden it
                // marks letters an earlier editor saw, which are text.
                return $"({Inner(e)})";
            case "g":
                return e.Value.Length > 0 ? Inner(e) : "*";
            default:
                return Inner(e);
        }
    }

    private static bool IsEditorial(string? rend) =>
        rend != null && rend.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(r => r is "italic" or "smallit" or "sup");

    /// <summary>The brackets an element puts round its content, for one that spans a line break.</summary>
    private static (string Open, string Close) Brackets(XElement e) => e.Name.LocalName switch
    {
        "supplied" => (string?)e.Attribute("reason") switch
        {
            "omitted" => ("⟨", "⟩"),
            "subaudible" => ("(", ")"),
            _ => ("[", "]")
        },
        "del" => ("⟦", "⟧"),
        "surplus" => ("{", "}"),
        "ex" => ("(", ")"),
        _ => (string.Empty, string.Empty)
    };

    private static string Inner(XElement e)
    {
        var sb = new StringBuilder();
        foreach (var node in e.Nodes())
        {
            if (node is XText t) sb.Append(t.Value);
            else if (node is XElement c && c.Name.LocalName == "lb")
            {
                if ((string?)c.Attribute("break") == "no") sb.Append('-');
                sb.Append(" / ");
            }
            else if (node is XElement child) sb.Append(Render(child));
        }

        return sb.ToString();
    }

    /// <summary>
    /// A loss, printed as Leiden prints it: [- - -] where its extent is
    /// unknown, one dot per letter where it is counted, [- - - - - -] for lost
    /// lines, and dots without brackets for letters that are there but cannot
    /// be read.
    /// </summary>
    private static string Gap(XElement e)
    {
        var reason = (string?)e.Attribute("reason");
        var unit = (string?)e.Attribute("unit");
        var quantity = int.TryParse((string?)e.Attribute("quantity"), out var q) ? q : 0;
        var extent = (string?)e.Attribute("extent");

        if (unit == "line") return "[- - - - - -]";

        var dots = quantity > 0 && quantity <= 40 ? new string('.', quantity) : null;
        if (reason == "illegible") return dots ?? "- - -";
        if (extent == "unknown" || dots == null) return "[- - -]";
        return $"[{dots}]";
    }

    /// <summary>Each letter with a dot under it - the Leiden mark for a letter that is uncertain.</summary>
    public static string Underdot(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            sb.Append(ch);
            if (char.IsLetter(ch)) sb.Append('̣');
        }

        return sb.ToString();
    }

    private static void TrimEndAndHyphen(StringBuilder sb)
    {
        while (sb.Length > 0 && char.IsWhiteSpace(sb[^1])) sb.Length--;
        if (sb.Length > 0) sb.Append('-');
    }

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private static string Collapse(string s) => Spaces.Replace(s, " ").Trim();
}
