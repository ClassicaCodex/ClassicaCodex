using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// The words a work is made of, ranked by how much of it they account for.
///
/// The headline is the coverage line rather than the table: "learn the top
/// 250 headwords and you can read 80% of this work" is the thing that
/// changes what a beginner does next, and the table is the detail behind it.
///
/// Computed from the text on demand rather than stored. It takes a moment on
/// a long work, but a cached figure would go silently wrong the first time a
/// corpus was re-ingested, and this is a number people would act on.
/// </summary>
public class VocabularyForm : Form
{
    private readonly Work _work;
    private readonly string _authorName;
    private readonly int _editionId;

    private readonly Label _headlineLabel;
    private readonly Label _statusLabel;
    private readonly ListView _list;
    private readonly Button _copyButton;

    private readonly TextNodeRepository _textNodeRepo = new();
    private readonly LemmaRepository _lemmaRepo = new();

    private readonly string _language;

    private VocabularyProfile.Result? _result;

    public VocabularyForm(Work work, string authorName, int editionId, string? language)
    {
        _work = work;
        _authorName = authorName;
        _editionId = editionId;
        _language = string.IsNullOrWhiteSpace(language) ? "grc" : language;

        Text = "Core vocabulary";
        AppIcons.ApplyWindowIcon(this, "CoreVocabulary");
        ClientSize = new Size(760, 640);
        MinimumSize = new Size(620, 520);
        StartPosition = FormStartPosition.CenterParent;

        var titleLabel = new Label
        {
            Text = $"{authorName}, {work.Title}",
            Left = 16, Top = 14, Width = 720, Height = 26,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold)
        };
        Controls.Add(titleLabel);

        _headlineLabel = new Label
        {
            Text = "Counting...",
            Left = 16, Top = 44, Width = 720, Height = 60,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(_headlineLabel);

        _list = new ListView
        {
            Left = 16, Top = 112, Width = 728, Height = 452,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false
        };
        _list.Columns.Add("#", 50);
        _list.Columns.Add("Headword", 300);
        _list.Columns.Add("Occurrences", 100);
        _list.Columns.Add("Running total", 110);
        _list.Columns.Add("", 140);
        Controls.Add(_list);

        _statusLabel = new Label
        {
            Left = 16, Top = 574, Width = 500, Height = 36,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray
        };
        Controls.Add(_statusLabel);

        _copyButton = new Button
        {
            Text = "Copy List", Left = 552, Top = 598, Width = 96, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Enabled = false
        };
        _copyButton.Click += (_, _) => CopyList();
        Controls.Add(_copyButton);

        var closeButton = new Button
        {
            Text = "Close", Left = 656, Top = 598, Width = 88, Height = 30,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(closeButton);
        CancelButton = closeButton;

        Load += async (_, _) => await BuildProfileAsync();

        ReadingTheme.AttachTo(this, () => _statusLabel.ForeColor = ReadingTheme.MutedText);
    }

    private async Task BuildProfileAsync()
    {
        try
        {
            // Reading lines only: a core-vocabulary list is a list of the
            // words the author used, and speaker tags, stage directions and
            // headings are not among them.
            var passages = await _textNodeRepo.GetByEditionAsync(_editionId, readingLinesOnly: true);
            var formCounts = VocabularyProfile.CountForms(passages.Select(p => p.Text));

            if (formCounts.Count == 0)
            {
                _headlineLabel.Text = "This edition has no text loaded to count.";
                return;
            }

            var headwords = await _lemmaRepo.GetHeadwordsForFormsAsync(formCounts.Keys, _language);
            _result = VocabularyProfile.Build(formCounts, headwords);

            ShowResult(_result);
        }
        catch (Exception ex)
        {
            _headlineLabel.Text = $"Couldn't build the vocabulary list: {ex.Message}";
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
                + "The Setup Wizard can fetch the lemma files that make this work.";
            return;
        }

        var forHalf = VocabularyProfile.HeadwordsToReach(result.Entries, 0.50);
        var forEighty = VocabularyProfile.HeadwordsToReach(result.Entries, 0.80);

        _headlineLabel.Text =
            $"{result.TotalTokens:N0} running words, built from {result.Entries.Count:N0} headwords.\r\n"
            + $"Learn the top {forHalf:N0} and you can read half of this work; "
            + $"the top {forEighty:N0} gets you to four fifths.";

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();

            foreach (var entry in result.Entries)
            {
                var item = new ListViewItem(new[]
                {
                    entry.Rank.ToString("N0"),
                    entry.Headword,
                    entry.Occurrences.ToString("N0"),
                    entry.CumulativeCoverage.ToString("P1"),

                    // Said on the row rather than in a legend, because it
                    // qualifies this specific count: the form could belong to
                    // another headword and nothing in the form decides it.
                    entry.Ambiguous ? "form is ambiguous" : string.Empty
                });

                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        _statusLabel.Text = result.UnknownShare > 0.01
            ? $"{result.UnknownShare:P0} of the running words have no lemma data, so they cannot be "
              + "covered by learning from this list."
            : "Running total is the share of the whole work covered by this headword and everything above it.";

        _copyButton.Enabled = true;
    }

    private void CopyList()
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

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            _statusLabel.Text = $"{_result.Entries.Count:N0} headwords copied to the clipboard.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't copy: {ex.Message}";
        }
    }
}
