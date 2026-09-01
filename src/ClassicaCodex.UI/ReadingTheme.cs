using System.Runtime.InteropServices;

namespace ClassicaCodex.UI;

public enum ReadingThemeMode
{
    Light,
    Dark
}

/// <summary>
/// Light/dark theming for the reading surfaces. WinForms has no built-in
/// theme system, so this is explicit color assignment applied recursively
/// over a control tree.
///
/// The dark palette deliberately avoids pure black on pure white: the
/// background is a dark grey rather than #000, and the text a warm off-white
/// rather than #FFF, because maximum contrast is genuinely harder to read
/// for long stretches - which is the entire point of having this mode for a
/// tool you sit and read Homer in.
/// </summary>
public static class ReadingTheme
{
    public static ReadingThemeMode Mode { get; private set; } = ReadingThemeMode.Light;

    /// <summary>Raised after the mode changes, so open windows can re-apply.</summary>
    public static event Action? Changed;

    public static bool IsDark => Mode == ReadingThemeMode.Dark;

    // Window chrome / form background
    // Light mode is parchment rather than the system grey - the same tone
    // the icon tiles are drawn on, so the toolbar reads as one surface
    // instead of illustrations pasted onto a control panel. It suits a
    // reader for texts that spent most of their life on vellum.
    public static Color Background => IsDark ? Color.FromArgb(30, 30, 32) : Color.FromArgb(237, 231, 218);

    // Reading surfaces - text panes, trees, lists
    // A shade lighter than the chrome around it, so a reading pane still
    // reads as the page rather than merging into the frame.
    public static Color Surface => IsDark ? Color.FromArgb(24, 24, 26) : Color.FromArgb(250, 247, 240);

    public static Color Text => IsDark ? Color.FromArgb(232, 228, 218) : Color.Black;

    public static Color MutedText => IsDark ? Color.FromArgb(150, 148, 142) : Color.DimGray;

    public static Color SelectionBackground => IsDark ? Color.FromArgb(38, 79, 120) : SystemColors.Highlight;

    public static Color SelectionText => IsDark ? Color.FromArgb(245, 243, 238) : SystemColors.HighlightText;

    public static Color Border => IsDark ? Color.FromArgb(60, 60, 64) : Color.FromArgb(199, 190, 172);

    /// <summary>Column-header strip - a shade off the reading surface so it reads as chrome, not content.</summary>
    public static Color HeaderBackground => IsDark ? Color.FromArgb(44, 44, 50) : Color.FromArgb(228, 220, 204);

    /// <summary>
    /// Owner-draws a ListView's column headers so they follow the theme.
    ///
    /// The header is a separate native control (SysHeader32) parented to the
    /// ListView, and it takes no notice of the ListView's BackColor - so in
    /// dark mode it stays stubbornly light while the rows around it go dark.
    /// Owner-drawing is the reliable way to reach it.
    ///
    /// Rows are left to the default renderer (DrawDefault), which already
    /// honours the ListView's themed BackColor and ForeColor - only the
    /// header actually needs drawing by hand.
    ///
    /// Safe to call repeatedly. It used to say "call this once at
    /// construction", which meant every new ListView had to remember to ask
    /// for it and one promptly didn't - the Where to start dialog shipped
    /// with a white header strip across an otherwise dark window. The
    /// handlers are named rather than lambdas so they can be detached before
    /// being reattached, which is what makes a second call harmless, and
    /// Apply now calls this for every ListView it walks.
    /// </summary>
    public static void EnableThemedHeader(ListView listView)
    {
        listView.OwnerDraw = true;

        listView.DrawColumnHeader -= DrawThemedColumnHeader;
        listView.DrawColumnHeader += DrawThemedColumnHeader;

        // Rows are drawn here too, not left to DrawDefault. Default drawing
        // paints the row using the system's own colours for grid lines and
        // selection, neither of which follows the app's theme - see
        // DrawThemedListItem.
        listView.DrawItem -= DrawItemDefault;
        listView.DrawItem -= DrawThemedListItem;
        listView.DrawItem += DrawThemedListItem;

        listView.DrawSubItem -= DrawSubItemDefault;
        listView.DrawSubItem -= DrawThemedListSubItem;
        listView.DrawSubItem += DrawThemedListSubItem;

        listView.Resize -= StretchLastColumn;
        listView.Resize += StretchLastColumn;
        StretchLastColumn(listView, EventArgs.Empty);
    }

    /// <summary>
    /// Widens the last column to take up whatever width the columns leave
    /// spare.
    ///
    /// This is a theming fix, not a layout preference. The strip of header to
    /// the right of the last column is painted by the native header control,
    /// which raises no draw event and ignores BackColor - so in dark mode a
    /// grid whose columns did not reach the right edge showed a bright white
    /// band beside a themed header, with nothing in managed code able to paint
    /// over it. Leaving no spare width is the only way to remove it.
    ///
    /// The designed width is remembered on the column's Tag so repeated
    /// resizes measure from the original rather than compounding, and so
    /// shrinking the window returns the column to the width the form asked
    /// for rather than to whatever the last resize left.
    /// </summary>
    private static void StretchLastColumn(object? sender, EventArgs e)
    {
        if (sender is not ListView list || list.Columns.Count == 0) return;
        if (list.View != View.Details) return;

        var last = list.Columns[^1];
        if (last.Tag is not int designed)
        {
            designed = last.Width;
            last.Tag = designed;
        }

        var others = 0;
        for (var i = 0; i < list.Columns.Count - 1; i++) others += list.Columns[i].Width;

        // ClientSize already excludes a visible vertical scrollbar, so no
        // allowance is needed for one; the -4 is the border inset, without
        // which the stretch overshoots by a couple of pixels and produces a
        // horizontal scrollbar on a grid that fits.
        var available = list.ClientSize.Width - others - 4;
        last.Width = Math.Max(designed, available);
    }

    /// <summary>
    /// ListViews whose form asked for grid lines before the native ones were
    /// switched off.
    ///
    /// A side table rather than the control's Tag: Tag belongs to whoever
    /// built the control, and a theme quietly taking it is the kind of thing
    /// that breaks a form months later for no visible reason. The table holds
    /// weak references, so a closed form's controls are still collectable.
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ListView, object>
        GridLineRequests = new();

    private static bool WantsGridLines(ListView list) =>
        GridLineRequests.TryGetValue(list, out _);

    private static readonly object GridLinesMarker = new();


    /// <summary>
    /// Draws a row's background and grid lines in theme colours.
    /// </summary>

    private static void DrawThemedListItem(object? sender, DrawListViewItemEventArgs e)
    {
        if (sender is not ListView list) return;

        var selected = e.Item != null && e.Item.Selected && list.Focused;
        using (var back = new SolidBrush(selected ? SelectionBackground : list.BackColor))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }

        if (!WantsGridLines(list)) return;

        using var gridPen = new Pen(Border);
        e.Graphics.DrawLine(gridPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private static void DrawThemedListSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (sender is not ListView list || e.SubItem == null) return;

        var selected = e.Item != null && e.Item.Selected && list.Focused;

        TextRenderer.DrawText(
            e.Graphics,
            e.SubItem.Text,
            e.SubItem.Font ?? list.Font,
            new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
            selected ? SelectionText : (e.SubItem.ForeColor.IsEmpty ? list.ForeColor : e.SubItem.ForeColor),
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (!WantsGridLines(list)) return;

        using var gridPen = new Pen(Border);
        e.Graphics.DrawLine(gridPen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
    }

    private static void DrawItemDefault(object? sender, DrawListViewItemEventArgs e) => e.DrawDefault = true;

    private static void DrawSubItemDefault(object? sender, DrawListViewSubItemEventArgs e) => e.DrawDefault = true;

    private static void DrawThemedColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var font = e.Font ?? (sender as ListView)?.Font ?? SystemFonts.DefaultFont;

        using (var backBrush = new SolidBrush(HeaderBackground))
        {
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        }

        using (var borderPen = new Pen(Border))
        {
            // Right edge only - column separators, without boxing in
            // every cell.
            e.Graphics.DrawLine(borderPen, e.Bounds.Right - 1, e.Bounds.Top + 3,
                e.Bounds.Right - 1, e.Bounds.Bottom - 3);
            e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1,
                e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        var textBounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? string.Empty,
            font, textBounds, Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// Themes a ContextMenuStrip and its items.
    ///
    /// A ContextMenuStrip isn't reachable through Apply's control-tree walk
    /// - it's a top-level component referenced by whatever control shows it
    /// (list.ContextMenuStrip = menu), not a child sitting in that control's
    /// Controls collection - so it needs its own entry point rather than
    /// falling out of the generic walk. Its chrome (background, selection
    /// highlight, borders, the icon margin down the left) is also drawn by a
    /// ToolStripRenderer's color table rather than simple BackColor/ForeColor
    /// properties, so a themed color table is assigned as the renderer
    /// rather than just setting colors and hoping they're honored.
    ///
    /// Call this again after a theme change and reachable from wherever the
    /// menu is held onto - a form that stays open across a toggle (unlike
    /// the modal dialogs, which are theme-stable for their whole lifetime)
    /// needs to re-run this the same way it re-runs Apply.
    /// </summary>
    public static void ApplyToContextMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.Renderer = new ToolStripProfessionalRenderer(new ThemedMenuColorTable());

        foreach (ToolStripItem item in menu.Items) ApplyToMenuItem(item);
    }

    /// <summary>
    /// Themes one menu item and anything hanging off it.
    ///
    /// RECURSIVE, because the top-level loop was not. A submenu's items are not
    /// in menu.Items - they are in the parent item's DropDownItems, and a
    /// separate ToolStripDropDown with its own BackColor and its own renderer.
    /// So "Show" was themed and the Text / Speakers / Stage directions entries
    /// inside it kept the system default: dark ink on a dark surface, invisible
    /// in dark mode and only findable by knowing it was there.
    ///
    /// The renderer has to be set on each drop-down as well as on the menu:
    /// ToolStripDropDown does not inherit its owner's.
    ///
    /// PUBLIC because recursion from the top is not enough on its own. A
    /// submenu built when the menu opens is empty when this walk reaches it, so
    /// the walk returns before touching a drop-down that does not exist yet -
    /// and the items that appear a moment later have never been themed at all.
    /// Whoever fills such a menu has to call this on the parent afterwards; see
    /// MainForm.BuildKindMenu.
    /// </summary>
    public static void ApplyToMenuItem(ToolStripItem item)
    {
        item.ForeColor = Text;
        item.BackColor = Surface;

        if (item is not ToolStripMenuItem parent || !parent.HasDropDownItems) return;

        parent.DropDown.BackColor = Surface;
        parent.DropDown.ForeColor = Text;
        parent.DropDown.Renderer = new ToolStripProfessionalRenderer(new ThemedMenuColorTable());

        foreach (ToolStripItem child in parent.DropDownItems) ApplyToMenuItem(child);
    }

    /// <summary>
    /// Themes a ToolTip. Unlike ContextMenuStrip, a plain (non-owner-drawn)
    /// ToolTip's BackColor/ForeColor are honored directly, so no custom
    /// renderer is needed - just the theme's colors, read once at whatever
    /// point the tooltip is created. That's enough here: every ToolTip in
    /// this app lives on a modal dialog that's rebuilt fresh each time it's
    /// shown, and the theme can't change while a modal dialog is blocking
    /// the window behind it, so there's nothing to re-apply later.
    /// </summary>
    public static void ApplyToToolTip(ToolTip tip)
    {
        tip.BackColor = Surface;
        tip.ForeColor = Text;
    }

    /// <summary>
    /// Color table backing <see cref="ApplyToContextMenu"/>. Every property
    /// reads the corresponding ReadingTheme color live rather than caching
    /// it, so a menu re-themed after a toggle paints correctly the next
    /// time it's shown, with no separate light/dark branching needed here -
    /// the ReadingTheme properties it reads already branch internally.
    /// </summary>
    private sealed class ThemedMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color MenuItemSelected => SelectionBackground;
        public override Color MenuItemSelectedGradientBegin => SelectionBackground;
        public override Color MenuItemSelectedGradientEnd => SelectionBackground;
        public override Color MenuItemPressedGradientBegin => SelectionBackground;
        public override Color MenuItemPressedGradientMiddle => SelectionBackground;
        public override Color MenuItemPressedGradientEnd => SelectionBackground;
    }

    public static void SetMode(ReadingThemeMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        Save();
        Changed?.Invoke();
    }

    public static void Toggle() => SetMode(IsDark ? ReadingThemeMode.Light : ReadingThemeMode.Dark);

    /// <summary>
    /// Wires a form to the theme: applies it on load, keeps it in sync if
    /// the mode changes while the form is open, and unsubscribes on close so
    /// the handler can't outlive the window.
    ///
    /// <paramref name="extra"/> runs after each apply, for the odd control
    /// that needs a colour the generic walk wouldn't pick - a scroll host
    /// sitting directly behind a canvas, say, which wants the reading
    /// surface rather than the window background.
    /// </summary>
    public static void AttachTo(Form form, Action? extra = null)
    {
        void ApplyNow()
        {
            Apply(form);
            extra?.Invoke();
            form.Invalidate(true);
        }

        form.Load += (_, _) => ApplyNow();

        // The title bar again, after the window is actually on screen.
        //
        // DWM accepts the immersive-dark attribute at Load and reports
        // success, but the caption is not repainted from it until the frame
        // is next drawn - so a dialog kept whatever the window was created
        // with, which is the Windows accent colour, while its whole client
        // area was correctly dark. MainForm never showed this because it
        // re-applies the theme in its own Shown handler; every other window
        // did.
        //
        // Only the title bar is redone here, not the whole tree walk: the
        // walk overwrites per-selection colours, which is the problem
        // ReapplyDimmedText exists to undo.
        form.Shown += (_, _) => ApplyNativeTitleBar(form);

        Changed += ApplyNow;
        form.FormClosed += (_, _) => Changed -= ApplyNow;
    }

    /// <summary>
    /// Walks a control tree applying theme colors. Control types are handled
    /// individually rather than blanket-set, because WinForms controls vary
    /// in which properties actually take effect - a Button needs FlatStyle
    /// changed before its BackColor is honored at all, for instance.
    /// </summary>
    public static void Apply(Control root)
    {
        ApplyToControl(root);

        foreach (Control child in root.Controls)
        {
            Apply(child);
        }
    }

    private static void ApplyToControl(Control control)
    {
        switch (control)
        {
            case Form form:
                form.BackColor = Background;
                form.ForeColor = Text;
                ApplyNativeTitleBar(form);
                break;

            case GraphCanvas graph:
                // A content surface like the reader panes, not window chrome.
                graph.BackColor = Surface;
                graph.ForeColor = Text;
                break;

            case TimelineCanvas timeline:
                timeline.BackColor = Surface;
                timeline.ForeColor = Text;
                break;

            case FingerprintCanvas fingerprint:
                fingerprint.BackColor = Surface;
                fingerprint.ForeColor = Text;
                break;

            case SyncListView syncList:
                syncList.BackColor = Surface;
                syncList.ForeColor = Text;
                ApplyNativeScrollbarTheme(syncList);
                break;

            case TreeView tree:
                tree.BackColor = Surface;
                tree.ForeColor = Text;
                tree.LineColor = MutedText;
                ApplyNativeScrollbarTheme(tree);
                break;

            case DataGridView grid:
                // DataGridView does not inherit useful colours from its
                // parent. Its cells and headers each keep separate system
                // defaults, which otherwise leaves a white table inside an
                // entirely dark form.
                grid.BackgroundColor = Surface;
                grid.GridColor = Border;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBackground;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBackground;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
                grid.RowHeadersDefaultCellStyle.BackColor = HeaderBackground;
                grid.RowHeadersDefaultCellStyle.ForeColor = Text;
                grid.RowHeadersDefaultCellStyle.SelectionBackColor = SelectionBackground;
                grid.RowHeadersDefaultCellStyle.SelectionForeColor = SelectionText;
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = SelectionBackground;
                grid.DefaultCellStyle.SelectionForeColor = SelectionText;
                grid.RowsDefaultCellStyle.BackColor = Surface;
                grid.RowsDefaultCellStyle.ForeColor = Text;
                grid.RowsDefaultCellStyle.SelectionBackColor = SelectionBackground;
                grid.RowsDefaultCellStyle.SelectionForeColor = SelectionText;
                grid.AlternatingRowsDefaultCellStyle.BackColor = IsDark
                    ? Color.FromArgb(29, 29, 32)
                    : Color.FromArgb(245, 241, 232);
                grid.AlternatingRowsDefaultCellStyle.ForeColor = Text;
                grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = SelectionBackground;
                grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = SelectionText;
                ApplyNativeScrollbarTheme(grid);
                break;

            case ListView listView:
                // A results grid is a content surface like the lists above.
                listView.BackColor = Surface;
                listView.ForeColor = Text;
                ApplyNativeScrollbarTheme(listView);

                // GridLines must be switched off, not merely drawn over. The
                // native control paints its lines beneath owner-drawn content
                // and across the whole client area including the empty space
                // below the last row - so an empty grid came out as a full
                // page of white rules with nothing in it, and a populated one
                // kept white lines under the themed ones.
                //
                // The form's intent is remembered on Tag so the themed
                // painters know whether to draw lines at all.
                if (listView.GridLines)
                {
                    GridLineRequests.Remove(listView);
                    GridLineRequests.Add(listView, GridLinesMarker);
                    listView.GridLines = false;
                }

                // The header is a separate native control that ignores the
                // BackColor set just above, so it is owner-drawn. Done here
                // rather than left to each form to ask for, because a form
                // that forgets gets a white strip across a dark window and
                // nothing about the code looks wrong.
                EnableThemedHeader(listView);
                break;

            case ListBox listBox:
                listBox.BackColor = Surface;
                listBox.ForeColor = Text;

                // A row wider than the list is otherwise cut off at the edge
                // with nothing to say so and no way to see the rest of it.
                //
                // This was removed once, on evidence: setting it makes WinForms
                // measure every item with GDI+ to size the scroll extent, and
                // the places map went down with "a generic error occurred in
                // GDI+" when that measurement met the private-use codepoints
                // the Menota transcriptions use for medieval glyphs. The note
                // left behind said the measurement was the thing that failed,
                // so the fix was to stop asking for it.
                //
                // Re-tested since, against the library rather than against a
                // guess: all 279,195 Menota passages, all 20,412 passages
                // anywhere in the corpus that carry a private-use character,
                // and every work title and author name - 299,607 rows through
                // both Graphics.MeasureString and a list with this property
                // set. Nothing failed. Lone surrogates, reversed pairs, NUL,
                // and 64k-character rows do not reproduce it either. Whatever
                // took the map down, the diagnosis recorded for it does not
                // hold, and the whole corpus is a better witness than the
                // inference was.
                //
                // Set here rather than per form for the reason the search
                // results said it best: one rule for every list is easier to
                // keep than an exception. Owner-drawn lists are unaffected -
                // WinForms will not measure what it does not draw, so they
                // stay at extent 0 and show no bar until a form sets one
                // deliberately. That is what keeps the wrapped translation
                // columns wrapping.
                listBox.HorizontalScrollbar = true;

                ApplyNativeScrollbarTheme(listBox);
                break;

            // Both TextBox and RichTextBox, matched on their shared base -
            // a RichTextBox is not a TextBox, so the case below would have
            // missed it entirely and left a white panel in dark mode.
            case TextBoxBase textBox:
                textBox.BackColor = Surface;
                textBox.ForeColor = Text;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                ApplyNativeScrollbarTheme(textBox);
                break;

            case ComboBox combo:
                combo.BackColor = Surface;
                combo.ForeColor = Text;
                combo.FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.Standard;

                // The dropdown list is a separate native window that won't
                // take BackColor, so its items have to be drawn by hand.
                // Unsubscribe-then-subscribe keeps this idempotent, since
                // Apply runs again on every theme toggle.
                combo.DrawItem -= DrawComboItem;
                if (IsDark)
                {
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.DrawItem += DrawComboItem;
                }
                else
                {
                    combo.DrawMode = DrawMode.Normal;
                }

                ApplyNativeScrollbarTheme(combo);
                break;

            // Before the general Button case, which would otherwise claim
            // these and put a border back around them.
            case IconButton iconButton:
                iconButton.FlatStyle = FlatStyle.Flat;
                iconButton.FlatAppearance.BorderSize = 0;

                // Matches the toolbar it sits on, so only the icon reads as
                // a shape. The hover and press tints are the sole feedback -
                // deliberately faint, since a full-strength highlight behind
                // an already-busy tile just muddies it.
                iconButton.BackColor = Background;
                iconButton.ForeColor = Text;
                iconButton.FlatAppearance.MouseOverBackColor =
                    IsDark ? Color.FromArgb(58, 58, 64) : Color.FromArgb(222, 214, 196);
                iconButton.FlatAppearance.MouseDownBackColor =
                    IsDark ? Color.FromArgb(72, 72, 80) : Color.FromArgb(208, 198, 176);
                iconButton.UseVisualStyleBackColor = false;
                break;

            case Button button:
                // A Button ignores BackColor entirely under the default
                // FlatStyle.System rendering, so dark mode has to switch it
                // to Flat for the color to apply at all.
                button.FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                button.BackColor = IsDark ? Color.FromArgb(48, 48, 52) : Color.FromArgb(232, 226, 212);
                button.ForeColor = IsDark ? Text : SystemColors.ControlText;
                button.FlatAppearance.BorderColor = Border;
                button.UseVisualStyleBackColor = !IsDark;
                break;

            case CheckBox or RadioButton:
                control.BackColor = Color.Transparent;
                control.ForeColor = Text;
                break;

            case Label label:
                label.BackColor = Color.Transparent;

                // A label deliberately coloured red is carrying emphasis
                // (the NonCommercial licence notice, for one). Flattening it
                // to body text would lose that, so it keeps a red - just one
                // that's actually legible on the current surface, since dark
                // red on a dark background isn't.
                if (IsWarningLabel(label))
                {
                    label.ForeColor = IsDark ? Color.FromArgb(255, 130, 130) : Color.DarkRed;
                    break;
                }

                // Labels already deliberately set to a muted grey stay muted -
                // overriding them would lose the visual hierarchy they carry.
                label.ForeColor = IsSubduedLabel(label) ? MutedText : Text;
                break;

            case Panel panel:
                panel.BackColor = Background;
                panel.ForeColor = Text;
                // A Panel with AutoScroll draws native scrollbars, same as
                // the list controls above - without this they stay light
                // against a dark surface.
                ApplyNativeScrollbarTheme(panel);
                break;

            // ThemedTabControl draws its own strip and needs nothing here
            // beyond a repaint, because the native strip can only be covered
            // after base.WndProc has painted it - see that class.
            case ThemedTabControl themedTabs:
                themedTabs.BackColor = Background;
                themedTabs.ForeColor = Text;
                themedTabs.Invalidate();
                break;

            // A plain TabControl cannot be themed from out here: its strip is
            // painted by the native control after every managed hook has run.
            // The colours below at least keep the pages right; the strip stays
            // light. Use ThemedTabControl instead.
            case TabControl tabs:
                tabs.BackColor = Background;
                tabs.ForeColor = Text;
                break;

            case SplitContainer or SplitterPanel or TabPage:
                control.BackColor = Background;
                control.ForeColor = Text;
                break;

            case NumericUpDown numeric:
                numeric.BackColor = Surface;
                numeric.ForeColor = Text;
                break;

            default:
                control.BackColor = Background;
                control.ForeColor = Text;
                break;
        }
    }

    /// <summary>
    /// Whether a label is carrying red-for-emphasis. Checked against both
    /// palettes' reds so a mode switch doesn't strip the emphasis on the
    /// second pass.
    /// </summary>
    private static bool IsWarningLabel(Label label) =>
        label.ForeColor == Color.DarkRed
        || label.ForeColor == Color.FromArgb(255, 130, 130);

    /// <summary>
    /// Whether a label was already styled as secondary text. Checked against
    /// both palettes' muted colors so a mode switch doesn't permanently
    /// promote a hint label to full-contrast body text.
    /// </summary>
    private static bool IsSubduedLabel(Label label) =>
        label.ForeColor == Color.DimGray
        || label.ForeColor == Color.FromArgb(150, 148, 142)
        || label.ForeColor == Color.DarkSlateGray;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClassicaCodex", "theme.txt");

    // --- Native theming ---------------------------------------------------
    //
    // Scrollbars and the window title bar are drawn by Windows itself, not
    // by WinForms, so no .NET color property reaches them. Both have a
    // documented-enough native escape hatch, used below. Every call here is
    // best-effort: if a given Windows build doesn't support it, the call
    // fails harmlessly and that element just stays light rather than
    // breaking anything.

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    // Windows 11 22000+. Setting the caption and its text explicitly is the
    // only thing that beats the "Show accent colour on title bars and window
    // borders" personalisation setting - with that on, Windows paints the
    // active window's caption in the accent colour and ignores immersive
    // dark mode entirely, which is why the focused window was the one that
    // looked wrong while every window behind it looked right.
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    // Hands the caption back to Windows rather than pinning it to a colour.
    private const int DwmwaColorDefault = unchecked((int)0xFFFFFFFF);

    /// <summary>
    /// Switches a control's native scrollbars between the dark and light
    /// Explorer themes. "DarkMode_Explorer" is the theme name the Windows
    /// shell itself uses for dark scrollbars - it isn't formally documented
    /// for third-party use, but it's the standard approach and degrades
    /// silently on builds that don't recognize it.
    /// </summary>
    private static void ApplyNativeScrollbarTheme(Control control)
    {
        if (!control.IsHandleCreated) return;

        try
        {
            SetWindowTheme(control.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch
        {
            // Older Windows without the dark shell themes - leave as-is.
        }
    }

    /// <summary>
    /// Darkens the window's title bar. The attribute id changed between
    /// Windows 10 builds (19 before 20H1, 20 after), so both are attempted.
    /// </summary>
    private static void ApplyNativeTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;

        try
        {
            var useDark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeLegacy, ref useDark, sizeof(int));
            }

            // Immersive dark mode alone is not enough when the accent colour
            // is set to paint title bars: Windows uses the accent for the
            // active window regardless, so the window being looked at was
            // the one that stayed light. An explicit caption colour takes
            // precedence over both.
            //
            // Light mode hands the caption back to Windows rather than
            // pinning it to the parchment background - someone running the
            // accent on title bars chose that, and there is no reason to
            // override it in the theme that already matches the system.
            var caption = IsDark ? ToColorRef(Background) : DwmwaColorDefault;
            var captionText = IsDark ? ToColorRef(Text) : DwmwaColorDefault;

            // Both fail harmlessly on Windows 10, where these attributes do
            // not exist - the immersive flag above is all that platform has,
            // and it is enough there because the accent setting behaves
            // differently.
            DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, DwmwaTextColor, ref captionText, sizeof(int));
        }
        catch
        {
            // Pre-dark-mode Windows - title bar simply stays light.
        }
    }

    /// <summary>
    /// A Color as a Win32 COLORREF, which orders its bytes 0x00BBGGRR -
    /// backwards from the 0xRRGGBB most colour literals are written in, and a
    /// silent source of blue-for-red if assumed either way round.
    /// </summary>
    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    /// <summary>
    /// Draws one combo box item. A ComboBox's dropdown list is a native
    /// window that ignores BackColor entirely, so owner-drawing the items
    /// is the only way to color the list portion.
    /// </summary>
    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo) return;

        var selected = (e.State & DrawItemState.Selected) != 0;
        var backColor = selected ? SelectionBackground : Surface;
        var foreColor = selected ? SelectionText : Text;

        using (var brush = new SolidBrush(backColor))
        {
            e.Graphics.FillRectangle(brush, e.Bounds);
        }

        if (e.Index >= 0 && e.Index < combo.Items.Count)
        {
            var text = combo.Items[e.Index]?.ToString() ?? string.Empty;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, e.Bounds, foreColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        e.DrawFocusRectangle();
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var saved = File.ReadAllText(SettingsPath).Trim();
            if (Enum.TryParse<ReadingThemeMode>(saved, ignoreCase: true, out var mode)) Mode = mode;
        }
        catch
        {
            // A theme preference isn't worth failing startup over - the
            // default (light) is a perfectly usable fallback.
        }
    }

    private static void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath, Mode.ToString());
        }
        catch
        {
            // Same reasoning as Load - a failed save just means the
            // preference doesn't survive restart.
        }
    }
}
