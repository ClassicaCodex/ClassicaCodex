using ClassicaCodex.Core;
using System.Globalization;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Ingestion;

namespace ClassicaCodex.UI;

/// <summary>
/// Lines up two or more translations of the SAME work side by side - three
/// different English renderings of Agamemnon, say - rather than Compare
/// Sources' comparison across different authors sharing a tag. Each column
/// is that translation's full running text, independently scrollable; there's
/// no "nothing tagged" case here, since an ingested edition always has text.
/// </summary>
public class CompareTranslationsForm : ScaledForm
{
    private readonly ListBox _workList;
    private readonly CheckedListBox _translationCheckList;
    private readonly Button _compareButton;
    private readonly Panel _columnsHost;
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

    private List<(int WorkId, string AuthorName, string WorkTitle)> _works = new();
    private List<Edition> _currentTranslations = new();

    /// <summary>
    /// The rendered columns, kept so they can be re-laid-out when the window
    /// is resized. Wrapped rows are measured against the column's width at the
    /// moment they're added, and WinForms gives no way to ask a ListBox to
    /// measure again - so the rows are put back to make it happen.
    /// </summary>
    private readonly List<(ListBox List, Edition Translation, List<TextNode> Nodes)> _columns = new();

    public CompareTranslationsForm()
    {
        Text = "Compare Translations";
        AppIcons.ApplyWindowIcon(this, "CompareTexts");
        Width = 1200;
        Height = 780;
        StartPosition = FormStartPosition.CenterParent;

        var workLabel = new Label { Text = "Works with 2+ translations:", Left = 12, Top = 10, Width = 260 };
        _workList = new ListBox
        {
            Left = 12,
            Top = 32,
            Width = 260,
            Height = 300,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _workList.SelectedIndexChanged += async (_, _) => await LoadTranslationsAsync();

        var translationLabel = new Label { Text = "Translations to compare:", Left = 12, Top = 344, Width = 260 };
        _translationCheckList = new CheckedListBox
        {
            Left = 12,
            Top = 366,
            Width = 260,
            Height = 260,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            CheckOnClick = true
        };

        _compareButton = new Button
        {
            Text = "Compare Selected",
            Left = 12,
            Top = 646,
            Width = 260,
            Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _compareButton.Click += async (_, _) => await RenderColumnsAsync();

        _columnsHost = new Panel
        {
            Left = 284,
            Top = 32,
            Width = 900,
            Height = 648,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(workLabel);
        Controls.Add(_workList);
        Controls.Add(translationLabel);
        Controls.Add(_translationCheckList);
        Controls.Add(_compareButton);
        Controls.Add(_columnsHost);

        Load += async (_, _) => await LoadWorksAsync();
        ResizeEnd += (_, _) => RewrapColumns();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task LoadWorksAsync()
    {
        _works = await _editionRepo.GetWorksWithMultipleTranslationsAsync();
        _workList.Items.Clear();

        if (_works.Count == 0)
        {
            _workList.Items.Add("(no work has 2+ translations ingested yet)");
            return;
        }

        foreach (var w in _works)
        {
            _workList.Items.Add($"{w.AuthorName}, {w.WorkTitle}");
        }
    }

    private async Task LoadTranslationsAsync()
    {
        _translationCheckList.Items.Clear();
        _currentTranslations = new();

        var index = _workList.SelectedIndex;
        if (index < 0 || index >= _works.Count) return;

        var editions = await _editionRepo.GetByWorkAsync(_works[index].WorkId);
        _currentTranslations = editions.Where(e => e.Kind == EditionKind.Translation).ToList();

        // Checked by default: with usually just two or three translations
        // ingested for a work, the common case is comparing all of them,
        // not picking a subset - unlike Compare Sources, where a tag can
        // easily match a dozen+ authors and an explicit choice matters more.
        foreach (var t in _currentTranslations)
        {
            _translationCheckList.Items.Add(TranslatorLabel(t), true);
        }
    }

    /// <summary>
    /// Same fallback MainForm's own edition dropdown uses when a translation
    /// wasn't tagged with a translator name in its source metadata - the
    /// last segment of its CTS identifier, which is at least distinguishing
    /// even if not a proper name.
    /// </summary>
    private static string TranslatorLabel(Edition edition)
    {
        var generated = GeneratedAt(edition);

        if (!string.IsNullOrWhiteSpace(edition.Translator))
        {
            // Two AI renderings of the same work are both called "A.I." and
            // are otherwise indistinguishable in a column header, which is no
            // use when the point of the screen is telling renderings apart.
            return generated == null
                ? edition.Translator
                : $"{edition.Translator} - {generated.Value.ToLocalTime():d MMM yyyy, HH:mm}";
        }

        var suffix = edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(suffix) ? "Untitled translation" : suffix;
    }

    /// <summary>
    /// When an AI translation was made, read back out of its CTS identifier.
    ///
    /// CreateTranslationForm mints those as "...ai-gemini-20260808193042", so
    /// the moment is already recorded and needs no column of its own. Anything
    /// that isn't one of those returns null and is labelled as before.
    /// </summary>
    private static DateTime? GeneratedAt(Edition edition)
    {
        const string marker = "ai-gemini-";

        var at = edition.CtsUrn.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;

        var stamp = edition.CtsUrn[(at + marker.Length)..];

        return DateTime.TryParseExact(stamp, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private async Task RenderColumnsAsync()
    {
        var checkedTranslations = _translationCheckList.CheckedIndices
            .Cast<int>()
            .Where(i => i < _currentTranslations.Count)
            .Select(i => _currentTranslations[i])
            .ToList();

        if (checkedTranslations.Count < 2)
        {
            MessageBox.Show(this, "Check at least two translations first.", "Nothing to compare",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _compareButton.Enabled = false;
        try
        {
            _columnsHost.Controls.Clear();

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = checkedTranslations.Count,
                RowCount = 1
            };
            foreach (var _ in checkedTranslations)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / checkedTranslations.Count));
            }
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // Tracked so the deliberate header-chrome color below can be
            // restored after the generic theming pass, which flattens every
            // Label's background to transparent by default - same reasoning
            // as Compare Sources' identical column layout.
            var headers = new List<Label>();
            var columnLists = new List<(ListBox List, Edition Translation)>();

            for (var col = 0; col < checkedTranslations.Count; col++)
            {
                var translation = checkedTranslations[col];

                var columnTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
                columnTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                columnTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                columnTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

                var header = new Label
                {
                    Text = TranslatorLabel(translation),
                    Dock = DockStyle.Fill,
                    Font = new Font(Font, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                headers.Add(header);

                // Owner-drawn with variable row heights, so a passage longer
                // than the column wraps onto as many lines as it needs instead
                // of running off to the right behind a horizontal scrollbar.
                // Reading three translations side by side means reading down
                // three columns, and a horizontal scrollbar per column makes
                // that impossible.
                var list = new ListBox
                {
                    Dock = DockStyle.Fill,
                    DrawMode = DrawMode.OwnerDrawVariable,
                    IntegralHeight = false
                };
                list.MeasureItem += MeasureWrappedItem;
                list.DrawItem += DrawWrappedItem;
                columnLists.Add((list, translation));

                columnTable.Controls.Add(header, 0, 0);
                columnTable.Controls.Add(list, 0, 1);
                table.Controls.Add(columnTable, col, 0);
            }

            _columnsHost.Controls.Add(table);

            // Each column's text is fetched independently and concurrently -
            // the skeleton above is already visible while these load.
            _columns.Clear();

            await Task.WhenAll(columnLists.Select(async cl =>
            {
                var nodes = await _textNodeRepo.GetByEditionAsync(cl.Translation.EditionId);

                cl.List.BeginUpdate();
                foreach (var n in nodes)
                {
                    cl.List.Items.Add(n.Text);
                }
                cl.List.EndUpdate();

                _columns.Add((cl.List, cl.Translation, nodes));

                ListResultHelpers.AttachCitationTooltip(cl.List,
                    i => i < nodes.Count ? nodes[i].CitationRef : null);

                var menu = ListResultHelpers.AttachCopyToClipboardMenu(cl.List,
                    i => i < nodes.Count
                        ? $"{TranslatorLabel(cl.Translation)} [{PassageCitation.Display(nodes[i].CitationRef)}]: {nodes[i].Text}"
                        : null);

                AttachExportMenu(menu, cl.Translation, nodes);
            }));

            // Built fresh on every click, well after the form's own one-time
            // theming ran on Load, so themed explicitly here - identical
            // reasoning to Compare Sources' own column rendering.
            ReadingTheme.Apply(table);
            foreach (var header in headers)
            {
                header.BackColor = ReadingTheme.HeaderBackground;
                header.ForeColor = ReadingTheme.Text;
            }
        }
        finally
        {
            _compareButton.Enabled = true;
        }
    }

    /// <summary>
    /// The flags for both measuring and drawing a wrapped row. They have to
    /// match exactly, or the height reserved and the height painted disagree
    /// and rows overlap.
    /// </summary>
    private const TextFormatFlags WrapFlags =
        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

    /// <summary>Room either side of the text, and clear of the scrollbar.</summary>
    private const int RowPadding = 4;

    private static int WrapWidth(ListBox list) =>
        Math.Max(40, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - RowPadding * 2);

    private static void MeasureWrappedItem(object? sender, MeasureItemEventArgs e)
    {
        if (sender is not ListBox list || e.Index < 0 || e.Index >= list.Items.Count) return;

        var text = list.Items[e.Index]?.ToString() ?? "";
        var size = TextRenderer.MeasureText(
            e.Graphics, text, list.Font, new Size(WrapWidth(list), int.MaxValue), WrapFlags);

        e.ItemHeight = size.Height + RowPadding * 2;
    }

    private static void DrawWrappedItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox list || e.Index < 0 || e.Index >= list.Items.Count) return;

        var selected = (e.State & DrawItemState.Selected) != 0;
        var back = selected ? ReadingTheme.SelectionBackground : list.BackColor;
        var fore = selected ? ReadingTheme.SelectionText : list.ForeColor;

        using (var brush = new SolidBrush(back))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        var text = list.Items[e.Index]?.ToString() ?? "";
        var bounds = new Rectangle(
            e.Bounds.Left + RowPadding,
            e.Bounds.Top + RowPadding,
            WrapWidth(list),
            e.Bounds.Height - RowPadding * 2);

        TextRenderer.DrawText(e.Graphics, text, list.Font, bounds, fore, WrapFlags);
    }

    /// <summary>
    /// Re-measures every wrapped row against the column's new width.
    ///
    /// A ListBox measures a row once, when it is added, and offers no way to
    /// ask again - so the rows go out and come back. Done on ResizeEnd rather
    /// than on Resize, because rebuilding a few thousand rows on every pixel
    /// of a window drag would be unusable.
    /// </summary>
    private void RewrapColumns()
    {
        foreach (var column in _columns)
        {
            if (column.List.IsDisposed) continue;

            var selected = column.List.SelectedIndex;
            var top = column.List.TopIndex;

            column.List.BeginUpdate();
            column.List.Items.Clear();
            foreach (var n in column.Nodes) column.List.Items.Add(n.Text);
            column.List.EndUpdate();

            if (selected >= 0 && selected < column.List.Items.Count) column.List.SelectedIndex = selected;
            if (top >= 0 && top < column.List.Items.Count) column.List.TopIndex = top;
        }
    }

    /// <summary>
    /// Adds "Export this translation" to a column's existing right-click menu,
    /// writing out the whole column rather than the one passage the Copy item
    /// above it takes - each passage labelled with its citation reference, so
    /// the export can be read back against the original.
    /// </summary>
    private void AttachExportMenu(ContextMenuStrip menu, Edition translation, List<TextNode> nodes)
    {
        var exportItem = menu.Items.Add("Export this translation...");

        exportItem.Click += (_, _) =>
        {
            var label = TranslatorLabel(translation);
            var title = _workList.SelectedIndex >= 0 && _workList.SelectedIndex < _works.Count
                ? $"{_works[_workList.SelectedIndex].AuthorName}, {_works[_workList.SelectedIndex].WorkTitle} - {label}"
                : label;

            using var save = new SaveFileDialog
            {
                Title = "Export translation",
                FileName = SafeFileName(title),
                Filter = "Text file (*.txt)|*.txt|Word document (*.docx)|*.docx|PDF (*.pdf)|*.pdf",
                FilterIndex = 1
            };

            if (save.ShowDialog(this) != DialogResult.OK) return;

            var chunks = nodes.Select(n => (Label: n.CitationRef, n.Text)).ToList();
            var sourceUrl =
                "Perseus Digital Library (via Classica Codex) - see About for full attribution and licensing.";

            try
            {
                var extension = Path.GetExtension(save.FileName).ToLowerInvariant();

                switch (extension)
                {
                    case ".docx":
                        PassageExportService.ExportDocx(save.FileName, title, sourceUrl, chunks, ExportFont);
                        break;
                    case ".pdf":
                        PassageExportService.ExportPdf(save.FileName, title, sourceUrl, chunks, ExportFont);
                        break;
                    default:
                        PassageExportService.ExportText(save.FileName, title, sourceUrl, chunks);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
    }

    private const string ExportFont = "Palatino Linotype";

    private static string SafeFileName(string title)
    {
        var cleaned = new string(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());
        return cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }
}
