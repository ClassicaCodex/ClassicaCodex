using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ClassicaCodex.Core;
using ClassicaCodex.UI;

namespace ClassicaCodex.Mac.Features.PlacesMap;

/// <summary>
/// Plots places on a simple equirectangular (lat/lon grid) projection, with
/// coastlines drawn underneath as landmass silhouettes - Natural Earth's when
/// the World Map Data setup step has been run, the hand-approximated shapes in
/// AncientWorldCoastline when it hasn't. No map tiles and no connection, the
/// same self-contained approach as the myth network and the timeline.
///
/// Markers are pin-shaped, sized by how often the place has been tagged;
/// click one to browse its passages. Drag or swipe with two fingers to move
/// about, pinch or scroll a mouse wheel to zoom.
///
/// Every colour comes from the palette, so parchment land on a soft sea in
/// the light appearance becomes the Lamplight equivalent in the dark one.
/// </summary>
public sealed class MapCanvas : Control
{
    public sealed class PlaceMarker
    {
        public string Name { get; init; } = string.Empty;
        public double Lat { get; init; }
        public double Lon { get; init; }
        public int UsageCount { get; init; }

        /// <summary>
        /// What sort of place it is. Drives pin colour, and which toggle hides
        /// it - two hundred pins on a Mediterranean-sized map is more than can
        /// be read at once, and most of the time you want one kind of thing.
        /// </summary>
        public PlaceKind Kind { get; init; } = PlaceKind.City;
    }

    // MaxLat 59 rather than 54: just enough for Moscow, Edinburgh and Elsinore
    // (55.8-56.0) with a little padding, not a general-purpose world view. The
    // hand-drawn fallback coastline was never drawn for Scandinavia or Russia,
    // so without Natural Earth's data Moscow and Elsinore sit on bare sea - a
    // labelled point with no landmass under it, the same graceful degradation
    // every optional-data feature here accepts.
    private const double MinLon = -12, MaxLon = 56;
    private const double MinLat = 22, MaxLat = 59;

    // The margin kept round the map's own rectangle of the world when it is
    // fitted to the view, and how close to the edge of the view a pan may
    // bring that rectangle's edge.
    private const double MapInset = 40;

    private const double MinZoom = 1.0, MaxZoom = 12.0;

    // A few pixels of slop before a press counts as a drag: hands aren't
    // perfectly still between press and release, and without it a wobble
    // during an ordinary click would both suppress the click and nudge the view.
    private const double DragSlop = 3;

    /// <summary>
    /// How far outside its outline a pin still answers. A pin is eight points
    /// across at the full view and a trackpad is not a precise pointer, so it
    /// is given a margin rather than demanding a direct hit.
    /// </summary>
    private const double HitTolerance = 3;

    private static readonly Pen HitPen = new(Brushes.Black, HitTolerance * 2);

    private List<PlaceMarker> _markers = new();

    /// <summary>
    /// Which kinds of place are drawn. Everything by default; the legend on the
    /// window switches them. Filtered here rather than in the caller so
    /// hit-testing and drawing can never disagree about what is on screen - a
    /// hidden pin that still answers a click is worse than no filtering at all.
    /// </summary>
    private readonly HashSet<PlaceKind> _visibleKinds = new(Enum.GetValues<PlaceKind>());

    private PlaceMarker? _hovered;
    private PlaceMarker? _selected;

    // View transform: screen = projected * _zoom + _offset. At the default
    // (1.0, no offset) every place on the map is in view, centred.
    private double _zoom = 1.0;
    private Vector _offset;

    // Drag-to-pan bookkeeping. _dragMoved tells a drag from a click: every
    // press ends in a release, so without it finishing a pan on top of a pin
    // would also count as clicking that pin.
    private bool _pressed;
    private bool _dragMoved;
    private Point _dragStart;
    private Vector _dragStartOffset;
    private double _lastPinchScale = 1;

    private DispatcherTimer? _animation;

    // The projection at zoom 1 for the current size, and everything drawn in
    // its coordinates - the land and the graticule - built once per size and
    // drawn through the view transform, so a drag repaints without
    // re-projecting several thousand coastline points.
    private Size _layoutSize;
    private double _scale;
    private Point _origin;
    private List<StreamGeometry> _land = new();
    private StreamGeometry? _graticule;

    private Inks? _inks;
    private readonly Dictionary<(string Name, bool Bold, bool Italic), LabelText> _labels = new();
    private readonly Dictionary<double, StreamGeometry> _pinShapes = new();
    private readonly Dictionary<string, LabelText> _axisLabels = new(StringComparer.Ordinal);

    public MapCanvas()
    {
        ClipToBounds = true;
        Focusable = true;

        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnMagnify);
        AddHandler(PinchEvent, OnPinch);
        AddHandler(PinchEndedEvent, (_, _) => _lastPinchScale = 1);

        ActualThemeVariantChanged += (_, _) =>
        {
            _inks = null;
            _labels.Clear();
            _axisLabels.Clear();
            InvalidateVisual();
            AppearanceChanged?.Invoke();
        };
    }

    /// <summary>Raised on click with the place clicked.</summary>
    public event Action<PlaceMarker>? PlaceClicked;

    /// <summary>Raised when the zoom changes, so the window can enable its zoom buttons to match.</summary>
    public event Action? ViewChanged;

    /// <summary>Raised when the appearance changes, so the legend can recolour its swatches.</summary>
    public event Action? AppearanceChanged;

    public IReadOnlyList<PlaceMarker> Markers => _markers;

    public double Zoom => _zoom;

    public bool CanZoomIn => _zoom < MaxZoom - 1e-6;

    public bool CanZoomOut => _zoom > MinZoom + 1e-6;

    /// <summary>True when Natural Earth's coastlines are drawn rather than the built-in sketch.</summary>
    public bool UsingRealCoastline { get; private set; }

    /// <summary>The place whose passages the window is showing, drawn ringed so it can be found again.</summary>
    public PlaceMarker? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            InvalidateVisual();
        }
    }

    public void SetData(List<PlaceMarker> markers)
    {
        _markers = markers;
        _hovered = null;
        InvalidateVisual();
    }

    /// <summary>Shows or hides one kind of place.</summary>
    public void SetKindVisible(PlaceKind kind, bool visible)
    {
        if (visible) _visibleKinds.Add(kind);
        else _visibleKinds.Remove(kind);

        _hovered = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    public bool IsKindVisible(PlaceKind kind) => _visibleKinds.Contains(kind);

    private bool Shown(PlaceMarker m) => _visibleKinds.Contains(m.Kind);

    /// <summary>The colour a kind's pins are drawn in, for the legend to match.</summary>
    public Color LegendColorFor(PlaceKind kind) => GetInks().Pin(kind).Fill.Color;

    /// <summary>The outline a kind's pins are drawn with, for the legend's swatches.</summary>
    public Color LegendOutlineFor(PlaceKind kind) => GetInks().Pin(kind).Outline.Color;

    // ---- Projection -------------------------------------------------------

    /// <summary>
    /// Fits the map's rectangle of the world to the control, using ONE shared
    /// scale for both axes rather than stretching longitude to the width and
    /// latitude to the height independently.
    ///
    /// That rectangle is a wide 68 x 37 degrees and the control is usually much
    /// closer to square, so stretching each axis to fill its own dimension
    /// would squash every landmass out of proportion. One scale for both -
    /// whichever is more constraining - with the result centred, keeps every
    /// shape true at any size, the same thing an image viewer does with a
    /// picture that doesn't match its frame. Where an image viewer leaves the
    /// spare room blank, the map simply carries on into it: more of Africa
    /// below, more of the north above.
    /// </summary>
    private void EnsureLayout()
    {
        var size = Bounds.Size;
        if (size == _layoutSize && _graticule != null) return;

        _layoutSize = size;

        var usableWidth = Math.Max(size.Width - MapInset * 2, 100);
        var usableHeight = Math.Max(size.Height - MapInset * 2, 100);
        var lonRange = MaxLon - MinLon;
        var latRange = MaxLat - MinLat;
        _scale = Math.Min(usableWidth / lonRange, usableHeight / latRange);

        var mapWidth = lonRange * _scale;
        var mapHeight = latRange * _scale;
        _origin = new Point(MapInset + (usableWidth - mapWidth) / 2, MapInset + (usableHeight - mapHeight) / 2);

        BuildLand();
        BuildGraticule();
        _offset = Clamp(_offset, _zoom);
    }

    /// <summary>A lat/lon point on the map at zoom 1.</summary>
    private Point Project(double lat, double lon) =>
        new(_origin.X + (lon - MinLon) * _scale, _origin.Y + (MaxLat - lat) * _scale);

    /// <summary>
    /// A lat/lon point on screen. Zoom and pan are applied here, in the ONE
    /// projection everything shares - coastline, graticule, pins and
    /// hit-testing all go through it, so none of them can end up looking at a
    /// different world.
    /// </summary>
    private Point LatLonToPoint(double lat, double lon)
    {
        var p = Project(lat, lon);
        return new Point(p.X * _zoom + _offset.X, p.Y * _zoom + _offset.Y);
    }

    private Matrix ViewMatrix => Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset.X, _offset.Y);

    /// <summary>The map's rectangle of the world at zoom 1 - where every place in the catalogue lies.</summary>
    private Rect BaseExtent => new(Project(MaxLat, MinLon), Project(MinLat, MaxLon));

    /// <summary>
    /// Keeps the map from being dragged away from its places: along an axis
    /// where their rectangle is larger than the view it can be moved until an
    /// edge reaches the margin and no further, and along one where it still
    /// fits it can move only within the view. Fully zoomed out means home - the
    /// pan snaps back too - so zooming out never strands the map half
    /// off-screen with no way to tell where it went.
    ///
    /// Not centred while it fits: that would pull the point under the pointer
    /// away from it on the first few steps in, which is the one thing a zoom
    /// anchored on the pointer must not do.
    /// </summary>
    private Vector Clamp(Vector offset, double zoom)
    {
        if (zoom <= MinZoom + 1e-9) return default;

        var extent = BaseExtent;

        double Axis(double value, double start, double end, double length)
        {
            var leading = MapInset - start * zoom;
            var trailing = length - MapInset - end * zoom;
            return Math.Clamp(value, Math.Min(leading, trailing), Math.Max(leading, trailing));
        }

        return new Vector(
            Axis(offset.X, extent.Left, extent.Right, Bounds.Width),
            Axis(offset.Y, extent.Top, extent.Bottom, Bounds.Height));
    }

    // ---- Geometry ---------------------------------------------------------

    /// <summary>
    /// The landmasses, projected through the same transform the graticule and
    /// pins use, so everything lines up at any size, zoom and pan.
    ///
    /// Prefers real Natural Earth geometry when the World Map Data setup step
    /// has been run, and falls back to the hand-approximated shapes in
    /// AncientWorldCoastline when it hasn't. A Natural Earth polygon can carry
    /// hole rings (ring 0 is the outer boundary, later rings are holes), so
    /// each feature is one geometry filled even-odd, which punches the holes
    /// out. Each landmass stays its own geometry rather than all of them
    /// sharing one: the hand-drawn shapes overlap where they meet, and even-odd
    /// over the union would cut a hole wherever two of them do.
    /// </summary>
    private void BuildLand()
    {
        _land = new List<StreamGeometry>();

        var real = NaturalEarthCoastline.Load();
        UsingRealCoastline = real != null;

        if (real != null)
        {
            foreach (var rings in real) _land.Add(Polygon(rings));
            return;
        }

        foreach (var ring in AncientWorldCoastline.Landmasses) _land.Add(Polygon(new[] { ring }));
    }

    private StreamGeometry Polygon(IEnumerable<(double Lat, double Lon)[]> rings)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.SetFillRule(FillRule.EvenOdd);

        foreach (var ring in rings)
        {
            if (ring.Length < 3) continue;

            context.BeginFigure(Project(ring[0].Lat, ring[0].Lon), isFilled: true);
            for (var i = 1; i < ring.Length; i++) context.LineTo(Project(ring[i].Lat, ring[i].Lon));
            context.EndFigure(isClosed: true);
        }

        return geometry;
    }

    // How far the graticule runs: well past the places' rectangle, since the
    // map carries on past it into whatever room the window has.
    private const double GraticuleMinLon = -60, GraticuleMaxLon = 120;
    private const double GraticuleMinLat = -10, GraticuleMaxLat = 80;

    /// <summary>Every ten degrees.</summary>
    private void BuildGraticule()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var lon = GraticuleMinLon; lon <= GraticuleMaxLon; lon += 10)
            {
                context.BeginFigure(Project(GraticuleMinLat, lon), isFilled: false);
                context.LineTo(Project(GraticuleMaxLat, lon));
                context.EndFigure(isClosed: false);
            }

            for (var lat = GraticuleMinLat; lat <= GraticuleMaxLat; lat += 10)
            {
                context.BeginFigure(Project(lat, GraticuleMinLon), isFilled: false);
                context.LineTo(Project(lat, GraticuleMaxLon));
                context.EndFigure(isClosed: false);
            }
        }

        _graticule = geometry;
    }

    /// <summary>
    /// Pin size from tag count - shared by drawing and hit-testing, because two
    /// copies of this formula would eventually disagree.
    ///
    /// Log-scaled, not linear: real tag counts on well-used places run into the
    /// thousands (Athens at ~5,000, Troy at ~1,400), and a linear formula would
    /// put every real place at the same enormous size, piling pins only two or
    /// three degrees apart into one heap over the Aegean. Log2 keeps a visible
    /// difference between 1,400 and 5,000 while staying small enough not to
    /// dominate the whole-Mediterranean view: 1 tag = 5.5, ~100 = 8.3,
    /// ~5,000 = 11.1. Half the range it once had - a pin head 26 points wide
    /// covered a meaningful fraction of Italy at the full view.
    /// </summary>
    private static double PinSize(int usageCount)
    {
        var logComponent = Math.Log2(Math.Max(usageCount, 1) + 1);
        return 5 + Math.Min(logComponent, 13) * 0.5;
    }

    /// <summary>The scale a pin is drawn at: larger while it is pointed at or chosen.</summary>
    private double PinScale(PlaceMarker marker) =>
        marker == _hovered || marker == _selected ? 1.35 : 1.0;

    /// <summary>
    /// A classic map-pin silhouette - a round head above a pointed tip, the
    /// sides running tangent from the tip to the head - as one geometry with
    /// the tip at the origin, so drawing and hit-testing can never disagree
    /// about the shape. The tip anchors exactly on the coordinate it marks,
    /// like a real pin stuck into a map, rather than a dot floating near it.
    /// </summary>
    private StreamGeometry PinShape(double size)
    {
        if (_pinShapes.TryGetValue(size, out var cached)) return cached;

        var r = size * 0.62;
        var centreY = -size;
        var cos = r / size;
        var sin = Math.Sqrt(1 - cos * cos);
        var right = new Point(r * sin, centreY + r * cos);
        var left = new Point(-r * sin, centreY + r * cos);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 0), isFilled: true);
            context.LineTo(right);
            context.ArcTo(left, new Size(r, r), 0, isLargeArc: true, SweepDirection.CounterClockwise);
            context.EndFigure(isClosed: true);
        }

        _pinShapes[size] = geometry;
        return geometry;
    }

    // ---- Drawing ----------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        EnsureLayout();
        var inks = GetInks();
        var view = new Rect(Bounds.Size);

        context.DrawRectangle(inks.Sea, null, view);

        using (context.PushTransform(ViewMatrix))
        {
            DrawCoastline(context, inks);
            if (_graticule != null) context.DrawGeometry(null, new Pen(inks.Graticule, 0.75 / _zoom), _graticule);
        }

        DrawGraticuleLabels(context, inks, view);
        DrawPins(context, inks, view);
        DrawLabels(context, inks, view);
    }

    /// <summary>
    /// Shallow water first, then the coast, then the land over both. The coast
    /// is stroked at twice its width and the land laid over it, so only its
    /// seaward half shows - which also buries the seams where the hand-drawn
    /// shapes overlap, leaving one clean shoreline rather than a line across
    /// the Pyrenees.
    /// </summary>
    private void DrawCoastline(DrawingContext context, Inks inks)
    {
        var shallowFar = new Pen(inks.ShallowFar, 10 / _zoom) { LineJoin = PenLineJoin.Round };
        var shallowNear = new Pen(inks.ShallowNear, 4.5 / _zoom) { LineJoin = PenLineJoin.Round };
        var coast = new Pen(inks.Coast, 2.4 / _zoom) { LineJoin = PenLineJoin.Round };

        foreach (var land in _land) context.DrawGeometry(null, shallowFar, land);
        foreach (var land in _land) context.DrawGeometry(null, shallowNear, land);
        foreach (var land in _land) context.DrawGeometry(null, coast, land);
        foreach (var land in _land) context.DrawGeometry(inks.Land, null, land);
    }

    /// <summary>
    /// The degrees, along the top and the left edge of the view where an atlas
    /// numbers its graticule - pinned to the edges rather than to the lines'
    /// ends, so a reader zoomed into the Aegean can still tell where on the
    /// map they are. The bottom corners are left to the zoom buttons and the
    /// note about the coastline.
    /// </summary>
    private void DrawGraticuleLabels(DrawingContext context, Inks inks, Rect view)
    {
        for (var lon = GraticuleMinLon; lon <= GraticuleMaxLon; lon += 10)
        {
            var x = LatLonToPoint(MinLat, lon).X;
            var label = AxisLabel(lon == 0 ? "0°" : lon < 0 ? $"{-lon}° W" : $"{lon}° E", inks);
            if (x - label.Text.Width / 2 < 34 || x + label.Text.Width / 2 > view.Width - 6) continue;

            DrawLabelText(context, inks, label, new Point(x - label.Text.Width / 2, 7));
        }

        for (var lat = GraticuleMinLat; lat <= GraticuleMaxLat; lat += 10)
        {
            var y = LatLonToPoint(lat, MinLon).Y;
            var label = AxisLabel(lat == 0 ? "0°" : lat < 0 ? $"{-lat}° S" : $"{lat}° N", inks);
            if (y - label.Text.Height / 2 < 26 || y + label.Text.Height / 2 > view.Height - 6) continue;

            DrawLabelText(context, inks, label, new Point(8, y - label.Text.Height / 2));
        }
    }

    /// <summary>
    /// Every visible pin, in list order, with the one pointed at and the one
    /// chosen drawn last so nothing sits on top of them.
    /// </summary>
    private void DrawPins(DrawingContext context, Inks inks, Rect view)
    {
        foreach (var marker in _markers)
        {
            if (!Shown(marker) || marker == _hovered || marker == _selected) continue;
            DrawPin(context, inks, view, marker);
        }

        if (_selected != null && Shown(_selected)) DrawPin(context, inks, view, _selected);
        if (_hovered != null && _hovered != _selected && Shown(_hovered)) DrawPin(context, inks, view, _hovered);
    }

    private void DrawPin(DrawingContext context, Inks inks, Rect view, PlaceMarker marker)
    {
        var tip = LatLonToPoint(marker.Lat, marker.Lon);
        var size = PinSize(marker.UsageCount);
        var scale = PinScale(marker);
        if (!view.Inflate(size * 3).Contains(tip)) return;

        var ink = inks.Pin(marker.Kind);
        var r = size * 0.62;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(tip.X, tip.Y)))
        {
            context.DrawEllipse(inks.PinShadow, null, new Point(0.6, 0.2), size * 0.42, size * 0.17);
            context.DrawGeometry(ink.Fill, new Pen(ink.Outline, 1 / scale), PinShape(size));
            context.DrawEllipse(ink.Eye, null, new Point(0, -size), r * 0.38, r * 0.38);

            if (marker == _selected)
            {
                context.DrawEllipse(null, new Pen(inks.Halo, 3.5 / scale), new Point(0, -size), r + 2.6, r + 2.6);
                context.DrawEllipse(null, new Pen(inks.Ink, 1.4 / scale), new Point(0, -size), r + 2.6, r + 2.6);
            }
        }
    }

    /// <summary>
    /// Draws a name beside every pin that has room for one.
    ///
    /// WITH THE FULL CATALOGUE THE MEDITERRANEAN IS UNREADABLE OTHERWISE. Two
    /// hundred places, most of them packed into the Aegean, drew two hundred
    /// labels over each other - a smear from Sicily to Ionia with nothing
    /// legible in it.
    ///
    /// Hiding PINS would be the wrong fix: the pin is what you click, and a
    /// place you cannot click is a place the map does not have. So every pin is
    /// drawn and only the labels compete. Sorted by how often you have tagged
    /// the place, so the contest is won by the places this library actually
    /// uses; and because zooming spreads the pins out, more names appear as you
    /// go in, which gives the zoom something to do beyond magnifying. The place
    /// pointed at and the place chosen go first and are always drawn - they are
    /// the ones being asked about.
    ///
    /// Greedy and single-pass. Real label placement would try several positions
    /// around each pin to find a gap, where this only ever goes up and to the
    /// right, so a few names are lost that would have fitted elsewhere. Worth it
    /// for a repaint that has to keep up with a drag.
    /// </summary>
    private void DrawLabels(DrawingContext context, Inks inks, Rect view)
    {
        var placed = new List<Rect>();

        IEnumerable<PlaceMarker> Contest()
        {
            if (_hovered != null && Shown(_hovered)) yield return _hovered;
            if (_selected != null && _selected != _hovered && Shown(_selected)) yield return _selected;

            foreach (var marker in _markers.Where(Shown).OrderByDescending(m => m.UsageCount))
            {
                if (marker != _hovered && marker != _selected) yield return marker;
            }
        }

        foreach (var marker in Contest())
        {
            var tip = LatLonToPoint(marker.Lat, marker.Lon);
            var scale = PinScale(marker);
            var size = PinSize(marker.UsageCount) * scale;
            var headRadius = size * 0.62;
            var emphasised = marker == _hovered || marker == _selected;

            var label = Label(marker, emphasised, inks);
            var at = new Point(tip.X + headRadius + 4, tip.Y - size - headRadius - label.Text.Height * 0.5 + 1);

            // A little padding, so two labels that clear each other by a hair
            // still read as two words rather than one.
            var bounds = new Rect(at.X - 2, at.Y - 1, label.Text.Width + 4, label.Text.Height + 2);
            if (!bounds.Intersects(view)) continue;

            if (!emphasised && placed.Any(r => r.Intersects(bounds))) continue;

            DrawLabelText(context, inks, label, at);
            placed.Add(bounds);
        }
    }

    /// <summary>A name with a halo of the page behind it, so it reads over coast, graticule and sea alike.</summary>
    private static void DrawLabelText(DrawingContext context, Inks inks, LabelText label, Point at)
    {
        using (context.PushTransform(Matrix.CreateTranslation(at.X, at.Y)))
        {
            context.DrawGeometry(null, inks.HaloPen, label.Outline);
            context.DrawText(label.Text, new Point(0, 0));
        }
    }

    /// <summary>
    /// A place's name, set once and kept. Rivers and seas are in italic, as
    /// an atlas sets water, so a name in the middle of the Aegean reads as the
    /// sea's and not as an island's.
    /// </summary>
    private LabelText Label(PlaceMarker marker, bool bold, Inks inks)
    {
        var italic = marker.Kind == PlaceKind.Water;
        var key = (marker.Name, bold, italic);
        if (_labels.TryGetValue(key, out var cached)) return cached;

        var typeface = new Typeface(inks.LabelFont, italic ? FontStyle.Italic : FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal);
        var text = new FormattedText(marker.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 11.5,
            italic ? inks.WaterLabel : inks.Ink);

        var label = new LabelText(text, text.BuildGeometry(new Point(0, 0)) ?? new StreamGeometry());
        _labels[key] = label;
        return label;
    }

    private LabelText AxisLabel(string text, Inks inks)
    {
        if (_axisLabels.TryGetValue(text, out var cached)) return cached;

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), 10, inks.AxisLabel);
        var label = new LabelText(formatted, formatted.BuildGeometry(new Point(0, 0)) ?? new StreamGeometry());
        _axisLabels[text] = label;
        return label;
    }

    private sealed record LabelText(FormattedText Text, Geometry Outline);

    // ---- Hit-testing and interaction ---------------------------------------

    /// <summary>
    /// The pin under a point: inside its outline or within a few points of it,
    /// and of several, the one whose head is nearest. Uses the same geometry
    /// the pin is drawn with, at the size it is drawn.
    /// </summary>
    private PlaceMarker? HitTest(Point point)
    {
        PlaceMarker? best = null;
        var bestDistance = double.MaxValue;

        foreach (var marker in _markers)
        {
            if (!Shown(marker)) continue;

            var tip = LatLonToPoint(marker.Lat, marker.Lon);
            var scale = PinScale(marker);
            var size = PinSize(marker.UsageCount);
            var local = new Point((point.X - tip.X) / scale, (point.Y - tip.Y) / scale);

            // Cheap rejection before the geometry is asked.
            if (Math.Abs(local.X) > size + HitTolerance * 2 || local.Y > HitTolerance * 2 || local.Y < -size * 2 - HitTolerance * 2)
                continue;

            var shape = PinShape(size);
            if (!shape.FillContains(local) && !shape.StrokeContains(HitPen, local)) continue;

            var dx = local.X;
            var dy = local.Y + size;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                best = marker;
                bestDistance = distance;
            }
        }

        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        StopAnimation();
        Focus();

        // Double-click on open sea resets the view; on a pin it stays a pin
        // interaction (the first click has already chosen it).
        if (e.ClickCount == 2)
        {
            if (HitTest(point.Position) == null) ResetView();
            e.Handled = true;
            return;
        }

        _pressed = true;
        _dragMoved = false;
        _dragStart = point.Position;
        _dragStartOffset = _offset;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);

        if (_pressed)
        {
            var delta = position - _dragStart;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) > DragSlop) _dragMoved = true;

            if (_dragMoved)
            {
                _offset = Clamp(_dragStartOffset + delta, _zoom);
                Cursor = new Cursor(StandardCursorType.SizeAll);
                InvalidateVisual();
            }

            return; // no hover updates mid-pan
        }

        UpdateHover(position);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_pressed) return;

        _pressed = false;
        e.Pointer.Capture(null);

        var position = e.GetPosition(this);

        // A completed pan is not a click - see _dragMoved.
        if (!_dragMoved)
        {
            var hit = HitTest(position);
            if (hit != null)
            {
                Selected = hit;
                PlaceClicked?.Invoke(hit);
            }
        }

        _dragMoved = false;
        UpdateHover(position);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pressed = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_pressed || _hovered == null) return;

        _hovered = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    private void UpdateHover(Point position)
    {
        var hover = HitTest(position);
        Cursor = hover != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        if (hover == _hovered) return;

        _hovered = hover;
        ToolTip.SetTip(this, hover == null ? null : TipFor(hover));
        InvalidateVisual();
    }

    private static string TipFor(PlaceMarker marker)
    {
        var kind = marker.Kind switch
        {
            PlaceKind.City => "city",
            PlaceKind.Sanctuary => "sanctuary",
            PlaceKind.Battlefield => "battlefield",
            PlaceKind.Water => "river, sea or lake",
            _ => "region or island"
        };

        return marker.UsageCount > 0
            ? $"{marker.Name} - {kind}, tagged {marker.UsageCount:N0} time(s)"
            : $"{marker.Name} - {kind}";
    }

    /// <summary>
    /// Scrolling, as a Mac reader expects it. A trackpad's two-finger swipe
    /// moves the map, as it does in Maps, and arrives as a stream of small
    /// fractional deltas, often with a sideways part. A mouse wheel arrives in
    /// whole notches straight up or down, and zooms, as it does on Windows -
    /// 1.25 a notch, anchored on the pointer. ⌘ (or Control) with either zooms,
    /// smoothly, by however far the fingers moved.
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var delta = e.Delta;
        var position = e.GetPosition(this);
        var zoomChord = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var notch = delta.X == 0 && delta.Y != 0 && Math.Abs(delta.Y - Math.Round(delta.Y)) < 1e-9;

        if (zoomChord || notch)
        {
            ZoomAt(Math.Pow(1.25, Math.Clamp(delta.Y, -4, 4)), position);
        }
        else
        {
            // Avalonia scales a trackpad's deltas so that 1.0 is fifty points,
            // which is what a ScrollViewer moves by; the same here keeps the
            // map under the fingers.
            PanBy(new Vector(delta.X, delta.Y) * 50);
        }

        e.Handled = true;
    }

    /// <summary>A trackpad pinch - the gesture the Mac sends as "magnify".</summary>
    private void OnMagnify(object? sender, PointerDeltaEventArgs e)
    {
        ZoomAt(1 + e.Delta.X, e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>A touch-screen pinch, about the middle of the map: the Mac's pinch arrives as magnify, above.</summary>
    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (e.Scale <= 0) return;

        var factor = e.Scale / _lastPinchScale;
        _lastPinchScale = e.Scale;
        ZoomAt(factor, new Point(Bounds.Width / 2, Bounds.Height / 2));
        e.Handled = true;
    }

    private void PanBy(Vector delta)
    {
        StopAnimation();
        var offset = Clamp(_offset + delta, _zoom);
        if (offset == _offset) return;

        _offset = offset;
        InvalidateVisual();
    }

    /// <summary>
    /// Zooms anchored on a point: whatever spot on the map is under it stays
    /// under it afterward, so zooming dives toward the place being pointed at
    /// rather than toward the middle while the point of interest slides away.
    /// </summary>
    private void ZoomAt(double factor, Point anchor)
    {
        StopAnimation();
        EnsureLayout();

        var newZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 0.0001) return;

        _offset = Clamp(AnchoredOffset(newZoom, anchor), newZoom);
        _zoom = newZoom;
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    private Vector AnchoredOffset(double newZoom, Point anchor)
    {
        var anchorX = (anchor.X - _offset.X) / _zoom;
        var anchorY = (anchor.Y - _offset.Y) / _zoom;
        return new Vector(anchor.X - anchorX * newZoom, anchor.Y - anchorY * newZoom);
    }

    /// <summary>Zooms about the middle of the map, gliding there rather than jumping: the + and − buttons, ⌘+ and ⌘−.</summary>
    public void ZoomBy(double factor)
    {
        EnsureLayout();
        var target = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        AnimateTo(target, Clamp(AnchoredOffset(target, centre), target));
    }

    /// <summary>Back to the whole map, every place in view.</summary>
    public void ResetView() => AnimateTo(MinZoom, default);

    /// <summary>
    /// Glides from the current view to another over a fifth of a second,
    /// easing out. Zoom moves in proportion rather than in steps, so going in
    /// fourfold looks as smooth as going in twofold.
    /// </summary>
    private void AnimateTo(double targetZoom, Vector targetOffset)
    {
        StopAnimation();

        var startZoom = _zoom;
        var startOffset = _offset;
        if (Math.Abs(startZoom - targetZoom) < 0.0001 && startOffset == targetOffset) return;

        var started = DateTime.UtcNow;
        const double duration = 220;

        _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
        {
            var t = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / duration);
            var eased = 1 - Math.Pow(1 - t, 3);

            _zoom = Math.Exp(Math.Log(startZoom) + (Math.Log(targetZoom) - Math.Log(startZoom)) * eased);
            _offset = startOffset + (targetOffset - startOffset) * eased;
            if (t >= 1)
            {
                _zoom = targetZoom;
                _offset = targetOffset;
                StopAnimation();
            }

            InvalidateVisual();
            ViewChanged?.Invoke();
        });
        _animation.Start();
    }

    private void StopAnimation()
    {
        _animation?.Stop();
        _animation = null;
    }

    /// <summary>Where a place's pin head is, in this control's coordinates - for tests that click it.</summary>
    internal Point PinHeadOf(PlaceMarker marker)
    {
        EnsureLayout();
        var tip = LatLonToPoint(marker.Lat, marker.Lon);
        return new Point(tip.X, tip.Y - PinSize(marker.UsageCount) * PinScale(marker));
    }

    /// <summary>Where a coordinate is on screen, for tests.</summary>
    internal Point PointOf(double lat, double lon)
    {
        EnsureLayout();
        return LatLonToPoint(lat, lon);
    }

    /// <summary>The pin a click at this point would choose, for tests.</summary>
    internal PlaceMarker? MarkerAt(Point point)
    {
        EnsureLayout();
        return HitTest(point);
    }

    // ---- Colour -------------------------------------------------------------

    private Inks GetInks() => _inks ??= Inks.From(this);

    /// <summary>
    /// The map's colours, mixed from the palette for the appearance in force.
    /// Nothing is named outright: the sea is lapis and verdigris washed pale
    /// (or deep), the land is the card washed with gold, the coast is the
    /// bronze accent - so the parchment map of the light appearance becomes a
    /// lamplit one in the dark without a second set of colours to keep in step.
    /// </summary>
    private sealed class Inks
    {
        private readonly Dictionary<PlaceKind, PinInk> _pins = new();

        public required IBrush Sea { get; init; }
        public required IBrush ShallowFar { get; init; }
        public required IBrush ShallowNear { get; init; }
        public required IBrush Coast { get; init; }
        public required IBrush Land { get; init; }
        public required IBrush Graticule { get; init; }
        public required IBrush AxisLabel { get; init; }
        public required IBrush Ink { get; init; }
        public required IBrush WaterLabel { get; init; }
        public required IBrush Halo { get; init; }
        public required Pen HaloPen { get; init; }
        public required IBrush PinShadow { get; init; }
        public required FontFamily LabelFont { get; init; }

        public PinInk Pin(PlaceKind kind) => _pins[kind];

        public static Inks From(Control control)
        {
            Color C(string key)
            {
                if (control.TryFindResource(key, control.ActualThemeVariant, out var value) ||
                    (Application.Current?.TryGetResource(key, control.ActualThemeVariant, out value) ?? false))
                {
                    if (value is ISolidColorBrush brush)
                        return Color.FromArgb((byte)Math.Round(brush.Color.A * brush.Opacity), brush.Color.R, brush.Color.G, brush.Color.B);
                    if (value is Color color) return color;
                }

                return Colors.Gray;
            }

            var page = C("Cc.Page");
            var card = C("Cc.Card");
            var ink = C("Cc.Ink");
            var inkMuted = C("Cc.InkMuted");
            var inkFaint = C("Cc.InkFaint");
            var accentStrong = C("Cc.AccentStrong");
            var gold = C("Cc.Gold");
            var verdigris = C("Cc.Verdigris");
            var lapis = C("Cc.Lapis");
            var danger = C("Cc.Danger");

            var dark = Luminance(page) < 0.5;

            // The sea takes its hue from between the lapis and the verdigris and
            // is washed out to a pale, soft blue under parchment - or, in
            // Lamplight, deepened to a slate under a warm brown, the Windows dark
            // map's land and sea arrived at from this palette rather than copied.
            // A wash rather than a mix with the page: the page is warm, and warm
            // paper mixed into blue makes grey, not sea.
            var seaHue = Mix(lapis, verdigris, 0.45);
            var sea = dark ? Wash(seaHue, 0.24, 0.19) : Wash(seaHue, 0.30, 0.81);
            var shallowFar = dark ? Wash(seaHue, 0.24, 0.205) : Wash(seaHue, 0.30, 0.835);
            var shallowNear = dark ? Wash(seaHue, 0.24, 0.225) : Wash(seaHue, 0.30, 0.865);
            var land = Mix(card, gold, dark ? 0.17 : 0.15);
            var coast = dark ? Mix(land, gold, 0.6) : Mix(land, accentStrong, 0.62);

            FontFamily font = FontFamily.Default;
            if (control.TryFindResource("Cc.DisplayFont", out var family) && family is FontFamily display) font = display;

            var halo = new ImmutableSolidColorBrush(WithAlpha(dark ? Mix(sea, land, 0.5) : page, 0.82));

            var inks = new Inks
            {
                Sea = new ImmutableSolidColorBrush(sea),
                ShallowFar = new ImmutableSolidColorBrush(shallowFar),
                ShallowNear = new ImmutableSolidColorBrush(shallowNear),
                Coast = new ImmutableSolidColorBrush(coast),
                Land = new ImmutableSolidColorBrush(land),
                Graticule = new ImmutableSolidColorBrush(WithAlpha(dark ? inkFaint : inkMuted, dark ? 0.28 : 0.2)),
                AxisLabel = new ImmutableSolidColorBrush(dark ? inkFaint : inkMuted),
                Ink = new ImmutableSolidColorBrush(ink),
                WaterLabel = new ImmutableSolidColorBrush(Mix(ink, Turn(verdigris, 14), dark ? 0.45 : 0.55)),
                Halo = halo,
                HaloPen = new Pen(halo, 3.2) { LineJoin = PenLineJoin.Round },
                PinShadow = new ImmutableSolidColorBrush(WithAlpha(Colors.Black, dark ? 0.45 : 0.22)),
                LabelFont = font
            };

            // Pin fill per kind, chosen for separation at pin size against both
            // land and sea rather than for prettiness: at eight points across,
            // hue is nearly all there is to read, so the five are spaced round
            // the wheel instead of being shades of one colour.
            //
            // Cities keep the terracotta of the Windows map, which is the
            // palette's own Danger. Sanctuaries are the palette's gold. The
            // palette has no violet or green, so battlefields - which would
            // naturally be red, and cannot be, the red being the cities' - turn
            // the lapis toward violet, the furthest point on the wheel from the
            // map's own colours, and regions turn the verdigris toward green.
            // Rivers and seas take the verdigris nudged to turquoise rather than
            // anything blue: blue is the sea most of them sit in, and a river pin
            // the colour of the water under it is invisible.
            void Add(PlaceKind kind, Color fill) => inks._pins[kind] = PinInk.For(fill, dark);

            Add(PlaceKind.City, danger);
            Add(PlaceKind.Sanctuary, dark ? gold : Mix(gold, accentStrong, 0.25));
            Add(PlaceKind.Battlefield, Turn(lapis, 52));
            Add(PlaceKind.Region, Turn(verdigris, -52));
            Add(PlaceKind.Water, Turn(verdigris, 14));

            return inks;
        }
    }

    private sealed record PinInk(ImmutableSolidColorBrush Fill, ImmutableSolidColorBrush Outline, IBrush Eye)
    {
        public static PinInk For(Color fill, bool dark) => new(
            new ImmutableSolidColorBrush(fill),
            new ImmutableSolidColorBrush(Mix(fill, Colors.Black, dark ? 0.55 : 0.45)),
            new ImmutableSolidColorBrush(Mix(fill, Colors.White, dark ? 0.7 : 0.78)));
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t),
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>A colour's hue at a given saturation and lightness: a palette colour washed pale or deepened.</summary>
    private static Color Wash(Color c, double saturation, double lightness) =>
        new HslColor(1, c.ToHsl().H, saturation, lightness).ToRgb();

    private static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(255 * alpha), c.R, c.G, c.B);

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    /// <summary>The same colour turned round the hue wheel, keeping its weight: a palette colour's sibling.</summary>
    private static Color Turn(Color c, double degrees)
    {
        var hsl = c.ToHsl();
        var hue = (hsl.H + degrees) % 360;
        if (hue < 0) hue += 360;
        return new HslColor(hsl.A, hue, hsl.S, hsl.L).ToRgb();
    }
}
