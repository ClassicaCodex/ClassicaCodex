using System.Text;
using PdfSharp.Fonts;

namespace ClassicaCodex.Mac.Features.Export;

/// <summary>One face of an installed font: where its bytes are and what it calls itself.</summary>
internal sealed record FontFace(
    string Family,
    string? TypographicFamily,
    bool Bold,
    bool Italic,
    int Weight,
    int Width,
    string Path,
    int CollectionIndex)
{
    /// <summary>The name PdfSharp is told, and hands back to <see cref="MacFontResolver.GetFont"/>.</summary>
    public string Key => $"{Path}#{CollectionIndex}";
}

/// <summary>
/// The fonts PDF export can embed, read from the font folders themselves.
///
/// Only faces with TrueType outlines are listed. PdfSharp embeds glyf-based
/// fonts and nothing else, so an OpenType face with PostScript outlines would
/// be offered here and then fail inside the export, which is worse than its
/// never having been offered.
///
/// Read from each file's own name table rather than inferred from its file
/// name: macOS keeps Palatino, Times, Geeza Pro and Arial Hebrew in
/// collections whose file names say nothing about which faces are inside.
/// </summary>
internal sealed class FontCatalog
{
    private readonly Dictionary<string, List<FontFace>> _byFamily = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Where a Mac keeps fonts - the system's, the machine's, the reader's -
    /// and then where a Linux machine does, so the export can be exercised
    /// by the headless tests on a machine that is not a Mac.
    /// </summary>
    public static IReadOnlyList<string> SystemDirectories
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                "/System/Library/Fonts",
                "/Library/Fonts",
                System.IO.Path.Combine(home, "Library", "Fonts"),
                "/usr/share/fonts",
                "/usr/local/share/fonts",
                System.IO.Path.Combine(home, ".local", "share", "fonts"),
                System.IO.Path.Combine(home, ".fonts")
            };
        }
    }

    public int FaceCount { get; private set; }

    public static FontCatalog Scan(IEnumerable<string> directories)
    {
        var catalog = new FontCatalog();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

        foreach (var directory in directories.Distinct(StringComparer.Ordinal))
        {
            if (!Directory.Exists(directory)) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (extension is not (".ttf" or ".otf" or ".ttc" or ".otc")) continue;

                foreach (var face in SfntReader.ReadFaces(file)) catalog.Add(face);
            }
        }

        return catalog;
    }

    private void Add(FontFace face)
    {
        FaceCount++;
        AddUnder(face.Family, face);
        if (face.TypographicFamily != null && !string.Equals(face.TypographicFamily, face.Family, StringComparison.OrdinalIgnoreCase))
            AddUnder(face.TypographicFamily, face);
    }

    private void AddUnder(string family, FontFace face)
    {
        if (!_byFamily.TryGetValue(family, out var faces)) _byFamily[family] = faces = new List<FontFace>();
        faces.Add(face);
    }

    public bool Has(string family) => _byFamily.ContainsKey(family);

    public IReadOnlyList<FontFace> FacesOf(string family) =>
        _byFamily.TryGetValue(family, out var faces) ? faces : Array.Empty<FontFace>();

    public IEnumerable<string> Families => _byFamily.Keys;
}

/// <summary>
/// Hands PdfSharp the font files a Mac actually has.
///
/// PdfSharp 6 does not look for fonts by name on its own; it asks an
/// IFontResolver for the bytes. The shared export service installs
/// WindowsFontResolver, which reads the Windows registry - on a Mac there is
/// no registry, every family resolves to an Arial it then cannot find, and
/// the export fails with a bare null reference. This is the Mac's answer to
/// the same question, installed in its place by <see cref="PdfFonts"/>.
///
/// A family is asked for the way the reader names it: a list, as Avalonia
/// and CSS write one ("Palatino, Palatino Linotype, DejaVu Serif, serif"),
/// tried in order. A name that is not installed is looked up under the names
/// it goes by elsewhere - the Windows app asks for Palatino Linotype, which a
/// Mac calls Palatino - and if nothing matches, a serif that carries
/// polytonic Greek is used rather than failing the export: a passage in the
/// wrong face is still the passage, and the export is for the text.
/// </summary>
internal sealed class MacFontResolver : IFontResolver
{
    private readonly FontCatalog _catalog;
    private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>The same faces under the names other systems give them.</summary>
    private static readonly string[][] Equivalents =
    {
        new[] { "Palatino", "Palatino Linotype", "Book Antiqua", "TeX Gyre Pagella", "P052", "URW Palladio L" },
        new[] { "Times New Roman", "Times", "Liberation Serif", "Tinos", "Nimbus Roman", "FreeSerif" },
        new[] { "Georgia", "Gelasio" },
        new[] { "Arial", "Helvetica", "Liberation Sans", "Arimo", "Nimbus Sans", "FreeSans" }
    };

    /// <summary>
    /// When nothing asked for is installed: serifs that set polytonic Greek
    /// as well as Latin, since the original-language passages are the ones a
    /// substitute face is most likely to lose.
    /// </summary>
    private static readonly string[] SerifFallback =
    {
        "Palatino", "Times New Roman", "Georgia", "Iowan Old Style", "DejaVu Serif", "FreeSerif",
        "Liberation Serif", "Noto Serif"
    };

    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "$Default"
    };

    public MacFontResolver(FontCatalog catalog) => _catalog = catalog;

    /// <summary>How many faces it found to embed - none means PDF export cannot work on this machine.</summary>
    public int FaceCount => _catalog.FaceCount;

    /// <summary>
    /// The installed family a list stands for: the first one present, under
    /// any of its names, else the serif fallback. What a Word document names
    /// as its font, so the .docx and the PDF of the same passage agree.
    /// </summary>
    public string FamilyFor(string families)
    {
        foreach (var name in Candidates(families))
        {
            if (_catalog.Has(name)) return name;
        }

        return SerifFallback.FirstOrDefault(_catalog.Has)
               ?? Split(families).FirstOrDefault()
               ?? "Times New Roman";
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var family = FamilyFor(familyName);
        var faces = _catalog.FacesOf(family);
        if (faces.Count == 0)
        {
            // Nothing by that name or any fallback: any face at all, so the
            // export is in some font rather than none.
            var anyFamily = _catalog.Families.FirstOrDefault();
            if (anyFamily == null) return null;
            faces = _catalog.FacesOf(anyFamily);
        }

        var face = BestFace(faces, isBold, isItalic);
        return new FontResolverInfo(face.Key, isBold && !face.Bold, isItalic && !face.Italic);
    }

    public byte[]? GetFont(string faceName)
    {
        lock (_gate)
        {
            if (_bytes.TryGetValue(faceName, out var cached)) return cached;
        }

        var hash = faceName.LastIndexOf('#');
        if (hash < 0 || !int.TryParse(faceName[(hash + 1)..], out var index)) return null;

        var bytes = SfntReader.ReadFaceBytes(faceName[..hash], index);
        if (bytes == null) return null;

        lock (_gate)
        {
            _bytes[faceName] = bytes;
        }

        return bytes;
    }

    /// <summary>
    /// The face closest to the style asked for. A bold face is never used for
    /// regular text, nor an italic for upright; the other way round PdfSharp
    /// can simulate the style, which is better than a heavier or slanted
    /// passage nobody asked for.
    /// </summary>
    private static FontFace BestFace(IReadOnlyList<FontFace> faces, bool bold, bool italic) =>
        faces.OrderBy(f =>
            {
                var penalty = 0;
                if (f.Bold && !bold) penalty += 1000;
                if (f.Italic && !italic) penalty += 1000;
                if (bold && !f.Bold) penalty += 100;
                if (italic && !f.Italic) penalty += 100;
                penalty += Math.Abs(f.Weight - (bold ? 700 : 400)) / 10;
                penalty += Math.Abs(f.Width - 5) * 20;
                return penalty;
            })
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .First();

    private static IEnumerable<string> Candidates(string families)
    {
        foreach (var name in Split(families))
        {
            yield return name;

            var group = Equivalents.FirstOrDefault(g => g.Contains(name, StringComparer.OrdinalIgnoreCase));
            if (group == null) continue;

            foreach (var alias in group) yield return alias;
        }
    }

    private static IEnumerable<string> Split(string families) =>
        families.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.Trim('"', '\''))
            .Where(n => n.Length > 0 && !GenericNames.Contains(n));
}

/// <summary>
/// Just enough of the OpenType file format to list a file's faces and to
/// take one face out of a collection.
/// </summary>
internal static class SfntReader
{
    private const uint CollectionTag = 0x74746366; // 'ttcf'

    /// <summary>Every embeddable face in a font file; none if the file cannot be read.</summary>
    public static List<FontFace> ReadFaces(string path)
    {
        var faces = new List<FontFace>();

        try
        {
            using var stream = File.OpenRead(path);
            if (ReadUInt32(stream, 0) == CollectionTag)
            {
                var count = ReadUInt32(stream, 8);
                for (var i = 0; i < Math.Min(count, 256); i++)
                {
                    var offset = ReadUInt32(stream, 12 + 4 * i);
                    if (ReadFace(stream, offset, path, i) is { } face) faces.Add(face);
                }
            }
            else if (ReadFace(stream, 0, path, 0) is { } face)
            {
                faces.Add(face);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException)
        {
            // A damaged or unreadable font is one face fewer to choose from,
            // not a reason to stop looking.
        }

        return faces;
    }

    /// <summary>
    /// The bytes of one face, as a standalone font. PdfSharp reads a single
    /// font and nothing else - handed a whole collection it fails - so a face
    /// in a .ttc is rebuilt on its own: its tables, copied, under a table
    /// directory pointing at where they now sit.
    /// </summary>
    public static byte[]? ReadFaceBytes(string path, int index)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (data.Length < 12 || ReadUInt32(data, 0) != CollectionTag) return data;

        var faceCount = ReadUInt32(data, 8);
        if (index < 0 || index >= faceCount) return null;

        var offset = (int)ReadUInt32(data, 12 + 4 * index);
        var tableCount = ReadUInt16(data, offset + 4);
        var headerLength = 12 + 16 * tableCount;

        var tables = new List<(int Record, int Offset, int Length)>();
        var total = headerLength;
        for (var t = 0; t < tableCount; t++)
        {
            var record = offset + 12 + 16 * t;
            var tableOffset = (int)ReadUInt32(data, record + 8);
            var length = (int)ReadUInt32(data, record + 12);
            tables.Add((record, tableOffset, length));
            total += (length + 3) & ~3;
        }

        var result = new byte[total];
        Array.Copy(data, offset, result, 0, 12);

        var position = headerLength;
        for (var t = 0; t < tables.Count; t++)
        {
            var (record, tableOffset, length) = tables[t];
            var target = 12 + 16 * t;

            Array.Copy(data, record, result, target, 8); // tag and checksum, unchanged
            WriteUInt32(result, target + 8, (uint)position);
            WriteUInt32(result, target + 12, (uint)length);
            Array.Copy(data, tableOffset, result, position, length);

            position += (length + 3) & ~3;
        }

        return result;
    }

    private static FontFace? ReadFace(Stream stream, long offset, string path, int index)
    {
        var tableCount = ReadUInt16(stream, offset + 4);
        var tables = new Dictionary<string, (long Offset, int Length)>(StringComparer.Ordinal);

        for (var t = 0; t < tableCount; t++)
        {
            var record = offset + 12 + 16 * t;
            var tag = Encoding.ASCII.GetString(ReadBytes(stream, record, 4));
            tables[tag] = (ReadUInt32(stream, record + 8), (int)ReadUInt32(stream, record + 12));
        }

        // TrueType outlines only: see FontCatalog.
        if (!tables.ContainsKey("glyf") || !tables.TryGetValue("name", out var name)) return null;

        var names = ReadBytes(stream, name.Offset, Math.Min(name.Length, 1 << 20));
        var family = NameString(names, 1);
        if (string.IsNullOrWhiteSpace(family)) return null;

        var typographic = NameString(names, 16);

        bool bold, italic;
        int weight = 400, width = 5;

        if (tables.TryGetValue("OS/2", out var os2) && os2.Length >= 64)
        {
            weight = ReadUInt16(stream, os2.Offset + 4);
            width = ReadUInt16(stream, os2.Offset + 6);
            var selection = ReadUInt16(stream, os2.Offset + 62);
            italic = (selection & 0x0001) != 0 || (selection & 0x0200) != 0;
            bold = (selection & 0x0020) != 0;
        }
        else if (tables.TryGetValue("head", out var head) && head.Length >= 46)
        {
            var macStyle = ReadUInt16(stream, head.Offset + 44);
            bold = (macStyle & 0x1) != 0;
            italic = (macStyle & 0x2) != 0;
            weight = bold ? 700 : 400;
        }
        else
        {
            bold = italic = false;
        }

        return new FontFace(family.Trim(), typographic?.Trim(), bold, italic, weight, width, path, index);
    }

    /// <summary>
    /// A name from the name table, preferring the Windows English record,
    /// which every font made in the last thirty years carries, then any
    /// Unicode record, then the old Macintosh one.
    /// </summary>
    private static string? NameString(byte[] table, int nameId)
    {
        if (table.Length < 6) return null;

        var count = ReadUInt16(table, 2);
        var storage = ReadUInt16(table, 4);

        string? best = null;
        var bestScore = -1;

        for (var i = 0; i < count; i++)
        {
            var record = 6 + 12 * i;
            if (record + 12 > table.Length) break;

            if (ReadUInt16(table, record + 6) != nameId) continue;

            var platform = ReadUInt16(table, record);
            var language = ReadUInt16(table, record + 4);
            var length = ReadUInt16(table, record + 8);
            var start = storage + ReadUInt16(table, record + 10);
            if (start + length > table.Length) continue;

            var score = platform switch
            {
                3 when language == 0x0409 => 4,
                3 => 3,
                0 => 2,
                1 when language == 0 => 1,
                _ => 0
            };
            if (score <= bestScore) continue;

            best = platform is 3 or 0
                ? Encoding.BigEndianUnicode.GetString(table, start, length)
                : Encoding.Latin1.GetString(table, start, length);
            bestScore = score;
        }

        return best;
    }

    private static byte[] ReadBytes(Stream stream, long offset, int count)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static uint ReadUInt32(Stream stream, long offset)
    {
        var b = ReadBytes(stream, offset, 4);
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static ushort ReadUInt16(Stream stream, long offset)
    {
        var b = ReadBytes(stream, offset, 2);
        return (ushort)(b[0] << 8 | b[1]);
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);

    private static ushort ReadUInt16(byte[] data, int offset) => (ushort)(data[offset] << 8 | data[offset + 1]);

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
}
