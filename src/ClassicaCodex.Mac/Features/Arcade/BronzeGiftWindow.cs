using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Mac.Common;

namespace ClassicaCodex.Mac.Features.Arcade;

/// <summary>
/// The shrine that opens after a recovered verse: three divine gifts, one to
/// keep for the rest of the adventure. The offer is BronzeGifts.Offer's,
/// seeded by the run and the chapter, so closing the shrine and coming back
/// shows the same three - a player cannot reroll the gods by asking twice.
///
/// Each card says what the gift does in the arena first and where the motif
/// comes from after, with the ancient source named, and the footer says
/// plainly that the arena powers are adaptations - the Windows wording, kept.
/// 1, 2 or 3 chooses; Escape decides later.
/// </summary>
internal sealed class BronzeGiftWindow : FeatureWindow
{
    private readonly BronzeGift[] _offers;
    private readonly List<string> _cardTexts = new();

    public BronzeGiftId? SelectedGift { get; private set; }

    public BronzeGiftWindow(BronzeGift[] offers)
    {
        _offers = offers;
        Title = "The gods have taken notice";
        Width = 870;
        Height = 560;
        MinWidth = 760;
        MinHeight = 560;
        if (BronzeIcons.DivineGift() is { } icon) Icon = new WindowIcon(icon);

        var heading = new TextBlock { Text = "CHOOSE YOUR DIVINE GIFT", Classes = { "h1" }, LetterSpacing = 1.2 };
        heading.Bind(TextBlock.FontFamilyProperty, heading.GetResourceObservable("Cc.DisplayFont"));
        heading.Bind(TextBlock.ForegroundProperty, heading.GetResourceObservable("Cc.Accent"));

        var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(heading);
        header.Children.Add(new TextBlock
        {
            Text = "One gift. Yours for the rest of this adventure. Gifts combine with those you already carry.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap
        });

        var cards = new UniformGrid { Columns = Math.Max(1, offers.Length), Rows = 1 };
        for (var i = 0; i < offers.Length; i++) cards.Children.Add(Card(offers[i], i + 1));

        var footer = new TextBlock
        {
            Text = "1 / 2 / 3: choose · Esc: decide later. These arena powers are playful adaptations of the myths.",
            Classes = { "caption" },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var layout = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(cards);
        Content = layout;

        KeyDown += (_, e) =>
        {
            var i = e.Key - Key.D1;
            if (e.KeyModifiers == KeyModifiers.None && i >= 0 && i < _offers.Length) { Accept(_offers[i].Id); e.Handled = true; }
        };
    }

    /// <summary>The offers on the cards, left to right.</summary>
    internal IReadOnlyList<BronzeGift> Offers => _offers;

    /// <summary>What each card says, as the Windows card's text box holds it.</summary>
    internal IReadOnlyList<string> CardTexts => _cardTexts;

    internal void Accept(BronzeGiftId gift)
    {
        SelectedGift = gift;
        Close(true);
    }

    private Control Card(BronzeGift gift, int number)
    {
        var art = new GiftGlyph(gift.Id) { Width = 76, Height = 76, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 10) };

        var patron = new TextBlock { Text = gift.Patron, Classes = { "section" }, LetterSpacing = 1.4 };
        patron.Bind(TextBlock.ForegroundProperty, patron.GetResourceObservable("Cc.Accent"));
        var name = new TextBlock { Text = gift.Name, Classes = { "h2" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
        name.Bind(TextBlock.FontFamilyProperty, name.GetResourceObservable("Cc.DisplayFont"));

        _cardTexts.Add(gift.Effect + "\n\n" + gift.Story + "\n\nMythic source: " + gift.Source);

        var source = new Run("Mythic source: " + gift.Source) { FontSize = 12 };
        source.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("Cc.InkMuted"));
        var text = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            Inlines = new InlineCollection
            {
                new Run(gift.Effect) { FontWeight = FontWeight.SemiBold },
                new LineBreak(), new LineBreak(),
                new Run(gift.Story),
                new LineBreak(), new LineBreak(),
                source
            }
        };

        var cap = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 0, 5, 1),
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = number.ToString(), FontSize = 11, FontWeight = FontWeight.SemiBold }
        };
        var label = new TextBlock { Text = "Accept gift", VerticalAlignment = VerticalAlignment.Center };
        cap.Bind(Border.BorderBrushProperty, label.GetObservable(TextBlock.ForegroundProperty));
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(label);
        content.Children.Add(cap);

        var accept = new Button
        {
            Content = content,
            Classes = { "accent" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 34,
            Margin = new Thickness(0, 12, 0, 0)
        };
        accept.Click += (_, _) => Accept(gift.Id);

        var top = new StackPanel();
        top.Children.Add(art);
        top.Children.Add(patron);
        top.Children.Add(name);

        var card = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(accept, Dock.Bottom);
        card.Children.Add(top);
        card.Children.Add(accept);
        card.Children.Add(new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        return new Border
        {
            Classes = { "card" },
            Margin = new Thickness(6, 0),
            Padding = new Thickness(16, 14, 16, 14),
            Child = card
        };
    }

    /// <summary>
    /// The gift's emblem - shield, sandal, cuirass, bow, trident, helm - in
    /// the Windows game's strokes, gold and cyan, on a lapis medallion. The
    /// medallion is metal and so a fixed colour, like the tiles in Common/Art;
    /// it is what keeps gold and cyan legible on parchment as well as on
    /// lamplight.
    /// </summary>
    private sealed class GiftGlyph(BronzeGiftId id) : Control
    {
        private static readonly IBrush Medallion = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#4E6FA8"), 0), new GradientStop(Color.Parse("#22375E"), 1) }
        };

        private static readonly IPen Rim = new Pen(new SolidColorBrush(Color.Parse("#D2AE62")), 2);
        private static readonly IPen Gold = new Pen(new SolidColorBrush(Color.FromRgb(255, 207, 113)), 4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        private static readonly IPen Cyan = new Pen(new SolidColorBrush(Color.FromRgb(102, 240, 216)), 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        public override void Render(DrawingContext g)
        {
            var size = Math.Min(Bounds.Width, Bounds.Height);
            var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
            g.DrawEllipse(Medallion, Rim, centre, size / 2 - 1, size / 2 - 1);

            using var place = g.PushTransform(Matrix.CreateTranslation(centre.X - 24, centre.Y - 24));
            switch (id)
            {
                case BronzeGiftId.Athena:
                    Shape(g, Gold, true, (4, 4), (44, 4), (40, 30), (24, 46), (8, 30));
                    Shape(g, Cyan, false, (16, 28), (32, 12)); break;
                case BronzeGiftId.Hermes:
                    Shape(g, Gold, false, (18, 8), (18, 35), (40, 35), (40, 42), (8, 42));
                    for (var i = 0; i < 3; i++) Shape(g, Cyan, false, (18, 14 + i * 5), (2, 3 + i * 5)); break;
                case BronzeGiftId.Hephaestus:
                    Shape(g, Gold, true, (8, 4), (17, 9), (31, 9), (40, 4), (38, 44), (10, 44));
                    Shape(g, Cyan, false, (24, 13), (24, 37)); break;
                case BronzeGiftId.Apollo:
                    Arc(g, Gold, new Point(19, 1), new Point(19, 47), new Size(18, 23));
                    Shape(g, Cyan, false, (20, 2), (20, 46));
                    Shape(g, Cyan, false, (6, 24), (46, 24));
                    Shape(g, Cyan, false, (39, 17), (46, 24), (39, 31)); break;
                case BronzeGiftId.Poseidon:
                    Shape(g, Gold, false, (24, 2), (24, 46));
                    Shape(g, Cyan, false, (8, 3), (8, 23), (40, 23), (40, 3)); break;
                case BronzeGiftId.Hades:
                    Arc(g, Gold, new Point(6, 21), new Point(42, 21), new Size(18, 18));
                    Shape(g, Gold, false, (6, 21), (6, 42), (17, 42), (17, 28), (31, 28), (31, 42), (42, 42), (42, 21));
                    Shape(g, Cyan, false, (12, 20), (19, 20));
                    Shape(g, Cyan, false, (29, 20), (36, 20)); break;
            }
        }

        private static void Shape(DrawingContext g, IPen pen, bool closed, params (double X, double Y)[] points)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(points[0].X, points[0].Y), false);
                foreach (var (x, y) in points.Skip(1)) context.LineTo(new Point(x, y));
                context.EndFigure(closed);
            }

            g.DrawGeometry(null, pen, geometry);
        }

        /// <summary>Half an ellipse, clockwise from <paramref name="from"/> - GDI+'s 180-degree DrawArc.</summary>
        private static void Arc(DrawingContext g, IPen pen, Point from, Point to, Size radii)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(from, false);
                context.ArcTo(to, radii, 0, false, SweepDirection.Clockwise);
                context.EndFigure(false);
            }

            g.DrawGeometry(null, pen, geometry);
        }
    }
}
