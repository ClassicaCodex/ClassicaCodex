using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.MythNetwork;

/// <summary>
/// Gives each tag category its own node shape, so the myth network tells
/// gods from heroes from places at a glance - the Windows CategoryShapesForm.
///
/// The category list is read from the tags actually in use rather than
/// fixed, since categories are free text typed while tagging. Each choice
/// shows the node as the network will draw it, in that category's colour.
/// </summary>
internal static class CategoryShapesWindow
{
    /// <param name="shapesChanged">Called on every change, so the graph can repaint while this is open.</param>
    public static async Task ShowAsync(Window owner, Action shapesChanged)
    {
        var window = new Window
        {
            Title = "Category Shapes",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,180"), Margin = new Thickness(12, 8) };
        var status = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };

        var close = new Button { Content = "Close", Classes = { "accent" }, IsDefault = true, IsCancel = true, MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close();

        var layout = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 12 };
        layout.Children.Add(new TextBlock
        {
            Text = "Give each category its own shape - gods as circles, heroes as squares, places as triangles, or whatever suits.",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap
        });
        layout.Children.Add(new Border
        {
            Classes = { "panel" },
            Child = new ScrollViewer { Content = rows, MaxHeight = 340 }
        });
        layout.Children.Add(status);
        layout.Children.Add(close);
        window.Content = layout;

        window.Opened += async (_, _) => await LoadCategoriesAsync(rows, status, shapesChanged);

        await window.ShowDialog(owner);
    }

    private static async Task LoadCategoriesAsync(Grid rows, TextBlock status, Action shapesChanged)
    {
        List<string> categories;
        try
        {
            var tags = await new TagRepository().GetAllTagsAsync();
            categories = tags
                .Select(t => t.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            status.Text = $"Couldn't read the categories: {ex.Message}";
            return;
        }

        rows.Children.Clear();
        rows.RowDefinitions.Clear();

        if (categories.Count == 0)
        {
            status.Text = "No categories yet - add one when tagging (the “Category” box in Auto-Tag).";
            return;
        }

        for (var i = 0; i < categories.Count; i++)
        {
            var category = categories[i];
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBlock { Text = category, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

            var combo = new ComboBox
            {
                ItemsSource = Enum.GetValues<NodeShape>(),
                SelectedItem = CategoryShapes.For(category),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 3),
                // Null while a row is cleared for reuse, which an enum cannot hold.
                ItemTemplate = new FuncDataTemplate<NodeShape?>((shape, _) => shape is { } s ? ShapeLabel(s, category) : null)
            };

            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is NodeShape selected)
                {
                    CategoryShapes.Set(category, selected);
                    shapesChanged();
                }
            };

            Grid.SetRow(label, i);
            Grid.SetRow(combo, i);
            Grid.SetColumn(combo, 2);
            rows.Children.Add(label);
            rows.Children.Add(combo);
        }

        status.Text = $"{categories.Count} categor{(categories.Count == 1 ? "y" : "ies")}.";
    }

    private static Control ShapeLabel(NodeShape shape, string category)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new ShapeSwatch(shape, category) { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = shape.ToString(), VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    /// <summary>A node as the network draws it, small: the shape, in the category's colour.</summary>
    private sealed class ShapeSwatch : Control
    {
        private readonly NodeShape _shape;
        private readonly string _category;

        public ShapeSwatch(NodeShape shape, string category)
        {
            _shape = shape;
            _category = category;
            ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);

            // The polygons reach past the radius (a triangle's points, a
            // star's), so the radius is chosen for the widest of them.
            var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 / 1.45;

            var fill = new SolidColorBrush(GraphCanvas.CategoryColor(this, _category));
            var pen = new Pen(GraphCanvas.BrushOf(this, "Cc.InkMuted"), 1);
            context.DrawGeometry(fill, pen, GraphCanvas.ShapeGeometry(_shape, center, radius));
        }
    }
}
