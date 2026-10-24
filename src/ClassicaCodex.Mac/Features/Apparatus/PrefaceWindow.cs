using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Help;

namespace ClassicaCodex.Mac.Features.Apparatus;

/// <summary>
/// A translator's preface, or similar front matter, that the reader held back
/// from its page: it has nothing on the other side to line up against, and
/// used to sit at the top of the list looking like the first line of the
/// actual text. Opened from "View Preface…" on a page's right-click menu, which
/// only appears when the edition loaded there has one.
///
/// Resizable rather than a fixed dialog, and set like Help - this is the same
/// kind of content, a block of prose someone is reading, not a small form to
/// fill in: a page in the translation face, selectable so a passage of it can
/// be quoted.
/// </summary>
public sealed class PrefaceWindow : FeatureWindow
{
    public PrefaceWindow(string title, string prefaceText)
    {
        Title = title;
        Width = 760;
        Height = 640;
        MinWidth = 480;
        MinHeight = 320;

        Text = prefaceText.ReplaceLineEndings("\n").Trim();

        var heading = Prose.Heading(title.Replace("Preface — ", string.Empty), 22, top: 0);
        heading.Margin = new Thickness(0, 0, 0, 4);

        var body = new SelectableTextBlock
        {
            Text = Text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            LineHeight = 23
        };
        Prose.UseFont(body, "Cc.TranslationFont");

        var column = new StackPanel { Margin = new Thickness(40, 30, 40, 36), MaxWidth = 660 };
        column.Children.Add(new TextBlock { Text = "PREFACE", Classes = { "section" }, Margin = new Thickness(0, 0, 0, 6) });
        column.Children.Add(heading);
        column.Children.Add(Prose.Rule(12, 18));
        column.Children.Add(body);

        Content = new Border
        {
            Classes = { "page" },
            Margin = new Thickness(16),
            Child = new ScrollViewer { Content = column }
        };
    }

    /// <summary>The preface as shown.</summary>
    internal string Text { get; }
}
