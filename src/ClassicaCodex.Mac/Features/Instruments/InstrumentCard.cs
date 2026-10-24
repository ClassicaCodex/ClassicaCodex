using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.Help;

namespace ClassicaCodex.Mac.Features.Instruments;

/// <summary>
/// The card that stands in front of each of the three instruments - the
/// Antikythera mechanism, Ptolemy's cosmos, Ptolemy's harmony: what it is, how
/// much of it is reconstruction, where every number came from, and a button
/// that opens the instrument itself.
///
/// <b>Why a page in a browser rather than a window here.</b> Each instrument
/// is drawn on a canvas at sixty frames a second, and each already existed as
/// one self-contained page that runs anywhere. Re-implementing them would buy
/// nothing but a second copy to keep in step with the first - so the Mac app
/// ships the very same files the Windows app does, in a Pages folder beside
/// its executable, and hands them to the reader's own browser. The pages carry
/// no scripts from anywhere else and make no network requests at all, so they
/// work with the machine offline, and they ship no font.
///
/// <b>Why the explanation is here rather than in the page.</b> Someone who
/// chose a menu item in a library application has not asked for a
/// planetarium. The card says what they are about to open, and admits how
/// much of it is reconstruction, before it opens.
/// </summary>
internal abstract class InstrumentCard : FeatureWindow
{
    private readonly StackPanel _body;
    private readonly ScrollViewer _scroller;
    private readonly string _pageFile;
    private readonly string _pageName;
    private readonly string _logContext;
    private readonly string _failurePrefix;

    protected InstrumentCard(
        string title, string tagline, string tileKey, string glyph,
        string pageFile, string pageName, string openLabel, string logContext, string failurePrefix)
    {
        _pageFile = pageFile;
        _pageName = pageName;
        _logContext = logContext;
        _failurePrefix = failurePrefix;

        Title = title;
        Width = 720;
        Height = 660;
        MinWidth = 520;
        MinHeight = 400;

        var tile = Art.Tile(tileKey, glyph, 58);
        tile.VerticalAlignment = VerticalAlignment.Center;
        tile.Margin = new Thickness(0, 0, 16, 0);

        var name = Prose.Heading(title, 26, top: 0);
        name.Margin = new Thickness(0);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(name);
        titles.Children.Add(new TextBlock { Text = tagline, Classes = { "caption" }, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(tile, Dock.Left);
        header.Children.Add(tile);
        header.Children.Add(titles);

        _body = new StackPanel { Margin = new Thickness(28, 24, 28, 28), MaxWidth = 720 };
        _body.Children.Add(header);

        _scroller = new ScrollViewer { Content = _body };

        OpenButton = new Button
        {
            Content = openLabel,
            Classes = { "accent" },
            IsDefault = true,
            MinWidth = 120,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        OpenButton.Click += async (_, _) => await OpenPageAsync();

        var close = new Button { Content = "Close", MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(close);
        buttons.Children.Add(OpenButton);

        var footer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 12, 20, 14),
            Child = buttons
        };
        footer.Bind(Border.BorderBrushProperty, footer.GetResourceObservable("Cc.Rule"));
        footer.Bind(Border.BackgroundProperty, footer.GetResourceObservable("Cc.PageHeader"));

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_scroller);
        Content = root;

        // The card must open at its top - on the Harmonics card that is not
        // cosmetic, because the thing at the top is the warning. Focus goes to
        // the footer, outside the scrolling column, so nothing on opening can
        // bring a link two thirds of the way down into view and decide where
        // the card starts. That is exactly what happened on Windows, where the
        // first focusable control in the panel was a citation link.
        Opened += (_, _) =>
        {
            _scroller.Offset = default;
            OpenButton.Focus();
        };
    }

    /// <summary>The button that opens the instrument's page.</summary>
    internal Button OpenButton { get; }

    /// <summary>Where the page ships: in Pages beside the executable, copied there by the build.</summary>
    internal string PagePath => Path.Combine(AppContext.BaseDirectory, "Pages", _pageFile);

    protected void Heading(string text) => _body.Children.Add(Prose.Heading(text));

    protected void Paragraph(string text) => _body.Children.Add(Prose.Paragraph(text));

    protected void Muted(string text) => _body.Children.Add(Prose.Paragraph(text, muted: true));

    protected void Bullet(string title, string body) => _body.Children.Add(Prose.Bullet(title, body));

    protected void Source(string title, string citation, string? url) =>
        _body.Children.Add(Prose.Source(this, title, citation, url, Title ?? _pageName));

    /// <summary>
    /// The one line on a card that has to be read before the button is
    /// pressed. Bold, a size up, and in the accent - the attention colour -
    /// rather than the warning red: nothing is wrong, and a red line would say
    /// something was.
    /// </summary>
    protected void Caution(string text)
    {
        var icon = new PathIcon { Data = Glyphs.Get("ReadAloud"), Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        icon.Bind(PathIcon.ForegroundProperty, icon.GetResourceObservable("Cc.Accent"));

        var line = new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        line.Bind(TextBlock.ForegroundProperty, line.GetResourceObservable("Cc.AccentStrong"));

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(line);

        _body.Children.Add(new Border { Classes = { "notice" }, Child = row, Margin = new Thickness(0, 10, 0, 12), Padding = new Thickness(14, 10) });
    }

    /// <summary>
    /// Hands the page to the browser where it ships. Windows writes its copy
    /// out of the executable into the temporary folder first, because there
    /// the page is a resource inside the .exe; here it is already a file in
    /// the app, and the browser can read it where it is.
    /// </summary>
    private async Task OpenPageAsync()
    {
        try
        {
            var path = PagePath;
            if (!File.Exists(path))
                throw new FileNotFoundException($"The {_pageName} page is missing from this build.", path);

            if (!ExternalLinks.Opener(path))
                throw new InvalidOperationException("macOS did not hand the page to a browser.");
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, _logContext);
            await Alert.Error(this, Title ?? _pageName, _failurePrefix + ex.Message);
        }
    }
}
