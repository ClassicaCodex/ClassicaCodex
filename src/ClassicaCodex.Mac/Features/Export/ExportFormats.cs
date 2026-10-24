using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ClassicaCodex.Ingestion;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Platform;

namespace ClassicaCodex.Mac.Features.Export;

public enum ExportFormat { Text, Word, Pdf, BibTeX, Ris }

/// <summary>
/// The "Export as" choice, as one row of radio buttons with the file glyph
/// beside each - the Windows dialogs' format panel.
/// </summary>
internal sealed class FormatPicker : UserControl
{
    private readonly List<(ExportFormat Format, RadioButton Button)> _buttons = new();

    public FormatPicker(bool includeReferences)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };

        void Add(ExportFormat format, string label, string glyph)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(Glyphs.Icon(glyph, 15));
            content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });

            var button = new RadioButton
            {
                GroupName = "export-format",
                Content = content,
                Margin = new Thickness(0, 0, 18, 4),
                IsChecked = format == ExportFormat.Text
            };
            _buttons.Add((format, button));
            row.Children.Add(button);
        }

        Add(ExportFormat.Text, "Plain text (.txt)", "FileText");
        Add(ExportFormat.Word, "Word document (.docx)", "FileDoc");
        Add(ExportFormat.Pdf, "PDF (.pdf)", "FilePdf");

        if (includeReferences)
        {
            Add(ExportFormat.BibTeX, "BibTeX (.bib)", "Quotes");
            Add(ExportFormat.Ris, "RIS (.ris)", "Quotes");
        }

        Content = row;
    }

    public ExportFormat Selected
    {
        get => _buttons.FirstOrDefault(b => b.Button.IsChecked == true).Format;
        set
        {
            foreach (var (format, button) in _buttons) button.IsChecked = format == value;
        }
    }
}

/// <summary>Writing an export once its content is settled: the file, then the offer to show it.</summary>
internal static class ExportFiles
{
    public static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Text => "txt",
        ExportFormat.Word => "docx",
        ExportFormat.Pdf => "pdf",
        ExportFormat.BibTeX => "bib",
        _ => "ris"
    };

    public static FilePickerFileType FileType(ExportFormat format) => format switch
    {
        ExportFormat.Text => Files.Text,
        ExportFormat.Word => Files.Word,
        ExportFormat.Pdf => Files.Pdf,
        ExportFormat.BibTeX => new FilePickerFileType("BibTeX") { Patterns = new[] { "*.bib" } },
        _ => new FilePickerFileType("RIS") { Patterns = new[] { "*.ris" } }
    };

    /// <summary>A file name with nothing in it the file system would refuse.</summary>
    public static string SafeName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name;
    }

    /// <summary>
    /// Writes the passage through the shared export service, off the UI
    /// thread: a whole work as a PDF is measured word by word, and the first
    /// PDF of a session also reads the font folders.
    /// </summary>
    /// <param name="fontFamilies">The reading font's family list; the export uses the first of them installed.</param>
    public static Task WriteAsync(
        ExportFormat format, string path, string title, string source,
        IReadOnlyList<(string Label, string Text)> chunks, string fontFamilies) =>
        Task.Run(() =>
        {
            switch (format)
            {
                case ExportFormat.Text:
                    PassageExportService.ExportText(path, title, source, chunks);
                    break;
                case ExportFormat.Word:
                    PassageExportService.ExportDocx(path, title, source, chunks, PdfFonts.FamilyFor(fontFamilies));
                    break;
                case ExportFormat.Pdf:
                    PdfFonts.EnsureInstalled();
                    PassageExportService.ExportPdf(path, title, source, chunks, PdfFonts.FamilyFor(fontFamilies));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format), format, "References are written by the window that builds them.");
            }
        });

    /// <summary>The Windows "open the containing folder?" - here, the file selected in a Finder window.</summary>
    public static async Task OfferToRevealAsync(Window owner, string path)
    {
        if (await Alert.Confirm(owner, "Done", $"Exported to {path}.\n\nShow it in the Finder?", "Show in Finder", "Close", AlertKind.Info))
            MacShell.Reveal(path);
    }
}
