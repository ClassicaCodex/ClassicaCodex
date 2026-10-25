using System.Reflection;
using ClassicaCodex.Ingestion;
using PdfSharp.Fonts;

namespace ClassicaCodex.Mac.Features.Export;

/// <summary>
/// Puts the Mac's font resolver where PdfSharp will use it, and keeps it
/// there.
///
/// The shared PassageExportService installs its own resolver on the first
/// PDF it writes: GlobalFontSettings.FontResolver = new WindowsFontResolver(),
/// unconditionally, remembered in a private static flag so it only happens
/// once. Installing the Mac's resolver first is therefore not enough - the
/// first export would put the Windows one back over it. So the flag is
/// marked as already done before the Mac's resolver goes in, and the shared
/// code then leaves it alone.
///
/// That reaches into a private field of a class this project does not own,
/// which is a workaround rather than a design. The change that would retire
/// it is one line in PassageExportService.EnsureFontResolverRegistered:
/// install WindowsFontResolver only when GlobalFontSettings.FontResolver is
/// still null. Until then, if the field is ever renamed, the same effect is
/// had by letting the shared code register once - a PDF of nothing, to a
/// scratch file, which on a Mac fails at its first font - and replacing what
/// it registered.
///
/// Every Mac caller of PassageExportService.ExportPdf goes through
/// <see cref="EnsureInstalled"/> first.
/// </summary>
public static class PdfFonts
{
    private static readonly object Gate = new();
    private static MacFontResolver? _resolver;

    /// <summary>
    /// Scans the font folders, the first time, and installs the resolver.
    /// Safe from any thread; the scan reads a few hundred name tables, so
    /// callers run it off the UI thread.
    /// </summary>
    public static void EnsureInstalled()
    {
        lock (Gate)
        {
            if (_resolver != null) return;

            var resolver = new MacFontResolver(FontCatalog.Scan(FontCatalog.SystemDirectories));

            ClaimSharedRegistration();

            // Clears whatever PdfSharp cached under the Windows resolver, if
            // anything reached it first - a face it failed to load stays
            // failed until the cache is reset.
            GlobalFontSettings.ResetFontManagement();
            GlobalFontSettings.FontResolver = resolver;
            _resolver = resolver;
        }
    }

    /// <summary>
    /// The one installed family a reading font's list stands for - what an
    /// export names as its font, so the .docx and the PDF of the same passage
    /// are set in the same face.
    /// </summary>
    public static string FamilyFor(string families)
    {
        EnsureInstalled();
        return _resolver!.FamilyFor(families);
    }

    /// <summary>How many faces were found to embed. None means no PDF can be written here.</summary>
    public static int FaceCount
    {
        get
        {
            EnsureInstalled();
            return _resolver!.FaceCount;
        }
    }

    private static void ClaimSharedRegistration()
    {
        var flag = typeof(PassageExportService).GetField("_fontResolverRegistered", BindingFlags.NonPublic | BindingFlags.Static);
        if (flag?.FieldType == typeof(bool))
        {
            flag.SetValue(null, true);
            return;
        }

        var scratch = Path.Combine(Path.GetTempPath(), $"classicacodex-pdf-{Guid.NewGuid():N}.pdf");
        try
        {
            PassageExportService.ExportPdf(scratch, " ", " ", Array.Empty<(string, string)>(), "Times New Roman");
        }
        catch (Exception)
        {
            // Expected: the Windows resolver has no fonts to give on a Mac.
            // What mattered was that it registered, and it has.
        }
        finally
        {
            try
            {
                File.Delete(scratch);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A stray empty file in the temporary folder is harmless.
            }
        }
    }
}
