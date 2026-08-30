using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ClassicaCodex.UI;

/// <summary>
/// Right-click export for the result tables on the validation benches.
///
/// WHY THIS TAKES ROWS RATHER THAN READING THE LIST. A ListView holds strings
/// that were formatted for a screen: margins rounded to three decimals,
/// correlations to two, percentages to none. Exporting those would hand an
/// analyst the rounded numbers and nothing else - and this project has already
/// spent several rounds recomputing statistics from three-decimal screen reads
/// and getting slightly different answers from the application that produced
/// them.
///
/// So a caller supplies a row provider that pulls from the underlying result
/// objects at full precision, and the export carries what the run actually
/// computed. Where no provider is given the visible cells are used, which is
/// better than nothing and is labelled as such in the file.
///
/// Invariant culture throughout. A CSV written with a comma decimal separator
/// and comma delimiters is not a CSV, and a file that parses differently on a
/// German machine than an American one is worse than one that is merely
/// inconvenient.
/// </summary>
internal static class ResultExport
{
    /// <summary>
    /// Adds a context menu with copy and export options to a results list.
    /// </summary>
    /// <param name="rows">
    /// Full-precision rows, header first. When null the ListView's own
    /// formatted cells are exported instead.
    /// </param>
    /// <param name="suggestedName">
    /// Evaluated when the file is written, not when the menu is attached. A
    /// name fixed at attach time follows the form rather than the data: loading
    /// an experiment into a window opened on a different author produced
    /// "perturbation-Aelian.csv" containing Plato. The header was right by then
    /// and the filename still lied, which is the same failure one layer out.
    /// </param>
    /// <param name="notes">
    /// Lines written above the table - the run's settings, seed, pool. Without
    /// them an exported table is a set of numbers whose provenance has to be
    /// remembered, and the whole point of a seed is that it does not.
    /// </param>
    public static void AttachTo(
        ListView list,
        Func<string> suggestedName,
        Func<IReadOnlyList<IReadOnlyList<string>>>? rows = null,
        Func<IReadOnlyList<string>>? notes = null)
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Copy selected rows", null, (_, _) => Copy(list, rows, selectedOnly: true));
        menu.Items.Add("Copy all rows", null, (_, _) => Copy(list, rows, selectedOnly: false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Export to CSV...", null, (_, _) => Export(list, suggestedName(), rows, notes, "csv"));
        menu.Items.Add("Export to tab-separated text...", null, (_, _) => Export(list, suggestedName(), rows, notes, "txt"));
        menu.Items.Add("Export to Excel...", null, (_, _) => Export(list, suggestedName(), rows, notes, "xlsx"));

        // Themed on open, and here rather than at each call site.
        //
        // A ContextMenuStrip is not in the control tree a theme toggle walks,
        // so a menu themed once when its form was built keeps the mode that was
        // current then. Three of the four benches using this had never themed
        // theirs at all - dark ink on a dark surface, and only found by knowing
        // the menu was there. Doing it on Opening fixes those, survives a
        // toggle made while the window is open, and means the next caller
        // cannot forget.
        menu.Opening += (_, _) => ReadingTheme.ApplyToContextMenu(menu);

        list.ContextMenuStrip = menu;
    }

    private static IReadOnlyList<IReadOnlyList<string>> Gather(
        ListView list, Func<IReadOnlyList<IReadOnlyList<string>>>? rows, bool selectedOnly)
    {
        if (rows != null && !selectedOnly) return rows();

        var table = new List<IReadOnlyList<string>>
        {
            list.Columns.Cast<ColumnHeader>().Select(c => c.Text).ToList()
        };

        var items = selectedOnly && list.SelectedItems.Count > 0
            ? list.SelectedItems.Cast<ListViewItem>()
            : list.Items.Cast<ListViewItem>();

        foreach (var item in items)
            table.Add(item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text).ToList());

        return table;
    }

    private static void Copy(
        ListView list, Func<IReadOnlyList<IReadOnlyList<string>>>? rows, bool selectedOnly)
    {
        var table = Gather(list, rows, selectedOnly);
        if (table.Count == 0) return;

        // Tab-separated, because that is what pastes into a spreadsheet as
        // columns rather than as one mangled cell.
        var text = string.Join(Environment.NewLine, table.Select(r => string.Join("\t", r)));

        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // The clipboard is occasionally held by another process. Nothing
            // useful to do, and nothing worth interrupting the user over.
        }
    }

    private static void Export(
        ListView list,
        string suggestedName,
        Func<IReadOnlyList<IReadOnlyList<string>>>? rows,
        Func<IReadOnlyList<string>>? notes,
        string extension)
    {
        var table = Gather(list, rows, selectedOnly: false);
        if (table.Count == 0)
        {
            MessageBox.Show("Nothing to export - run something first.",
                "No results", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var filter = extension switch
        {
            "csv" => "Comma-separated values (*.csv)|*.csv",
            "txt" => "Tab-separated text (*.txt)|*.txt",
            _ => "Excel workbook (*.xlsx)|*.xlsx"
        };

        var safe = string.Join("_", suggestedName.Split(Path.GetInvalidFileNameChars()));

        using var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = $"{safe}.{extension}",
            Title = "Export results"
        };

        if (dialog.ShowDialog() != DialogResult.OK) return;

        var header = notes?.Invoke() ?? Array.Empty<string>();

        try
        {
            switch (extension)
            {
                case "csv": WriteDelimited(dialog.FileName, table, header, ","); break;
                case "txt": WriteDelimited(dialog.FileName, table, header, "\t"); break;
                default: WriteWorkbook(dialog.FileName, table, header); break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not write the file.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void WriteDelimited(
        string path,
        IReadOnlyList<IReadOnlyList<string>> table,
        IReadOnlyList<string> notes,
        string delimiter)
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

    /// <summary>
    /// Quotes a field if it contains anything that would break the row.
    ///
    /// The BOM on the file matters as much as this does: Excel opens a UTF-8
    /// CSV without one as if it were the system code page, which turns every
    /// Greek work title into mojibake. Menota manuscript titles carry
    /// characters that survive nothing else.
    /// </summary>
    private static string Quote(string value, string delimiter)
    {
        if (!value.Contains(delimiter) && !value.Contains('"') && !value.Contains('\n'))
            return value;

        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    private static void WriteWorkbook(
        string path,
        IReadOnlyList<IReadOnlyList<string>> table,
        IReadOnlyList<string> notes)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        worksheetPart.Worksheet = new Worksheet(data);

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Results"
        });

        foreach (var note in notes)
            data.Append(new Row(TextCell(note)));

        if (notes.Count > 0) data.Append(new Row());

        foreach (var row in table)
        {
            var xlRow = new Row();

            foreach (var value in row)
            {
                // Numbers written as numbers, so a column can be sorted and
                // charted without a reimport. Anything that is not a clean
                // number - "25/25", "Euripides (23/25)", a work title - stays
                // text, and the invariant parse is what keeps a decimal comma
                // from turning 0.020 into twenty.
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
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
    /// An inline string cell - no shared-string table.
    ///
    /// A shared string table is smaller for a document that repeats text often,
    /// and it is one more part that has to stay consistent with the sheet. For
    /// tables of a few hundred rows the saving is irrelevant and the risk of
    /// writing an index that points at the wrong string is not.
    /// </summary>
    private static Cell TextCell(string value) => new()
    {
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value))
    };
}
