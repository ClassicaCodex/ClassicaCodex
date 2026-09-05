using ClassicaCodex.Core;
using System.Drawing.Drawing2D;

namespace ClassicaCodex.UI;

internal sealed class BronzeCollectionForm : ScaledForm
{
    private readonly Icon _windowIcon = BronzeIcons.Bestiary();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private readonly PictureBox _portrait = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.CenterImage };
    private static readonly Color Ink = Color.FromArgb(22, 18, 34);
    private static readonly string[] Tactics =
    {
        "SERPENT\nA low, relentless pursuer. Keep your spear between its head and your heels.",
        "HARPY\nA winged skirmisher that holds its distance. Watch its warning, then dodge across the bolt's path.",
        "BOAR\nIts warning marks a charge. Step aside before it commits, then strike from the flank.",
        "CYCLOPS\nA close-range guardian with a crushing stomp. Leave the wide pink ring before the blow lands.",
        "GORGON\nA ranged guardian whose bolts punish a direct approach. Athena's shield can return them.",
        "HYDRA\nFive bolts spread from a single warning. Find a gap, or erase nearby bolts with the thunder ring."
    };
    private static readonly string[] MythicEchoes =
    {
        "Mythic echo: Apollo kills the serpent Python at Delphi. Apollodorus, Library 1.4.1.",
        "Mythic echo: the Harpies torment Phineus by stealing his food. Apollodorus, Library 1.9.21.",
        "Mythic echo: Heracles captures the Erymanthian boar alive. Apollodorus, Library 2.5.4.",
        "Mythic echo: Odysseus meets Polyphemus in the Cyclops's cave. Homer, Odyssey 9.",
        "Mythic echo: Perseus approaches Medusa by watching her reflection. Apollodorus, Library 2.4.2.",
        "Mythic echo: Heracles fights the Lernaean Hydra with Iolaus's help. Apollodorus, Library 2.5.2."
    };

    public BronzeCollectionForm(BronzeChronicle chronicle, Func<ArcadePassage, Task> reopen,
        Func<BronzeEnemyKind, CancellationToken, Task<List<BronzeWitness>>>? loadWitnesses = null)
    {
        Icon = _windowIcon;
        Text = "The hero's chronicle — Bestiary & laurels"; ClientSize = new Size(940, 720);
        MinimumSize = new Size(760, 620); StartPosition = FormStartPosition.CenterParent;
        BackColor = Ink; ForeColor = Color.Wheat; Font = new Font("Segoe UI", 10);
        Padding = new Padding(10);
        var tabs = new ChronicleTabs { Dock = DockStyle.Fill };
        var bestiary = new TabPage("Discovered bestiary") { BackColor = Ink, ForeColor = Color.Wheat };
        var laurels = new TabPage("Hall of laurels") { BackColor = Ink, ForeColor = Color.Wheat };
        tabs.TabPages.Add(bestiary); tabs.TabPages.Add(laurels); Controls.Add(tabs);
        var split = Split(); bestiary.Controls.Add(split);
        var creatures = List(); split.Panel1.Controls.Add(creatures);
        foreach (var kind in Enum.GetValues<BronzeEnemyKind>())
        {
            var discovery = chronicle.Bestiary.FirstOrDefault(d => d.Kind == kind);
            creatures.Items.Add(discovery == null ? "?  Undiscovered" : $"{kind}  ·  {discovery.Defeats:N0}");
        }
        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10) };
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        detail.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); split.Panel2.Controls.Add(detail);
        detail.Controls.Add(_portrait, 0, 0);
        var description = TextPanel(); detail.Controls.Add(description, 0, 1);
        var ancient = new Button { Text = "Scholia · Read the ancient witnesses", Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(102, 240, 216), Enabled = false };
        detail.Controls.Add(ancient, 0, 2);
        var loadingWitness = false;
        ancient.Click += async (_, _) =>
        {
            var index = creatures.SelectedIndex;
            if (loadingWitness || index < 0 || loadWitnesses == null) return;
            loadingWitness = true; ancient.Enabled = false; ancient.Text = "Consulting the ancient witnesses…";
            try
            {
                var witnesses = await loadWitnesses((BronzeEnemyKind)index, _lifetime.Token);
                if (IsDisposed || _lifetime.IsCancellationRequested || creatures.SelectedIndex != index) return;
                if (witnesses.Count == 0)
                {
                    MessageBox.Show(this, "This library does not contain the referenced passages for this creature. Its mythic references remain in the bestiary.", "No ancient witness in this library");
                    return;
                }
                using var scholia = new BronzeWitnessForm(witnesses);
                if (scholia.ShowDialog(this) == DialogResult.OK && scholia.SelectedPassage is { } row)
                { await reopen(row); DialogResult = DialogResult.OK; Close(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Could not consult this witness"); }
            finally
            {
                if (!IsDisposed)
                {
                    loadingWitness = false; ancient.Text = "Scholia · Read the ancient witnesses";
                    ancient.Enabled = chronicle.Bestiary.Any(d => (int)d.Kind == creatures.SelectedIndex) && loadWitnesses != null;
                }
            }
        };
        var memories = new MemoryReader(reopen, this) { Dock = DockStyle.Fill }; detail.Controls.Add(memories, 0, 3);
        creatures.SelectedIndexChanged += (_, _) =>
        {
            var kind = (BronzeEnemyKind)creatures.SelectedIndex;
            var entry = chronicle.Bestiary.FirstOrDefault(d => d.Kind == kind);
            ancient.Enabled = entry != null && loadWitnesses != null && !loadingWitness;
            _portrait.Image?.Dispose(); _portrait.Image = null;
            if (entry == null)
            {
                description.Text = "A SHADOW WITHOUT A NAME\n\nDefeat this creature to reveal its portrait and arena tactics.";
                memories.Set(Array.Empty<BronzeRecoveredVerse>(), "No encounter recorded yet."); return;
            }
            _portrait.Image = Portrait(kind);
            description.Text = Tactics[(int)kind] + "\n\n" + MythicEchoes[(int)kind]
                + $"\n\n{entry.Defeats:N0} defeated across your adventures.";
            memories.Set(entry.Verses, "Verses you recovered after encounters with this creature:");
        };
        creatures.SelectedIndex = 0;

        var hall = Split(); laurels.Controls.Add(hall);
        var victories = List(); hall.Panel1.Controls.Add(victories);
        var trophies = chronicle.Trophies.OrderByDescending(t => t.EarnedAt).ToList();
        foreach (var trophy in trophies) victories.Items.Add("★ " + trophy.ArcTitle);
        var hallDetail = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12) };
        hallDetail.RowStyles.Add(new RowStyle(SizeType.Absolute, 220)); hallDetail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        hall.Panel2.Controls.Add(hallDetail);
        var inscription = TextPanel(); hallDetail.Controls.Add(inscription, 0, 0);
        var story = new MemoryReader(reopen, this) { Dock = DockStyle.Fill }; hallDetail.Controls.Add(story, 0, 1);
        inscription.Text = "THE STARS ARE WAITING\n\nFinish a connected story to earn a laurel, a heroic epithet, and a star on the title screen. Your completed stories stay here when you begin another adventure.";
        victories.SelectedIndexChanged += (_, _) =>
        {
            if (victories.SelectedIndex < 0) return;
            var trophy = trophies[victories.SelectedIndex];
            inscription.Text = $"{trophy.Epithet.ToUpperInvariant()}\n{trophy.ArcTitle} · {trophy.Score:N0} points · {trophy.EarnedAt.LocalDateTime:d}\n\n"
                + trophy.Premise + "\n\n" + trophy.Payoff + "\n\nGifts: " + string.Join(" · ", trophy.Gifts.Select(g => BronzeGifts.Get(g).Name));
            if (chronicle.Trophies.Select(t => t.ArcKey).Distinct().Count() >= QuestArcs.All.Length)
                inscription.AppendText("\n\nTHE OWL'S SECRET: You have returned every lost story to the sky. Even Athena closes her book to applaud.");
            story.Set(trophy.Verses, "The verses that made this story:");
        };
        if (trophies.Count > 0) victories.SelectedIndex = 0;
    }

    private static SplitContainer Split() => new() { Dock = DockStyle.Fill, SplitterDistance = 210, Size = new Size(930, 650), Panel1MinSize = 170, Panel2MinSize = 350 };
    private static ListBox List() => new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 25, 49), ForeColor = Color.Wheat,
        BorderStyle = BorderStyle.None, IntegralHeight = false, HorizontalScrollbar = true, ItemHeight = 28 };
    private static RichTextBox TextPanel() => new() { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = true,
        BackColor = Ink, ForeColor = Color.Wheat, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10), DetectUrls = false };
    private static Bitmap Portrait(BronzeEnemyKind kind)
    {
        using var small = new Bitmap(40, 40); using var sprites = new BronzeSprites();
        using (var g = Graphics.FromImage(small)) sprites.Draw(g, kind.ToString(), 20, 29, false, true, 0, false);
        var large = new Bitmap(100, 100);
        using (var g = Graphics.FromImage(large))
        { g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(small, new Rectangle(0, 0, 100, 100)); }
        return large;
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    { if (keyData == Keys.Escape) { Close(); return true; } return base.ProcessCmdKey(ref msg, keyData); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
            _portrait.Image?.Dispose(); _portrait.Image = null; _windowIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    // Keep native tab navigation and accessibility, but paint the whole strip
    // so Windows' pale unused header area cannot leak into the arcade palette.
    private sealed class ChronicleTabs : TabControl
    {
        private readonly Font _headerFont = new("Segoe UI", 11, FontStyle.Bold);
        private readonly Bitmap[] _icons;
        public ChronicleTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(260, 58);
            using var beast = BronzeIcons.Bestiary();
            using var laurel = BronzeIcons.Laurels();
            using var smallBeast = new Icon(beast, 32, 32);
            using var smallLaurel = new Icon(laurel, 32, 32);
            _icons = new[] { smallBeast.ToBitmap(), smallLaurel.ToBitmap() };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Ink);
            var scale = DeviceDpi / 96f;
            var inset = (int)(10 * scale);
            var iconSize = (int)(32 * scale);
            for (var i = 0; i < TabCount; i++)
            {
                var bounds = GetTabRect(i);
                bounds.Inflate(-2, -2);
                var selected = SelectedIndex == i;
                var accent = selected ? Color.FromArgb(255, 207, 113) : Color.FromArgb(116, 91, 137);
                using var fill = new SolidBrush(selected ? Color.FromArgb(55, 35, 68) : Color.FromArgb(35, 25, 49));
                using var border = new Pen(accent);
                e.Graphics.FillRectangle(fill, bounds);
                e.Graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                if (i < _icons.Length)
                    e.Graphics.DrawImage(_icons[i], bounds.X + inset, bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
                var textBounds = new Rectangle(bounds.X + inset * 2 + iconSize, bounds.Y,
                    bounds.Width - inset * 3 - iconSize, bounds.Height);
                TextRenderer.DrawText(e.Graphics, TabPages[i].Text, _headerFont, textBounds,
                    selected ? Color.FromArgb(255, 225, 173) : Color.FromArgb(211, 197, 224),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                if (selected)
                {
                    using var underline = new SolidBrush(accent);
                    e.Graphics.FillRectangle(underline, bounds.X, bounds.Bottom - 3, bounds.Width, 3);
                    if (Focused && ShowFocusCues)
                    {
                        var focus = bounds; focus.Inflate(-5, -5);
                        ControlPaint.DrawFocusRectangle(e.Graphics, focus, Color.Wheat, fill.Color);
                    }
                }
            }
        }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { foreach (var icon in _icons) icon.Dispose(); _headerFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class MemoryReader : UserControl
    {
        private readonly ListBox _verses = List();
        private readonly RichTextBox _text = TextPanel();
        private readonly Label _heading = new() { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(102, 240, 216) };
        private readonly Button _open = new() { Text = "Reopen selected verse in the library", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        private IReadOnlyList<BronzeRecoveredVerse> _items = Array.Empty<BronzeRecoveredVerse>();
        public MemoryReader(Func<ArcadePassage, Task> reopen, Form owner)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.Controls.Add(_heading, 0, 0); layout.Controls.Add(_verses, 0, 1); layout.Controls.Add(_text, 0, 2); layout.Controls.Add(_open, 0, 3);
            Controls.Add(layout); _open.Enabled = false;
            _verses.SelectedIndexChanged += (_, _) =>
            {
                _open.Enabled = _verses.SelectedIndex >= 0;
                if (_verses.SelectedIndex < 0) return;
                var item = _items[_verses.SelectedIndex];
                _text.Text = item.ArcTitle + "\n\n" + item.Passage.Text + "\n\n" + item.Meaning;
            };
            _open.Click += async (_, _) =>
            {
                if (_verses.SelectedIndex < 0) return;
                var row = _items[_verses.SelectedIndex].Passage; _open.Enabled = false;
                try { await reopen(row); owner.DialogResult = DialogResult.OK; owner.Close(); }
                catch (Exception ex) { if (!IsDisposed) MessageBox.Show(owner, ex.Message, "Could not reopen verse"); }
                finally { if (!IsDisposed) _open.Enabled = true; }
            };
        }
        public void Set(IReadOnlyList<BronzeRecoveredVerse> items, string heading)
        {
            _items = items; _heading.Text = heading; _verses.Items.Clear(); _text.Clear(); _open.Enabled = false;
            foreach (var item in items) _verses.Items.Add($"{item.Passage.Author} — {item.Passage.Title} {PassageCitation.Display(item.Passage.Citation)}");
            if (items.Count > 0) _verses.SelectedIndex = 0;
            else _text.Text = "Recover a story passage after battle to add its reading trail here.";
        }
    }
}

