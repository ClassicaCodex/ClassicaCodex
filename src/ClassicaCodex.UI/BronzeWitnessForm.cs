using ClassicaCodex.Core;

namespace ClassicaCodex.UI;

internal sealed class BronzeWitnessForm : ScaledForm
{
    private readonly ComboBox _witness = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _edition = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RichTextBox _note = ReadTextBox(10);
    private readonly RichTextBox _passage = ReadTextBox(13);
    private readonly RichTextBox _citation = ReadTextBox(9);
    private readonly Icon _windowIcon = BronzeIcons.Bestiary();
    private readonly IReadOnlyList<BronzeWitness> _sources;
    public ArcadePassage? SelectedPassage { get; private set; }

    public BronzeWitnessForm(IReadOnlyList<BronzeWitness> sources)
    {
        _sources = sources;
        Text = "ΣΧΟΛΙΑ — Ancient witnesses"; Icon = _windowIcon;
        ClientSize = new Size(800, 660); MinimumSize = new Size(620, 500); StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(22, 18, 34); ForeColor = Color.Wheat; Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(16) };
        foreach (var height in new[] { 34, 34, 80 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.Controls.Add(_witness, 0, 0); layout.Controls.Add(_edition, 0, 1); layout.Controls.Add(_note, 0, 2);
        layout.Controls.Add(_passage, 0, 3); layout.Controls.Add(_citation, 0, 4);
        var open = new Button { Text = "Open this passage in the reader", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        open.Click += (_, _) => { if (SelectedPassage != null) { DialogResult = DialogResult.OK; Close(); } };
        layout.Controls.Add(open, 0, 5);
        layout.Controls.Add(new Label { Text = "From your installed corpus · Select text to copy · Esc returns to the bestiary", Dock = DockStyle.Fill }, 0, 6);
        Controls.Add(layout);
        foreach (var source in sources) _witness.Items.Add(source.Witness.Title);
        _witness.SelectedIndexChanged += (_, _) =>
        {
            var source = _sources[_witness.SelectedIndex]; _note.Text = source.Witness.Note
                + (source.Witness.Section ? "\n\nThis is the beginning of section " + source.Witness.Citation + ". Continue in the reader to follow its remaining paragraphs." : "");
            _edition.Items.Clear();
            for (var i = 0; i < source.Editions.Count; i++)
            {
                var language = source.Editions[i].Language switch { "grc" => "Greek", "eng" or "en" => "English", "lat" => "Latin", var other => other };
                _edition.Items.Add($"{language} text · edition {i + 1}");
            }
            if (_edition.Items.Count > 0) _edition.SelectedIndex = 0;
        };
        _edition.SelectedIndexChanged += (_, _) =>
        {
            if (_witness.SelectedIndex < 0 || _edition.SelectedIndex < 0) return;
            SelectedPassage = _sources[_witness.SelectedIndex].Editions[_edition.SelectedIndex];
            _passage.Text = SelectedPassage.Text;
            _citation.Text = $"{SelectedPassage.Author} — {SelectedPassage.Title} {PassageCitation.Display(SelectedPassage.Citation)}\nStored citation: {SelectedPassage.Citation}";
        };
        if (sources.Count > 0) _witness.SelectedIndex = 0;
    }
    private static RichTextBox ReadTextBox(float size) => new() { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = true,
        ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(22, 18, 34),
        ForeColor = Color.Wheat, Font = new Font("Segoe UI", size), DetectUrls = false };
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    { if (keyData == Keys.Escape) { Close(); return true; } return base.ProcessCmdKey(ref msg, keyData); }
    protected override void Dispose(bool disposing)
    { if (disposing) _windowIcon.Dispose(); base.Dispose(disposing); }
}

