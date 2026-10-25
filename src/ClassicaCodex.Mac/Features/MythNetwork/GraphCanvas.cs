using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.MythNetwork;

/// <summary>
/// Draws the tag co-occurrence network: a node per tag, sized by how often
/// the tag is used and shaped by its category, joined by lines weighted by
/// how strongly two tags co-occur. The layout is a small
/// Fruchterman-Reingold-style force simulation run once on load (repulsion
/// between all nodes, attraction along edges, a cooling schedule) - not a
/// canned diagram, an actual physics settle. Nodes can be dragged afterwards
/// to untangle anything that overlaps.
///
/// The Windows GraphCanvas, drawn with Avalonia's DrawingContext: the same
/// layout, the same shapes, the same click, drag and right-click. The colours
/// are the palette's, so the graph follows the appearance like everything
/// else.
/// </summary>
internal sealed class GraphCanvas : Control
{
    public string EmptyMessage { get; set; } = "No tags yet - right-click a line in the reader to add some, then reopen this.";

    private sealed class VisualNode
    {
        public int TagId;
        public string Name = string.Empty;
        public string? Category;
        public int UsageCount;
        public Point Position;
        public double Radius;
    }

    private sealed class VisualEdge
    {
        public VisualNode A = null!;
        public VisualNode B = null!;
        public int Weight;
    }

    private List<VisualNode> _nodes = new();
    private List<VisualEdge> _allEdges = new();
    private List<VisualEdge> _edges = new();
    private int _minSharedWorks = 1;

    /// <summary>The extent the positions were last laid out in, so a resize can carry them with it.</summary>
    private Size _layoutExtent;

    private VisualNode? _dragging;
    private Vector _dragOffset;
    private Point _dragStart;
    private bool _mouseMovedDuringDrag;
    private VisualNode? _hovered;
    private VisualEdge? _hoveredEdgeForTooltip;

    /// <summary>
    /// How far a press may wander before it is a drag rather than a click. A
    /// trackpad click rarely lands without moving a pixel, and on Windows any
    /// movement at all made it a drag - a click that did nothing.
    /// </summary>
    private const double DragThreshold = 3;

    /// <summary>Raised when a node is clicked (not dragged) - passes the tag name.</summary>
    public event Action<string>? NodeClicked;

    /// <summary>Raised when an edge is clicked - passes the two endpoint tag names.</summary>
    public event Action<string, string>? EdgeClicked;

    /// <summary>
    /// Raised on a right-click over a node - passes the tag name. Separate
    /// from NodeClicked, since a right-click is never the start of a drag the
    /// way a left-click can be.
    /// </summary>
    public event Action<string>? NodeRightClicked;

    public GraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;

        // Portraits are drawn well below their stored size, and the default
        // resampler makes that look gritty.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);

        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public int NodeCount => _nodes.Count;

    public int VisibleEdgeCount => _edges.Count;

    public int MaxEdgeWeight => _allEdges.Count == 0 ? 1 : _allEdges.Max(e => e.Weight);

    /// <summary>The names drawn, in drawing order; for tests.</summary>
    internal IReadOnlyList<string> NodeNames => _nodes.Select(n => n.Name).ToList();

    /// <summary>Raises the click a node would raise; for tests.</summary>
    internal void ClickNode(string name) => NodeClicked?.Invoke(name);

    /// <summary>Raises the click an edge would raise; for tests.</summary>
    internal void ClickEdge(string nameA, string nameB) => EdgeClicked?.Invoke(nameA, nameB);

    /// <summary>Where a node sits; for tests.</summary>
    internal Point? PositionOf(string name) => _nodes.FirstOrDefault(n => n.Name == name)?.Position;

    /// <summary>
    /// The size the physics works in: the canvas, but never less than 400 on
    /// a side, as on Windows - a cramped window gets a graph to scroll the
    /// eye across rather than a knot.
    /// </summary>
    private Size Extent => new(Math.Max(Bounds.Width, 400), Math.Max(Bounds.Height, 400));

    public void SetData(
        List<(int TagId, string Name, string? Category, int UsageCount)> nodes,
        List<(int TagId1, int TagId2, int SharedWorkCount)> edges)
    {
        var rng = new Random();
        var extent = Extent;

        _nodes = nodes.Select(n => new VisualNode
        {
            TagId = n.TagId,
            Name = n.Name,
            Category = n.Category,
            UsageCount = n.UsageCount,
            Position = new Point(rng.NextDouble() * extent.Width, rng.NextDouble() * extent.Height),
            Radius = 6 + Math.Min(n.UsageCount, 20) * 1.2
        }).ToList();

        var byId = _nodes.ToDictionary(n => n.TagId);
        _allEdges = edges
            .Where(e => byId.ContainsKey(e.TagId1) && byId.ContainsKey(e.TagId2))
            .Select(e => new VisualEdge { A = byId[e.TagId1], B = byId[e.TagId2], Weight = e.SharedWorkCount })
            .ToList();

        _hovered = null;
        _hoveredEdgeForTooltip = null;
        _dragging = null;

        ApplyEdgeThreshold();
        RunLayout();
        InvalidateVisual();
    }

    /// <summary>
    /// Only edges at or above this many shared works connect nodes - both on
    /// screen and in the layout physics. Raising it thins out the "everything
    /// touches everything" density that shows up with only a handful of tags,
    /// since almost any two figures share at least one work by chance; it is
    /// meant to reveal the stronger, more deliberate connections once there is
    /// enough tag data for that distinction to matter.
    /// </summary>
    public void SetMinSharedWorks(int minSharedWorks)
    {
        _minSharedWorks = Math.Max(minSharedWorks, 1);
        ApplyEdgeThreshold();
        RunLayout();
        InvalidateVisual();
    }

    private void ApplyEdgeThreshold()
    {
        _edges = _allEdges.Where(e => e.Weight >= _minSharedWorks).ToList();
        if (_hoveredEdgeForTooltip != null && !_edges.Contains(_hoveredEdgeForTooltip)) ClearEdgeTip();
    }

    /// <summary>Re-randomises positions and re-settles - handy if a layout has knotted up.</summary>
    public void Relayout()
    {
        var rng = new Random();
        var extent = Extent;
        foreach (var node in _nodes)
            node.Position = new Point(rng.NextDouble() * extent.Width, rng.NextDouble() * extent.Height);

        RunLayout();
        InvalidateVisual();
    }

    /// <summary>
    /// Classic force-directed layout: nodes repel each other (so they spread
    /// out), edges pull their endpoints together (so connected tags cluster),
    /// with a cooling schedule so it settles instead of oscillating forever.
    /// </summary>
    private void RunLayout()
    {
        var extent = Extent;
        _layoutExtent = extent;
        if (_nodes.Count == 0) return;

        var w = extent.Width;
        var h = extent.Height;
        var k = Math.Sqrt(w * h / Math.Max(_nodes.Count, 1)); // ideal spacing

        const int iterations = 300;
        var temperature = w / 10; // max displacement per step, cools over time

        var index = new Dictionary<VisualNode, int>(_nodes.Count);
        for (var i = 0; i < _nodes.Count; i++) index[_nodes[i]] = i;

        var dispX = new double[_nodes.Count];
        var dispY = new double[_nodes.Count];

        for (var iter = 0; iter < iterations; iter++)
        {
            Array.Clear(dispX);
            Array.Clear(dispY);

            // Repulsion: every node pushes every other node away.
            for (var i = 0; i < _nodes.Count; i++)
            {
                for (var j = i + 1; j < _nodes.Count; j++)
                {
                    var dx = _nodes[i].Position.X - _nodes[j].Position.X;
                    var dy = _nodes[i].Position.Y - _nodes[j].Position.Y;
                    var dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.01);
                    var force = k * k / dist;
                    var ux = dx / dist;
                    var uy = dy / dist;

                    dispX[i] += ux * force;
                    dispY[i] += uy * force;
                    dispX[j] -= ux * force;
                    dispY[j] -= uy * force;
                }
            }

            // Attraction: edges pull their two tags together, harder for tags
            // that share more works.
            foreach (var edge in _edges)
            {
                var a = index[edge.A];
                var b = index[edge.B];
                var dx = edge.A.Position.X - edge.B.Position.X;
                var dy = edge.A.Position.Y - edge.B.Position.Y;
                var dist = Math.Max(Math.Sqrt(dx * dx + dy * dy), 0.01);
                var force = dist * dist / k * (1 + edge.Weight * 0.15);
                var ux = dx / dist;
                var uy = dy / dist;

                dispX[a] -= ux * force;
                dispY[a] -= uy * force;
                dispX[b] += ux * force;
                dispY[b] += uy * force;
            }

            // Apply, capped by the current temperature, and keep on-canvas.
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == _dragging) continue; // don't fight the reader's own drag

                var length = Math.Max(Math.Sqrt(dispX[i] * dispX[i] + dispY[i] * dispY[i]), 0.01);
                var capped = Math.Min(length, temperature);

                var x = node.Position.X + dispX[i] / length * capped;
                var y = node.Position.Y + dispY[i] / length * capped;

                node.Position = new Point(
                    Math.Clamp(x, node.Radius, w - node.Radius),
                    Math.Clamp(y, node.Radius, h - node.Radius));
            }

            temperature *= 0.97; // cool down so it converges instead of jittering
        }
    }

    /// <summary>
    /// Carries the nodes with the canvas when the window is resized, so a
    /// graph laid out in a small window spreads into a large one and a
    /// shrinking window does not leave nodes stranded past its edge. Windows
    /// left them where they were; a Mac window is resized far more often.
    /// </summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        var extent = Extent;
        if (_nodes.Count == 0 || _layoutExtent.Width <= 0 || _layoutExtent.Height <= 0 || extent == _layoutExtent)
        {
            _layoutExtent = extent;
            return;
        }

        var scaleX = extent.Width / _layoutExtent.Width;
        var scaleY = extent.Height / _layoutExtent.Height;
        foreach (var node in _nodes)
        {
            node.Position = new Point(
                Math.Clamp(node.Position.X * scaleX, node.Radius, extent.Width - node.Radius),
                Math.Clamp(node.Position.Y * scaleY, node.Radius, extent.Height - node.Radius));
        }

        _layoutExtent = extent;
        InvalidateVisual();
    }

    /// <summary>A palette brush, as it stands in the appearance <paramref name="host"/> is drawn in.</summary>
    internal static IBrush BrushOf(Control host, string key) =>
        host.TryFindResource(key, host.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    private static Color ColorOf(Control host, string key) =>
        BrushOf(host, key) is ISolidColorBrush solid ? solid.Color : Colors.Gray;

    private IBrush Brush(string key) => BrushOf(this, key);

    /// <summary>The fill a node of this category draws in, for the canvas and the Shapes dialog alike.</summary>
    internal static Color CategoryColor(Control host, string? category) =>
        ColorForCategory(
            category,
            ColorOf(host, "Cc.Page"),
            new[] { ColorOf(host, "Cc.Verdigris"), ColorOf(host, "Cc.Lapis"), ColorOf(host, "Cc.Accent") },
            ColorOf(host, "Cc.InkFaint"));

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);

        // Transparent rather than nothing, so the whole canvas takes the
        // pointer and not just the strokes drawn on it.
        context.FillRectangle(Brushes.Transparent, bounds);

        var ink = Brush("Cc.Ink");
        var fontFamily = TextElement.GetFontFamily(this);

        if (_nodes.Count == 0)
        {
            var empty = new FormattedText(EmptyMessage, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(fontFamily, FontStyle.Italic), 13, Brush("Cc.InkMuted"))
            {
                MaxTextWidth = Math.Max(bounds.Width - 32, 100)
            };
            context.DrawText(empty, new Point(16, 16));
            return;
        }

        // Edges first, so nodes draw on top.
        var plainEdge = Brush("Cc.RuleStrong");
        var nodeEdge = Brush("Cc.Accent");
        var hoveredEdge = Brush("Cc.Verdigris");

        foreach (var edge in _edges)
        {
            var isNodeHighlighted = _hovered != null && (edge.A == _hovered || edge.B == _hovered);
            var isDirectlyHovered = edge == _hoveredEdgeForTooltip;
            var thickness = Math.Min(1 + edge.Weight, 6);

            var brush = isDirectlyHovered ? hoveredEdge : isNodeHighlighted ? nodeEdge : plainEdge;
            var pen = new Pen(brush, isDirectlyHovered ? thickness + 1 : thickness, lineCap: PenLineCap.Round);
            context.DrawLine(pen, edge.A.Position, edge.B.Position);
        }

        var outline = Brush("Cc.InkMuted");
        var hoverFill = Brush("Cc.Gold");
        var fills = new Dictionary<string, IBrush>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in _nodes)
        {
            var isHovered = node == _hovered;
            var key = node.Category?.Trim() ?? string.Empty;
            if (!fills.TryGetValue(key, out var categoryFill))
            {
                categoryFill = new SolidColorBrush(CategoryColor(this, node.Category));
                fills[key] = categoryFill;
            }

            var fill = isHovered ? hoverFill : categoryFill;
            var pen = new Pen(outline, isHovered ? 2 : 1);

            var shape = CategoryShapes.For(node.Category);
            context.DrawGeometry(fill, pen, ShapeGeometry(shape, node.Position, node.Radius));

            // A portrait inside the shape rather than instead of it. The shape
            // carries the category and the size carries how often the tag is
            // used; replacing the node with a picture would throw both away to
            // say something the label already says.
            DrawFigure(context, node, pen);

            var label = new FormattedText(node.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(fontFamily, FontStyle.Normal, isHovered ? FontWeight.Bold : FontWeight.Medium), 12, ink);

            // Below the node and centred on it, as on Windows - but kept on
            // the canvas. The layout parks nodes against the edges, where a
            // centred label ran off the side and one below a node at the
            // bottom vanished; this one moves in, or goes above.
            var below = node.Position.Y + node.Radius + 2;
            var top = below + label.Height <= bounds.Height ? below : node.Position.Y - node.Radius * 1.4 - 2 - label.Height;
            var left = Math.Clamp(node.Position.X - label.Width / 2, 2, Math.Max(2, bounds.Width - label.Width - 2));
            context.DrawText(label, new Point(left, top));
        }
    }

    /// <summary>
    /// Below this radius a portrait is a smudge rather than a face.
    ///
    /// Node radius runs from about 7 to 30, so this shows portraits on tags
    /// used a handful of times or more - the ones worth recognising at a
    /// glance, the rest being one-offs whose label is the only thing that
    /// identifies them anyway.
    /// </summary>
    private const double MinimumRadiusForFigure = 13;

    private static void DrawFigure(DrawingContext context, VisualNode node, IPen pen)
    {
        if (node.Radius < MinimumRadiusForFigure) return;

        var figure = FigureImages.For(node.Name);
        if (figure == null) return;

        // Inset so the category shape stays visible as a ring around the
        // portrait - at the same radius a round image would cover a circle
        // node entirely and clip the points off a star.
        var inset = node.Radius * 0.78;
        var box = new Rect(node.Position.X - inset, node.Position.Y - inset, inset * 2, inset * 2);
        var circle = new EllipseGeometry(box);

        using (context.PushGeometryClip(circle))
        {
            context.DrawImage(figure, new Rect(figure.Size), box);
        }

        context.DrawGeometry(null, pen, circle);
    }

    /// <summary>
    /// One node's outline in its assigned shape, sized so every shape covers
    /// roughly the area the circle of the same radius would - otherwise a
    /// square reads as noticeably bigger than a circle beside it and the
    /// size-means-usage signal gets muddied.
    /// </summary>
    internal static Geometry ShapeGeometry(NodeShape shape, Point center, double radius)
    {
        switch (shape)
        {
            case NodeShape.Square:
            {
                // A square of side r*sqrt(pi) matches a circle of radius r.
                var half = radius * 0.886;
                return new RectangleGeometry(new Rect(center.X - half, center.Y - half, half * 2, half * 2));
            }

            case NodeShape.Triangle:
                return Polygon(RegularPolygon(center, radius * 1.35, 3, startAngle: -90));

            case NodeShape.Diamond:
                return Polygon(RegularPolygon(center, radius * 1.25, 4, startAngle: -90));

            case NodeShape.Hexagon:
                return Polygon(RegularPolygon(center, radius * 1.05, 6, startAngle: -90));

            case NodeShape.Star:
                return Polygon(Star(center, radius * 1.4, radius * 0.6, 5));

            default:
                return new EllipseGeometry(new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
        }
    }

    private static Geometry Polygon(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], isFilled: true);
            for (var i = 1; i < points.Count; i++) g.LineTo(points[i]);
            g.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private static Point[] RegularPolygon(Point center, double radius, int sides, double startAngle)
    {
        var points = new Point[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = (startAngle + i * 360.0 / sides) * Math.PI / 180.0;
            points[i] = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        }

        return points;
    }

    private static Point[] Star(Point center, double outerRadius, double innerRadius, int points)
    {
        var result = new Point[points * 2];
        for (var i = 0; i < points * 2; i++)
        {
            var radius = i % 2 == 0 ? outerRadius : innerRadius;
            var angle = (-90 + i * 180.0 / points) * Math.PI / 180.0;
            result[i] = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
        }

        return result;
    }

    /// <summary>
    /// A colour per category, the same one every time for the same name.
    ///
    /// Windows takes a hue from the category's hash; here it is one of the
    /// palette's three accent metals - verdigris, lapis, bronze - at one of two
    /// strengths, laid over the page, so a graph in Lamplight is drawn in
    /// lamplight's colours rather than pastels meant for paper. Gold is kept
    /// for the node under the pointer, as on Windows. Six colours will repeat
    /// before categories run out; that is what the shapes are for.
    ///
    /// The hash is computed here rather than taken from string.GetHashCode,
    /// which .NET randomises per process: the Windows code means the same
    /// category to keep its colour across sessions, and with that hash it
    /// does not.
    /// </summary>
    internal static Color ColorForCategory(string? category, Color page, IReadOnlyList<Color> hues, Color neutral)
    {
        if (string.IsNullOrEmpty(category)) return Mix(page, neutral, 0.45);

        var hash = 0;
        foreach (var c in category.Trim().ToLowerInvariant()) hash = unchecked(hash * 31 + c);
        hash = Math.Abs(hash % (hues.Count * 2));

        return Mix(page, hues[hash % hues.Count], hash < hues.Count ? 0.55 : 0.85);
    }

    private static Color Mix(Color under, Color over, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromRgb(Channel(under.R, over.R), Channel(under.G, over.G), Channel(under.B, over.B));
    }

    private VisualNode? HitTest(Point p)
    {
        // Later-drawn nodes are on top, so check in reverse.
        for (var i = _nodes.Count - 1; i >= 0; i--)
        {
            var n = _nodes[i];
            var dx = p.X - n.Position.X;
            var dy = p.Y - n.Position.Y;
            if (dx * dx + dy * dy <= n.Radius * n.Radius) return n;
        }

        return null;
    }

    /// <summary>Nearest edge to a point, within a few pixels - a plain distance-to-segment test.</summary>
    private VisualEdge? HitTestEdge(Point p)
    {
        const double threshold = 6;
        VisualEdge? closest = null;
        var closestDistance = threshold;

        foreach (var edge in _edges)
        {
            var distance = DistancePointToSegment(p, edge.A.Position, edge.B.Position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = edge;
            }
        }

        return closest;
    }

    private static double DistancePointToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        var ddx = p.X - (a.X + t * dx);
        var ddy = p.Y - (a.Y + t * dy);
        return Math.Sqrt(ddx * ddx + ddy * ddy);
    }

    /// <summary>
    /// Holds a dragged node inside the canvas, and survives a canvas too small
    /// to hold it.
    ///
    /// On Windows this was once two Math.Clamp calls of the form (radius,
    /// Width - radius). Math.Clamp throws when its low bound exceeds its high
    /// one, so once the canvas was narrower than twice a node's radius -
    /// a heavily used tag draws at 30, so under about 60 of canvas - dragging
    /// that node threw. Where the node cannot fit, it sits in the middle of
    /// what there is, which is the only sensible answer and is not an
    /// exception.
    /// </summary>
    internal static Point KeepInside(Point desired, double radius, double width, double height)
    {
        return new Point(OnOneAxis(desired.X, radius, width), OnOneAxis(desired.Y, radius, height));

        static double OnOneAxis(double value, double radius, double extent)
        {
            if (extent <= radius * 2) return extent / 2;
            return Math.Clamp(value, radius, extent - radius);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        var hit = HitTest(point.Position);
        if (hit == null) return;

        // Control-click is a right-click on a Mac, and a trackpad may have no
        // other way to make one.
        var secondary = point.Properties.IsRightButtonPressed
                        || (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control) && OperatingSystem.IsMacOS());

        if (secondary)
        {
            e.Handled = true;
            NodeRightClicked?.Invoke(hit.Name);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;

        _dragging = hit;
        _dragOffset = point.Position - hit.Position;
        _dragStart = point.Position;
        _mouseMovedDuringDrag = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);

        if (_dragging != null)
        {
            if (!_mouseMovedDuringDrag)
            {
                var moved = position - _dragStart;
                if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold) return;
                _mouseMovedDuringDrag = true;
            }

            _dragging.Position = KeepInside(position - _dragOffset, _dragging.Radius, Bounds.Width, Bounds.Height);
            InvalidateVisual();
            return;
        }

        var hover = HitTest(position);
        if (hover != _hovered)
        {
            _hovered = hover;
            Cursor = hover != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            InvalidateVisual();
        }

        if (hover == null)
        {
            var edge = HitTestEdge(position);
            if (edge != _hoveredEdgeForTooltip)
            {
                _hoveredEdgeForTooltip = edge;
                if (edge != null)
                {
                    Cursor = new Cursor(StandardCursorType.Hand);
                    ToolTip.SetTip(this, $"{edge.A.Name} ↔ {edge.B.Name} ({edge.Weight} shared)");
                    ToolTip.SetIsOpen(this, true);
                }
                else
                {
                    Cursor = Cursor.Default;
                    ClearEdgeTip();
                }

                InvalidateVisual();
            }
        }
        else if (_hoveredEdgeForTooltip != null)
        {
            ClearEdgeTip();
            InvalidateVisual();
        }
    }

    private void ClearEdgeTip()
    {
        _hoveredEdgeForTooltip = null;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragging != null)
        {
            var clicked = !_mouseMovedDuringDrag ? _dragging : null;
            _dragging = null;
            e.Pointer.Capture(null);
            if (clicked != null) NodeClicked?.Invoke(clicked.Name);
            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Left) return;

        // No node was involved in this click - see if an edge was.
        var edgeHit = HitTestEdge(e.GetPosition(this));
        if (edgeHit != null) EdgeClicked?.Invoke(edgeHit.A.Name, edgeHit.B.Name);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = null;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragging != null) return;

        if (_hovered != null || _hoveredEdgeForTooltip != null)
        {
            _hovered = null;
            ClearEdgeTip();
            Cursor = Cursor.Default;
            InvalidateVisual();
        }
    }
}
