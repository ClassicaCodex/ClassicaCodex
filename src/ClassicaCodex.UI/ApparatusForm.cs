using ClassicaCodex.Core;
using ClassicaCodex.Core.Models;
using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// The editor's notes on a line, and on the edition around it.
///
/// Called "Editor's Notes" in the interface rather than "Textual Apparatus",
/// because that is what the content usually turns out to be. A strict
/// apparatus criticus records manuscript variants - Plutarch's Greek gives
/// "εἶτʼ οὐ R: εἶτα", where R is a manuscript. But most of what the corpus
/// actually carries is commentary: Fowler on Ad principem ineruditum
/// identifies a Nauck fragment, cites Diogenes Laertius, notes where Cicero
/// translates a line. Both are useful and both belong here; only one of them
/// is an apparatus, so the broader name is the honest one.
///
/// The types stay ApparatusEntry and ApparatusRepository - the TEI elements
/// behind them really are apparatus markup, and renaming those would move the
/// mismatch rather than remove it.
///
/// A printed edition sets this in small type at the foot of the page: which
/// manuscripts read what, who conjectured what, which lines are doubted and by
/// whom. It is the record of how the text in front of you came to look the way
/// it does, and it is normally the first thing lost when a text is digitised
/// for reading.
///
/// Two views because two questions get asked. "Why does this line look like
/// this?" is about one line. "What did this editor actually do?" is about the
/// whole edition - and reading an editor's notes straight through is how you
/// find out whether they are conservative or interventionist.
/// </summary>
public class ApparatusForm : ScaledForm
{
    private readonly ApparatusRepository _apparatusRepo = new();

    private readonly int _editionId;
    private readonly string _citationRef;
    private readonly string _lineText;
    private readonly string _editionLabel;

    private readonly Label _lineLabel;
    private readonly ListBox _entryList;
    private readonly TextBox _detailBox;
    private readonly RadioButton _thisLineRadio;
    private readonly RadioButton _wholeEditionRadio;
    private readonly Label _statusLabel;

    public ApparatusForm(int editionId, string citationRef, string lineText, string editionLabel)
    {
        _editionId = editionId;
        _citationRef = citationRef;
        _lineText = lineText;
        _editionLabel = editionLabel;

        Text = "Editor's Notes";
        AppIcons.ApplyWindowIcon(this, "Concordance");
        Width = 900;
        Height = 640;
        StartPosition = FormStartPosition.CenterParent;

        // The line itself sits at the top, because every entry below is a
        // comment on it and reading them without it in view means holding the
        // Greek in your head while you read about it.
        _lineLabel = new Label
        {
            Left = 12,
            Top = 12,
            Width = 860,
            Height = 44,
            Font = new Font("Palatino Linotype", 12f),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _thisLineRadio = new RadioButton
        {
            Text = "This line",
            Left = 12,
            Top = 62,
            Width = 90,
            Checked = true
        };

        _wholeEditionRadio = new RadioButton
        {
            Text = "Whole edition",
            Left = 108,
            Top = 62,
            Width = 120
        };

        _thisLineRadio.CheckedChanged += async (_, _) => { if (_thisLineRadio.Checked) await LoadAsync(); };
        _wholeEditionRadio.CheckedChanged += async (_, _) => { if (_wholeEditionRadio.Checked) await LoadAsync(); };

        _entryList = new ListBox
        {
            Left = 12,
            Top = 92,
            Width = 860,
            Height = 330,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        _entryList.SelectedIndexChanged += (_, _) => ShowDetail();

        // Entries are often long and full of sigla; the list truncates and
        // this shows the whole thing, selectable so it can be copied into a
        // note.
        _detailBox = new TextBox
        {
            Left = 12,
            Top = 432,
            Width = 860,
            Height = 130,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Palatino Linotype", 11f),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _statusLabel = new Label
        {
            Left = 12,
            Top = 572,
            Width = 860,
            Height = 34,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.Add(_lineLabel);
        Controls.Add(_thisLineRadio);
        Controls.Add(_wholeEditionRadio);
        Controls.Add(_entryList);
        Controls.Add(_detailBox);
        Controls.Add(_statusLabel);

        Load += async (_, _) => await LoadAsync();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private List<ApparatusEntry> _entries = new();

    private async Task LoadAsync()
    {
        _lineLabel.Text = $"[{_citationRef}]  {_lineText}";

        try
        {
            _entries = _wholeEditionRadio.Checked
                ? await _apparatusRepo.GetForEditionAsync(_editionId)
                : await _apparatusRepo.GetForLineAsync(_editionId, _citationRef);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
            return;
        }

        _entryList.Items.Clear();
        foreach (var e in _entries) _entryList.Items.Add(Describe(e));

        if (_entries.Count == 0)
        {
            _detailBox.Text = string.Empty;

            // Absence has two quite different causes and the distinction
            // matters: an edition can carry no apparatus at all, or carry one
            // that simply says nothing about this line. Reporting the
            // edition-wide count separates them, so nobody concludes the
            // feature is broken when the truth is that Browning's translation
            // has no apparatus to show.
            var editionTotal = await _apparatusRepo.CountForEditionAsync(_editionId);
            _statusLabel.Text = editionTotal == 0
                ? $"{_editionLabel} carries no editor's notes. Translations often don't, and " +
                  "not every edition records them."
                : $"Nothing recorded for this line. {editionTotal:N0} entries elsewhere in this edition.";
            return;
        }

        _entryList.SelectedIndex = 0;
        _statusLabel.Text = _wholeEditionRadio.Checked
            ? $"{_entries.Count:N0} entries across {_editionLabel}."
            : $"{_entries.Count} entr{(_entries.Count == 1 ? "y" : "ies")} for this line.";
    }

    /// <summary>
    /// One line for the list. Structured entries get lemma and siglum spelled
    /// out; prose notes are shown as the editor wrote them, since imposing a
    /// shape on them would mean inventing one.
    /// </summary>
    private string Describe(ApparatusEntry e)
    {
        var prefix = _wholeEditionRadio.Checked ? $"[{PassageCitation.Display(e.CitationRef, e.Milestone)}] " : string.Empty;

        if (e.Kind == "variant")
        {
            // A Menota note is written as the two readings either side of a
            // colon, so its Content already opens with the lemma and prefixing
            // it again gives "Uphaf ] Uphaf : Vphaf Sogo". Printed only where
            // the lemma came from somewhere the reader cannot see - a @wit or
            // @resp attribute, as in the Perseus editions.
            var repeated = !string.IsNullOrWhiteSpace(e.Lemma)
                           && e.Content.StartsWith(e.Lemma, StringComparison.Ordinal);

            var lemma = string.IsNullOrWhiteSpace(e.Lemma) || repeated ? "" : $"{e.Lemma}  ]  ";
            var witness = string.IsNullOrWhiteSpace(e.Witness) ? "" : $"  ({e.Witness})";
            return $"{prefix}{lemma}{e.Content}{witness}";
        }

        var resp = string.IsNullOrWhiteSpace(e.Witness) ? "" : $"  - {e.Witness}";
        return $"{prefix}{e.Content}{resp}";
    }

    private void ShowDetail()
    {
        if (_entryList.SelectedIndex < 0 || _entryList.SelectedIndex >= _entries.Count)
        {
            _detailBox.Text = string.Empty;
            return;
        }

        var e = _entries[_entryList.SelectedIndex];
        var lines = new List<string>();

        if (_wholeEditionRadio.Checked) lines.Add($"Line {PassageCitation.Display(e.CitationRef, e.Milestone)}");

        lines.Add(e.Kind == "variant" ? "Manuscript variant" : "Editor's note");
        if (!string.IsNullOrWhiteSpace(e.Lemma)) lines.Add($"Adopted reading: {e.Lemma}");
        if (!string.IsNullOrWhiteSpace(e.Witness)) lines.Add($"Witness / editor: {e.Witness}");

        lines.Add(string.Empty);
        lines.Add(e.Content);

        _detailBox.Text = string.Join(Environment.NewLine, lines);
    }
}
