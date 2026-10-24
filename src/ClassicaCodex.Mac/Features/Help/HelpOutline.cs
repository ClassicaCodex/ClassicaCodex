using ClassicaCodex.Data;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.Help;

internal enum HelpBlockKind { Heading, Paragraph, Item }

/// <summary>One piece of a help topic: a heading, a paragraph or a list item, at a depth of indent.</summary>
internal sealed record HelpBlock(HelpBlockKind Kind, int Level, string Text);

/// <summary>
/// Reads the help text's outline - the Windows format, where three spaces of
/// indent put a line under the one above it - into headings, paragraphs and
/// list items.
///
/// The Windows window shows the text as it is, in a read-only text box, and
/// the indents do the work of headings. Here they become headings, so the
/// rules are the ones a reader of that text box applies by eye: a line with
/// lines indented under it is a heading over them, unless it ends with a
/// colon, when it is the sentence introducing them; two or more lines at one
/// depth with no blank line between are a list; anything else is a paragraph.
/// </summary>
internal static class HelpOutline
{
    private const int IndentWidth = 3;

    public static IReadOnlyList<HelpBlock> Parse(string body)
    {
        var blocks = new List<HelpBlock>();
        var group = new List<(int Level, string Text)>();

        foreach (var raw in body.ReplaceLineEndings("\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                Read(group, 0, group.Count, blocks);
                group.Clear();
                continue;
            }

            var indent = raw.Length - raw.TrimStart(' ').Length;
            group.Add((indent / IndentWidth, raw.Trim()));
        }

        Read(group, 0, group.Count, blocks);
        return blocks;
    }

    private static void Read(List<(int Level, string Text)> lines, int start, int end, List<HelpBlock> blocks)
    {
        var i = start;
        while (i < end)
        {
            var (level, text) = lines[i];
            var under = ChildrenEnd(lines, i, end);

            if (under > i + 1)
            {
                blocks.Add(new HelpBlock(text.EndsWith(':') ? HelpBlockKind.Paragraph : HelpBlockKind.Heading, level, text));
                Read(lines, i + 1, under, blocks);
                i = under;
                continue;
            }

            // Lines at this depth with nothing under them, up to the next one
            // that has something under it: that one is a heading of its own.
            var run = i + 1;
            while (run < end && lines[run].Level == level && ChildrenEnd(lines, run, end) == run + 1) run++;

            var kind = run - i >= 2 ? HelpBlockKind.Item : HelpBlockKind.Paragraph;
            for (var k = i; k < run; k++) blocks.Add(new HelpBlock(kind, level, lines[k].Text));
            i = run;
        }
    }

    /// <summary>The index just past the lines indented under line <paramref name="i"/>.</summary>
    private static int ChildrenEnd(List<(int Level, string Text)> lines, int i, int end)
    {
        var j = i + 1;
        while (j < end && lines[j].Level > lines[i].Level) j++;
        return j;
    }

    /// <summary>
    /// The help text with the folders this Mac actually uses written in, the
    /// home folder shortened to ~ as the Finder's Go to Folder takes it.
    /// Worked out rather than written down, because where .NET puts
    /// application data on a Mac is .NET's decision, and a help page that
    /// names a folder the app does not use sends the reader to an empty one.
    /// </summary>
    public static string Expand(string body)
    {
        var preferences = Path.GetDirectoryName(CrashLog.LogFile) ?? string.Empty;

        return body
            .Replace("%DATABASE%", Home(DbConnectionFactory.DefaultDatabasePath))
            .Replace("%DOWNLOADS%", Home(DataFolderSettings.DefaultRoot))
            .Replace("%PREFERENCES%", Home(preferences))
            .Replace("%ERRORS_LOG%", Home(CrashLog.LogFile))
            .Replace("%SKIPPED_LOG%", Home(Path.Combine(preferences, "ingest-skipped.log")));
    }

    /// <summary>Whether a line is one of the folders <see cref="Expand"/> writes in, whatever shape it has.</summary>
    public static bool IsFolder(string text) =>
        Expand("%DATABASE%|%DOWNLOADS%|%PREFERENCES%|%ERRORS_LOG%|%SKIPPED_LOG%").Split('|').Contains(text);

    private static string Home(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrEmpty(home) && path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? "~" + path[home.Length..]
            : path;
    }
}
