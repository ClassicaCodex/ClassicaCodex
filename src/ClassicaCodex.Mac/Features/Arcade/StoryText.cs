using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// The story panel under the arena: what the oracle says, the clue, the
/// verse once found. The Windows game writes it into a read-only rich text
/// box as one string; so does this, and <see cref="Story"/> is that string,
/// appended to the way the Windows code appends - the oracle's hint, a
/// "that is not the passage yet" - so every message reaches the player
/// whole and in order.
///
/// What is added is typography. The game marks its headings by shouting
/// them - "THE HERO HAS FALLEN", "VERSE 2 / 5 — THE ORACLE'S CLUE",
/// "FOUND: Homer — Iliad 1.106" - and a paragraph that opens with such a line
/// has that line set as a heading in the accent colour, the rest in the
/// reading face. Selectable, as the Windows box is, because a clue is
/// something a player copies into a search.
/// </summary>
internal sealed class StoryText : SelectableTextBlock
{
    public StoryText()
    {
        TextWrapping = TextWrapping.Wrap;
        FontSize = 15;
        LineHeight = 22;
        this.Bind(FontFamilyProperty, this.GetResourceObservable("Cc.SourceFont"));
        this.Bind(ForegroundProperty, this.GetResourceObservable("Cc.Ink"));
    }

    /// <summary>Everything the panel says, as the Windows game would have written it.</summary>
    public string Story { get; private set; } = string.Empty;

    public void Set(string text)
    {
        Story = text;
        var inlines = new InlineCollection();
        var lines = text.Split('\n');
        var paragraphStart = true;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i > 0) inlines.Add(new LineBreak());

            if (paragraphStart && IsHeading(line))
            {
                var heading = new Run(line)
                {
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 13.5,
                    FontFamily = FontFamily.Default
                };
                heading.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("Cc.Accent"));
                inlines.Add(heading);
            }
            else if (line.Length > 0)
            {
                inlines.Add(new Run(line));
            }

            paragraphStart = line.Trim().Length == 0;
        }

        Inlines = inlines;
    }

    public void Append(string text) => Set(Story + text);

    /// <summary>
    /// A line the game shouts: its first word is capitals, two letters or
    /// more, and it is not a sentence. "WASD / arrows: move and aim …" opens
    /// with capitals too, but it ends in a full stop - it is instructions,
    /// and stays in the reading face.
    /// </summary>
    internal static bool IsHeading(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.EndsWith('.') || trimmed.EndsWith('?') || trimmed.EndsWith('!')) return false;

        var word = new string(trimmed.TakeWhile(char.IsLetter).ToArray());
        return word.Length >= 2 && word.All(char.IsUpper);
    }
}
