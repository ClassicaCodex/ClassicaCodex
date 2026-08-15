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

        /// <summary>
        /// True for a place matched from your own tags (the original
        /// behavior); false for a place shown only because it's in the
        /// full PlaceData catalog and hasn't been tagged. Drives both pin
        /// color and what a click does - a tag-search for the former, a
        /// text search for the latter, since an untagged place has no tag
        /// data to search.
        /// </summary>
        public bool IsYourTag = true;

        /// <summary>
        /// What sort of place it is. Drives pin colour, and which toggle hides
        /// it - a hundred pins on a Mediterranean-sized map is more than can be
        /// read at once, and most of the time you want one kind of thing.
        /// </summary>
        public ClassicaCodex.Core.PlaceKind Kind = ClassicaCodex.Core.PlaceKind.City;
    }

    // MaxLat raised from 54 to 59 alongside PlaceData's Renaissance/Byzantine
    // additions - just enough to fit Moscow, Edinburgh, and Elsinore (the
    // northernmost of the new entries, at 55.8-56.0) with a little padding,
    // not a general-purpose world view. One honest gap this opens: the
    // hand-drawn fallback coastline (AncientWorldCoastline) already reaches
    // this far for the British Isles, but was never drawn for Scandinavia or
    // Russia, so Moscow and Elsinore's pins will sit on bare sea color
    // without Natural Earth's real coastline data loaded - a labeled point
    // with no local landmass under it, same graceful-degradation tradeoff
    // every optional-data feature here already accepts, not a bug.
    private const double MinLon = -12, MaxLon = 56;
    private const double MinLat = 22, MaxLat = 59;
    private const int Margin = 40;

    private readonly Color _seaColor;
    private readonly Color _landColor;
    private readonly Color _coastlineColor;
    private readonly Color _gridLineColor;
    private readonly Color _axisLabelColor;
    private readonly Color _markerLabelColor;
    private readonly Color _emptyMessageColor;

    // Public: PlacesMapForm's legend swatches reference these directly,
    // so the key showing what each color means can never drift from what
    // the pins are actually drawn in. Instance, not static - static would
    // be fixed at whatever ReadingTheme.IsDark happened to be the first
    // time this type was ever touched, never updating for a later
    // MapCanvas opened after a theme change.
    public readonly Color PinFillColor;
    public readonly Color KnownPlaceFillColor;
    public static readonly Color PinHoverFillColor = Color.Gold;
    private readonly Color _pinOutlineColor;
    private readonly bool _darkTheme = ReadingTheme.IsDark;

    private List<PlaceMarker> _allPlaceMarkers = new();

    /// <summary>Whether the full PlaceData catalog is drawn alongside your tag-based pins.</summary>
    public bool ShowAllKnownPlaces
    {
        get => _showAllKnownPlaces;
        set { _showAllKnownPlaces = value; Invalidate(); }
    }
    private bool _showAllKnownPlaces;

    private List<PlaceMarker> _markers = new();

    /// <summary>
    /// Which kinds of place are drawn. Everything by default; the legend on
    /// PlacesMapForm switches them.
    ///
    /// Filtering happens here rather than in the caller so hit-testing and
    /// drawing can never disagree about what is on screen - a hidden pin that
    /// still answers a click is worse than no filtering at all.
    /// </summary>
    private readonly HashSet<ClassicaCodex.Core.PlaceKind> _visibleKinds =
        new(Enum.GetValues<ClassicaCodex.Core.PlaceKind>());

    /// <summary>Shows or hides one kind of place.</summary>
    public void SetKindVisible(ClassicaCodex.Core.PlaceKind kind, bool visible)
    {
        if (visible) _visibleKinds.Add(kind); else _visibleKinds.Remove(kind);
        _hovered = null;
        Invalidate();
    }

    public bool IsKindVisible(ClassicaCodex.Core.PlaceKind kind) => _visibleKinds.Contains(kind);

    private bool Shown(PlaceMarker m) => _visibleKinds.Contains(m.Kind);

    /// <summary>
    /// Pin fill per kind.
    ///
    /// Chosen for separation at pin size against both the land and sea colours
    /// rather than for prettiness: at eight pixels across, hue is nearly all
    /// there is to read, so these are spaced around the wheel instead of being
    /// shades of one colour.
    ///
    /// Cities keep the existing PinFillColor. They are 77 of the 100 entries,
    /// so giving them a new colour would change how the map looks for everyone
    /// in order to distinguish twenty-three pins. Battlefields would naturally
    /// be red and cannot be - that terracotta is already the city colour - so
    /// they take violet, which is the furthest thing on the wheel from all
    /// three of the map's own colours.
    /// </summary>
    /// <summary>The colour a kind's pins are drawn in, for the legend to match.</summary>
    public Color LegendColorFor(ClassicaCodex.Core.PlaceKind kind) => FillFor(kind, PinFillColor);

    private Color FillFor(ClassicaCodex.Core.PlaceKind kind, Color fallback) => kind switch
    {
        ClassicaCodex.Core.PlaceKind.Sanctuary => _darkTheme
            ? Color.FromArgb(214, 170, 74) : Color.FromArgb(176, 124, 18),
        ClassicaCodex.Core.PlaceKind.Battlefield => _darkTheme
            ? Color.FromArgb(168, 130, 205) : Color.FromArgb(104, 62, 148),
        ClassicaCodex.Core.PlaceKind.Region => _darkTheme
            ? Color.FromArgb(122, 166, 122) : Color.FromArgb(58, 106, 58),

        // Turquoise rather than blue. Blue is the sea these mostly sit in, and a
        // river pin the colour of the water under it is invisible.
        ClassicaCodex.Core.PlaceKind.Water => _darkTheme
            ? Color.FromArgb(96, 190, 196) : Color.FromArgb(20, 120, 130),
        _ => fallback
    };
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

    /// <summary>Fired on click with the place name and whether it came from your tags (true) or the full catalog only (false).</summary>
    public event Action<string, bool>? PlaceClicked;

    public MapCanvas()
    {
        if (ReadingTheme.IsDark)
        {
            _seaColor = Color.FromArgb(28, 42, 58);
            _landColor = Color.FromArgb(75, 65, 45);
            _coastlineColor = Color.FromArgb(210, 190, 150);
            _gridLineColor = Color.FromArgb(90, 100, 115);
            _axisLabelColor = Color.FromArgb(180, 185, 195);
            _markerLabelColor = Color.FromArgb(235, 235, 230);
            _emptyMessageColor = Color.FromArgb(160, 160, 160);
            PinFillColor = Color.FromArgb(215, 95, 80);
            KnownPlaceFillColor = Color.FromArgb(100, 155, 170);
            _pinOutlineColor = Color.FromArgb(35, 18, 10);
        }
        else
        {
            _seaColor = Color.FromArgb(196, 223, 235);
            _landColor = Color.FromArgb(232, 217, 181);
            _coastlineColor = Color.FromArgb(107, 90, 58);
            _gridLineColor = Color.LightSteelBlue;
            _axisLabelColor = Color.SlateGray;
            _markerLabelColor = Color.Black;
            _emptyMessageColor = Color.Gray;
            PinFillColor = Color.FromArgb(165, 60, 50);
            KnownPlaceFillColor = Color.FromArgb(70, 110, 120);
            _pinOutlineColor = Color.FromArgb(80, 40, 20);
        }

        DoubleBuffered = true;
        BackColor = _seaColor;

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

    /// <summary>The full-catalog pins, shown only when ShowAllKnownPlaces is on. Expected to already exclude anything also in the tag-based list, so a tagged place doesn't get two stacked pins.</summary>
    public void SetAllPlacesData(List<PlaceMarker> markers)
    {
        _allPlaceMarkers = markers;
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

        var showingAnyKnownPlaces = ShowAllKnownPlaces && _allPlaceMarkers.Count > 0;
        if (_markers.Count == 0 && !showingAnyKnownPlaces)
        {
            using var emptyFont = new Font(Font, FontStyle.Italic);
            using var emptyBrush = new SolidBrush(_emptyMessageColor);
            e.Graphics.DrawString(
                "No place tags matched yet - tag a line with a place name (e.g. \"Athens\", \"Troy\", \"Rome\") " +
                "and reopen this, or check \"Show all known places\" above to browse the full reference catalog.",
                emptyFont, emptyBrush, new PointF(Margin + 8, Margin + 8));
            return;
        }

        // Known-places pins drawn first, so a your-tags pin sitting near one
        // renders on top rather than being partly hidden underneath it.
        // Pins first, in the old order so tagged pins still sit on top of
        // catalog ones, then labels across both sets in one contest. Doing
        // labels per-pass instead would let a catalog label claim space a
        // tagged one wanted, or force the pins to be drawn in the wrong order
        // to prevent it.
        if (showingAnyKnownPlaces) DrawPins(e.Graphics, _allPlaceMarkers, KnownPlaceFillColor);
        DrawPins(e.Graphics, _markers, PinFillColor);

        var labelled = new List<PlaceMarker>(_markers.Where(Shown));
        if (showingAnyKnownPlaces) labelled.AddRange(_allPlaceMarkers.Where(Shown));

        DrawLabels(e.Graphics, labelled);
    }

    /// <summary>
    /// Draws the pins, in the order given, so the caller controls what sits on
    /// top of what. Labels are a separate pass.
    /// </summary>
    private void DrawPins(Graphics g, List<PlaceMarker> markers, Color fillColor)
    {
        foreach (var marker in markers)
        {
            if (!Shown(marker)) continue;

            var tip = LatLonToPoint(marker.Lat, marker.Lon);
            var isHovered = marker == _hovered;

            using var path = BuildPinPath(tip, PinSize(marker.UsageCount));
            using var fillBrush = new SolidBrush(
                isHovered ? PinHoverFillColor : FillFor(marker.Kind, fillColor));
            g.FillPath(fillBrush, path);
            using var pen = new Pen(_pinOutlineColor, isHovered ? 2 : 1);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>
    /// Draws a name beside every pin that has room for one.
    ///
    /// WITH THE FULL CATALOG THE MEDITERRANEAN IS UNREADABLE OTHERWISE. A
    /// hundred and eighty places, most of them packed into the Aegean, drew a
    /// hundred and eighty labels over each other - a black smear from Sicily to
    /// Ionia with nothing legible in it.
    ///
    /// Hiding PINS would be the wrong fix: the pin is what you click, and a
    /// place you cannot click is a place the map does not have. So every pin is
    /// drawn and only the labels compete. Sorted by how often you have tagged
    /// the place, so the contest is won by the places this library actually
    /// uses; and because zooming spreads the pins out, more names appear as you
    /// go in, which gives the zoom something to do beyond magnifying.
    ///
    /// Greedy and single-pass. Real label placement would try several positions
    /// around each pin to find a gap, where this only ever goes up and to the
    /// right, so a few names are lost that would have fitted elsewhere. Worth it
    /// for a repaint that has to keep up with a drag.
    /// </summary>
    private void DrawLabels(Graphics g, List<PlaceMarker> markers)
    {
        var placed = new List<RectangleF>();
        using var labelBrush = new SolidBrush(_markerLabelColor);

        foreach (var marker in markers.OrderByDescending(m => m.UsageCount))
        {
            var tip = LatLonToPoint(marker.Lat, marker.Lon);
            var isHovered = marker == _hovered;
            var size = PinSize(marker.UsageCount);

            var labelFont = isHovered ? new Font(Font, FontStyle.Bold) : Font;

            try
            {
                var headRadius = size * 0.62f;
                var at = new PointF(tip.X + headRadius + 4, tip.Y - size - headRadius - 7);
                var extent = g.MeasureString(marker.Name, labelFont);

                // A little padding, so two labels that clear each other by a
                // hair still read as two words rather than one.
                var bounds = new RectangleF(at.X - 2, at.Y - 1, extent.Width + 4, extent.Height + 2);

                // The hovered pin's name is always drawn - it is the one being
                // asked about.
                if (!isHovered && placed.Any(r => r.IntersectsWith(bounds))) continue;

                g.DrawString(marker.Name, labelFont, labelBrush, at.X, at.Y);
                placed.Add(bounds);
            }
            finally
            {
                if (isHovered) labelFont.Dispose();
            }
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
        using var landBrush = new SolidBrush(_landColor);
        using var coastPen = new Pen(_coastlineColor, 1.25f);

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
        using var gridPen = new Pen(_gridLineColor);
        using var axisFont = new Font(Font, FontStyle.Regular);
        using var axisBrush = new SolidBrush(_axisLabelColor);

        for (var lon = Math.Ceiling(MinLon / 10) * 10; lon <= MaxLon; lon += 10)
        {
            var p1 = LatLonToPoint(MinLat, lon);
            var p2 = LatLonToPoint(MaxLat, lon);
            g.DrawLine(gridPen, p1, p2);
            g.DrawString($"{lon}°", axisFont, axisBrush, p1.X - 10, p1.Y + 4);
        }

        for (var lat = Math.Ceiling(MinLat / 10) * 10; lat <= MaxLat; lat += 10)
        {
            var p1 = LatLonToPoint(lat, MinLon);
            var p2 = LatLonToPoint(lat, MaxLon);
            g.DrawLine(gridPen, p1, p2);
            g.DrawString($"{lat}°", axisFont, axisBrush, 4, p1.Y - 7);
        }
    }

    private PlaceMarker? HitTest(Point p)
    {
        foreach (var marker in _markers)
        {
            if (!Shown(marker)) continue;

            var tip = LatLonToPoint(marker.Lat, marker.Lon);
            var size = PinSize(marker.UsageCount);

            using var path = BuildPinPath(tip, size);
            if (path.IsVisible(p)) return marker;
        }

        if (ShowAllKnownPlaces)
        {
            foreach (var marker in _allPlaceMarkers)
            {
                var tip = LatLonToPoint(marker.Lat, marker.Lon);
                var size = PinSize(marker.UsageCount);

                using var path = BuildPinPath(tip, size);
                if (path.IsVisible(p)) return marker;
            }
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
        if (hit != null) PlaceClicked?.Invoke(hit.Name, hit.IsYourTag);
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
