using ClassicaCodex.Core;
using System.Drawing.Drawing2D;

namespace ClassicaCodex.UI;

internal sealed class BronzeGiftForm : ScaledForm
{
    public BronzeGiftId? SelectedGift { get; private set; }
    private readonly BronzeGift[] _offers;
    private readonly Icon _giftIcon = BronzeIcons.DivineGift();
    public BronzeGiftForm(BronzeGift[] offers)
    {
        _offers = offers;
        Icon = _giftIcon;
        Text = "The gods have taken notice"; ClientSize = new Size(870, 560); MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(15, 12, 27); ForeColor = Color.Wheat;
        Font = new Font("Segoe UI", 10); KeyPreview = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.Controls.Add(new Label { Text = "CHOOSE YOUR DIVINE GIFT\nOne gift. Yours for the rest of this adventure. Gifts combine with those you already carry.",
            Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12, FontStyle.Bold) }, 0, 0);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = offers.Length, RowCount = 1 };
        for (var i = 0; i < offers.Length; i++)
        {
            var gift = offers[i]; var number = i + 1;
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / offers.Length));
            var card = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(5), Padding = new Padding(12),
                BackColor = Color.FromArgb(35, 25, 49), RowCount = 4, ColumnCount = 1 };
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 75)); card.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            var art = new GiftGlyph(gift.Id) { Dock = DockStyle.Fill }; card.Controls.Add(art, 0, 0);
            card.Controls.Add(new Label { Text = gift.Patron + "\n" + gift.Name, Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(255, 207, 113), Font = new Font("Segoe UI", 11, FontStyle.Bold) }, 0, 1);
            card.Controls.Add(new RichTextBox { Text = gift.Effect + "\n\n" + gift.Story + "\n\nMythic source: " + gift.Source,
                ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = card.BackColor, ForeColor = Color.Wheat,
                Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10), ScrollBars = RichTextBoxScrollBars.Vertical }, 0, 2);
            var accept = new Button { Text = $"[{number}] Accept gift", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(102, 240, 216) };
            accept.Click += (_, _) => Accept(gift.Id); card.Controls.Add(accept, 0, 3); cards.Controls.Add(card, i, 0);
        }
        layout.Controls.Add(cards, 0, 1);
        layout.Controls.Add(new Label { Text = "1 / 2 / 3: choose · Esc: decide later. These arena powers are playful adaptations of the myths.",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(170, 157, 187) }, 0, 2);
        Controls.Add(layout);
    }
    private void Accept(BronzeGiftId gift) { SelectedGift = gift; DialogResult = DialogResult.OK; Close(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _giftIcon.Dispose();
        base.Dispose(disposing);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        var i = (int)keyData - (int)Keys.D1;
        if (i >= 0 && i < _offers.Length) { Accept(_offers[i].Id); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private sealed class GiftGlyph(BronzeGiftId id) : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.None;
            g.TranslateTransform(Width / 2f - 24, Height / 2f - 24);
            using var gold = new Pen(Color.FromArgb(255, 207, 113), 4);
            using var cyan = new Pen(Color.FromArgb(102, 240, 216), 3);
            switch (id)
            {
                case BronzeGiftId.Athena:
                    g.DrawPolygon(gold, new[] { new Point(4, 4), new Point(44, 4), new Point(40, 30), new Point(24, 46), new Point(8, 30) });
                    g.DrawLine(cyan, 16, 28, 32, 12); break;
                case BronzeGiftId.Hermes:
                    g.DrawLines(gold, new[] { new Point(18, 8), new Point(18, 35), new Point(40, 35), new Point(40, 42), new Point(8, 42) });
                    for (var i = 0; i < 3; i++) g.DrawLine(cyan, 18, 14 + i * 5, 2, 3 + i * 5); break;
                case BronzeGiftId.Hephaestus:
                    g.DrawPolygon(gold, new[] { new Point(8, 4), new Point(17, 9), new Point(31, 9), new Point(40, 4), new Point(38, 44), new Point(10, 44) });
                    g.DrawLine(cyan, 24, 13, 24, 37); break;
                case BronzeGiftId.Apollo:
                    g.DrawArc(gold, 1, 1, 36, 46, -90, 180); g.DrawLine(cyan, 20, 2, 20, 46);
                    g.DrawLine(cyan, 6, 24, 46, 24); g.DrawLines(cyan, new[] { new Point(39, 17), new Point(46, 24), new Point(39, 31) }); break;
                case BronzeGiftId.Poseidon:
                    g.DrawLine(gold, 24, 2, 24, 46); g.DrawLines(cyan, new[] { new Point(8, 3), new Point(8, 23), new Point(40, 23), new Point(40, 3) }); break;
                case BronzeGiftId.Hades:
                    g.DrawArc(gold, 6, 3, 36, 36, 180, 180); g.DrawLines(gold, new[] { new Point(6, 21), new Point(6, 42), new Point(17, 42), new Point(17, 28), new Point(31, 28), new Point(31, 42), new Point(42, 42), new Point(42, 21) });
                    g.DrawLine(cyan, 12, 20, 19, 20); g.DrawLine(cyan, 29, 20, 36, 20); break;
            }
        }
    }
}
