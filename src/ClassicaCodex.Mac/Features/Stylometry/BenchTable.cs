using System.Collections;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace ClassicaCodex.Mac.Features.Stylometry;

/// <summary>
/// How a row of a bench table is marked: the ink its text is drawn in and
/// the tint behind it, as palette keys, or null for neither.
///
/// The Windows benches colour rows to carry a reading - dark red for a work
/// that was not recovered, grey for a control series, pink for a depth two
/// standard deviations out. The colours are the palette's here, so the marks
/// stay legible in both appearances, but they mark the same rows for the same
/// reasons.
/// </summary>
internal interface IMarkedRow
{
    string? InkKey { get; }

    string? ShadeKey { get; }
}

/// <summary>
/// The result tables of the stylometry windows: a DataGrid whose columns
/// draw text computed from the row rather than bound to a property, so a
/// column can be built for a settings profile discovered at run time, and
/// whose sorting goes by the number behind a cell rather than by its
/// rounded text - a column of "+0.012" and "-0.004" sorted as strings puts
/// the signs in order and the values nowhere.
///
/// The headers are drawn here too. Fluent keeps a fixed slot of 32 pixels
/// at the right of every header for a sort arrow, sorted or not, and these
/// tables have eleven columns of short numbers: with that slot, "% of
/// baseline" needs a column twice as wide as anything under it, and the
/// perturbation table could not be read without scrolling sideways. The
/// arrow is drawn only on the column that is sorted.
/// </summary>
internal static class BenchTable
{
    private static readonly ConditionalWeakTable<DataGridRow, IDisposable> Shades = new();
    private static ControlTheme? _headerTheme;

    /// <param name="framed">
    /// Whether the table draws its own border. A table set on a page - the
    /// reader-style pane with a header - leaves the frame to the page, or it
    /// is drawn twice.
    /// </param>
    public static DataGrid Create(bool framed = false)
    {
        var grid = new DataGrid
        {
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 28
        };

        if (!framed)
        {
            grid.BorderThickness = new Thickness(0);
            grid.CornerRadius = new CornerRadius(0);
        }

        if (HeaderTheme() is { } theme) grid.ColumnHeaderTheme = theme;

        grid.LoadingRow += (_, e) => Shade(e.Row);
        grid.UnloadingRow += (_, e) => Unshade(e.Row);
        return grid;
    }

    /// <summary>The width a header's own words need: its text, the padding, and room for the arrow.</summary>
    private static double HeaderWidth(string header) => header.Length * 6.6 + 30;

    /// <summary>
    /// One column. <paramref name="sortKey"/> is what a click on the header
    /// sorts by; without one the column sorts by its text.
    /// </summary>
    public static DataGridColumn Column<T>(
        string header,
        Func<T, string> text,
        Func<T, IComparable?>? sortKey = null,
        double width = 0,
        bool numeric = false,
        bool star = false,
        FontFamily? font = null,
        double starMinimum = 140) where T : class
    {
        // A column is never narrower than its header or than the width it was
        // given - the grid squeezes fixed columns to their minimum before it
        // scrolls, and a column of margins squeezed to "0...." says nothing.
        // A column that takes what is left over keeps room for a title.
        var minimum = star
            ? Math.Max(HeaderWidth(header), starMinimum)
            : Math.Max(HeaderWidth(header), width);

        return new DataGridTemplateColumn
        {
            Header = header,
            Width = star
                ? new DataGridLength(width <= 0 ? 1 : width, DataGridLengthUnitType.Star)
                : new DataGridLength(minimum),
            MinWidth = minimum,
            CellTemplate = new FuncDataTemplate<T>((row, _) => new CellText<T>(text, numeric, font, row)),
            CanUserSort = true,
            CustomSortComparer = new KeyComparer<T>(sortKey ?? (r => text(r)))
        };
    }

    /// <summary>
    /// The header theme: Fluent's own, with a template that sets the title
    /// and an arrow that appears only on the sorted column. Sorting and
    /// resizing are the header control's own and are untouched.
    /// </summary>
    private static ControlTheme? HeaderTheme()
    {
        if (_headerTheme != null) return _headerTheme;

        if (Application.Current?.TryFindResource(typeof(DataGridColumnHeader), out var found) != true
            || found is not ControlTheme fluent)
        {
            return null;
        }

        _headerTheme = new ControlTheme(typeof(DataGridColumnHeader))
        {
            BasedOn = fluent,
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty,
                    new FuncControlTemplate<DataGridColumnHeader>((header, _) => BuildHeader(header)))
            }
        };

        return _headerTheme;
    }

    private static Control BuildHeader(DataGridColumnHeader header)
    {
        var title = new ContentPresenter
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        title.Bind(ContentPresenter.ContentProperty, header.GetObservable(ContentControl.ContentProperty));

        var arrow = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L8,0 L4,5 Z"),
            Width = 8,
            Height = 5,
            Stretch = Stretch.Fill,
            Margin = new Thickness(5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center,
            IsVisible = false
        };
        arrow.Bind(Shape.FillProperty, header.GetObservable(TemplatedControl.ForegroundProperty));

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 0, 8, 0) };
        Grid.SetColumn(arrow, 1);
        layout.Children.Add(title);
        layout.Children.Add(arrow);

        var border = new Border { Child = layout, BorderThickness = new Thickness(0, 0, 1, 1) };
        border.Bind(Border.BackgroundProperty, header.GetObservable(TemplatedControl.BackgroundProperty));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Cc.Rule"));

        // The sort state is a pair of pseudo-classes on the header; the arrow
        // points up for ascending, as Finder's does.
        void ShowSort()
        {
            var ascending = header.Classes.Contains(":sortascending");
            var descending = header.Classes.Contains(":sortdescending");
            arrow.IsVisible = ascending || descending;
            arrow.RenderTransform = ascending ? new RotateTransform(180) : null;
        }

        header.Classes.CollectionChanged += (_, _) => ShowSort();
        ShowSort();
        return border;
    }

    /// <summary>
    /// A palette brush at a fraction of its strength, for a row tint that
    /// reads as a mark rather than as a selection.
    /// </summary>
    private static object? Soft(object? value) =>
        value is ISolidColorBrush brush ? new SolidColorBrush(brush.Color, 0.17) : value;

    private static void Shade(DataGridRow row)
    {
        Unshade(row);
        if (row.DataContext is IMarkedRow { ShadeKey: { } key })
            Shades.Add(row, row.Bind(DataGridRow.BackgroundProperty, row.GetResourceObservable(key, Soft)));
    }

    private static void Unshade(DataGridRow row)
    {
        if (Shades.TryGetValue(row, out var binding))
        {
            binding.Dispose();
            Shades.Remove(row);
        }

        row.ClearValue(DataGridRow.BackgroundProperty);
    }

    /// <summary>
    /// A cell's text, rebuilt whenever the row it sits on is handed a
    /// different item - the grid recycles rows as it scrolls, so a cell
    /// must not keep the item it was first built for.
    /// </summary>
    private sealed class CellText<T> : TextBlock where T : class
    {
        private readonly Func<T, string> _text;
        private IDisposable? _ink;

        public CellText(Func<T, string> text, bool numeric, FontFamily? font, T? initial)
        {
            _text = text;
            VerticalAlignment = VerticalAlignment.Center;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            TextAlignment = numeric ? TextAlignment.Right : TextAlignment.Left;
            TextTrimming = TextTrimming.CharacterEllipsis;
            Margin = new Thickness(10, 0);
            if (font != null) FontFamily = font;
            if (numeric) FontFeatures = new FontFeatureCollection { new FontFeature { Tag = "tnum" } };

            DataContextChanged += (_, _) => Show(DataContext as T);
            Show(initial);
        }

        protected override Type StyleKeyOverride => typeof(TextBlock);

        private void Show(T? row)
        {
            Text = row == null ? string.Empty : _text(row);

            _ink?.Dispose();
            _ink = null;
            ClearValue(ForegroundProperty);

            if (row is IMarkedRow { InkKey: { } key })
                _ink = this.Bind(ForegroundProperty, this.GetResourceObservable(key));

            // The whole text on hover where a narrow column may have cut it.
            ToolTip.SetTip(this, TextAlignment == TextAlignment.Left && Text is { Length: > 14 } ? Text : null);
        }
    }

    private sealed class KeyComparer<T> : IComparer where T : class
    {
        private readonly Func<T, IComparable?> _key;

        public KeyComparer(Func<T, IComparable?> key) => _key = key;

        public int Compare(object? x, object? y)
        {
            var a = x is T tx ? _key(tx) : null;
            var b = y is T ty ? _key(ty) : null;

            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            return a.CompareTo(b);
        }
    }
}
