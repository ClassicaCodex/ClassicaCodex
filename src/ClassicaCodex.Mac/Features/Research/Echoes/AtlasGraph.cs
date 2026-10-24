using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ClassicaCodex.Mac.Features.Research.Echoes;

/// <summary>
/// The atlas's network: works (and, in the motif view, motifs) as nodes sized by
/// how many records touch them, joined by lines weighted by how many passage pairs
/// they share.
///
/// Laid out by the same small Fruchterman-Reingold settle the Windows graph uses -
/// repulsion between every pair, attraction along edges, a cooling schedule - run
/// once when the data arrives. Nodes can be dragged afterwards to untangle anything
/// that overlaps; a click on a node or a line asks the window for the records
/// behind it, because every visual edge must drill back to its evidence.
/// </summary>
internal sealed class AtlasGraph : Control
{
    private sealed class Node
    {
        public int Id;
        public string Name = string.Empty;
        public string? Category;
        public double Radius;
        public Point Position;
    }

    private sealed class Edge
    {
        public Node A = null!;
        public Node B = null!;
        public int Weight;
    }

    private List<Node> _nodes = new();
    private List<Edge> _edges = new();
    private Size _laidOutFor;
    private Node? _dragging;
    private Vector _dragOffset;
    private bool _moved;
    private Node? _hovered;
    private Edge? _hoveredEdge;

    public AtlasGraph()
    {
        ClipToBounds = true;
        Focusable = true;

        // Drawn from the palette, so a change of appearance has to redraw it.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public string EmptyMessage { get; set; } = "No connections match these atlas filters.";

    /// <summary>A node was clicked rather than dragged: its label.</summary>
    public event Action<string>? NodeClicked;

    /// <summary>A line was clicked: the labels at its two ends.</summary>
    public event Action<string, string>? EdgeClicked;

    internal int NodeCount => _nodes.Count;
    internal int EdgeCount => _edges.Count;

    public void SetData(
        IReadOnlyList<(int Id, string Name, string? Category, int Usage)> nodes,
        IReadOnlyList<(int A, int B, int Weight)> edges)
    {
        var size = LayoutSize();
        var random = new Random(17);
        _nodes = nodes.Select(n => new Node
        {
            Id = n.Id,
            Name = n.Name,
            Category = n.Category,
            Radius = 6 + Math.Min(n.Usage, 20) * 1.2,
            Position = new Point(random.NextDouble() * size.Width, random.NextDouble() * size.Height)
        }).ToList();

        var byId = _nodes.ToDictionary(n => n.Id);
        _edges = edges.Where(e => byId.ContainsKey(e.A) && byId.ContainsKey(e.B))
            .Select(e => new Edge { A = byId[e.A], B = byId[e.B], Weight = e.Weight })
            .ToList();
        _hovered = null;
        _hoveredEdge = null;
        RunLayout(size);
        InvalidateVisual();
    }

    /// <summary>Scatters the nodes and settles them again - for a layout that has knotted up.</summary>
    public void Relayout()
    {
        var size = LayoutSize();
        var random = new Random();
        foreach (var node in _nodes)
            node.Position = new Point(random.NextDouble() * size.Width, random.NextDouble() * size.Height);
        RunLayout(size);
        InvalidateVisual();
    }

    private Size LayoutSize() => new(Math.Max(Bounds.Width, 400), Math.Max(Bounds.Height, 400));

    /// <summary>
    /// Classic force-directed layout: nodes repel each other (so they spread
    /// out), edges pull their endpoints together (so connected works cluster),
    /// with a cooling schedule so it settles instead of oscillating forever.
    /// </summary>
    private void RunLayout(Size size)
    {
        _laidOutFor = size;
        if (_nodes.Count == 0) return;

        var w = size.Width;
        var h = size.Height;
        var k = Math.Sqrt(w * h / Math.Max(_nodes.Count, 1));
        var temperature = w / 10;
        var displacement = new Vector[_nodes.Count];
        var index = new Dictionary<Node, int>();
        for (var i = 0; i < _nodes.Count; i++) index[_nodes[i]] = i;

        for (var iteration = 0; iteration < 300; iteration++)
        {
            Array.Clear(displacement);

            for (var i = 0; i < _nodes.Count; i++)
            {
                for (var j = i + 1; j < _nodes.Count; j++)
                {
                    var delta = _nodes[i].Position - _nodes[j].Position;
                    var distance = Math.Max(Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y), 0.01);
                    var push = delta / distance * (k * k / distance);
                    displacement[i] += push;
                    displacement[j] -= push;
                }
            }

            foreach (var edge in _edges)
            {
                var delta = edge.A.Position - edge.B.Position;
                var distance = Math.Max(Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y), 0.01);
                var pull = delta / distance * (distance * distance / k * (1 + edge.Weight * 0.15));
                displacement[index[edge.A]] -= pull;
                displacement[index[edge.B]] += pull;
            }

            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == _dragging) continue;
                var d = displacement[i];
                var length = Math.Max(Math.Sqrt(d.X * d.X + d.Y * d.Y), 0.01);
                var step = Math.Min(length, temperature);
                var next = node.Position + d / length * step;
                node.Position = KeepInside(next, node.Radius + 14, w, h);
            }

            temperature *= 0.97;
        }
    }

    /// <summary>
    /// Holds a node inside the canvas, and survives a canvas too small to hold it:
    /// where the node cannot fit it sits in the middle of what there is, rather than
    /// asking Math.Clamp for a range whose low end is above its high end.
    /// </summary>
    internal static Point KeepInside(Point desired, double radius, double width, double height)
    {
        static double Axis(double value, double radius, double extent) =>
            extent <= radius * 2 ? extent / 2 : Math.Clamp(value, radius, extent - radius);

        return new Point(Axis(desired.X, radius, width), Axis(desired.Y, radius, height));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // Laid out before the window had its size, or resized since: stretch the
        // settled positions to the new canvas rather than settling again, so a
        // node the reader has dragged stays where they put it relative to the rest.
        if (_nodes.Count == 0 || _laidOutFor.Width <= 0 || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        var sx = Math.Max(e.NewSize.Width, 1) / _laidOutFor.Width;
        var sy = Math.Max(e.NewSize.Height, 1) / _laidOutFor.Height;
        foreach (var node in _nodes)
            node.Position = KeepInside(new Point(node.Position.X * sx, node.Position.Y * sy), node.Radius + 14, e.NewSize.Width, e.NewSize.Height);
        _laidOutFor = e.NewSize;
        InvalidateVisual();
    }

    // The palette always has these; the fallback only keeps a missing key from
    // drawing nothing at all.
    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    public override void Render(DrawingContext context)
    {
        var page = Brush("Cc.Page");
        var ink = Brush("Cc.Ink");
        var muted = Brush("Cc.InkMuted");
        var rule = Brush("Cc.RuleStrong");
        var accent = Brush("Cc.Accent");
        var verdigris = Brush("Cc.Verdigris");
        var lapis = Brush("Cc.Lapis");
        var gold = Brush("Cc.Gold");

        context.FillRectangle(page, new Rect(Bounds.Size));

        if (_nodes.Count == 0)
        {
            var empty = Text(EmptyMessage, muted, 13, FontStyle.Italic);
            empty.MaxTextWidth = Math.Max(100, Bounds.Width - 32);
            context.DrawText(empty, new Point(16, 16));
            return;
        }

        foreach (var edge in _edges)
        {
            var nodeHighlighted = _hovered != null && (edge.A == _hovered || edge.B == _hovered);
            var direct = edge == _hoveredEdge;
            var thickness = Math.Min(1 + edge.Weight, 6) + (direct ? 1 : 0);
            var brush = direct ? verdigris : nodeHighlighted ? accent : rule;
            context.DrawLine(new Pen(brush, thickness) { LineCap = PenLineCap.Round }, edge.A.Position, edge.B.Position);
        }

        foreach (var node in _nodes)
        {
            var hovered = node == _hovered;
            var fill = hovered ? gold : node.Category == "motif" ? verdigris : lapis;
            var outline = new Pen(hovered ? accent : page, hovered ? 2.5 : 1.5);
            if (node.Category == "motif")
            {
                // A diamond for a motif, a circle for a work, at about the same area.
                var r = node.Radius * 1.25;
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(node.Position.X, node.Position.Y - r), true);
                    g.LineTo(new Point(node.Position.X + r, node.Position.Y));
                    g.LineTo(new Point(node.Position.X, node.Position.Y + r));
                    g.LineTo(new Point(node.Position.X - r, node.Position.Y));
                    g.EndFigure(true);
                }

                context.DrawGeometry(fill, outline, geometry);
            }
            else
            {
                context.DrawEllipse(fill, outline, node.Position, node.Radius, node.Radius);
            }

            // On a wash of the page, so a line running under a label does not
            // strike through the name.
            var label = Text(node.Name, ink, 11.5, FontStyle.Normal, hovered ? FontWeight.SemiBold : FontWeight.Normal);
            var at = new Point(node.Position.X - label.Width / 2, node.Position.Y + node.Radius + 3);
            using (context.PushOpacity(0.85))
                context.FillRectangle(page, new Rect(at.X - 3, at.Y - 1, label.Width + 6, label.Height + 2), 3);
            context.DrawText(label, at);
        }
    }

    private static FormattedText Text(string text, IBrush brush, double size, FontStyle style, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, style, weight), size, brush);

    private Node? HitNode(Point p)
    {
        for (var i = _nodes.Count - 1; i >= 0; i--)
        {
            var n = _nodes[i];
            var d = p - n.Position;
            var r = n.Category == "motif" ? n.Radius * 1.25 : n.Radius;
            if (d.X * d.X + d.Y * d.Y <= r * r) return n;
        }

        return null;
    }

    /// <summary>Nearest line to a point, within a few pixels - a plain distance-to-segment test.</summary>
    private Edge? HitEdge(Point p)
    {
        const double threshold = 6;
        Edge? closest = null;
        var best = threshold;
        foreach (var edge in _edges)
        {
            var distance = DistanceToSegment(p, edge.A.Position, edge.B.Position);
            if (distance < best)
            {
                best = distance;
                closest = edge;
            }
        }

        return closest;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
        if (lengthSquared < 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        var t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSquared, 0, 1);
        var projection = a + ab * t;
        var d = p - projection;
        return Math.Sqrt(d.X * d.X + d.Y * d.Y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        var hit = HitNode(point);
        if (hit == null) return;

        _dragging = hit;
        _dragOffset = point - hit.Position;
        _moved = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_dragging != null)
        {
            _moved = true;
            _dragging.Position = KeepInside(point - _dragOffset, _dragging.Radius, Bounds.Width, Bounds.Height);
            InvalidateVisual();
            return;
        }

        var node = HitNode(point);
        var edge = node == null ? HitEdge(point) : null;
        if (node == _hovered && edge == _hoveredEdge) return;

        _hovered = node;
        _hoveredEdge = edge;
        Cursor = node != null || edge != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        ToolTip.SetTip(this, edge != null ? $"{edge.A.Name} ↔ {edge.B.Name} ({edge.Weight} shared)" : node?.Name);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging != null)
        {
            var clicked = _dragging;
            _dragging = null;
            e.Pointer.Capture(null);
            if (!_moved) NodeClicked?.Invoke(clicked.Name);
            return;
        }

        if (HitEdge(e.GetPosition(this)) is { } edge) EdgeClicked?.Invoke(edge.A.Name, edge.B.Name);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hovered == null && _hoveredEdge == null) return;
        _hovered = null;
        _hoveredEdge = null;
        InvalidateVisual();
    }

    /// <summary>For the tests: clicks a node by its label, as a pointer would.</summary>
    internal void Click(string label) => NodeClicked?.Invoke(label);
}
