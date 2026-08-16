using ClassicaCodex.Core;

namespace ClassicaCodex.UI;

/// <summary>
/// Records what the reader thinks about a work's attribution.
///
/// The library ships a hand-curated catalog of the well-known cases, and this
/// is how somebody disagrees with it. That matters more than it might look:
/// attribution is exactly the sort of question where a reader working through
/// the text reaches their own view, and a library that cannot record it is
/// telling them their opinion does not count.
///
/// Setting anything here marks the work as decided by hand, after which no
/// catalog update and no re-ingest will revise it. "Use the library's answer"
/// hands it back.
/// </summary>
public class AttributionForm : ScaledForm
{
    private readonly RadioButton _accepted;
    private readonly RadioButton _disputed;
    private readonly RadioButton _spurious;
    private readonly TextBox _note;
    private readonly Label _catalogSays;

    /// <summary>Null when the reader chose to hand the work back to the catalog.</summary>
    public (AttributionStatus Status, string? Note)? Chosen { get; private set; }

    /// <summary>True when the reader asked for the catalog's answer instead.</summary>
    public bool ClearOverride { get; private set; }

    public AttributionForm(
        string authorName,
        string workTitle,
        AttributionStatus current,
        string? currentNote,
        bool setByUser)
    {
        Text = "Attribution";
        AppIcons.ApplyWindowIcon(this, "Help");
        Width = 520;
        Height = 400;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var heading = new Label
        {
            Text = $"{authorName}, {workTitle}",
            Left = 16, Top = 14, Width = 470, Height = 20,
            Font = new Font(Font, FontStyle.Bold)
        };

        var prompt = new Label
        {
            Text = "How securely is this work attributed to the author it is filed under?",
            Left = 16, Top = 38, Width = 470, Height = 20
        };

        // Worded as what each means rather than as its enum name. "Spurious" is
        // a term of art; "transmitted under the name, but not by this author"
        // is what it says.
        _accepted = new RadioButton
        {
            Text = "Securely attributed - no serious doubt",
            Left = 24, Top = 68, Width = 460, AutoSize = false, Height = 22
        };

        _disputed = new RadioButton
        {
            Text = "Disputed - defended and rejected by serious editors",
            Left = 24, Top = 94, Width = 460, AutoSize = false, Height = 22
        };

        _spurious = new RadioButton
        {
            Text = "Not by this author - transmitted under the name and generally rejected",
            Left = 24, Top = 120, Width = 460, AutoSize = false, Height = 22
        };

        (current switch
        {
            AttributionStatus.Disputed => _disputed,
            AttributionStatus.Spurious => _spurious,
            _ => _accepted
        }).Checked = true;

        var noteLabel = new Label
        {
            Text = "Why (shown in Work Details):",
            Left = 16, Top = 154, Width = 470, Height = 18
        };

        _note = new TextBox
        {
            Left = 16, Top = 174, Width = 470, Height = 60,
            Multiline = true,
            Text = currentNote ?? string.Empty
        };

        // Says where the current value came from, because "disputed" means
        // something different when the reader set it than when a table did.
        var catalogEntry = DisputedWorkData.Lookup(authorName, workTitle);

        _catalogSays = new Label
        {
            Left = 16, Top = 242, Width = 470, Height = 54,
            Text = setByUser
                ? "You set this. The library's own catalog will not change it." +
                  (catalogEntry == null
                      ? " The catalog has no entry for this work."
                      : $" The catalog would say: {Describe(catalogEntry.Status)}.")
                : catalogEntry == null
                    ? "From the default - the library's catalog has no entry for this work."
                    : $"From the library's catalog: {catalogEntry.Note}"
        };

        var useCatalog = new Button
        {
            Text = "Use the library's answer",
            Left = 16, Top = 302, Width = 180, Height = 30,
            Enabled = setByUser
        };

        useCatalog.Click += (_, _) =>
        {
            ClearOverride = true;
            DialogResult = DialogResult.OK;
            Close();
        };

        var save = new Button
        {
            Text = "Save", Left = 296, Top = 302, Width = 90, Height = 30,
            DialogResult = DialogResult.OK
        };

        var cancel = new Button
        {
            Text = "Cancel", Left = 394, Top = 302, Width = 90, Height = 30,
            DialogResult = DialogResult.Cancel
        };

        save.Click += (_, _) =>
        {
            var status = _disputed.Checked ? AttributionStatus.Disputed
                       : _spurious.Checked ? AttributionStatus.Spurious
                       : AttributionStatus.Accepted;

            var note = _note.Text.Trim();
            Chosen = (status, string.IsNullOrEmpty(note) ? null : note);
        };

        Controls.Add(heading);
        Controls.Add(prompt);
        Controls.Add(_accepted);
        Controls.Add(_disputed);
        Controls.Add(_spurious);
        Controls.Add(noteLabel);
        Controls.Add(_note);
        Controls.Add(_catalogSays);
        Controls.Add(useCatalog);
        Controls.Add(save);
        Controls.Add(cancel);

        AcceptButton = save;
        CancelButton = cancel;

        ReadingTheme.AttachTo(this, () => _catalogSays.ForeColor = ReadingTheme.MutedText);
    }

    private static string Describe(AttributionStatus status) => status switch
    {
        AttributionStatus.Disputed => "disputed",
        AttributionStatus.Spurious => "not by this author",
        _ => "securely attributed"
    };
}
