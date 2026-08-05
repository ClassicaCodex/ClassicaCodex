namespace ClassicaCodex.UI;

/// <summary>
/// How large text is drawn in the reader and the workbench, across sessions.
///
/// Two sizes, not one. Polytonic Greek is why this exists at all - breathings,
/// accents and iota subscript are what you need to see in order to look a word
/// up, and at 11pt on a high-resolution display they are a few pixels each, so
/// the step before meaning fails before meaning is attempted. English has no
/// such marks and does not need the same size to be legible.
///
/// But the two sit side by side in the reader, and text at two different sizes
/// in adjacent panes reads as a mistake rather than as a setting. So they are
/// linked by default and move together, and come apart only when someone says
/// they should - which is the minority case of wanting the Greek larger while
/// keeping more English on screen.
///
/// Stored as a plain file under %LocalAppData%, the same approach
/// SpeechSettings and TranslationSettings use: machine-local configuration,
/// not corpus data, and so not something that belongs in a library file that
/// might be copied between machines.
///
/// The Changed event exists because the reader and the workbench can both be
/// open at once. Without it, changing size in one would leave the other at the
/// old size until reopened, which reads as the setting not having worked.
/// </summary>
public static class ReadingFontSettings
{
    /// <summary>
    /// What the panes shipped with before this was configurable. Kept as the
    /// default so an existing reader sees no change until they ask for one.
    /// </summary>
    public const float DefaultSize = 11F;

    /// <summary>
    /// Below 8pt diacritics stop resolving at all, and above 28pt a verse line
    /// stops fitting across the pane, which costs more than the size gains.
    /// Both ends are clamps rather than validation errors - a settings file
    /// edited by hand should be corrected, not refused.
    /// </summary>
    public const float MinimumSize = 8F;
    public const float MaximumSize = 28F;

    private static float? _source;
    private static float? _translation;
    private static bool? _linked;

    public static event Action? Changed;

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "reading-font-size.txt");

    /// <summary>Size of Greek and Latin text.</summary>
    public static float SourceSize
    {
        get { EnsureLoaded(); return _source!.Value; }
    }

    /// <summary>Size of English translations.</summary>
    public static float TranslationSize
    {
        get { EnsureLoaded(); return _translation!.Value; }
    }

    /// <summary>Whether the two move together.</summary>
    public static bool Linked
    {
        get { EnsureLoaded(); return _linked!.Value; }
    }

    public static void SetSource(float size)
    {
        EnsureLoaded();

        var clamped = Clamp(size);
        var target = _linked!.Value ? clamped : _translation!.Value;
        Store(clamped, target, _linked.Value);
    }

    public static void SetTranslation(float size)
    {
        EnsureLoaded();

        var clamped = Clamp(size);
        var target = _linked!.Value ? clamped : _source!.Value;
        Store(target, clamped, _linked.Value);
    }

    /// <summary>
    /// Linking pulls the translation to the source size rather than the other
    /// way round or to some average. The source size is the one someone has
    /// been deliberately adjusting - it is the reason the dialog was opened -
    /// so it is the one that should survive the two being joined.
    /// </summary>
    public static void SetLinked(bool linked)
    {
        EnsureLoaded();

        if (linked == _linked!.Value) return;
        Store(_source!.Value, linked ? _source.Value : _translation!.Value, linked);
    }

    private static void Store(float source, float translation, bool linked)
    {
        var unchanged = Math.Abs(source - _source!.Value) < 0.01F
                        && Math.Abs(translation - _translation!.Value) < 0.01F
                        && linked == _linked!.Value;
        if (unchanged) return;

        _source = source;
        _translation = translation;
        _linked = linked;

        try
        {
            var directory = Path.GetDirectoryName(SettingsFile)!;
            Directory.CreateDirectory(directory);

            var culture = System.Globalization.CultureInfo.InvariantCulture;
            File.WriteAllText(SettingsFile,
                $"source={source.ToString(culture)}\n"
                + $"translation={translation.ToString(culture)}\n"
                + $"linked={(linked ? "true" : "false")}\n");
        }
        catch
        {
            // Failing to persist is not worth interrupting reading over - the
            // sizes still apply for this session.
        }

        // Fired once for the whole change rather than per value: a linked
        // change alters both sizes, and two events would make every listener
        // relayout twice for one user action.
        Changed?.Invoke();
    }

    private static void EnsureLoaded()
    {
        if (_source.HasValue) return;

        _source = DefaultSize;
        _translation = DefaultSize;
        _linked = true;

        try
        {
            if (!File.Exists(SettingsFile)) return;

            var text = File.ReadAllText(SettingsFile).Trim();
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            var style = System.Globalization.NumberStyles.Float;

            // The first version of this file held a bare number and nothing
            // else. Reading it as the source size, with the translation
            // matched and linked on, is what that file meant - so an early
            // reader keeps their setting instead of being silently reset.
            if (float.TryParse(text, style, culture, out var legacy))
            {
                _source = Clamp(legacy);
                _translation = _source;
                return;
            }

            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (key == "source" && float.TryParse(value, style, culture, out var s)) _source = Clamp(s);
                else if (key == "translation" && float.TryParse(value, style, culture, out var t)) _translation = Clamp(t);
                else if (key == "linked") _linked = value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Unreadable preference file - use the defaults rather than
            // throwing on startup over a corrupted settings file, the same
            // fallback SpeechSettings uses.
            _source = DefaultSize;
            _translation = DefaultSize;
            _linked = true;
        }
    }

    private static float Clamp(float size) => Math.Clamp(size, MinimumSize, MaximumSize);
}
