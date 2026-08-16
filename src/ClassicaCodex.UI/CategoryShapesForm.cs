using ClassicaCodex.Data.Repositories;

namespace ClassicaCodex.UI;

/// <summary>
/// Assigns a node shape to each tag category, so the myth network can
/// distinguish gods from heroes from places visually.
///
/// The category list is read from the tags actually in use rather than
/// being fixed, since categories are free text typed during tagging.
/// </summary>
public class CategoryShapesForm : ScaledForm
{
    private readonly TagRepository _tagRepo = new();
    private readonly TableLayoutPanel _rows;
    private readonly Label _statusLabel;

    /// <summary>Raised when an assignment changes, so the graph can repaint.</summary>
    public event Action? ShapesChanged;

    public CategoryShapesForm()
    {
        Text = "Category Shapes";
        // ClientSize, not Width/Height - see AboutForm for why; same fix,
        // same reason the Close button's bottom edge was getting clipped.
        ClientSize = new Size(420, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var explainer = new Label
        {
            Left = 12,
            Top = 12,
            Width = 380,
            Height = 34,
            ForeColor = Color.DimGray,
            Text = "Give each category its own shape - gods as circles, heroes as squares, places as triangles, or whatever suits."
        };

        _rows = new TableLayoutPanel
        {
            Left = 12,
            Top = 52,
            Width = 380,
            Height = 340,
            ColumnCount = 2,
            AutoScroll = true
        };
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        _statusLabel = new Label { Left = 12, Top = 398, Width = 380, Height = 20, ForeColor = Color.DimGray };

        var closeButton = new Button
        {
            Text = "Close",
            Left = 316,
            Top = 420,
            Width = 76,
            Height = 28,
            DialogResult = DialogResult.OK
        };

        Controls.Add(explainer);
        Controls.Add(_rows);
        Controls.Add(_statusLabel);
        Controls.Add(closeButton);

        Load += async (_, _) => await LoadCategoriesAsync();
        ReadingTheme.AttachTo(this);
        WindowShortcuts.CloseOnEscape(this);
    }

    private async Task LoadCategoriesAsync()
    {
        var tags = await _tagRepo.GetAllTagsAsync();

        var categories = tags
            .Select(t => t.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _rows.Controls.Clear();
        _rows.RowStyles.Clear();
        _rows.RowCount = 0;

        if (categories.Count == 0)
        {
            _statusLabel.Text = "No categories yet - add one when tagging (the \"Category\" box in Auto-Tag).";
            return;
        }

        foreach (var category in categories)
        {
            var label = new Label
            {
                Text = category,
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Margin = new Padding(3, 8, 3, 3)
            };

            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 150,
                Anchor = AnchorStyles.Left
            };
            foreach (var shape in Enum.GetValues<NodeShape>()) combo.Items.Add(shape);
            combo.SelectedItem = CategoryShapes.For(category);

            // Captured so the handler assigns to the right category rather
            // than whatever the loop variable ended up as.
            var capturedCategory = category;
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (combo.SelectedItem is NodeShape selected)
                {
                    CategoryShapes.Set(capturedCategory, selected);
                    ShapesChanged?.Invoke();
                }
            };

            // Absolute rather than AutoSize: with the panel's own Height
            // fixed and taller than four rows actually need (deliberately,
            // so a few more categories can grow into it before it needs to
            // scroll), leftover space with AutoSize rows doesn't reliably
            // stay trailing space after the last row - it can show up as an
            // uneven gap between specific rows instead. A fixed height per
            // row is unambiguous regardless of how much the panel has left.
            _rows.RowCount++;
            _rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            _rows.Controls.Add(label);
            _rows.Controls.Add(combo);
        }

        _statusLabel.Text = $"{categories.Count} categor{(categories.Count == 1 ? "y" : "ies")}.";

        // These rows are built here, after AttachTo's one-time theming
        // already ran on Load - the DB round-trip above means they didn't
        // exist yet when that happened, so they'd otherwise stay unthemed
        // regardless of the current mode.
        ReadingTheme.Apply(_rows);
    }
}
