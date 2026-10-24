using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.Help;

/// <summary>
/// The pieces the reading windows are made of - About, Help, the three
/// instrument cards, Editor's Notes, a preface: headings, paragraphs that wrap
/// to whatever width they are given, the quieter kind of paragraph, a titled
/// bullet, a citation with its link.
///
/// The Windows windows lay these out by measuring every label's height up
/// front and stacking the results, and each of them carries a comment about
/// the line that measurement once cut off. A paragraph here is a paragraph:
/// Avalonia measures it at layout time, at the real font and the real width,
/// so nothing here needs a height and nothing can be clipped.
///
/// Paragraphs are selectable, as the Windows text boxes were, because the
/// useful ones are a citation or a licence somebody will want to paste.
/// </summary>
internal static class Prose
{
    /// <summary>A heading. <paramref name="size"/> 16 is a section; larger is a page title in the display face.</summary>
    public static TextBlock Heading(string text, double size = 16, double top = 18)
    {
        var heading = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, top, 0, 6)
        };

        if (size >= 20) UseFont(heading, "Cc.DisplayFont");
        return heading;
    }

    /// <summary>A wrapped paragraph in the interface face, selectable.</summary>
    public static SelectableTextBlock Paragraph(string text, bool muted = false, double bottom = 10)
    {
        var paragraph = new SelectableTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            Margin = new Thickness(0, 0, 0, bottom)
        };

        if (muted) paragraph.Bind(TextBlock.ForegroundProperty, paragraph.GetResourceObservable("Cc.InkMuted"));
        return paragraph;
    }

    /// <summary>
    /// A bold title with its explanation indented beneath it in the quieter
    /// colour - the Windows "•  title" label and the paragraph under it.
    /// </summary>
    public static Control Bullet(string title, string body)
    {
        var dot = new TextBlock { Text = "•", FontWeight = FontWeight.Bold, Width = 16, Margin = new Thickness(2, 0, 0, 0) };
        dot.Bind(TextBlock.ForegroundProperty, dot.GetResourceObservable("Cc.Accent"));

        var text = new StackPanel();
        text.Children.Add(new SelectableTextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
        text.Children.Add(Paragraph(body, muted: true, bottom: 0));

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(text);
        return row;
    }

    /// <summary>
    /// One citation: a bold heading, the reference itself in the quieter
    /// colour, and where it can be reached if it is online. The URL is its
    /// own line rather than buried in the prose, because a reader who wants
    /// to check a number wants to find the link without reading the sentence.
    /// </summary>
    public static Control Source(Window owner, string title, string citation, string? url, string failureTitle)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 12) };
        stack.Children.Add(new SelectableTextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
        stack.Children.Add(Paragraph(citation, muted: true, bottom: url == null ? 0 : 3));
        if (url != null) stack.Children.Add(Link(owner, url, failureTitle));
        return stack;
    }

    /// <summary>
    /// An address that opens in the reader's browser. The text is the address
    /// itself, as the Windows link labels show it, so it can be read and
    /// checked before it is followed.
    /// </summary>
    public static Button Link(Window owner, string url, string failureTitle, string? text = null)
    {
        var link = new Button
        {
            Classes = { "link" },
            Content = new TextBlock { Text = text ?? url, TextWrapping = TextWrapping.Wrap },
            HorizontalAlignment = HorizontalAlignment.Left
        };
        ToolTip.SetTip(link, url);
        link.Click += async (_, _) => await ExternalLinks.OpenAsync(owner, url, failureTitle);
        return link;
    }

    /// <summary>A rule across the column, for between sections.</summary>
    public static Border Rule(double top = 14, double bottom = 14) =>
        new() { Classes = { "rule" }, Margin = new Thickness(0, top, 0, bottom) };

    /// <summary>Sets a control's font family from a palette font, following the resource.</summary>
    public static void UseFont(Control control, string key) =>
        control.Bind(TextElement.FontFamilyProperty, control.GetResourceObservable(key));
}

/// <summary>
/// Hands addresses and pages to the reader's browser - through
/// <see cref="MacShell.Open"/>, or through whatever a test puts in its place,
/// so a test can press every button in these windows without a browser
/// appearing on the machine running it.
/// </summary>
internal static class ExternalLinks
{
    public static Func<string, bool> Opener { get; set; } = MacShell.Open;

    /// <summary>
    /// Opens an address, and says so when it could not be opened - with the
    /// address in the message, so it can be typed in by hand.
    /// </summary>
    public static async Task<bool> OpenAsync(Window owner, string url, string failureTitle)
    {
        if (Opener(url)) return true;

        await Alert.Warn(owner, failureTitle, $"Couldn't open that link in a browser. The address is:\n{url}");
        return false;
    }
}
