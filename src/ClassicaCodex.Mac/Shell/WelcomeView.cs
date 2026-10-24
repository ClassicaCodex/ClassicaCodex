using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Shell;

/// <summary>
/// What the reading area shows before a work is open: the welcome art, the
/// way back into whatever was last being read, and the doors most people
/// want first.
/// </summary>
public sealed class WelcomeView : UserControl
{
    private readonly MainWindow _main;
    private readonly WrapPanel _cards = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _stats = new() { Classes = { "caption" }, Margin = new Thickness(0, 22, 0, 0) };
    private readonly Image _hero = new() { Stretch = Stretch.UniformToFill };
    private readonly Panel _fallback;

    public WelcomeView(MainWindow main)
    {
        _main = main;

        _fallback = BuildFallbackArt();

        var title = new TextBlock { Text = "Classica Codex", Classes = { "display" }, FontSize = 46 };
        var subtitle = new TextBlock
        {
            Text = "The Greek and Latin classics, their translations and dictionaries, and the languages around them - read side by side, searched, and studied on your own Mac.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 500,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 15,
            Classes = { "muted" },
            Margin = new Thickness(0, 8, 0, 28)
        };

        var column = new StackPanel { Margin = new Thickness(56, 56, 56, 40), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        column.Children.Add(title);
        column.Children.Add(subtitle);
        column.Children.Add(_cards);
        column.Children.Add(_stats);

        var root = new Panel();
        root.Children.Add(_fallback);
        root.Children.Add(_hero);
        root.Children.Add(new ScrollViewer { Content = column });
        Content = root;

        ActualThemeVariantChanged += (_, _) => UpdateHero();
        UpdateHero();
    }

    /// <summary>Re-reads what the cards and the counts say - after a library is opened or added to.</summary>
    public async Task RefreshAsync()
    {
        _cards.Children.Clear();

        var saved = ReadingPosition.Load();
        var lastWork = saved == null ? null : _main.LibraryWorks.FirstOrDefault(w => w.CtsUrn == saved.Value.WorkCtsUrn);

        if (lastWork != null && saved != null)
        {
            var author = _main.AuthorNameFor(lastWork);
            _cards.Children.Add(Card("Continue Reading", $"{author}, {lastWork.Title} {saved.Value.CitationRef}", "reader", "Work", async () =>
            {
                var target = await new TextNodeRepository().FindByWorkUrnAndCitationAsync(saved.Value.WorkCtsUrn, saved.Value.CitationRef);
                if (target != null) await _main.NavigateToPassageAsync(target.Value.WorkId, target.Value.TextNodeId);
                else await _main.OpenWorkAsync(lastWork.WorkId);
            }, wide: true));
        }

        if (_main.LibraryWorkCount == 0)
        {
            _cards.Children.Add(Card("Set Up Your Library", "Download the classics, dictionaries and lemma data", "setup", "Setup", () => { _main.OpenTool("setup"); return Task.CompletedTask; }, wide: true));
        }

        _cards.Children.Add(Card("Search", "Every passage, every edition", "search", "Search", () => { _main.OpenTool("search"); return Task.CompletedTask; }));
        _cards.Children.Add(Card("Where Should I Start?", "Works worth translating first", "starting-points", "StartingPoints", () => { _main.OpenTool("starting-points"); return Task.CompletedTask; }));
        _cards.Children.Add(Card("Timeline", "The authors across time", "timeline", "Timeline", () => { _main.OpenTool("timeline"); return Task.CompletedTask; }));
        _cards.Children.Add(Card("All Tools", "Everything the app can do", "settings", "Gallery", () => { _main.OpenTool("gallery"); return Task.CompletedTask; }));

        var passages = 0L;
        try { passages = await new WordIndexRepository().GetTextNodeCountAsync(); }
        catch (Exception) { /* counts are decoration */ }

        _stats.Text = _main.LibraryWorkCount == 0
            ? "Your library is empty."
            : $"{_main.LibraryAuthorCount:N0} authors · {_main.LibraryWorkCount:N0} works · {passages:N0} passages";
    }

    private Button Card(string title, string subtitle, string tileKey, string glyph, Func<Task> action, bool wide = false)
    {
        var tile = Art.Tile(tileKey, glyph, 52);
        tile.VerticalAlignment = VerticalAlignment.Center;
        tile.Margin = new Thickness(0, 0, 14, 0);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, Classes = { "h3" }, FontSize = 14 });
        text.Children.Add(new TextBlock { Text = subtitle, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap });

        var row = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        row.Children.Add(tile);
        row.Children.Add(text);

        var button = new Button
        {
            Classes = { "tile" },
            Content = row,
            Width = wide ? 510 : 248,
            Height = 82,
            Margin = new Thickness(0, 0, 14, 14)
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private void UpdateHero()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var bitmap = Art.Load(dark ? "welcome-hero-dark.png" : "welcome-hero-light.png");
        _hero.Source = bitmap;
        _hero.IsVisible = bitmap != null;
        _fallback.IsVisible = bitmap == null;
        _hero.Opacity = 0.9;
    }

    /// <summary>
    /// Until the welcome painting arrives: a warm wash and two opening lines
    /// set large and faint - the Odyssey's invocation and the Aeneid's, the
    /// first words most readers of either language ever met.
    /// </summary>
    private static Panel BuildFallbackArt()
    {
        var panel = new Panel { ClipToBounds = true };

        var wash = new Border();
        wash.Bind(Border.BackgroundProperty, wash.GetResourceObservable("Cc.Window"));
        panel.Children.Add(wash);

        var glow = new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.85, 0.15, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.85, 0.15, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#40C9A45C"), 0),
                    new GradientStop(Color.Parse("#00C9A45C"), 1)
                }
            }
        };
        panel.Children.Add(glow);

        var greek = new TextBlock
        {
            Text = "ἄνδρα μοι ἔννεπε, μοῦσα",
            FontSize = 72,
            Opacity = 0.07,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -40, 96),
            Classes = { "display" }
        };
        var latin = new TextBlock
        {
            Text = "arma virumque cano",
            FontSize = 60,
            FontStyle = FontStyle.Italic,
            Opacity = 0.06,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 40, 24),
            Classes = { "display" }
        };
        panel.Children.Add(greek);
        panel.Children.Add(latin);
        return panel;
    }
}
