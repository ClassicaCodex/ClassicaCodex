using ClassicaCodex.Core.Models;

namespace ClassicaCodex.UI;

/// <summary>
/// Picks which edition of a work to translate from, when there is more than
/// one to choose between.
///
/// This is asked once and never again for a given translation, because the
/// answer is not really a preference - it decides the citation references
/// every passage gets filed under. Switching source part-way would leave
/// work already done keyed to a scheme the new source doesn't use, which is
/// the misalignment this whole approach exists to avoid.
/// </summary>
public class EditionChoiceForm : ScaledForm
{
    private readonly ListBox _list;
    private readonly List<Edition> _editions;

    public Edition? Chosen => _list.SelectedIndex >= 0 && _list.SelectedIndex < _editions.Count
        ? _editions[_list.SelectedIndex]
        : null;

    public EditionChoiceForm(string title, string prompt, List<Edition> editions, IReadOnlyDictionary<int, int> lineCounts)
    {
        _editions = editions;

        Text = title;
        AppIcons.ApplyWindowIcon(this, "CompareTexts");
        ClientSize = new Size(620, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label { Text = prompt, Left = 14, Top = 14, Width = 590, Height = 34 };

        _list = new ListBox { Left = 14, Top = 52, Width = 590, Height = 190 };

        foreach (var edition in editions)
        {
            // Line count included because it is the one thing that
            // distinguishes two editions of the same text at a glance -
            // often the only visible difference between them.
            var lines = lineCounts.TryGetValue(edition.EditionId, out var n) ? $"{n:N0} lines" : "unknown length";
            var translator = string.IsNullOrWhiteSpace(edition.Translator) ? null : edition.Translator;
            var suffix = edition.CtsUrn.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

            _list.Items.Add(translator == null
                ? $"{suffix}  -  {lines}"
                : $"{translator}  ({suffix})  -  {lines}");
        }

        if (_list.Items.Count > 0) _list.SelectedIndex = 0;

        var okButton = new Button
        {
            Text = "Use This", Left = 404, Top = 254, Width = 96, Height = 30, DialogResult = DialogResult.OK
        };
        var cancelButton = new Button
        {
            Text = "Cancel", Left = 508, Top = 254, Width = 96, Height = 30, DialogResult = DialogResult.Cancel
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.AddRange(new Control[] { label, _list, okButton, cancelButton });
        ReadingTheme.AttachTo(this);
    }
}
