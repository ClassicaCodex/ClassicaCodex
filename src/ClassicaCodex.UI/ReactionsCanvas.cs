using System.Drawing.Drawing2D;
using ClassicaCodex.Core.Reactions;

namespace ClassicaCodex.UI;

/// <summary>
/// One turn of a debate, with everything the canvas needs to draw it already
/// worked out.
///
/// Built by the form, which is where the database lives: whether a citation
/// resolves depends on which corpora this particular reader installed, and
/// that question costs a query. Doing it here would mean the paint handler
/// waiting on SQLite, which is how a scrolling list becomes a stutter.
/// </summary>
internal sealed record TurnView(
    DebateTurn Turn,
    AncientCritic Critic,
    bool ShowSpeaker,
    string? DividerBefore,
    string? PassageLabel,
    bool PassageResolved,
    string? SourceLabel,
    bool SourceResolved);

/// <summary>
/// The debate itself, drawn the way a group conversation is read: everyone
/// else's messages down the left, each with a face, each in their own colour.
///
/// The reader is a bystander here, not a participant, so nothing is aligned
/// right - that side is where your own messages go, and none of these are
/// yours. Consecutive turns by one speaker drop the name and the portrait and
/// tuck up under the one before, which is what every messaging application
/// does and what makes a nine-turn argument read as five people rather than as
/// nine paragraphs.
///
/// Drawn rather than built from controls. Nine turns of wrapped text with
/// optional chips underneath is nine variable-height composites, and laying
/// those out with anchors means fighting the layout engine for every pixel;
/// measuring and painting them is about two hundred lines and behaves
/// identically at every display scaling.
/// </summary>
internal sealed class ReactionsCanvas : Panel
{
    private const int AvatarSize = 44;
    private const int Gutter = 12;
    private const int Inset = 12;
    private const int BlockGap = 12;
    private const int TuckedGap = 4;
    private const int MaxBubbleWidth = 620;

    private const TextFormatFlags WrapFlags =
        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private readonly List<Block> _blocks = new();
    private IReadOnlyList<TurnView> _turns = Array.Empty<TurnView>();

    private Font _nameFont;
    private Font _metaFont;
    private Font _bodyFont;
    private Font _sourceFont;
    private Font _dividerFont;

    private Block? _hot;
    private HotSpot _hotSpot;

    public event Action<AncientCritic>? CriticClicked;
    public event Action<DebateTurn>? PassageClicked;
    public event Action<DebateTurn>? SourceClicked;

    private enum HotSpot { None, Speaker, Passage, Source }

    public ReactionsCanvas()
    {
        AutoScroll = true;
        DoubleBuffered = true;
        BackColor = ReadingTheme.Background;

        // Windows delivers the wheel to whatever has focus, not to whatever is
        // under the cursor, and a Panel cannot take focus unless it is told it
        // may. Without both of these the only way to scroll a nine-turn debate
        // is to drag the scrollbar, while the wheel silently operates the
        // combo box in the header.
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;

        _bodyFont = new Font("Segoe UI", 9.75f);
        _nameFont = new Font("Segoe UI Semibold", 9.75f, FontStyle.Bold);
        _metaFont = new Font("Segoe UI", 8.25f);
        _sourceFont = new Font("Segoe UI", 8.25f, FontStyle.Italic);
        _dividerFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);
    }

    public void SetTurns(IReadOnlyList<TurnView> turns)
    {
        _turns = turns;
        AutoScrollPosition = new Point(0, 0);
        Rebuild();
    }

    /// <summary>
    /// Text size is a reading preference and this is text to read, so the
    /// bubbles follow it. The name and the small print scale with it rather
    /// than staying put, or a large body size would leave the speaker's name
    /// looking like a footnote to their own sentence.
    /// </summary>
    public void UseTextSize(float points)
    {
        points = Math.Clamp(points, 8f, 18f);

        _bodyFont.Dispose();
        _nameFont.Dispose();
        _metaFont.Dispose();
        _sourceFont.Dispose();
        _dividerFont.Dispose();

        _bodyFont = new Font("Segoe UI", points);
        _nameFont = new Font("Segoe UI Semibold", points, FontStyle.Bold);
        _metaFont = new Font("Segoe UI", Math.Max(7f, points - 1.5f));
        _sourceFont = new Font("Segoe UI", Math.Max(7f, points - 1.5f), FontStyle.Italic);
        _dividerFont = new Font("Segoe UI", Math.Max(7f, points - 1.5f), FontStyle.Bold);

        Rebuild();
    }

    private sealed class Block
    {
        public TurnView? View;
        public string Divider = string.Empty;

        public Rectangle Avatar;
        public Rectangle Bubble;
        public Rectangle Name;
        public Rectangle Body;
        public Rectangle Passage;
        public Rectangle Source;
        public int Bottom;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Rebuild();
    }

    /// <summary>
    /// Repaints the whole canvas on any scroll rather than the strip Windows
    /// has just exposed.
    ///
    /// Scrolling blits the existing pixels and repaints only the newly
    /// uncovered band, which is right for a grid of rectangles and risky for
    /// this: the bubbles are rounded and antialiased, so the seam between
    /// blitted and freshly drawn pixels can fall in the middle of a curve. A
    /// debate is ten blocks and repainting all of them is not worth measuring.
    /// </summary>
    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate();
    }

    /// <summary>
    /// Measures every block and records where each piece goes.
    ///
    /// A control with no handle measures nothing - TextRenderer returns
    /// plausible-looking numbers against a device context that is not this
    /// one - so a layout computed before the window is shown is quietly wrong.
    /// The guard is the same one the reader panes needed, and cost a release
    /// there before it was understood.
    /// </summary>
    private void Rebuild()
    {
        _blocks.Clear();
        _hot = null;

        if (!IsHandleCreated || _turns.Count == 0)
        {
            AutoScrollMinSize = Size.Empty;
            Invalidate();
            return;
        }

        var available = ClientSize.Width - Inset * 2 - AvatarSize - Gutter;
        var bubbleWidth = Math.Min(MaxBubbleWidth, Math.Max(180, available));
        var textWidth = bubbleWidth - Inset * 2;

        var y = Inset;

        foreach (var view in _turns)
        {
            if (view.DividerBefore != null)
            {
                var divider = new Block { Divider = view.DividerBefore };
                var height = TextRenderer.MeasureText(view.DividerBefore, _dividerFont).Height + 14;
                divider.Bubble = new Rectangle(0, y + 8, ClientSize.Width, height);
                divider.Bottom = divider.Bubble.Bottom + 8;
                _blocks.Add(divider);
                y = divider.Bottom;
            }

            var block = new Block { View = view };
            var left = Inset + AvatarSize + Gutter;
            var top = y;

            var inner = top + Inset;

            if (view.ShowSpeaker)
            {
                block.Avatar = new Rectangle(Inset, top, AvatarSize, AvatarSize);

                var nameHeight = TextRenderer.MeasureText(view.Critic.Name, _nameFont).Height;
                var metaHeight = TextRenderer.MeasureText(SpeakerMeta(view.Critic), _metaFont).Height;

                block.Name = new Rectangle(left + Inset, inner, textWidth, nameHeight + metaHeight + 2);
                inner = block.Name.Bottom + 6;
            }

            var bodyHeight = TextRenderer.MeasureText(
                view.Turn.Text, _bodyFont, new Size(textWidth, int.MaxValue), WrapFlags).Height;

            block.Body = new Rectangle(left + Inset, inner, textWidth, bodyHeight);
            inner = block.Body.Bottom;

            if (view.PassageLabel != null)
            {
                var chip = TextRenderer.MeasureText(view.PassageLabel, _metaFont);
                block.Passage = new Rectangle(left + Inset, inner + 8, chip.Width + 18, chip.Height + 8);
                inner = block.Passage.Bottom;
            }

            if (view.SourceLabel != null)
            {
                var height = TextRenderer.MeasureText(
                    view.SourceLabel, _sourceFont, new Size(textWidth, int.MaxValue), WrapFlags).Height;

                block.Source = new Rectangle(left + Inset, inner + 8, textWidth, height);
                inner = block.Source.Bottom;
            }

            block.Bubble = new Rectangle(left, top, bubbleWidth, inner + Inset - top);
            block.Bottom = block.Bubble.Bottom;

            _blocks.Add(block);
            y = block.Bottom + (view.ShowSpeaker ? BlockGap : TuckedGap);
        }

        AutoScrollMinSize = new Size(0, y + Inset);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Rebuild();
    }

    private static string SpeakerMeta(AncientCritic critic)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(critic.Role)) parts.Add(critic.Role!);
        if (!string.IsNullOrWhiteSpace(critic.Era)) parts.Add(critic.Era);
        parts.Add(critic.FloruitLabel);
        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// Where a block's content coordinates land on screen right now.
    ///
    /// <b>This is done by arithmetic and not by Graphics.TranslateTransform,
    /// and the difference is the whole of a bug that shipped in this file.</b>
    ///
    /// The transform is a GDI+ concept. Every shape here is GDI+ - FillPath,
    /// DrawEllipse, DrawImage - and moved with it correctly. Every word here
    /// is drawn by TextRenderer, which is GDI, and GDI has never heard of the
    /// transform: it kept drawing at the unscrolled coordinates.
    ///
    /// At the top of a list the scroll offset is zero and the two agree
    /// exactly, which is why every screenshot taken of this window looked
    /// perfect and a reader who scrolled saw the bubbles slide up empty, with
    /// a speaker's name stranded across the bubble above and a chip's label
    /// floating outside its chip. The lesson generalises: a picture of a
    /// scrolling list at the top is not a picture of a scrolling list.
    /// </summary>
    private Rectangle At(Rectangle content) => content with
    {
        X = content.X + AutoScrollPosition.X,
        Y = content.Y + AutoScrollPosition.Y
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.Clear(ReadingTheme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        foreach (var block in _blocks)
        {
            // Nothing off-screen, so a long debate does not pay to lay out
            // what nobody can see - and, less obviously, so that a stale
            // rectangle cannot be drawn over the header.
            if (!ClientRectangle.IntersectsWith(At(block.Bubble))) continue;

            if (block.View == null) DrawDivider(e.Graphics, block);
            else DrawTurn(e.Graphics, block, block.View);
        }
    }

    /// <summary>
    /// The "Tuesday" line in a messaging app, doing the job that matters most
    /// in this window: these people are centuries apart and nobody is replying
    /// to anybody.
    /// </summary>
    private void DrawDivider(Graphics g, Block block)
    {
        var size = TextRenderer.MeasureText(block.Divider, _dividerFont);
        var pill = At(new Rectangle(
            (ClientSize.Width - size.Width) / 2 - 12,
            block.Bubble.Top,
            size.Width + 24,
            size.Height + 10));

        using (var path = Rounded(pill, pill.Height / 2))
        using (var fill = new SolidBrush(ReadingTheme.HeaderBackground))
        using (var edge = new Pen(ReadingTheme.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }

        TextRenderer.DrawText(g, block.Divider, _dividerFont, pill, ReadingTheme.MutedText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawTurn(Graphics g, Block block, TurnView view)
    {
        var signature = AncientAvatars.Signature(view.Critic.Avatar);

        // Every rectangle is put through At() here, and none of them is used
        // raw. A block's own rectangles are in content coordinates - which is
        // what the hit testing and the layout want - and the difference
        // between the two is the scroll position.
        var bubble = At(block.Bubble);

        // Toward the window's own surface rather than to a fixed white, so one
        // set of colours works in both themes: a pale wash in light mode and a
        // dark, saturated one in dark mode, from the same source colour.
        var fill = AncientAvatars.Mix(signature, ReadingTheme.Surface, ReadingTheme.IsDark ? 0.74f : 0.86f);

        using (var path = Rounded(bubble, 12))
        using (var brush = new SolidBrush(fill))
        using (var edge = new Pen(AncientAvatars.Mix(signature, ReadingTheme.Border, 0.55f)))
        {
            g.FillPath(brush, path);
            g.DrawPath(edge, path);
        }

        // A stripe of the speaker's own colour down the leading edge. The wash
        // above is deliberately faint so the text stays readable, which leaves
        // it too faint to identify anybody - this is the part you actually
        // recognise at a glance.
        using (var clip = Rounded(bubble, 12))
        {
            var state = g.Save();
            g.SetClip(clip);
            using var stripe = new SolidBrush(signature);
            g.FillRectangle(stripe, bubble.Left, bubble.Top, 4, bubble.Height);
            g.Restore(state);
        }

        if (!block.Avatar.IsEmpty)
        {
            g.DrawImage(AncientAvatars.Portrait(view.Critic, AvatarSize * 2), At(block.Avatar));
        }

        if (!block.Name.IsEmpty)
        {
            var name = At(block.Name);
            var nameSize = TextRenderer.MeasureText(view.Critic.Name, _nameFont);
            var nameRect = new Rectangle(name.Left, name.Top, nameSize.Width, nameSize.Height);

            var nameColour = _hot == block && _hotSpot == HotSpot.Speaker
                ? ReadingTheme.ActiveLinkText
                : ReadingTheme.Text;

            TextRenderer.DrawText(g, view.Critic.Name, _nameFont, nameRect, nameColour,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            if (view.Critic.Kind == CriticKind.Historical)
            {
                DrawBadge(g, new Point(nameRect.Right + 8, nameRect.Top), "real person");
            }

            TextRenderer.DrawText(g, SpeakerMeta(view.Critic), _metaFont,
                new Rectangle(name.Left, nameRect.Bottom + 2, name.Width, name.Height),
                ReadingTheme.MutedText, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        TextRenderer.DrawText(g, view.Turn.Text, _bodyFont, At(block.Body), ReadingTheme.Text, WrapFlags);

        if (!block.Passage.IsEmpty && view.PassageLabel != null)
        {
            var chip = At(block.Passage);
            var enabled = view.PassageResolved;
            var hot = enabled && _hot == block && _hotSpot == HotSpot.Passage;

            using var path = Rounded(chip, chip.Height / 2);
            using var brush = new SolidBrush(enabled
                ? AncientAvatars.Mix(signature, ReadingTheme.Surface, hot ? 0.35f : 0.55f)
                : ReadingTheme.HeaderBackground);
            using var edge = new Pen(ReadingTheme.Border);

            g.FillPath(brush, path);
            g.DrawPath(edge, path);

            TextRenderer.DrawText(g, view.PassageLabel, _metaFont, chip,
                enabled ? ReadingTheme.Text : ReadingTheme.MutedText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        if (!block.Source.IsEmpty && view.SourceLabel != null)
        {
            var colour = view.SourceResolved
                ? (_hot == block && _hotSpot == HotSpot.Source
                    ? ReadingTheme.ActiveLinkText
                    : ReadingTheme.LinkText)
                : ReadingTheme.MutedText;

            TextRenderer.DrawText(g, view.SourceLabel, _sourceFont, At(block.Source), colour, WrapFlags);
        }
    }

    /// <summary>
    /// Marks a turn as belonging to somebody who existed. The words are still
    /// a paraphrase written for this window - what the badge promises is that
    /// the view is attested and that the reference underneath is where to
    /// check it.
    /// </summary>
    private void DrawBadge(Graphics g, Point at, string text)
    {
        var size = TextRenderer.MeasureText(text, _metaFont);
        var rect = new Rectangle(at.X, at.Y, size.Width + 14, size.Height + 4);

        using var path = Rounded(rect, rect.Height / 2);
        using var brush = new SolidBrush(ReadingTheme.IsDark
            ? Color.FromArgb(60, 82, 60)
            : Color.FromArgb(222, 236, 218));
        using var edge = new Pen(ReadingTheme.IsDark
            ? Color.FromArgb(96, 130, 96)
            : Color.FromArgb(146, 178, 140));

        g.FillPath(brush, path);
        g.DrawPath(edge, path);

        TextRenderer.DrawText(g, text, _metaFont, rect,
            ReadingTheme.IsDark ? Color.FromArgb(198, 228, 192) : Color.FromArgb(40, 78, 36),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();

        if (radius <= 0 || rect.Width <= radius * 2 || rect.Height <= radius * 2)
        {
            path.AddRectangle(rect);
            return path;
        }

        var d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ---- hit testing --------------------------------------------------------

    private Point ToContent(Point client) =>
        new(client.X - AutoScrollPosition.X, client.Y - AutoScrollPosition.Y);

    private (Block? Block, HotSpot Spot) HitTest(Point client)
    {
        var at = ToContent(client);

        foreach (var block in _blocks)
        {
            if (block.View == null) continue;

            if (!block.Passage.IsEmpty && block.View.PassageResolved && block.Passage.Contains(at))
                return (block, HotSpot.Passage);

            if (!block.Source.IsEmpty && block.View.SourceResolved && block.Source.Contains(at))
                return (block, HotSpot.Source);

            if (!block.Avatar.IsEmpty && (block.Avatar.Contains(at) || block.Name.Contains(at)))
                return (block, HotSpot.Speaker);
        }

        return (null, HotSpot.None);
    }

    /// <summary>
    /// Takes focus when the pointer arrives, so the wheel scrolls what is
    /// under it. Guarded, because focusing a control that already has focus
    /// on every mouse-move message is a great deal of work for nothing.
    /// </summary>
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!Focused && CanFocus) Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var (block, spot) = HitTest(e.Location);
        if (ReferenceEquals(block, _hot) && spot == _hotSpot) return;

        _hot = block;
        _hotSpot = spot;
        Cursor = spot == HotSpot.None ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hot == null) return;
        _hot = null;
        _hotSpot = HotSpot.None;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;

        var (block, spot) = HitTest(e.Location);
        if (block?.View == null) return;

        switch (spot)
        {
            case HotSpot.Speaker: CriticClicked?.Invoke(block.View.Critic); break;
            case HotSpot.Passage: PassageClicked?.Invoke(block.View.Turn); break;
            case HotSpot.Source: SourceClicked?.Invoke(block.View.Turn); break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bodyFont.Dispose();
            _nameFont.Dispose();
            _metaFont.Dispose();
            _sourceFont.Dispose();
            _dividerFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
