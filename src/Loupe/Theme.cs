using System.Drawing.Drawing2D;

namespace Loupe;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(21, 21, 25);
    public static readonly Color Panel = Color.FromArgb(27, 27, 32);
    public static readonly Color Fill = Color.FromArgb(41, 41, 47);
    public static readonly Color Muted = Color.FromArgb(177, 177, 188);
    public static readonly Color Text = ColorTranslator.FromHtml("#FCFFFF");
    public static readonly Color Sky = ColorTranslator.FromHtml("#0593C6");
    public static readonly Color Pink = ColorTranslator.FromHtml("#D83995");
    public static readonly Color Border = Color.FromArgb(53, 53, 61);
    public static readonly Color Outline = Color.FromArgb(96, 96, 105);
    public static readonly Color AccentFill = Color.FromArgb(24, 45, 57);

    public static void Apply(Form form)
    {
        form.BackColor = Background; form.ForeColor = Text;
        form.Font = new Font("Segoe UI", 9.5f);
        form.Icon = AppIcon.Create();
    }

    public static GraphicsPath RoundRect(RectangleF bounds, float radius, bool openBottom = false)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;
        float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        if (openBottom) { path.AddLine(bounds.Right, bounds.Bottom, bounds.Left, bounds.Bottom); }
        else
        {
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        }
        path.CloseFigure();
        return path;
    }

    // Fill the outer silhouette and then its inset, instead of centering a pen on
    // an edge. The outer bounds and rounded corners stay identical in every state.
    public static void Surface(Graphics graphics, Size size, Color fill, Color border, float scale, bool openBottom = false)
    {
        if (size.Width < 3 || size.Height < 3) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        float thickness = Math.Max(1, (float)Math.Round(scale));
        float radius = 4 * scale;
        using var outer = RoundRect(new RectangleF(0, 0, size.Width, size.Height), radius, openBottom);
        using var inner = RoundRect(new RectangleF(thickness, thickness, size.Width - 2 * thickness, size.Height - (openBottom ? thickness : 2 * thickness)), Math.Max(1, radius - thickness), openBottom);
        using var edge = new SolidBrush(border);
        using var background = new SolidBrush(fill);
        graphics.FillPath(edge, outer);
        graphics.FillPath(background, inner);
        graphics.PixelOffsetMode = PixelOffsetMode.Default;
    }

    public static Button Button(string text, EventHandler? click, int width = 0, int height = 30)
    {
        var button = new ShadeButton
        {
            Text = text, AutoSize = width == 0, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Size = new Size(width == 0 ? 72 : width, height), MinimumSize = new Size(width, height),
            Margin = new Padding(0, 0, 6, 0), TabStop = true
        };
        button.Click += click;
        return button;
    }

    internal static void ButtonContent(Graphics graphics, string text, Font font, Rectangle bounds, Color color, float scale)
    {
        if (text is "+" or "−" or "×")
        {
            float x = bounds.Left + (bounds.Width - 1) / 2f, y = bounds.Top + (bounds.Height - 1) / 2f;
            float radius = 4 * scale;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(color, 1.5f * scale);
            if (text == "×")
            {
                graphics.DrawLine(pen, x - radius, y - radius, x + radius, y + radius);
                graphics.DrawLine(pen, x - radius, y + radius, x + radius, y - radius);
            }
            else
            {
                graphics.DrawLine(pen, x - radius, y, x + radius, y);
                if (text == "+") graphics.DrawLine(pen, x, y - radius, x, y + radius);
            }
            return;
        }

        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        if (text.EndsWith(" ▾", StringComparison.Ordinal))
        {
            text = text[..^2];
            int textWidth = TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int arrowSpace = (int)Math.Round(16 * scale);
            int width = Math.Min(bounds.Width, textWidth + arrowSpace);
            var label = new Rectangle(bounds.Left + (bounds.Width - width) / 2, bounds.Top, width - arrowSpace, bounds.Height);
            TextRenderer.DrawText(graphics, text, font, label, color, flags);
            float x = label.Right + arrowSpace - 3 * scale - 1, y = bounds.Top + (bounds.Height - 1) / 2f;
            using var pen = new Pen(color, 1.5f * scale);
            graphics.DrawLines(pen, [new PointF(x - 3 * scale, y - 1.5f * scale), new PointF(x, y + 1.5f * scale), new PointF(x + 3 * scale, y - 1.5f * scale)]);
        }
        else TextRenderer.DrawText(graphics, text, font, bounds, color, flags);
    }

    public static void SetActive(Button button, bool active)
    {
        if (button is ShadeButton shade) shade.Active = active;
    }

    public static void StyleMenu(ToolStrip menu)
    {
        menu.BackColor = Panel; menu.ForeColor = Text;
        menu.Font = new Font("Segoe UI", 9.5f);
        menu.Renderer = new ShadeMenuRenderer();
        if (menu is ToolStripDropDown dropDown)
            dropDown.HandleCreated += (_, _) => NativeMethods.ExcludeFromCapture(dropDown.Handle);
    }

    public static void WindowChrome(nint window)
    {
        int dark = 1, caption = ColorTranslator.ToWin32(Background), text = ColorTranslator.ToWin32(Text), border = ColorTranslator.ToWin32(Border);
        NativeMethods.DwmSetWindowAttribute(window, 20, ref dark, 4);
        NativeMethods.DwmSetWindowAttribute(window, 35, ref caption, 4);
        NativeMethods.DwmSetWindowAttribute(window, 36, ref text, 4);
        NativeMethods.DwmSetWindowAttribute(window, 34, ref border, 4);
    }
}

internal class ShadeButton : Button
{
    private bool _hover, _pressed, _active, _primary, _accentPink;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Ghost { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active { get => _active; set { if (_active != value) { _active = value; Invalidate(); } } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Primary { get => _primary; set { _primary = value; Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AccentPink { get => _accentPink; set { _accentPink = value; Invalidate(); } }

    public ShadeButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        BackColor = Theme.Panel; ForeColor = Theme.Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        float scale = DeviceDpi / 96f;
        Color accent = AccentPink ? Theme.Pink : Theme.Sky;
        Color fill = !Enabled ? Theme.Panel : Primary ? accent : Active ? Theme.AccentFill : _hover || _pressed ? Color.FromArgb(51, 51, 58) : Ghost ? Parent?.BackColor ?? Theme.Background : Theme.Fill;
        Color edge = Focused && ShowFocusCues && TabStop || Primary ? accent : Ghost ? fill : _hover ? Theme.Outline : Theme.Border;
        Theme.Surface(e.Graphics, ClientSize, fill, edge, scale);
        Theme.ButtonContent(e.Graphics, Text, Font, ClientRectangle, !Enabled ? Theme.Muted : Active ? accent : Theme.Text, scale);
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Math.Max(MinimumSize.Width, TextRenderer.MeasureText(Text, Font).Width + (int)Math.Round(24 * DeviceDpi / 96d)), Math.Max(MinimumSize.Height, (int)Math.Round(28 * DeviceDpi / 96d)));
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); _hover = _pressed = false; Invalidate(); }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); _pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { _pressed = true; Invalidate(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); _pressed = false; Invalidate(); }
}

internal sealed class ShadeMenuRenderer : ToolStripProfessionalRenderer
{
    public ShadeMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted; base.OnRenderItemText(e); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = e.Item is { Enabled: true, Selected: true } ? Theme.Sky : Theme.Muted; base.OnRenderArrow(e); }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        using var brush = new SolidBrush(Theme.Fill);
        e.Graphics.FillRectangle(brush, new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2));
    }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Sky, 2);
        var r = e.ImageRectangle;
        e.Graphics.DrawLines(pen, [new(r.Left + 3, r.Top + r.Height / 2), new(r.Left + 6, r.Bottom - 4), new(r.Right - 2, r.Top + 3)]);
    }
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Panel;
        public override Color ImageMarginGradientBegin => Theme.Panel;
        public override Color ImageMarginGradientMiddle => Theme.Panel;
        public override Color ImageMarginGradientEnd => Theme.Panel;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.Sky;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Panel;
    }
}

internal class OverlayForm : Form
{
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate; return parameters; }
    }
    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
        Theme.Apply(this);
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); NativeMethods.ExcludeFromCapture(Handle); }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmMouseActivate) { message.Result = 3; return; }
        base.WndProc(ref message);
    }
}
