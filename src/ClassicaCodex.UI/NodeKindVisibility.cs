using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// Which kinds of text node the reader shows.
///
/// A play's text is not only its lines. It is also who is speaking, who is on
/// stage, what the stage is doing, and who the characters are - all of it
/// printed in every edition, all of it now in TextNodes with a NodeKind saying
/// which is which.
///
/// Whether that belongs on the page depends entirely on who is reading. Someone
/// blocking a production wants the cast list and the stage directions and would
/// happily lose the headings; someone reading the Greek wants the verse and
/// nothing interrupting it; someone checking a speech attribution wants the
/// speakers and not much else. There is no default that serves all three, so
/// this is a switch rather than a decision made for them.
///
/// Everything is visible unless it has been turned off, which is what makes an
/// absent preference file mean the right thing: a reader who has never opened
/// this menu sees the complete edition, and the parser work that put those
/// nodes there is not silently undone by a setting nobody set.
///
/// Stored as a plain file under %LocalAppData%, alongside PaneSyncSettings and
/// ReadingPosition, and read the same way. Hidden kinds are listed rather than
/// visible ones, so a kind this version has never heard of - one a later parser
/// learns to emit - shows up by default instead of vanishing because it was
/// missing from a stored list of what to show.
/// </summary>
public static class NodeKindVisibility
{
    private static string HiddenFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "hidden-node-kinds.txt");

    /// <summary>
    /// What to call each kind in front of a reader.
    ///
    /// "line" is "Text" rather than "Line" because it is the thing itself, and
    /// the menu reads as a list of what to include beside the text - except
    /// that the text is includable too, since a director extracting nothing but
    /// entrances and exits is a real thing to want.
    /// </summary>
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        [TextNodeKinds.Line] = "Text",
        [TextNodeKinds.Speaker] = "Speakers",
        [TextNodeKinds.Stage] = "Stage directions",
        [TextNodeKinds.Cast] = "Cast list",
        [TextNodeKinds.Head] = "Headings",
        [TextNodeKinds.Paratext] = "Front and back matter",
        [TextNodeKinds.Attribution] = "Attributions"
    };

    /// <summary>
    /// A readable name for a kind, falling back to the raw value so an
    /// unrecognised one is still offered rather than hidden behind a blank
    /// menu entry.
    /// </summary>
    public static string Label(string kind)
    {
        if (Labels.TryGetValue(kind, out var label)) return label;
        if (string.IsNullOrWhiteSpace(kind)) return "Other";

        return char.ToUpperInvariant(kind[0]) + kind[1..];
    }

    /// <summary>
    /// The order kinds appear in the menu: the text first, then what sits
    /// around it, roughly as they sit on a page. Anything unrecognised sorts
    /// last rather than being dropped.
    /// </summary>
    private static readonly string[] MenuOrder =
    {
        TextNodeKinds.Line,
        TextNodeKinds.Speaker,
        TextNodeKinds.Stage,
        TextNodeKinds.Cast,
        TextNodeKinds.Head,
        TextNodeKinds.Attribution,
        TextNodeKinds.Paratext
    };

    public static IEnumerable<string> InMenuOrder(IEnumerable<string> kinds)
    {
        var present = kinds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return present
            .OrderBy(k =>
            {
                var i = Array.FindIndex(MenuOrder, m => string.Equals(m, k, StringComparison.OrdinalIgnoreCase));
                return i < 0 ? MenuOrder.Length : i;
            })
            .ThenBy(Label, StringComparer.CurrentCulture);
    }

    private static HashSet<string> Load()
    {
        try
        {
            if (!File.Exists(HiddenFile)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return File.ReadAllLines(HiddenFile)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save(IEnumerable<string> hidden)
    {
        try
        {
            var directory = Path.GetDirectoryName(HiddenFile);
            if (directory != null) Directory.CreateDirectory(directory);

            File.WriteAllLines(HiddenFile, hidden.Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth interrupting reading over - the choice still holds for
            // this session.
        }
    }

    public static bool IsVisible(string kind) => !Load().Contains(kind);

    public static void SetVisible(string kind, bool visible)
    {
        var hidden = Load();

        if (visible) hidden.Remove(kind);
        else hidden.Add(kind);

        Save(hidden);
    }

    /// <summary>The visible subset of a set of nodes, in their existing order.</summary>
    public static List<TextNode> Filter(IEnumerable<TextNode> nodes)
    {
        var hidden = Load();

        return nodes
            .Where(n => !hidden.Contains(string.IsNullOrWhiteSpace(n.NodeKind)
                ? TextNodeKinds.Line
                : n.NodeKind))
            .ToList();
    }

    /// <summary>Turns everything back on. The way out of a pane hidden to nothing.</summary>
    public static void ShowAll() => Save(Array.Empty<string>());

    public static bool AnythingHidden() => Load().Count > 0;
}
