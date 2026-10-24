using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.WordStudy;

/// <summary>
/// The page the lexical tools set their lists on: a header with a small
/// capitalised label and room on the right for a control, the body, and an
/// optional footer for a status line - the reader pane's own chrome, so a
/// list of occurrences looks like it belongs to the same book as the text
/// it came from.
/// </summary>
internal static class ToolPane
{
    public static Border Build(string title, Control body, Control? accessory = null, Control? footer = null)
    {
        var label = new TextBlock
        {
            Text = title,
            Classes = { "section" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 8, 0)
        };

        var header = new DockPanel { Height = 38 };
        DockPanel.SetDock(label, Dock.Left);
        header.Children.Add(label);
        if (accessory != null)
        {
            accessory.HorizontalAlignment = HorizontalAlignment.Right;
            accessory.VerticalAlignment = VerticalAlignment.Center;
            accessory.Margin = new Thickness(0, 0, 8, 0);
            header.Children.Add(accessory);
        }

        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.Bind(Border.BackgroundProperty, headerBorder.GetResourceObservable("Cc.PageHeader"));
        headerBorder.Bind(Border.BorderBrushProperty, headerBorder.GetResourceObservable("Cc.Rule"));

        var dock = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        dock.Children.Add(headerBorder);

        if (footer != null)
        {
            var footerBorder = new Border
            {
                Child = footer,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 6),
                MinHeight = 30
            };
            footerBorder.Bind(Border.BorderBrushProperty, footerBorder.GetResourceObservable("Cc.Rule"));
            DockPanel.SetDock(footerBorder, Dock.Bottom);
            dock.Children.Add(footerBorder);
        }

        dock.Children.Add(body);
        return new Border { Classes = { "page" }, Child = dock };
    }

    /// <summary>
    /// The quiet message laid over a list that has nothing to show yet -
    /// "Searching…", "(no occurrences found)" - in place of the placeholder
    /// rows the Windows lists add, which could be selected and double-clicked
    /// as though they were results.
    /// </summary>
    public static TextBlock Message() => new()
    {
        Classes = { "muted" },
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(20),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        MaxWidth = 380,
        IsVisible = false
    };

    /// <summary>A list with its message over it.</summary>
    public static Grid Overlay(Control list, TextBlock message)
    {
        var grid = new Grid();
        grid.Children.Add(list);
        grid.Children.Add(message);
        return grid;
    }

    /// <summary>A push button with a glyph before its title.</summary>
    public static Button GlyphButton(string text, string glyph)
    {
        var icon = Glyphs.Icon(glyph, 14);
        icon.VerticalAlignment = VerticalAlignment.Center;
        return new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { icon, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } }
            }
        };
    }

    /// <summary>A reading face from the palette - Cc.SourceFont, Cc.TranslationFont.</summary>
    public static FontFamily Font(string key) => (FontFamily)Application.Current!.FindResource(key)!;
}
