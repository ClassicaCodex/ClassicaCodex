using ClassicaCodex.Core;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Given a tag (myth/character/theme), lets you pick 2+ sources it appears
/// in and lines them up in side-by-side scrollable columns - e.g. Hesiod,
/// Aeschylus, and Ovid's treatments of Prometheus all visible at once.
/// </summary>
public class CompareForm : ScaledForm
{
    private readonly CheckedListBox _sourceCheckList;
    private readonly Panel _columnsHost;
    private readonly TagRepository _tagRepo = new();
    private readonly string _tagName;

    private List<(int WorkId, long TextNodeId, string AuthorName, string WorkTitle, string CitationRef, string Text)> _allResults = new();

    public CompareForm(string tagName)
    {
        _tagName = tagName;
        Text = $"Compare Sources - \"{tagName}\"";
        AppIcons.ApplyWindowIcon(this, "CompareTexts");
        Width = 1100;
        Height = 780;
        StartPosition = FormStartPosition.CenterParent;

        var instructions = new Label
        {
            Text = $"Check 2 or more sources tagged \"{tagName}\", then Compare:",
            Left = 12,
            Top = 10,
            Width = 600
        };

        _sourceCheckList = new CheckedListBox
        {
            Left = 12,
            Top = 34,
            Width = 280,
            Height = 600,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
            CheckOnClick = true
        };

        var compareButton = new Button
        {
            Text = "Compare Selected",
            Left = 12,
            Top = 646,
            Width = 160,
            Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        compareButton.Click += (_, _) => RenderColumns();

        _columnsHost = new Panel
        {
            Left = 304,
            Top = 34,
            Width = 772,
            Height = 646,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(instructions);
        Controls.Add(_sourceCheckList);
        Controls.Add(compareButton);
        Controls.Add(_columnsHost);

        Load += async (_, _) => await LoadSourcesAsync();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task LoadSourcesAsync()
    {
        _allResults = await _tagRepo.GetByTagAsync(_tagName);
        _sourceCheckList.Items.Clear();

        var sources = _allResults
            .Select(r => new SourceKey(r.AuthorName, r.WorkTitle))
            .Distinct()
            .OrderBy(s => s.AuthorName)
            .ThenBy(s => s.WorkTitle)
            .ToList();

        if (sources.Count == 0)
        {
            _sourceCheckList.Items.Add("(no sources found for this tag)");
            return;
        }

        foreach (var source in sources)
        {
            _sourceCheckList.Items.Add(source);
        }
    }

    private void RenderColumns()
    {
        var selected = _sourceCheckList.CheckedItems.OfType<SourceKey>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Check at least one source first.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _columnsHost.Controls.Clear();

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = selected.Count,
            RowCount = 1
        };
        foreach (var _ in selected)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / selected.Count));
        }
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Tracked so the deliberate header-chrome color below can be
        // restored after the generic theming pass, which flattens every
        // Label's background to transparent by default.
        var headers = new List<Label>();

        for (var col = 0; col < selected.Count; col++)
        {
            var source = selected[col];

            var passages = _allResults
                .Where(r => r.AuthorName == source.AuthorName && r.WorkTitle == source.WorkTitle)
                .ToList();

            // A 2-row table (fixed-height header, fill-height content) avoids
            // the classic Dock=Top/Dock=Fill ordering ambiguity entirely,
            // rather than relying on Controls.Add order to get it right.
            var columnTable = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            columnTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            columnTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            columnTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var header = new Label
            {
                Text = source.ToString(),
                Dock = DockStyle.Fill,
                Font = new Font(Font, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            headers.Add(header);
            var list = new ListBox { Dock = DockStyle.Fill };

            if (passages.Count == 0)
            {
                list.Items.Add("(nothing tagged with this in this source - unexpected, worth re-checking)");
            }
            else
            {
                foreach (var p in passages)
                {
                    list.Items.Add(p.Text);
                }

                ListResultHelpers.AttachCitationTooltip(list,
                    i => i < passages.Count ? passages[i].CitationRef : null);
                ListResultHelpers.AttachCopyToClipboardMenu(list,
                    i => i < passages.Count
                        ? $"{source.AuthorName}, {source.WorkTitle} [{PassageCitation.Display(passages[i].CitationRef)}]: {passages[i].Text}"
                        : null);
            }

            columnTable.Controls.Add(header, 0, 0);
            columnTable.Controls.Add(list, 0, 1);

            table.Controls.Add(columnTable, col, 0);
        }

        _columnsHost.Controls.Add(table);

        // This whole table is built fresh on every click, well after the
        // form's own one-time theming ran on Load, so it's themed
        // explicitly here. The header strips are deliberate chrome (a shade
        // off the surface, marking them as labels rather than content) -
        // that reads fine in light mode as a plain light gray, but the
        // generic pass has no way to know these Labels are special, so its
        // own color is restored right after.
        ReadingTheme.Apply(table);
        foreach (var header in headers)
        {
            header.BackColor = ReadingTheme.HeaderBackground;
            header.ForeColor = ReadingTheme.Text;
        }
    }

    /// <summary>Author + work pair, stored directly as checklist items so
    /// there's no fragile re-parsing of a display string later.</summary>
    private sealed record SourceKey(string AuthorName, string WorkTitle)
    {
        public override string ToString() => $"{AuthorName}, {WorkTitle}";
    }
}
