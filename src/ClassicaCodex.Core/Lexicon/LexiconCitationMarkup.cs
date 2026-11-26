using System.Text;

namespace ClassicaCodex.Core.Lexicon;

/// <summary>
/// A citation inside a dictionary entry, as Word Study shows it.
/// </summary>
/// <param name="Ordinal">Its place among the entry's citations, from 0 - what the citations table keys on.</param>
/// <param name="Label">The citation as the dictionary prints it: "Il. 1.1", "Cic. Div. 1, 13, 23".</param>
/// <param name="Target">The passage it names, or null where none can be named.</param>
public sealed record EntryCitation(int Ordinal, string Label, LexiconCitationTarget? Target);

/// <summary>A run of an entry: plain text, or one citation.</summary>
public sealed record EntryPiece(string Text, EntryCitation? Citation)
{
    public bool IsCitation => Citation != null;
}

/// <summary>
/// How an entry keeps its citations: inside its own text, each one marked off
/// where the dictionary printed it.
///
/// A citation is written <c>U+FDD0 target U+FDD1 label U+FDD2</c>: the
/// passage it names in the dotted URN shape (see
/// <see cref="LexiconCitationTarget"/>), empty where it names none, and the
/// citation as printed. The three are Unicode noncharacters, which the
/// standard sets aside for exactly this - a program's own use inside its own
/// text, never to be met in anyone else's - so no dictionary text can be
/// mistaken for one.
///
/// Inside the text rather than in a table beside it because the text is
/// what everything else reads: the entry's Greek is decoded and its lines
/// joined around the citations without either needing to know where they
/// are, and every pane that shows an entry gets them by calling
/// <see cref="LexiconEntryText"/>. An entry stored before citations were kept
/// has none of these characters and reads as it always did.
/// </summary>
public static class LexiconCitationMarkup
{
    public const char Open = '﷐';
    public const char Separator = '﷑';
    public const char Close = '﷒';

    /// <summary>One citation, marked off. Whitespace in the label is closed up, so a citation never breaks across lines.</summary>
    public static string Write(LexiconCitationTarget? target, string label) =>
        $"{Open}{target}{Separator}{CleanLabel(label)}{Close}";

    /// <summary>Whether the entry carries any citations at all.</summary>
    public static bool HasCitations(string? entry) => entry != null && entry.IndexOf(Open) >= 0;

    /// <summary>
    /// The entry as runs of text and citations, in order. A marker left open -
    /// an entry cut short in the middle of one - is read as its label alone.
    /// </summary>
    public static IReadOnlyList<EntryPiece> Parse(string entry)
    {
        var pieces = new List<EntryPiece>();
        var at = 0;
        var ordinal = 0;

        while (at < entry.Length)
        {
            var open = entry.IndexOf(Open, at);
            if (open < 0)
            {
                pieces.Add(new EntryPiece(entry[at..], null));
                break;
            }

            if (open > at) pieces.Add(new EntryPiece(entry[at..open], null));

            var separator = entry.IndexOf(Separator, open + 1);
            var close = separator < 0 ? -1 : entry.IndexOf(Close, separator + 1);
            var nextOpen = entry.IndexOf(Open, open + 1);
            if (separator < 0 || close < 0 || (nextOpen >= 0 && nextOpen < close))
            {
                // Broken: keep what a reader would have seen, without the marks.
                var end = nextOpen >= 0 ? nextOpen : entry.Length;
                var text = Strip(entry[(open + 1)..end]);
                if (text.Length > 0) pieces.Add(new EntryPiece(text, null));
                at = end;
                continue;
            }

            var target = LexiconCitationTarget.Parse(entry[(open + 1)..separator]);
            var label = entry[(separator + 1)..close];
            pieces.Add(new EntryPiece(label, new EntryCitation(ordinal++, label, target)));
            at = close + 1;
        }

        return pieces;
    }

    /// <summary>The citations alone, in order.</summary>
    public static IReadOnlyList<EntryCitation> Citations(string entry) =>
        Parse(entry).Where(p => p.Citation != null).Select(p => p.Citation!).ToList();

    /// <summary>The entry as plain text: each citation as printed, the marking gone.</summary>
    public static string Plain(string entry)
    {
        if (!HasCitations(entry)) return entry;
        return string.Concat(Parse(entry).Select(p => p.Text));
    }

    /// <summary>
    /// Applies <paramref name="transform"/> to the entry's own text and not to
    /// its citations - decoding LSJ's Beta Code, which would otherwise read a
    /// URN's letters as Greek.
    /// </summary>
    public static string MapText(string entry, Func<string, string> transform)
    {
        if (!HasCitations(entry)) return transform(entry);

        var sb = new StringBuilder(entry.Length);
        var at = 0;
        while (at < entry.Length)
        {
            var open = entry.IndexOf(Open, at);
            var close = open < 0 ? -1 : entry.IndexOf(Close, open);
            if (open < 0 || close < 0)
            {
                sb.Append(transform(entry[at..]));
                break;
            }

            if (open > at) sb.Append(transform(entry[at..open]));
            sb.Append(entry, open, close - open + 1);
            at = close + 1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Cuts an entry to at most <paramref name="maxVisible"/> characters of
    /// what a reader sees - text and citations as printed, not the passages
    /// they name - and never inside a citation. Returns the entry and how many
    /// citations it still holds, so citations past the cut can be dropped
    /// from the table too.
    /// </summary>
    public static (string Text, int CitationsKept) Truncate(string entry, int maxVisible, string ellipsis = "...")
    {
        const int InText = 0, InTarget = 1, InLabel = 2;

        var visible = 0;
        var kept = 0;
        var at = 0;
        var state = InText;
        var lastSafe = 0;
        var keptAtSafe = 0;

        while (at < entry.Length)
        {
            var c = entry[at];
            if (c == Open)
            {
                state = InTarget;
                lastSafe = at;
                keptAtSafe = kept;
            }
            else if (c == Separator && state == InTarget)
            {
                state = InLabel;
            }
            else if (c == Close && state == InLabel)
            {
                state = InText;
                kept++;
                lastSafe = at + 1;
                keptAtSafe = kept;
            }
            else if (state != InTarget)
            {
                if (++visible > maxVisible) break;
                if (state == InText)
                {
                    lastSafe = at + 1;
                    keptAtSafe = kept;
                }
            }

            at++;
        }

        if (at >= entry.Length) return (entry, kept);
        return (entry[..lastSafe] + ellipsis, keptAtSafe);
    }

    /// <summary>The marking taken out, the label kept: what a stray or broken marker leaves.</summary>
    private static string Strip(string text)
    {
        var separator = text.IndexOf(Separator);
        var label = separator >= 0 ? text[(separator + 1)..] : text;
        return label.Replace(Close.ToString(), string.Empty);
    }

    private static string CleanLabel(string label) =>
        string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Replace(Open.ToString(), string.Empty)
            .Replace(Separator.ToString(), string.Empty)
            .Replace(Close.ToString(), string.Empty);
}
