using System.Drawing.Drawing2D;

namespace Loupe;

internal sealed class ShadeToggle : CheckBox
{
    public ShadeToggle(string text, bool value)
    {
        Text = text; Checked = value; AutoSize = false; Height = 34; Dock = DockStyle.Fill;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        float s = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(s, Height / 2f - 9 * s, 32 * s, 18 * s);
        using var path = Theme.RoundRect(track, 9 * s);
        using var brush = new SolidBrush(Checked ? Theme.Sky : Theme.Border);
        e.Graphics.FillPath(brush, path);
        if (Focused) { using var focus = new Pen(Theme.Pink, s); e.Graphics.DrawPath(focus, path); }
        using var thumb = new SolidBrush(Theme.Text);
        e.Graphics.FillEllipse(thumb, track.X + (Checked ? 17 : 3) * s, track.Y + 3 * s, 12 * s, 12 * s);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(44 * s), 0, Width - (int)(44 * s), Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

internal sealed class ShadeSlider : Control
{
    private int _value;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value { get => _value; set { _value = Math.Clamp(value, 5, 70); Invalidate(); } }
    public ShadeSlider(int value)
    {
        Value = value; Height = 34; Dock = DockStyle.Fill; TabStop = true; Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Slider; AccessibleName = "Grid strength";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        float s = DeviceDpi / 96f;
        float left = 8 * s, right = Width - 52 * s, y = Height / 2f;
        float x = left + (right - left) * (Value - 5) / 65f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = new Pen(Theme.Border, 3 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var progress = new Pen(Theme.Sky, 3 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLine(track, left, y, right, y); e.Graphics.DrawLine(progress, left, y, x, y);
        using var thumb = new SolidBrush(Theme.Text);
        e.Graphics.FillEllipse(thumb, x - 5 * s, y - 5 * s, 10 * s, 10 * s);
        if (Focused) { using var focus = new Pen(Theme.Pink, s); e.Graphics.DrawEllipse(focus, x - 7 * s, y - 7 * s, 14 * s, 14 * s); }
        TextRenderer.DrawText(e.Graphics, $"{Value}%", Font, new Rectangle((int)(right + 9 * s), 0, (int)(40 * s), Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
    private void SetFromMouse(int x) { float s = DeviceDpi / 96f; Value = 5 + (int)Math.Round((x - 8 * s) / Math.Max(1, Width - 60 * s) * 65); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Capture = true; SetFromMouse(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) SetFromMouse(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Left) Value--; if (e.KeyCode == Keys.Right) Value++; if (e.KeyCode == Keys.Home) Value = 5; if (e.KeyCode == Keys.End) Value = 70; e.Handled = true; base.OnKeyDown(e); }
}
