namespace ClassicaCodex.UI;

/// <summary>
/// Plots places on a simple equirectangular (lat/lon grid) projection, with
/// a hand-approximated ancient-world coastline (see AncientWorldCoastline)
/// drawn underneath as landmass silhouettes - no map tiles or internet
/// dependency, same self-contained approach as the myth network and
/// timeline canvases. Markers are pin-shaped, sized by how often you've
/// used the place tag; click one to browse its passages.
/// </summary>
public class MapCanvas : Panel
{
    public class PlaceMarker
    {
        public string Name = string.Empty;
        public double Lat;
        public double Lon;
        public int UsageCount;
    }

    private const double MinLon = -12, MaxLon = 56;
    private const double MinLat = 22, MaxLat = 54;
    private const int Margin = 40;

    private static readonly Color SeaColor = Color.FromArgb(196, 223, 235);
    private static readonly Color LandColor = Color.FromArgb(232, 217, 181);
    private static readonly Color CoastlineColor = Color.FromArgb(107, 90, 58);
    private static readonly Color PinFillColor = Color.FromArgb(165, 60, 50);
    private static readonly Color PinHoverFillColor = Color.Gold;
    private static readonly Color PinOutlineColor = Color.FromArgb(80, 40, 20);

    private List<PlaceMarker> _markers = new();
    private PlaceMarker? _hovered;
    private readonly ToolTip _toolTip = new();

    // View transform: screen = projected * _zoom + _viewOffset. At the
    // default (1.0, zero offset) this is exactly the old fixed view.
    private double _zoom = 1.0;
    private PointF _viewOffset = PointF.Empty;

    // Drag-to-pan bookkeeping. _dragMoved distinguishes a drag from a
    // click: WinForms fires MouseClick after every press-release pair, so
    // without this, finishing a pan on top of a pin would ALSO count as
    // clicking that pin and yank the passage list to somewhere unintended.
    private bool _dragging;
    private bool _dragMoved;
    private Point _dragStart;
    private PointF _dragStartOffset;

    public event Action<string>? PlaceClicked;

    public MapCanvas()
    {
        DoubleBuffered = true;
        BackColor = SeaColor;

        // Panels aren't focusable by default, and mouse-wheel events only
        // go to the focused control - without these two lines the wheel
        // would scroll nothing and zoom would silently never fire.
        SetStyle(ControlStyles.Selectable, true);
        MouseEnter += (_, _) => Focus();

        MouseMove += MapCanvas_MouseMove;
        MouseClick += MapCanvas_MouseClick;
        MouseDown += MapCanvas_MouseDown;
        MouseUp += (_, _) => _dragging = false;
        MouseWheel += MapCanvas_MouseWheel;
        MouseDoubleClick += MapCanvas_MouseDoubleClick;
    }

    public void SetData(List<PlaceMarker> markers)
    {
        _markers = markers;
        Invalidate();
    }

    /// <summary>
    /// Projects a lat/lon point to canvas pixels, using ONE shared scale for
    /// both axes rather than independently stretching longitude to fill the
    /// panel's width and latitude to fill its height.
    ///
    /// That independent stretching is what a bare degree grid never
    /// revealed, but it badly distorts anything with a recognizable shape:
    /// the map's lat/lon range is a wide 68x32 degree rectangle, while the
    /// panel's actual pixel dimensions are usually much closer to square -
    /// stretching each axis to fill its own dimension regardless squashed
    /// every landmass out of proportion. Using one scale for both axes
    /// (whichever is more constraining) and centering the result rather
    /// than stretching it - the same idea an image viewer uses to avoid
    /// warping a picture that doesn't match its frame - keeps every shape's
    /// proportions correct regardless of the panel's actual size, at the
    /// cost of some blank margin above/below or left/right.
    /// </summary>
    private PointF LatLonToPoint(double lat, double lon)
    {
        var usableWidth = Math.Max(Width - Margin * 2, 100);
        var usableHeight = Math.Max(Height - Margin * 2, 100);

        var lonRange = MaxLon - MinLon;
        var latRange = MaxLat - MinLat;
        var scale = Math.Min(usableWidth / lonRange, usableHeight / latRange);

        var mapWidth = lonRange * scale;
        var mapHeight = latRange * scale;
        var offsetX = Margin + (usableWidth - mapWidth) / 2;
        var offsetY = Margin + (usableHeight - mapHeight) / 2;

        var x = offsetX + (lon - MinLon) * scale;
        var y = offsetY + (MaxLat - lat) * scale;

        // Zoom/pan applied here, in the ONE projection everything shares -
        // coastline, grid, pins, and hit-testing all go through this
        // method, so none of them can end up viewing a different world.
        return new PointF(
            (float)(x * _zoom + _viewOffset.X),
            (float)(y * _zoom + _viewOffset.Y));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        DrawCoastline(e.Graphics);
        DrawGrid(e.Graphics);

        if (_markers.Count == 0)
        {
            using var emptyFont = new Font(Font, FontStyle.Italic);
            e.Graphics.DrawString(
                "No place tags matched yet - tag a line with a place name (e.g. \"Athens\", \"Troy\", \"Rome\") and reopen this.",
                emptyFont, Brushes.Gray, new PointF(Margin + 8, Margin + 8));
            return;
        }

        foreach (var marker in _markers)
        {
            var tip = LatLonToPoint(marker.Lat, marker.Lon);
            var isHovered = marker == _hovered;
            var size = PinSize(marker.UsageCount);

            using var path = BuildPinPath(tip, size);
            using var fillBrush = new SolidBrush(isHovered ? PinHoverFillColor : PinFillColor);
            e.Graphics.FillPath(fillBrush, path);
            using var pen = new Pen(PinOutlineColor, isHovered ? 2 : 1);
            e.Graphics.DrawPath(pen, path);

            var labelFont = isHovered ? new Font(Font, FontStyle.Bold) : Font;
            var headRadius = size * 0.62f;
            e.Graphics.DrawString(marker.Name, labelFont, Brushes.Black, tip.X + headRadius + 4, tip.Y - size - headRadius - 7);
        }
    }

    /// <summary>
    /// Draws the landmasses, projected through the same lat/lon transform
    /// the grid and markers use, so everything lines up regardless of the
    /// canvas's current size, zoom, and pan.
    ///
    /// Prefers real Natural Earth geometry when the "World Map Data" setup
    /// step has been run; falls back to the hand-approximated shapes in
    /// AncientWorldCoastline when it hasn't. Natural Earth polygons can
    /// carry hole rings (ring 0 is the outer boundary, later rings are
    /// holes), so each feature is drawn as one GraphicsPath in Alternate
    /// fill mode, which punches the holes out automatically.
    /// </summary>
    private void DrawCoastline(Graphics g)
    {
        using var landBrush = new SolidBrush(LandColor);
        using var coastPen = new Pen(CoastlineColor, 1.25f);

        var realCoastline = NaturalEarthCoastline.Load();
        if (realCoastline != null)
        {
            foreach (var rings in realCoastline)
            {
                using var path = new System.Drawing.Drawing2D.GraphicsPath(
                    System.Drawing.Drawing2D.FillMode.Alternate);
                foreach (var ring in rings)
                {
                    var points = new PointF[ring.Length];
                    for (var i = 0; i < ring.Length; i++)
                    {
                        points[i] = LatLonToPoint(ring[i].Lat, ring[i].Lon);
                    }
                    path.AddPolygon(points);
                }
                g.FillPath(landBrush, path);
                g.DrawPath(coastPen, path);
            }
            return;
        }

        foreach (var ring in AncientWorldCoastline.Landmasses)
        {
            var points = new PointF[ring.Length];
            for (var i = 0; i < ring.Length; i++)
            {
                points[i] = LatLonToPoint(ring[i].Lat, ring[i].Lon);
            }

            g.FillPolygon(landBrush, points);
            g.DrawPolygon(coastPen, points);
        }
    }

    /// <summary>
    /// Pin size from tag count - shared by OnPaint and HitTest for the same
    /// reason BuildPinPath is: two copies of this formula would eventually
    /// disagree.
    ///
    /// Log-scaled, not linear: real tag counts on well-used places run into
    /// the thousands (Athens at ~5,000, Troy at ~1,400), and a linear
    /// formula capped at 15 tags would mean every real place maxes out at
    /// the same enormous size, with pins whose true coordinates are only
    /// 2-3 degrees apart piling into one unreadable heap over the Aegean.
    /// Log2 keeps a visible difference between 1,400 and 5,000 (one is
    /// still clearly bigger) while staying small enough not to dominate
    /// the whole-Mediterranean view: 1 tag = 5.5px, ~100 = 8.3px, ~5,000 =
    /// 11.1px.
    /// </summary>
    private static float PinSize(int usageCount)
    {
        // Roughly half the previous range (was 9.9-21.4px) - a place with
        // thousands of tags (Athens, Sparta) was landing near the old
        // maximum, which reads fine once zoomed in close but dominates the
        // view at the full-Mediterranean zoom level, where a 26px-wide pin
        // head covers a meaningful fraction of Italy's own width on screen.
        var logComponent = Math.Log2(Math.Max(usageCount, 1) + 1);
        return 5f + (float)Math.Min(logComponent, 13) * 0.5f;
    }

    /// <summary>
    /// A classic map-pin silhouette - a round head sitting above a pointed
    /// tip - built as one GraphicsPath so drawing and hit-testing
    /// (GraphicsPath.IsVisible) can never disagree about the shape. The tip
    /// anchors exactly on the coordinate it marks, like a real pin stuck
    /// into a map, rather than a dot floating near it.
    /// </summary>
    private static System.Drawing.Drawing2D.GraphicsPath BuildPinPath(PointF tip, float size)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var headRadius = size * 0.62f;
        var headCenterY = tip.Y - size;
        var headRect = new RectangleF(tip.X - headRadius, headCenterY - headRadius, headRadius * 2, headRadius * 2);

        path.AddEllipse(headRect);

        // A short triangle bridging the head's underside down to the tip,
        // overlapping the ellipse slightly so the join disappears once filled.
        var neckY = headCenterY + headRadius * 0.55f;
        var neckHalfWidth = headRadius * 0.55f;
        path.AddPolygon(new[]
        {
            new PointF(tip.X - neckHalfWidth, neckY),
            new PointF(tip.X + neckHalfWidth, neckY),
            tip
        });

        return path;
    }

    private void DrawGrid(Graphics g)
    {
        using var gridPen = new Pen(Color.LightSteelBlue);
        using var axisFont = new Font(Font, FontStyle.Regular);

        for (var lon = Math.Ceiling(MinLon / 10) * 10; lon <= MaxLon; lon += 10)
        {
            var p1 = LatLonToPoint(MinLat, lon);
            var p2 = LatLonToPoint(MaxLat, lon);
            g.DrawLine(gridPen, p1, p2);
            g.DrawString($"{lon}°", axisFont, Brushes.SlateGray, p1.X - 10, p1.Y + 4);
        }

        for (var lat = Math.Ceiling(MinLat / 10) * 10; lat <= MaxLat; lat += 10)
        {
            var p1 = LatLonToPoint(lat, MinLon);
            var p2 = LatLonToPoint(lat, MaxLon);
            g.DrawLine(gridPen, p1, p2);
            g.DrawString($"{lat}°", axisFont, Brushes.SlateGray, 4, p1.Y - 7);
        }
    }

    private PlaceMarker? HitTest(Point p)
    {
        foreach (var marker in _markers)
        {
            var tip = LatLonToPoint(marker.Lat, marker.Lon);
            var size = PinSize(marker.UsageCount);

            using var path = BuildPinPath(tip, size);
            if (path.IsVisible(p)) return marker;
        }
        return null;
    }

    private void MapCanvas_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragMoved = false;
        _dragStart = e.Location;
        _dragStartOffset = _viewOffset;
    }

    private void MapCanvas_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            var dx = e.Location.X - _dragStart.X;
            var dy = e.Location.Y - _dragStart.Y;

            // A few pixels of slop before a press counts as a drag - hands
            // aren't perfectly still between mouse-down and mouse-up, and
            // without this a tiny wobble during an ordinary click would
            // both suppress the click AND nudge the view.
            if (Math.Abs(dx) + Math.Abs(dy) > 3) _dragMoved = true;

            if (_dragMoved)
            {
                _viewOffset = new PointF(_dragStartOffset.X + dx, _dragStartOffset.Y + dy);
                Invalidate();
            }
            return; // no hover updates mid-pan
        }

        var hover = HitTest(e.Location);
        if (hover != _hovered)
        {
            _hovered = hover;
            Cursor = hover != null ? Cursors.Hand : Cursors.Default;
            Invalidate();

            if (hover != null)
            {
                _toolTip.SetToolTip(this, $"{hover.Name} (tagged {hover.UsageCount} time(s))");
            }
        }
    }

    private void MapCanvas_MouseClick(object? sender, MouseEventArgs e)
    {
        // A completed pan is not a click - see _dragMoved's field comment.
        if (_dragMoved) return;

        var hit = HitTest(e.Location);
        if (hit != null) PlaceClicked?.Invoke(hit.Name);
    }

    private void MapCanvas_MouseWheel(object? sender, MouseEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
        var newZoom = Math.Clamp(_zoom * factor, 1.0, 12.0);
        if (Math.Abs(newZoom - _zoom) < 0.0001) return;

        // Anchor the zoom on the cursor: whatever spot on the map is under
        // the mouse stays under the mouse afterward - zooming dives toward
        // the place being pointed at, rather than toward the panel's
        // center while the point of interest slides away.
        var anchorX = (e.Location.X - _viewOffset.X) / _zoom;
        var anchorY = (e.Location.Y - _viewOffset.Y) / _zoom;
        _viewOffset = new PointF(
            (float)(e.Location.X - anchorX * newZoom),
            (float)(e.Location.Y - anchorY * newZoom));
        _zoom = newZoom;

        // Fully zoomed out means home - snap the pan back too, so wheeling
        // out never strands the map half off-screen with no way to tell.
        if (_zoom <= 1.0) _viewOffset = PointF.Empty;

        Invalidate();
    }

    private void MapCanvas_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        // Double-click on open sea resets the view; on a pin it stays a
        // pin interaction (the single-click already fired for it).
        if (HitTest(e.Location) != null) return;

        _zoom = 1.0;
        _viewOffset = PointF.Empty;
        Invalidate();
    }
}
