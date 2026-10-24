using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data;
using ClassicaCodex.Mac.Features.Artifacts;
using ClassicaCodex.Mac.Features.PlacesMap;
using ClassicaCodex.Mac.Features.Timeline;
using ClassicaCodex.Mac.Shell;
using ClassicaCodex.UI;
using Xunit;

namespace ClassicaCodex.Mac.Tests.Features;

/// <summary>
/// The Places Map, the Timeline and the Perseus artifact browser, driven
/// against the sample library the way a reader would drive them: through the
/// tool menu, the pointer and the keyboard.
///
/// The sample library carries no Art &amp; Archaeology catalogue, and the
/// tests never reach Perseus: the artifact browser's photographs are always
/// fetched live, so its offline and online states are both played here with
/// the network stood in for.
/// </summary>
public class MapAndTimelineTests
{
    [AvaloniaFact]
    public async Task Places_map_pins_every_place_and_lists_the_passages_a_pin_finds()
    {
        var main = await OpenSampleLibraryAsync();
        if (main == null) return;

        try
        {
            main.OpenTool("places-map");
            await Screens.Settle(8);

            var map = main.OwnedWindows.OfType<PlacesMapWindow>().Single();
            Assert.Equal(PlaceData.All().Count, map.Canvas.Markers.Count);
            Assert.Equal(1.0, map.Canvas.Zoom);

            // A second click on the tool brings the open map forward instead of
            // stacking another on it.
            main.OpenTool("places-map");
            await Screens.Settle(2);
            Assert.Single(main.OwnedWindows.OfType<PlacesMapWindow>());

            // The legend counts each kind, and every count adds up to the catalogue.
            var boxes = map.GetVisualDescendants().OfType<CheckBox>().ToList();
            Assert.Equal(Enum.GetValues<PlaceKind>().Length, boxes.Count);
            Screens.Save(map, "10-places-map-light");

            // A mouse-wheel notch zooms on the pointer, as on Windows: whatever is
            // under it stays under it.
            var athens = map.Canvas.Markers.First(m => m.Name == "Athens");
            var athensTip = map.Canvas.PointOf(athens.Lat, athens.Lon);
            for (var i = 0; i < 4; i++) map.MouseWheel(ToWindow(map, athensTip), new Vector(0, 1));
            await Screens.Settle(2);
            Assert.Equal(Math.Pow(1.25, 4), map.Canvas.Zoom, 3);
            var after = map.Canvas.PointOf(athens.Lat, athens.Lon);
            Assert.True(Math.Abs(after.X - athensTip.X) < 1 && Math.Abs(after.Y - athensTip.Y) < 1, $"Athens moved from {athensTip} to {after}");

            // A trackpad's two-finger swipe pans rather than zooms.
            var zoomBefore = map.Canvas.Zoom;
            map.MouseWheel(ToWindow(map, athensTip), new Vector(0.37, -0.21));
            await Screens.Settle(1);
            Assert.Equal(zoomBefore, map.Canvas.Zoom, 6);
            var panned = map.Canvas.PointOf(athens.Lat, athens.Lon);
            Assert.True(Math.Abs(panned.X - after.X - 18.5) < 0.5, $"a swipe of 0.37 should move the map 18.5 points, moved {panned.X - after.X}");

            // Click Athens' pin with the pointer.
            var head = ToWindow(map, map.Canvas.PinHeadOf(athens));
            map.MouseMove(head);
            map.MouseDown(head, MouseButton.Left);
            map.MouseUp(head, MouseButton.Left);
            await Screens.Settle(2);
            Assert.Same(athens, map.Canvas.Selected);

            await map.PendingLoad;
            await Screens.Settle(4);
            Assert.NotEmpty(map.Passages);
            Assert.Contains("passage", map.ResultsStatus);

            // The library has no Art & Archaeology catalogue, and the browser says
            // so rather than implying Athens has no objects.
            Assert.Equal("No linked artifacts found.", map.Artifacts.HeaderText);
            Assert.Contains("Setup", map.Artifacts.EmptyNoteText);

            Screens.Save(map, "11-places-map-athens-light");

            // Unticking a kind takes its pins off the map and out of reach of a click.
            var cities = boxes[Array.IndexOf(Enum.GetValues<PlaceKind>(), PlaceKind.City)];
            cities.IsChecked = false;
            await Screens.Settle(2);
            Assert.False(map.Canvas.IsKindVisible(PlaceKind.City));
            Assert.NotEqual(athens, map.Canvas.MarkerAt(map.Canvas.PinHeadOf(athens)));
            Screens.Save(map, "12-places-map-no-cities");
            cities.IsChecked = true;
            await Screens.Settle(2);
            Assert.Same(athens, map.Canvas.MarkerAt(map.Canvas.PinHeadOf(athens)));

            // ⌘0 goes home.
            map.KeyPress(Key.D0, RawInputModifiers.Meta, PhysicalKey.Digit0, "0");
            await Screens.Settle(10);
            Assert.Equal(1.0, map.Canvas.Zoom, 3);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            await Screens.Settle(6);
            Screens.Save(map, "13-places-map-dark");

            // Into the Aegean, where the names compete hardest.
            var aegean = map.Canvas.PointOf(38.2, 24.5);
            for (var i = 0; i < 9; i++) map.MouseWheel(ToWindow(map, aegean), new Vector(0, 1));
            await Screens.Settle(4);
            Screens.Save(map, "14-places-map-aegean-dark");

            // Double-clicking open sea resets the view.
            Assert.True(map.Canvas.Zoom > 5);
            var openSea = FindPointWithoutPin(map.Canvas);
            var sea = ToWindow(map, openSea);
            map.MouseDown(sea, MouseButton.Left);
            map.MouseUp(sea, MouseButton.Left);
            map.MouseDown(sea, MouseButton.Left);
            map.MouseUp(sea, MouseButton.Left);
            await Screens.Settle(10);
            Assert.Equal(1.0, map.Canvas.Zoom, 3);

            // The map jumps through the main window's own navigation...
            Assert.Same(main, map.OnNavigate?.Target);

            // ...and what it asks for is recorded here rather than carried out:
            // the reader's side of a jump is the shell's to test.
            var jumps = new List<(int WorkId, long TextNodeId)>();
            map.OnNavigate = (workId, textNodeId) =>
            {
                jumps.Add((workId, textNodeId));
                return Task.CompletedTask;
            };

            // Click a passage, press Return: the reader goes to it, and the map
            // stays open beside the reader for the next one.
            var first = map.Passages[0];
            var firstRow = map.PassageList.ContainerFromIndex(0)!;
            var firstCentre = ToWindow(map, new Point(firstRow.Bounds.Width / 2, firstRow.Bounds.Height / 2), firstRow);
            map.MouseDown(firstCentre, MouseButton.Left);
            map.MouseUp(firstCentre, MouseButton.Left);
            await Screens.Settle(2);
            Assert.Equal(0, map.PassageList.SelectedIndex);
            map.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            await Screens.Settle(2);
            Assert.Equal(new[] { (first.WorkId, first.TextNodeId) }, jumps);
            Assert.True(map.IsVisible);

            // So does a double-click.
            var otherIndex = Math.Min(3, map.Passages.Count - 1);
            map.PassageList.ScrollIntoView(otherIndex);
            await Screens.Settle(4);
            var row = map.PassageList.ContainerFromIndex(otherIndex)!;
            var rowCentre = ToWindow(map, new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), row);
            map.MouseDown(rowCentre, MouseButton.Left);
            map.MouseUp(rowCentre, MouseButton.Left);
            map.MouseDown(rowCentre, MouseButton.Left);
            map.MouseUp(rowCentre, MouseButton.Left);
            await Screens.Settle(2);
            Assert.Equal((map.Passages[otherIndex].WorkId, map.Passages[otherIndex].TextNodeId), jumps.Last());

            map.Close();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Places_map_draws_the_real_coastline_when_world_map_data_is_installed()
    {
        // Natural Earth's land file is a setup download, not part of the sample
        // library; CC_NATURAL_EARTH points at a copy of it when there is one.
        if (Environment.GetEnvironmentVariable("CC_NATURAL_EARTH") is not { } geojson || !File.Exists(geojson)) return;

        var main = await OpenSampleLibraryAsync();
        if (main == null) return;

        var root = Path.Combine(Path.GetTempPath(), "classicacodex-map-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "map"));
        File.Copy(geojson, Path.Combine(root, "map", "ne_110m_land.geojson"));

        // The map reads the file from the data folder. Pointed there for the
        // one read the cache keeps, and straight back, so no other test sees a
        // different data folder.
        var chosen = typeof(DataFolderSettings).GetField("_chosenThisSession",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var previous = chosen.GetValue(null);

        try
        {
            chosen.SetValue(null, root);
            NaturalEarthCoastline.InvalidateCache();
            Assert.NotNull(NaturalEarthCoastline.Load());
            chosen.SetValue(null, previous);

            main.OpenTool("places-map");
            await Screens.Settle(8);
            var map = main.OwnedWindows.OfType<PlacesMapWindow>().Single();
            await Screens.Settle(4);
            Assert.True(map.Canvas.UsingRealCoastline);
            Screens.Save(map, "15-places-map-natural-earth-light");

            await map.ShowPlaceAsync("Delphi");
            await Screens.Settle(4);
            var delphi = map.Canvas.PointOf(38.48, 22.5);
            for (var i = 0; i < 7; i++) map.MouseWheel(ToWindow(map, delphi), new Vector(0, 1));
            await Screens.Settle(4);
            Screens.Save(map, "16-places-map-natural-earth-greece-light");

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            await Screens.Settle(6);
            Screens.Save(map, "17-places-map-natural-earth-greece-dark");
            map.Close();
        }
        finally
        {
            chosen.SetValue(null, previous);
            NaturalEarthCoastline.InvalidateCache();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            main.Close();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task Timeline_dates_the_authors_and_opens_a_work_from_one()
    {
        var main = await OpenSampleLibraryAsync();
        if (main == null) return;

        try
        {
            main.OpenTool("timeline");
            await Screens.Settle(4);

            var timeline = main.OwnedWindows.OfType<TimelineWindow>().Single();
            await timeline.PendingLoad;
            await Screens.Settle(4);

            // All seven sample authors are on record, oldest first.
            Assert.Equal(7, timeline.Canvas.Entries.Count);
            Assert.Equal(timeline.Canvas.Entries.OrderBy(e => e.StartYear).Select(e => e.AuthorId), timeline.Canvas.Entries.Select(e => e.AuthorId));
            Assert.Contains("rough consensus estimates, not settled fact", timeline.StatusText);
            Assert.Contains("0 others in your library have no date on record", timeline.StatusText);

            main.OpenTool("timeline");
            await Screens.Settle(2);
            Assert.Single(main.OwnedWindows.OfType<TimelineWindow>());

            Screens.Save(timeline, "20-timeline-light");

            // A click anywhere on an author's row - here, on the name - lists their works.
            var homer = timeline.Canvas.Entries.First(e => e.Name == "Homer");
            var row = ToWindow(timeline, timeline.Canvas.RowCentreOf(homer), timeline.Canvas);
            timeline.MouseMove(row);
            timeline.MouseDown(row, MouseButton.Left);
            timeline.MouseUp(row, MouseButton.Left);
            await Screens.Settle(2);
            await timeline.PendingLoad;
            await Screens.Settle(2);

            Assert.Same(homer, timeline.Canvas.Selected);
            Assert.NotEmpty(timeline.Works);
            Assert.All(timeline.Works, w => Assert.Equal(homer.AuthorId, w.AuthorId));
            Screens.Save(timeline, "21-timeline-homer-light");

            // Double-clicking a work opens it in the reader.
            timeline.WorkList.SelectedIndex = 0;
            await Screens.Settle(2);
            var item = timeline.WorkList.ContainerFromIndex(0)!;
            var centre = ToWindow(timeline, new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), item);
            timeline.MouseDown(centre, MouseButton.Left);
            timeline.MouseUp(centre, MouseButton.Left);
            timeline.MouseDown(centre, MouseButton.Left);
            timeline.MouseUp(centre, MouseButton.Left);
            await Screens.Settle(10);
            Assert.Equal(timeline.Works[0].WorkId, main.OpenWork?.WorkId);

            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            await Screens.Settle(6);
            Screens.Save(timeline, "22-timeline-homer-dark");

            timeline.Close();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            main.Close();
        }
    }

    [AvaloniaFact]
    public async Task Artifact_browser_says_why_it_is_empty_and_fetches_photographs_only_live()
    {
        var main = await OpenSampleLibraryAsync();
        if (main == null) return;

        try
        {
            ArtifactsFeature.Open(main, "Athena", "Athena");
            await Screens.Settle(4);

            var window = main.OwnedWindows.OfType<ArtifactBrowserWindow>().Single();
            await window.Loading;
            await Screens.Settle(2);

            // No catalogue in this library: the empty state says what to do about it.
            Assert.Equal("Artifacts — Athena", window.Title);
            Assert.Equal("No linked artifacts found.", window.Browser.HeaderText);
            Assert.Equal(ArtifactBrowser.MissingCatalogueNote, window.Browser.EmptyNoteText);
            Screens.Save(window, "30-artifacts-empty");

            ArtifactsFeature.Open(main, "Athena", "Athena");
            await Screens.Settle(2);
            Assert.Single(main.OwnedWindows.OfType<ArtifactBrowserWindow>());

            var browser = window.Browser;
            var images = new Dictionary<string, List<ArtifactImage>>
            {
                ["aa_vase"] = new()
                {
                    new() { ArtifactId = "aa_vase", ImageId = "1990.26.0801", Caption = "Side A: Athena", Credits = "Courtesy of the Museum of Fine Arts, Boston" },
                    new() { ArtifactId = "aa_vase", ImageId = "1990.26.0802", Caption = "Side B" }
                },
                ["aa_coin"] = new() { new() { ArtifactId = "aa_coin", ImageId = "1990.26.1001", Caption = "Obverse" } },
                ["aa_site"] = new()
            };
            browser.ImagesFor = id => Task.FromResult(images[id]);

            var artifacts = new List<Artifact>
            {
                new() { ArtifactId = "aa_vase", Type = "Vase", Name = "Boston 01.8027", Description = "Athena mounting a chariot, Herakles beside her." },
                new() { ArtifactId = "aa_coin", Type = "Coin", Name = "Athens tetradrachm", Description = "Head of Athena; owl on the reverse." },
                new() { ArtifactId = "aa_site", Type = "Site", Name = "Athens, Acropolis" }
            };

            // Offline: the description still shows, and the photograph's place
            // says why it is missing rather than staying blank.
            var fetched = new List<string>();
            ArtifactBrowser.FetchOverride = (url, _) =>
            {
                fetched.Add(url);
                throw new HttpRequestException("No route to host");
            };

            browser.LoadArtifacts(artifacts);
            await browser.PendingRender;
            await Screens.Settle(2);

            Assert.Equal("Vase - Boston 01.8027 (1 of 3)", browser.HeaderText);
            Assert.Contains("Couldn't reach Perseus", browser.FrameMessage);
            Assert.Contains("Herakles", browser.CaptionText);
            Assert.Contains("Museum of Fine Arts", browser.CreditText);
            Assert.Contains("never saved to this computer", browser.ConditionsLine);
            Assert.False(browser.HasPhoto);
            Assert.Equal(new[] { ArtifactBrowser.BuildIiifThumbnailUrl("1990.26.0801") }, fetched);
            Screens.Save(window, "31-artifacts-offline");

            // Online, with Perseus stood in for: the photograph shows, and paging
            // back to it is served from memory, not fetched twice.
            var photo = SamplePhoto();
            fetched.Clear();
            ArtifactBrowser.FetchOverride = (url, _) =>
            {
                fetched.Add(url);
                return Task.FromResult(photo);
            };

            browser.LoadArtifacts(artifacts);
            await browser.PendingRender;
            await Screens.Settle(2);
            Assert.True(browser.HasPhoto);
            Assert.Equal(string.Empty, browser.FrameMessage);
            Screens.Save(window, "32-artifacts-photo");

            browser.LoadArtifacts(artifacts);
            await browser.PendingRender;
            Assert.Single(fetched);

            // An object with no photograph keeps its words.
            browser.LoadArtifacts(new List<Artifact> { artifacts[2] });
            await browser.PendingRender;
            await Screens.Settle(2);
            Assert.Contains("(no photo available for this object)", browser.CaptionText);
            Assert.Equal(string.Empty, browser.ConditionsLine);

            window.Close();
        }
        finally
        {
            ArtifactBrowser.FetchOverride = null;
            main.Close();
        }
    }

    /// <summary>The sample library, open in a main window; null when there is no sample library to test against.</summary>
    private static async Task<MainWindow?> OpenSampleLibraryAsync()
    {
        if (Screens.SampleLibrary is not { } library) return null;

        DbConnectionFactory.Configure(library, remember: false);
        await SchemaInitializer.EnsureSchemaAsync();

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var window = new MainWindow { Width = 1360, Height = 860 };
        window.Show();
        await Screens.Settle(10);
        return window;
    }

    /// <summary>A point in one of a window's controls, in the window's own coordinates.</summary>
    private static Point ToWindow(Window window, Point point, Visual? from = null)
    {
        var source = from ?? (Visual)((PlacesMapWindow)window).Canvas;
        return source.TranslatePoint(point, window) ?? point;
    }

    /// <summary>Somewhere in the middle of the visible map that no pin answers.</summary>
    private static Point FindPointWithoutPin(MapCanvas canvas)
    {
        var centre = new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
        for (var radius = 0; radius < 300; radius += 15)
        {
            for (var angle = 0; angle < 360; angle += 30)
            {
                var p = centre + new Vector(Math.Cos(angle * Math.PI / 180), Math.Sin(angle * Math.PI / 180)) * radius;
                if (canvas.MarkerAt(p) == null) return p;
            }
        }

        throw new InvalidOperationException("Every point near the middle of the map has a pin.");
    }

    /// <summary>A small picture to stand in for a Perseus photograph.</summary>
    private static byte[] SamplePhoto()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(240, 160), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(196, 120, 64)), null, new Rect(0, 0, 240, 160));
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(40, 28, 20)), null, new Point(120, 80), 60, 52);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }
}
