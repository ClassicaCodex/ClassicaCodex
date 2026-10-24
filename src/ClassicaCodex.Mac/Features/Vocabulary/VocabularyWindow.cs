using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;
using ClassicaCodex.Mac.Common;
using ClassicaCodex.Mac.Features.WordStudy;

namespace ClassicaCodex.Mac.Features.Vocabulary;

/// <summary>
/// The words a work is made of, ranked by how much of it they account for.
///
/// The headline is the coverage line rather than the table: "learn the top
/// 250 headwords and you can read 80% of this work" is the thing that
/// changes what a beginner does next, and the table is the detail behind it.
///
/// Computed from the text on demand rather than stored. It takes a moment on
/// a long work, but a cached figure would go silently wrong the first time a
/// corpus was re-ingested, and this is a number people would act on. The
/// moment is spent off the UI thread here, so the window says "Counting..."
/// and means it.
/// </summary>
public sealed class VocabularyWindow : FeatureWindow
{
    private readonly Work _work;
    private readonly string _authorName;
    private readonly int _editionId;
    private readonly string _language;

    private readonly TextBlock _headlineLabel;
    private readonly TextBlock _summaryLabel;
    private readonly TextBlock _statusLabel;
    private readonly DataGrid _grid;
    private readonly Button _copyButton;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();

    private VocabularyProfile.Result? _result;
    private bool _closed;
    private Task _building = Task.CompletedTask;

    public VocabularyWindow(Work work, string authorName, int editionId, string? language)
    {
        _work = work;
        _authorName = authorName;
        _editionId = editionId;
        _language = string.IsNullOrWhiteSpace(language) ? "grc" : language;

        Title = "Core Vocabulary";
        Width = 780;
        Height = 700;
        MinWidth = 620;
        MinHeight = 520;

        var titleLabel = new TextBlock
        {
            Text = string.IsNullOrEmpty(authorName) ? work.Title : $"{authorName}, {work.Title}",
            Classes = { "h1" },
            TextWrapping = TextWrapping.Wrap
        };

        _summaryLabel = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

        _headlineLabel = new TextBlock
        {
            Text = "Counting...",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 17,
            LineHeight = 24,
            Margin = new Thickness(0, 10, 0, 0)
        };
        _headlineLabel.Bind(TextBlock.FontFamilyProperty, _headlineLabel.GetResourceObservable("Cc.DisplayFont"));

        var notice = new Border { Classes = { "notice" }, Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 12, 0, 14), Child = new StackPanel { Children = { _summaryLabel, _headlineLabel } } };
        _summaryLabel.Margin = new Thickness(0);

        _grid = new DataGrid
        {
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Columns =
            {
                new DataGridTextColumn
                {
                    Header = "#",
                    Binding = CompiledBinding.Create<VocabularyRow, int>(r => r.Rank, stringFormat: "{0:N0}"),
                    SortMemberPath = nameof(VocabularyRow.Rank),
                    Width = new DataGridLength(64)
                },
                new DataGridTextColumn
                {
                    Header = "Headword",
                    Binding = CompiledBinding.Create<VocabularyRow, string>(r => r.Headword),
                    SortMemberPath = nameof(VocabularyRow.Headword),
                    FontFamily = ToolPane.Font("Cc.SourceFont"),
                    FontSize = 15,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                },
                new DataGridTextColumn
                {
                    Header = "Occurrences",
                    Binding = CompiledBinding.Create<VocabularyRow, int>(r => r.Occurrences, stringFormat: "{0:N0}"),
                    SortMemberPath = nameof(VocabularyRow.Occurrences),
                    Width = new DataGridLength(128)
                },
                new DataGridTextColumn
                {
                    Header = "Running total",
                    Binding = CompiledBinding.Create<VocabularyRow, double>(r => r.CumulativeCoverage, stringFormat: "{0:P1}"),
                    SortMemberPath = nameof(VocabularyRow.CumulativeCoverage),
                    Width = new DataGridLength(118)
                },

                // Said on the row rather than in a legend, because it
                // qualifies this specific count: the form could belong to
                // another headword and nothing in the form decides it.
                new DataGridTextColumn
                {
                    Header = string.Empty,
                    Binding = CompiledBinding.Create<VocabularyRow, string>(r => r.Note),
                    SortMemberPath = nameof(VocabularyRow.Note),
                    FontStyle = FontStyle.Italic,
                    Width = new DataGridLength(150)
                }
            }
        };

        // Exported from the result rather than the visible cells: the screen
        // rounds coverage to one decimal, and a vocabulary list is exactly the
        // kind of table someone takes away to build a study deck from.
        TableExport.Attach(_grid, () => $"vocabulary-{_work.Title}", ExportRows, SelectedExportRows, ExportNotes);

        _statusLabel = new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

        _copyButton = ToolPane.GlyphButton("Copy List", "Copy");
        _copyButton.IsEnabled = false;
        _copyButton.Click += async (_, _) => await CopyListAsync();

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(_copyButton, Dock.Right);
        footer.Children.Add(_copyButton);
        _statusLabel.Margin = new Thickness(0, 0, 16, 0);
        footer.Children.Add(_statusLabel);

        var top = new StackPanel { Children = { titleLabel, notice } };

        var layout = new DockPanel { Margin = new Thickness(22, 20, 22, 20) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(footer);
        layout.Children.Add(_grid);
        Content = layout;

        Closed += (_, _) => _closed = true;
        Opened += (_, _) => _building = BuildProfileAsync();
    }

    // For the tests.
    internal Task Building => _building;
    internal VocabularyProfile.Result? Result => _result;
    internal string HeadlineText => _headlineLabel.Text ?? string.Empty;
    internal string SummaryText => _summaryLabel.Text ?? string.Empty;
    internal string StatusText => _statusLabel.Text ?? string.Empty;
    internal DataGrid Grid => _grid;

    /// <summary>
    /// The list at full precision, header first.
    ///
    /// Coverage is written as a plain fraction rather than the "62.4%" the
    /// screen shows, so a spreadsheet sorts and charts it as a number without
    /// the percent sign having to be stripped first - and so the value carried
    /// out is the one that was computed rather than the one that was rounded
    /// for a narrow column.
    /// </summary>
    private IReadOnlyList<IReadOnlyList<string>> ExportRows() =>
        ExportTable(_result?.Entries ?? Array.Empty<VocabularyProfile.Entry>());

    private IReadOnlyList<IReadOnlyList<string>> SelectedExportRows() =>
        ExportTable(_grid.SelectedItems.OfType<VocabularyRow>().OrderBy(r => r.Rank).Select(r => r.Entry));

    private static IReadOnlyList<IReadOnlyList<string>> ExportTable(IEnumerable<VocabularyProfile.Entry> entries)
    {
        var table = new List<IReadOnlyList<string>>
        {
            new[] { "Rank", "Headword", "Occurrences", "CumulativeCoverage", "FormIsAmbiguous" }
        };

        foreach (var entry in entries)
        {
            table.Add(new[]
            {
                entry.Rank.ToString(CultureInfo.InvariantCulture),
                entry.Headword,
                entry.Occurrences.ToString(CultureInfo.InvariantCulture),
                entry.CumulativeCoverage.ToString("R", CultureInfo.InvariantCulture),
                entry.Ambiguous ? "yes" : "no"
            });
        }

        return table;
    }

    /// <summary>
    /// What the coverage figures are a share of.
    ///
    /// The unknown-token share belongs with them and not in a footnote
    /// somewhere: those words can never be covered by learning headwords from
    /// this list, so a coverage number read without it promises more than it
    /// can deliver.
    /// </summary>
    private IReadOnlyList<string> ExportNotes()
    {
        var notes = new List<string>
        {
            $"Classica Codex core vocabulary - {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"{_authorName}, {_work.Title} ({_language})"
        };

        if (_result != null)
        {
            notes.Add($"{_result.TotalTokens:N0} running words, {_result.Entries.Count:N0} headwords.");
            notes.Add($"{_result.UnknownTokens:N0} running words have no lemma data at all and can " +
                      "never be covered by learning headwords from this list.");
        }

        notes.Add("CumulativeCoverage is a fraction of all running words, counting this entry and " +
                  "everything above it. A headword marked ambiguous has a form that could belong to " +
                  "another word, so its count is an upper bound.");

        return notes;
    }

    private async Task BuildProfileAsync()
    {
        try
        {
            var editionId = _editionId;
            var language = _language;

            var result = await Task.Run(async () =>
            {
                // Reading lines only: a core-vocabulary list is a list of the
                // words the author used, and speaker tags, stage directions and
                // headings are not among them.
                var passages = await _textNodeRepo.GetByEditionAsync(editionId, readingLinesOnly: true);
                var formCounts = VocabularyProfile.CountForms(passages.Select(p => p.Text));
                if (formCounts.Count == 0) return (VocabularyProfile.Result?)null;

                var headwords = await _lemmaRepo.GetHeadwordsForFormsAsync(formCounts.Keys, language);
                return VocabularyProfile.Build(formCounts, headwords);
            });

            if (_closed) return;

            if (result == null)
            {
                _headlineLabel.Text = "This edition has no text loaded to count.";
                return;
            }

            _result = result;
            ShowResult(result);
        }
        catch (Exception ex)
        {
            if (!_closed) _headlineLabel.Text = $"Couldn't build the vocabulary list: {ex.Message}";
        }
    }

    private void ShowResult(VocabularyProfile.Result result)
    {
        if (result.Entries.Count == 0)
        {
            // Distinguished from an empty text, because the fix is different:
            // this one needs lemma data fetched, not a corpus re-ingested.
            _headlineLabel.Text =
                $"{result.TotalTokens:N0} words, but none of them are in the lemma data for this language. "
                + "Setup can fetch the lemma files that make this work.";
            return;
        }

        var forHalf = VocabularyProfile.HeadwordsToReach(result.Entries, 0.50);
        var forEighty = VocabularyProfile.HeadwordsToReach(result.Entries, 0.80);

        _summaryLabel.Text = $"{result.TotalTokens:N0} running words, built from {result.Entries.Count:N0} headwords.";
        _headlineLabel.Text =
            $"Learn the top {forHalf:N0} and you can read half of this work; "
            + $"the top {forEighty:N0} gets you to four fifths.";

        _grid.ItemsSource = result.Entries.Select(e => new VocabularyRow(e)).ToList();

        _statusLabel.Text = result.UnknownShare > 0.01
            ? $"{result.UnknownShare:P0} of the running words have no lemma data, so they cannot be "
              + "covered by learning from this list."
            : "Running total is the share of the whole work covered by this headword and everything above it.";

        _copyButton.IsEnabled = true;
    }

    private async Task CopyListAsync()
    {
        if (_result == null) return;

        var lines = new List<string>
        {
            $"Core vocabulary - {_authorName}, {_work.Title}",
            $"{_result.TotalTokens:N0} running words",
            string.Empty
        };

        lines.AddRange(_result.Entries.Select(e =>
            $"{e.Rank}\t{e.Headword}\t{e.Occurrences}\t{e.CumulativeCoverage:P1}"
            + (e.Ambiguous ? "\tform is ambiguous" : string.Empty)));

        _statusLabel.Text = await ClipboardText.SetAsync(this, string.Join(Environment.NewLine, lines))
            ? $"{_result.Entries.Count:N0} headwords copied to the clipboard."
            : "Couldn't copy to the clipboard.";
    }

    /// <summary>One headword as the table shows it; the sort works on the numbers, not on their formatting.</summary>
    internal sealed class VocabularyRow
    {
        public VocabularyRow(VocabularyProfile.Entry entry) => Entry = entry;

        public VocabularyProfile.Entry Entry { get; }

        public int Rank => Entry.Rank;

        public string Headword => Entry.Headword;

        public int Occurrences => Entry.Occurrences;

        public double CumulativeCoverage => Entry.CumulativeCoverage;

        public string Note => Entry.Ambiguous ? "form is ambiguous" : string.Empty;
    }
}
