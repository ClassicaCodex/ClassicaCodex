using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Control = Avalonia.Controls.Control;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ClassicaCodex.Mac.Common;

/// <summary>
/// Copies and exports a result table: to the pasteboard as tab-separated
/// rows, or to CSV, tab-separated text or an Excel workbook.
///
/// The writing is the Windows ResultExport's, rule for rule - the BOM that
/// keeps Greek titles from turning to mojibake in Excel, the refusal to turn
/// "1.10" into the number 1.1, the cut at Excel's 32,767-character cell
/// limit - because a spreadsheet exported on a Mac should be the same file
/// one exported on Windows would be. Only the menu and the save panel are new.
/// </summary>
public static class TableExport
{
    /// <summary>
    /// Gives a control a right-click menu that copies and exports what
    /// <paramref name="rows"/> returns. The first row is the header.
    /// </summary>
    public static ContextMenu Attach(
        Control control,
        Func<string> suggestedName,
        Func<IReadOnlyList<IReadOnlyList<string>>> rows,
        Func<IReadOnlyList<IReadOnlyList<string>>>? selectedRows = null,
        Func<IReadOnlyList<string>>? notes = null)
    {
        var menu = control.ContextMenu ?? new ContextMenu();
        var items = new List<Control>();

        if (selectedRows != null)
            items.Add(Item("Copy Selected Rows", "Copy", async () => await CopyAsync(control, selectedRows())));
        items.Add(Item("Copy All Rows", "Copy", async () => await CopyAsync(control, rows())));
        items.Add(new Separator());
        items.Add(Item("Export as CSV…", "Export", async () => await ExportAsync(control, rows(), suggestedName(), notes, "csv")));
        items.Add(Item("Export as Tab-Separated Text…", "Export", async () => await ExportAsync(control, rows(), suggestedName(), notes, "txt")));
        items.Add(Item("Export as Excel Workbook…", "Export", async () => await ExportAsync(control, rows(), suggestedName(), notes, "xlsx")));

        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        foreach (var item in items) menu.Items.Add(item);

        control.ContextMenu = menu;
        return menu;
    }

    private static MenuItem Item(string header, string glyph, Func<Task> action)
    {
        var item = new MenuItem { Header = header, Icon = Glyphs.Icon(glyph, 14) };
        item.Click += async (_, _) => await action();
        return item;
    }

    public static async Task CopyAsync(Control anywhere, IReadOnlyList<IReadOnlyList<string>> table)
    {
        if (table.Count == 0) return;

        // Tab-separated, because that is what pastes into Numbers or Excel as
        // columns rather than as one mangled cell.
        var text = string.Join(Environment.NewLine, table.Select(r => string.Join("\t", r)));
        await ClipboardText.SetAsync(anywhere, text);
    }

    public static async Task ExportAsync(
        Control anywhere,
        IReadOnlyList<IReadOnlyList<string>> table,
        string suggestedName,
        Func<IReadOnlyList<string>>? notes,
        string extension)
    {
        var owner = TopLevel.GetTopLevel(anywhere) as Window;
        if (owner == null) return;

        // A header row on its own is not a result. Exporting it would produce
        // a file that looks like an answer and contains none.
        if (table.Count <= 1)
        {
            await Alert.Info(owner, "Nothing to export", "Run something first - there are no results yet.");
            return;
        }

        var type = extension switch
        {
            "csv" => Files.Csv,
            "txt" => Files.Tsv,
            _ => Files.Excel
        };

        var path = await Files.SaveAsync(owner, "Export Results", $"{suggestedName}.{extension}", type);
        if (path == null) return;

        try
        {
            Write(path, table, notes?.Invoke() ?? Array.Empty<string>(), extension);
        }
        catch (Exception ex)
        {
            await Alert.Error(owner, "Export failed", $"Could not write the file.\n\n{ex.Message}");
        }
    }

    /// <summary>Writes a table to <paramref name="path"/> in the format the extension names.</summary>
    public static void Write(string path, IReadOnlyList<IReadOnlyList<string>> table, IReadOnlyList<string> notes, string extension)
    {
        switch (extension)
        {
            case "csv": WriteDelimited(path, table, notes, ","); break;
            case "txt": WriteDelimited(path, table, notes, "\t"); break;
            default: WriteWorkbook(path, table, notes); break;
        }
    }

    private static void WriteDelimited(
        string path, IReadOnlyList<IReadOnlyList<string>> table, IReadOnlyList<string> notes, string delimiter)
    {
        var sb = new StringBuilder();

        // Notes first, each commented, so a spreadsheet shows them as text in
        // column A and a script can skip them on the leading character.
        foreach (var note in notes) sb.AppendLine("# " + note);
        if (notes.Count > 0) sb.AppendLine();

        foreach (var row in table)
            sb.AppendLine(string.Join(delimiter, row.Select(v => Quote(v, delimiter))));

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Quote(string value, string delimiter)
    {
        if (!value.Contains(delimiter) && !value.Contains('"') && !value.Contains('\n'))
            return value;

        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    private static void WriteWorkbook(string path, IReadOnlyList<IReadOnlyList<string>> table, IReadOnlyList<string> notes)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        worksheetPart.Worksheet = new Worksheet(data);

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Results" });

        foreach (var note in notes) data.Append(new Row(TextCell(note)));
        if (notes.Count > 0) data.Append(new Row());

        foreach (var row in table)
        {
            var xlRow = new Row();

            foreach (var value in row)
            {
                if (IsSafelyANumber(value, out var number))
                {
                    xlRow.Append(new Cell
                    {
                        DataType = CellValues.Number,
                        CellValue = new CellValue(number.ToString("R", CultureInfo.InvariantCulture))
                    });
                }
                else
                {
                    xlRow.Append(TextCell(value));
                }
            }

            data.Append(xlRow);
        }

        workbookPart.Workbook.Save();
    }

    /// <summary>
    /// Whether a value can go into a numeric cell without changing what it
    /// says - "1.10" is a passage, not the number 1.1. See the Windows
    /// ResultExport for the whole argument.
    /// </summary>
    internal static bool IsSafelyANumber(string value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return false;

        return string.Equals(number.ToString("R", CultureInfo.InvariantCulture), trimmed, StringComparison.Ordinal);
    }

    private static Cell TextCell(string value) => new()
    {
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(WithinExcelsCellLimit(value)))
    };

    internal static string WithinExcelsCellLimit(string value)
    {
        const int ExcelCellLimit = 32767;
        const string Marker = "… [cut: too long for one spreadsheet cell]";

        if (string.IsNullOrEmpty(value) || value.Length <= ExcelCellLimit) return value;

        var room = ExcelCellLimit - Marker.Length;
        if (char.IsHighSurrogate(value[room - 1])) room--;

        return value[..room] + Marker;
    }
}
