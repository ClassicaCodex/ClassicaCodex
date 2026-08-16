namespace ClassicaCodex.UI;

/// <summary>
/// Draws the tag co-occurrence network: circles sized by how often a tag is
/// used, connected by lines weighted by how many works two tags share.
/// Layout comes from a small Fruchterman-Reingold-style force simulation run
/// once on load (repulsion between all nodes, attraction along edges,
/// cooling schedule) - not a canned diagram, an actual physics settle.
/// Nodes can be dragged afterward to untangle anything that overlaps.
/// </summary>
public class GraphCanvas : Panel
{
    public string EmptyMessage { get; set; } = "No tags yet - right-click a line in the reader to add some, then reopen this.";
    private class VisualNode
    {
        public int TagId;
        public string Name = string.Empty;
        public string? Category;
        public int UsageCount;
        public PointF Position;
        public float Radius;
    }

    private class VisualEdge
    {
        public VisualNode A = null!;
        public VisualNode B = null!;
        public int Weight;
    }

    private List<VisualNode> _nodes = new();
    private List<VisualEdge> _allEdges = new();
    private List<VisualEdge> _edges = new();
    private int _minSharedWorks = 1;

    private VisualNode? _dragging;
    private PointF _dragOffset;
    private bool _mouseMovedDuringDrag;
    private VisualNode? _hovered;
    private VisualEdge? _hoveredEdgeForTooltip;
    private readonly ToolTip _toolTip = new();

    /// <summary>Raised when a node is clicked (not dragged) - passes the tag name.</summary>
    public event Action<string>? NodeClicked;

    /// <summary>Raised when an edge is clicked - passes the two endpoint tag names.</summary>
    public event Action<string, string>? EdgeClicked;

    /// <summary>Raised on right-click over a node - passes the tag name. Separate from NodeClicked since a right-click is never the start of a drag the way a left-click can be.</summary>
    public event Action<string>? NodeRightClicked;

    public GraphCanvas()
    {
        DoubleBuffered = true;
        BackColor = ReadingTheme.Surface;
        MouseDown += GraphCanvas_MouseDown;
        MouseMove += GraphCanvas_MouseMove;
        MouseUp += GraphCanvas_MouseUp;
    }

    public void SetData(
        List<(int TagId, string Name, string? Category, int UsageCount)> nodes,
        List<(int TagId1, int TagId2, int SharedWorkCount)> edges)
    {
        var rng = new Random();
        var w = Math.Max(Width, 400);
        var h = Math.Max(Height, 400);

        _nodes = nodes.Select(n => new VisualNode
        {
            TagId = n.TagId,
            Name = n.Name,
            Category = n.Category,
            UsageCount = n.UsageCount,
            Position = new PointF((float)(rng.NextDouble() * w), (float)(rng.NextDouble() * h)),
            Radius = 6f + Math.Min(n.UsageCount, 20) * 1.2f
        }).ToList();

        var byId = _nodes.ToDictionary(n => n.TagId);
        _allEdges = edges
            .Where(e => byId.ContainsKey(e.TagId1) && byId.ContainsKey(e.TagId2))
            .Select(e => new VisualEdge { A = byId[e.TagId1], B = byId[e.TagId2], Weight = e.SharedWorkCount })
            .ToList();

        ApplyEdgeThreshold();
        RunLayout();
        Invalidate();
    }

    /// <summary>
    /// Only edges at or above this many shared works actually connect nodes
    /// - both visually and in the layout physics. Raising this thins out the
    /// "everything touches everything" density that shows up with only a
    /// handful of tags loaded, since almost any two figures share at least
    /// one work by chance; it's meant to reveal the stronger, more
    /// deliberate connections once there's enough tag data for that
    /// distinction to matter.
    /// </summary>
    public void SetMinSharedWorks(int minSharedWorks)
    {
        _minSharedWorks = Math.Max(minSharedWorks, 1);
        ApplyEdgeThreshold();
        RunLayout();
        Invalidate();
    }

    public int MaxEdgeWeight => _allEdges.Count == 0 ? 1 : _allEdges.Max(e => e.Weight);

    private void ApplyEdgeThreshold()
    {
        _edges = _allEdges.Where(e => e.Weight >= _minSharedWorks).ToList();
    }

    /// <summary>Re-randomizes positions and re-settles - handy if a layout has knotted up.</summary>
    public void Relayout()
    {
        var rng = new Random();
        var w = Math.Max(Width, 400);
        var h = Math.Max(Height, 400);
        foreach (var node in _nodes)
        {
            node.Position = new PointF((float)(rng.NextDouble() * w), (float)(rng.NextDouble() * h));
        }
        RunLayout();
        Invalidate();
    }

    /// <summary>
    /// Classic force-directed layout: nodes repel each other (so they spread
    /// out), edges pull their endpoints together (so connected tags cluster),
    /// with a cooling schedule so it settles instead of oscillating forever.
    /// </summary>
    private void RunLayout()
    {
        if (_nodes.Count == 0) return;

        var w = Math.Max(Width, 400);
        var h = Math.Max(Height, 400);
        var area = (float)w * h;
        var k = (float)Math.Sqrt(area / Math.Max(_nodes.Count, 1)); // ideal spacing

        const int iterations = 300;
        var temperature = w / 10f; // max displacement per step, cools over time

        for (var iter = 0; iter < iterations; iter++)
        {
            var displacement = _nodes.ToDictionary(n => n, _ => new PointF(0, 0));

            // Repulsion: every node pushes every other node away
            for (var i = 0; i < _nodes.Count; i++)
            {
                for (var j = i + 1; j < _nodes.Count; j++)
                {
                    var a = _nodes[i];
                    var b = _nodes[j];
                    var dx = a.Position.X - b.Position.X;
                    var dy = a.Position.Y - b.Position.Y;
                    var dist = Math.Max((float)Math.Sqrt(dx * dx + dy * dy), 0.01f);
                    var force = (k * k) / dist;
                    var ux = dx / dist;
                    var uy = dy / dist;

                    displacement[a] = new PointF(displacement[a].X + ux * force, displacement[a].Y + uy * force);
                    displacement[b] = new PointF(displacement[b].X - ux * force, displacement[b].Y - uy * force);
                }
            }

            // Attraction: edges pull their two tags together, harder for
            // tags that share more works
            foreach (var edge in _edges)
            {
                var dx = edge.A.Position.X - edge.B.Position.X;
                var dy = edge.A.Position.Y - edge.B.Position.Y;
                var dist = Math.Max((float)Math.Sqrt(dx * dx + dy * dy), 0.01f);
                var force = (dist * dist) / k * (1f + edge.Weight * 0.15f);
                var ux = dx / dist;
                var uy = dy / dist;

                displacement[edge.A] = new PointF(displacement[edge.A].X - ux * force, displacement[edge.A].Y - uy * force);
                displacement[edge.B] = new PointF(displacement[edge.B].X + ux * force, displacement[edge.B].Y + uy * force);
            }

            // Apply, capped by the current temperature, and keep on-canvas
            foreach (var node in _nodes)
            {
                if (node == _dragging) continue; // don't fight the user's own drag

                var disp = displacement[node];
                var dLen = Math.Max((float)Math.Sqrt(disp.X * disp.X + disp.Y * disp.Y), 0.01f);
                var capped = Math.Min(dLen, temperature);

                var newX = node.Position.X + disp.X / dLen * capped;
                var newY = node.Position.Y + disp.Y / dLen * capped;

                node.Position = new PointF(
                    Math.Clamp(newX, node.Radius, w - node.Radius),
                    Math.Clamp(newY, node.Radius, h - node.Radius));
            }

            temperature *= 0.97f; // cool down so it converges instead of jittering
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Portraits are drawn well below their stored size, and the default
        // resampler makes that look gritty.
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        if (_nodes.Count == 0)
        {
            using var emptyFont = new Font(Font, FontStyle.Italic);
            using var emptyBrush = new SolidBrush(ReadingTheme.MutedText);
            e.Graphics.DrawString(
                EmptyMessage,
                emptyFont, emptyBrush, new PointF(16, 16));
            return;
        }

        // Edges first so nodes draw on top
        foreach (var edge in _edges)
        {
            var isNodeHighlighted = _hovered != null && (edge.A == _hovered || edge.B == _hovered);
            var isDirectlyHovered = edge == _hoveredEdgeForTooltip;
            var thickness = Math.Min(1 + edge.Weight, 6);

            var color = isDirectlyHovered ? Color.MediumSeaGreen
                : isNodeHighlighted ? Color.DarkOrange
                : ReadingTheme.IsDark ? Color.FromArgb(92, 92, 100) : Color.LightGray;

            using var pen = new Pen(color, isDirectlyHovered ? thickness + 1 : thickness);
            e.Graphics.DrawLine(pen, edge.A.Position, edge.B.Position);
        }

        foreach (var node in _nodes)
        {
            var isHovered = node == _hovered;
            var fillColor = ColorForCategory(node.Category, isHovered);

            using var brush = new SolidBrush(fillColor);
            using var pen = new Pen(Color.DimGray, isHovered ? 2 : 1);

            var shape = CategoryShapes.For(node.Category);
            DrawNodeShape(e.Graphics, brush, pen, shape, node.Position, node.Radius);

            // A portrait inside the shape rather than instead of it. The
            // shape carries the category and the size carries how often the
            // tag is used; replacing the node with a picture would throw
            // both away to say something the label already says.
            DrawFigure(e.Graphics, node, pen);

            var labelFont = isHovered ? new Font(Font, FontStyle.Bold) : Font;
            var labelSize = e.Graphics.MeasureString(node.Name, labelFont);
            using var labelBrush = new SolidBrush(ReadingTheme.Text);
            e.Graphics.DrawString(node.Name, labelFont, labelBrush,
                node.Position.X - labelSize.Width / 2, node.Position.Y + node.Radius + 2);
        }
    }

    /// <summary>
    /// Below this radius a portrait is a smudge rather than a face.
    ///
    /// Node radius runs from about 7 to 30, so this shows portraits on tags
    /// used a handful of times or more - which are the ones worth
    /// recognising at a glance, the rest being one-offs whose label is the
    /// only thing that identifies them anyway.
    /// </summary>
    private const float MinimumRadiusForFigure = 13f;

    private static void DrawFigure(Graphics graphics, VisualNode node, Pen pen)
    {
        if (node.Radius < MinimumRadiusForFigure) return;

        var figure = FigureImages.For(node.Name);
        if (figure == null) return;

        // Inset so the category shape stays visible as a ring around the
        // portrait - at the same radius a circular image would cover a
        // circle node entirely and clip the points off a star.
        var inset = node.Radius * 0.78f;
        var box = new RectangleF(
            node.Position.X - inset, node.Position.Y - inset, inset * 2, inset * 2);

        var clipped = graphics.Clip;
        using var circle = new System.Drawing.Drawing2D.GraphicsPath();
        circle.AddEllipse(box);

        graphics.SetClip(circle);
        graphics.DrawImage(figure, box);
        graphics.Clip = clipped;

        graphics.DrawEllipse(pen, box);
    }

    /// <summary>
    /// Draws one node in its assigned shape, sized so every shape occupies
    /// roughly the same visual area as the circle of the same radius would -
    /// otherwise a square reads as noticeably bigger than a circle beside it
    /// and the size-means-usage-count signal gets muddied.
    /// </summary>
    private static void DrawNodeShape(
        Graphics graphics, Brush brush, Pen pen, NodeShape shape, PointF center, float radius)
    {
        switch (shape)
        {
            case NodeShape.Square:
            {
                // A square of side r*sqrt(pi) matches a circle of radius r.
                var half = radius * 0.886f;
                var rect = new RectangleF(center.X - half, center.Y - half, half * 2, half * 2);
                graphics.FillRectangle(brush, rect);
                graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                break;
            }

            case NodeShape.Triangle:
            {
                var points = RegularPolygon(center, radius * 1.35f, 3, startAngle: -90);
                graphics.FillPolygon(brush, points);
                graphics.DrawPolygon(pen, points);
                break;
            }

            case NodeShape.Diamond:
            {
                var points = RegularPolygon(center, radius * 1.25f, 4, startAngle: -90);
                graphics.FillPolygon(brush, points);
                graphics.DrawPolygon(pen, points);
                break;
            }

            case NodeShape.Hexagon:
            {
                var points = RegularPolygon(center, radius * 1.05f, 6, startAngle: -90);
                graphics.FillPolygon(brush, points);
                graphics.DrawPolygon(pen, points);
                break;
            }

            case NodeShape.Star:
            {
                var points = Star(center, radius * 1.4f, radius * 0.6f, 5);
                graphics.FillPolygon(brush, points);
                graphics.DrawPolygon(pen, points);
                break;
            }

            default:
            {
                var rect = new RectangleF(
                    center.X - radius, center.Y - radius, radius * 2, radius * 2);
                graphics.FillEllipse(brush, rect);
                graphics.DrawEllipse(pen, rect);
                break;
            }
        }
    }

    private static PointF[] RegularPolygon(PointF center, float radius, int sides, float startAngle)
    {
        var points = new PointF[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = (startAngle + i * 360f / sides) * Math.PI / 180.0;
            points[i] = new PointF(
                center.X + (float)(radius * Math.Cos(angle)),
                center.Y + (float)(radius * Math.Sin(angle)));
        }
        return points;
    }

    private static PointF[] Star(PointF center, float outerRadius, float innerRadius, int points)
    {
        var result = new PointF[points * 2];
        for (var i = 0; i < points * 2; i++)
        {
            var radius = i % 2 == 0 ? outerRadius : innerRadius;
            var angle = (-90 + i * 180f / points) * Math.PI / 180.0;
            result[i] = new PointF(
                center.X + (float)(radius * Math.Cos(angle)),
                center.Y + (float)(radius * Math.Sin(angle)));
        }
        return result;
    }

    private static Color ColorForCategory(string? category, bool isHovered)
    {
        if (isHovered) return Color.Gold;
        if (string.IsNullOrEmpty(category)) return Color.LightSteelBlue;

        // Stable color per category name, so the same category always
        // renders the same hue across sessions.
        var hash = category.GetHashCode();
        var hue = (float)(Math.Abs(hash) % 360);
        return ColorFromHsl(hue, 0.55f, 0.72f);
    }

    private static Color ColorFromHsl(float hue, float saturation, float lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs((hue / 60f) % 2 - 1));
        var m = lightness - c / 2;

        (float r, float g, float b) = hue switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x)
        };

        return Color.FromArgb(
            (int)((r + m) * 255),
            (int)((g + m) * 255),
            (int)((b + m) * 255));
    }

    private VisualNode? HitTest(Point p)
    {
        // Later-drawn (later in list) nodes are visually on top, so check in reverse.
        for (var i = _nodes.Count - 1; i >= 0; i--)
        {
            var n = _nodes[i];
            var dx = p.X - n.Position.X;
            var dy = p.Y - n.Position.Y;
            if (dx * dx + dy * dy <= n.Radius * n.Radius) return n;
        }
        return null;
    }

    /// <summary>Nearest edge to a point, within a few pixels - a plain distance-to-line-segment test.</summary>
    private VisualEdge? HitTestEdge(Point p)
    {
        const float threshold = 6f;
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

    private static float DistancePointToSegment(PointF p, PointF a, PointF b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 0.0001f)
        {
            return (float)Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        }

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0f, 1f);

        var projX = a.X + t * dx;
        var projY = a.Y + t * dy;
        var ddx = p.X - projX;
        var ddy = p.Y - projY;

        return (float)Math.Sqrt(ddx * ddx + ddy * ddy);
    }

    private void GraphCanvas_MouseDown(object? sender, MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit == null) return;

        if (e.Button == MouseButtons.Right)
        {
            NodeRightClicked?.Invoke(hit.Name);
            return;
        }

        _dragging = hit;
        _dragOffset = new PointF(e.Location.X - hit.Position.X, e.Location.Y - hit.Position.Y);
        _mouseMovedDuringDrag = false;
    }

    private void GraphCanvas_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging != null)
        {
            _mouseMovedDuringDrag = true;
            _dragging.Position = new PointF(
                Math.Clamp(e.Location.X - _dragOffset.X, _dragging.Radius, Width - _dragging.Radius),
                Math.Clamp(e.Location.Y - _dragOffset.Y, _dragging.Radius, Height - _dragging.Radius));
            Invalidate();
            return;
        }

        var hover = HitTest(e.Location);
        if (hover != _hovered)
        {
            _hovered = hover;
            Cursor = hover != null ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        if (hover == null)
        {
            var hoveredEdge = HitTestEdge(e.Location);
            if (hoveredEdge != _hoveredEdgeForTooltip)
            {
                _hoveredEdgeForTooltip = hoveredEdge;
                if (hoveredEdge != null)
                {
                    Cursor = Cursors.Hand;
                    _toolTip.SetToolTip(this,
                        $"{hoveredEdge.A.Name} \u2194 {hoveredEdge.B.Name} ({hoveredEdge.Weight} shared)");
                }
                else
                {
                    Cursor = Cursors.Default;
                    _toolTip.SetToolTip(this, string.Empty);
                }
                Invalidate();
            }
        }
        else if (_hoveredEdgeForTooltip != null)
        {
            _hoveredEdgeForTooltip = null;
            _toolTip.SetToolTip(this, string.Empty);
            Invalidate();
        }
    }

    private void GraphCanvas_MouseUp(object? sender, MouseEventArgs e)
    {
        if (_dragging != null)
        {
            if (!_mouseMovedDuringDrag)
            {
                NodeClicked?.Invoke(_dragging.Name);
            }
            _dragging = null;
            return;
        }

        // No node was involved in this click - see if an edge was clicked instead.
        var edgeHit = HitTestEdge(e.Location);
        if (edgeHit != null)
        {
            EdgeClicked?.Invoke(edgeHit.A.Name, edgeHit.B.Name);
        }
    }
}
