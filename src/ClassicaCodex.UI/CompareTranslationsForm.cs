using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Lines up two or more translations of the SAME work side by side - three
/// different English renderings of Agamemnon, say - rather than Compare
/// Sources' comparison across different authors sharing a tag. Each column
/// is that translation's full running text, independently scrollable; there's
/// no "nothing tagged" case here, since an ingested edition always has text.
/// </summary>
public class CompareTranslationsForm : Form
{
    private readonly ListBox _workList;
    private readonly CheckedListBox _translationCheckList;
    private readonly Button _compareButton;
    private readonly Panel _columnsHost;
    private readonly EditionRepository _editionRepo = new();
    private readonly TextNodeRepository _textNodeRepo = new();

    private List<(int WorkId, string AuthorName, string WorkTitle)> _works = new();
    private List<Edition> _currentTranslations = new();

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
        ReadingTheme.AttachTo(this);
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
        if (!string.IsNullOrWhiteSpace(edition.Translator)) return edition.Translator;

        var suffix = edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return string.IsNullOrEmpty(suffix) ? "Untitled translation" : suffix;
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

                var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
                columnLists.Add((list, translation));

                columnTable.Controls.Add(header, 0, 0);
                columnTable.Controls.Add(list, 0, 1);
                table.Controls.Add(columnTable, col, 0);
            }

            _columnsHost.Controls.Add(table);

            // Each column's text is fetched independently and concurrently -
            // the skeleton above is already visible while these load.
            await Task.WhenAll(columnLists.Select(async cl =>
            {
                var nodes = await _textNodeRepo.GetByEditionAsync(cl.Translation.EditionId);

                foreach (var n in nodes)
                {
                    cl.List.Items.Add(n.Text);
                }

                ListResultHelpers.AttachCitationTooltip(cl.List,
                    i => i < nodes.Count ? nodes[i].CitationRef : null);
                ListResultHelpers.AttachCopyToClipboardMenu(cl.List,
                    i => i < nodes.Count
                        ? $"{TranslatorLabel(cl.Translation)} [{nodes[i].CitationRef}]: {nodes[i].Text}"
                        : null);
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
}
