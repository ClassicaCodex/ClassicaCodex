using System.Runtime.InteropServices;

namespace ClassicaCodex.UI;

/// <summary>
/// Loads the app's icon set from an "Icons" folder sitting next to the
/// executable, caching each one at the size it's asked for.
///
/// Every lookup returns null rather than throwing when a file is missing,
/// and callers treat null as "no icon" - so the app runs perfectly well
/// with the folder absent, just with text-only buttons. That matters
/// because the icons are an optional embellishment, not something worth
/// crashing a startup over if a deployment forgets to copy them.
/// </summary>
public static class AppIcons
{
    // Keyed by theme too, not just name/size: the icon sheet was drawn for
    // a light surface, and a couple of the more muted colors (Bookmarks'
    // gold, notably) sit too close in brightness to the dark button fill to
    // read clearly - see the lightening step below. Caching separately per
    // theme means that correction only ever applies once per icon+size+mode,
    // and never serves a light-mode render where it isn't wanted.
    private static readonly Dictionary<(string Name, int Size, bool IsDark), Image?> Cache = new();

    private static string IconDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Icons");

    /// <summary>
    /// The named icon scaled to a square of the given size, or null if the
    /// file isn't present. Names match the PNG filenames without extension
    /// ("MythNetwork", "Bookmarks", and so on).
    /// </summary>
    public static Image? Get(string name, int size = 20)
    {
        var isDark = ReadingTheme.IsDark;
        var key = (name, size, isDark);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        Image? result = null;

        try
        {
            // An icon that ships a light variant is one of the illustrated
            // set: the file loaded in dark mode is already its dark-mode
            // artwork, so lifting it only washes it out. Everything without
            // one is an older glyph drawn in dark ink for a light surface,
            // which genuinely disappears against a dark button.
            //
            // This replaced a luminance threshold, which was a guess that
            // went wrong as soon as the artwork changed: stripping the tile
            // panels moved Stylometry from 95 to 65 against a threshold of
            // 70, so it would have started being brightened for no reason
            // other than its bar chart being a bit darker than its siblings.
            var brightenForDarkMode = !HasLightVariant(name);

            var path = ResolvePath(name, isDark);
            if (path != null)
            {
                // Loaded via a stream copy rather than Image.FromFile, which
                // keeps a lock on the file for the lifetime of the Image.
                using var source = Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));

                var scaled = new Bitmap(size, size);
                using (var graphics = Graphics.FromImage(scaled))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    // Only the glyph-style icons want brightening. The set
                    // now mixes two kinds: line glyphs drawn on transparency,
                    // and full tiles with their own opaque background. On a
                    // tile the transform lifts the background rather than the
                    // artwork, turning a deliberate dark panel into a grey
                    // square that reads worse than leaving it alone -
                    // measured on this set, the tiles sit at a mean luminance
                    // of 71 against a button fill of 48, so they're already
                    // clear of it and need no help.
                    if (isDark && brightenForDarkMode)
                    {
                        // Lightens every pixel 30% of the way toward white
                        // (alpha untouched) - not recoloring, just brightening.
                        // Measured against the dark button fill (RGB 48,48,52):
                        // Bookmarks' muted gold sits at a color distance of
                        // ~49 before this and ~89 after - roughly where an
                        // icon that already reads fine, like AutoTag, starts
                        // out. Icons that are already bright are pushed a
                        // little brighter too, but the transform can only
                        // approach white, never overshoot it, so nothing
                        // already-legible gets harmed.
                        var lighten = new System.Drawing.Imaging.ColorMatrix(new float[][]
                        {
                            new float[] { 0.7f, 0,    0,    0, 0 },
                            new float[] { 0,    0.7f, 0,    0, 0 },
                            new float[] { 0,    0,    0.7f, 0, 0 },
                            new float[] { 0,    0,    0,    1, 0 },
                            new float[] { 0.3f, 0.3f, 0.3f, 0, 1 }
                        });
                        using var attributes = new System.Drawing.Imaging.ImageAttributes();
                        attributes.SetColorMatrix(lighten);

                        graphics.DrawImage(source,
                            new Rectangle(0, 0, size, size),
                            0, 0, source.Width, source.Height,
                            GraphicsUnit.Pixel, attributes);
                    }
                    else
                    {
                        graphics.DrawImage(source, 0, 0, size, size);
                    }
                }

                result = scaled;
            }
        }
        catch
        {
            // A missing or unreadable icon is cosmetic - fall through to null.
        }

        Cache[key] = result;
        return result;
    }

    /// <summary>
    /// Whether this icon ships a light-mode counterpart, which is what marks
    /// it as one of the illustrated set rather than an older line glyph.
    /// </summary>
    private static bool HasLightVariant(string name) =>
        File.Exists(Path.Combine(IconDirectory, "Light", name + ".png"));

    /// <summary>
    /// The file to load for this icon in this theme.
    ///
    /// The illustrated icons are pale artwork on transparency, which reads
    /// on a dark button as drawn. On a light one it needs darkening, so each
    /// has a counterpart in Icons/Light holding the same shape with the
    /// artwork brought down. That isn't cosmetic: measured across the set,
    /// the artwork against parchment sits at a contrast ratio of about 2.3
    /// to 2.8 undarkened, and about 4.4 to 9.2 darkened.
    ///
    /// Falls back to the single shared file when no light variant exists,
    /// which is the case for every older glyph icon - those were drawn for a
    /// light surface to begin with and get the brightening pass in dark mode
    /// instead.
    /// </summary>
    private static string? ResolvePath(string name, bool isDark)
    {
        if (!isDark)
        {
            var light = Path.Combine(IconDirectory, "Light", name + ".png");
            if (File.Exists(light)) return light;
        }

        var shared = Path.Combine(IconDirectory, name + ".png");
        return File.Exists(shared) ? shared : null;
    }


    /// <summary>
    /// Puts an icon on a button, left of its text, and leaves the button
    /// untouched if that icon isn't available.
    /// </summary>
    public static void Apply(Button button, string name, int size = 20)
    {
        var image = Get(name, size);
        if (image == null) return;

        button.Image = image;

        // An IconButton has no text, so the image is the whole content and
        // belongs in the middle. The layout below is for a labelled button,
        // and applied here it would shove the icon to the left edge and
        // leave a gap where the text would have been.
        if (button is IconButton)
        {
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.Padding = Padding.Empty;
            return;
        }

        button.ImageAlign = ContentAlignment.MiddleLeft;
        button.TextAlign = ContentAlignment.MiddleRight;
        button.TextImageRelation = TextImageRelation.ImageBeforeText;

        // Nudge the text clear of the glyph so the two don't crowd.
        button.Padding = new Padding(2, 0, 4, 0);
    }

    /// <summary>
    /// Same as the Button overload, for a RadioButton. RadioButton doesn't
    /// share a type with Button (both descend separately from ButtonBase),
    /// but it lays out Image/Text the same way once the check glyph's own
    /// space is set aside - see PassageExportForm's format picker, where
    /// this puts the format icon between the radio dot and its label.
    /// </summary>
    public static void Apply(RadioButton radioButton, string name, int size = 20)
    {
        var image = Get(name, size);
        if (image == null) return;

        radioButton.Image = image;
        radioButton.ImageAlign = ContentAlignment.MiddleLeft;
        radioButton.TextAlign = ContentAlignment.MiddleRight;
        radioButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        radioButton.Padding = new Padding(2, 0, 4, 0);
    }

    /// <summary>
    /// Sets a form's title-bar/taskbar icon to the named icon, if that icon
    /// exists - left as the OS default otherwise, same "never force it"
    /// policy as Apply/Get.
    ///
    /// Form.Icon needs an actual Icon (a native HICON), not the Bitmap
    /// everything else here deals in, so one is created via
    /// Bitmap.GetHicon(). That allocates a GDI handle .NET doesn't track or
    /// free by itself, so it's released via DestroyIcon when the form
    /// closes - otherwise every dialog opened over a session would leak one.
    /// </summary>
    public static void ApplyWindowIcon(Form form, string name)
    {
        var image = Get(name, 32);
        if (image is not Bitmap bitmap) return;

        var handle = bitmap.GetHicon();
        var icon = Icon.FromHandle(handle);
        form.Icon = icon;

        form.FormClosed += (_, _) =>
        {
            icon.Dispose();
            DestroyIcon(handle);
        };
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
