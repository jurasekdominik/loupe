using System.Drawing.Drawing2D;

namespace Loupe;

internal sealed class ShadeDropdown : Control
{
    private readonly string[] _values;
    private readonly ToolStripDropDown _popup = new() { AutoSize = false, DropShadowEnabled = false };
    private readonly ChoiceList _list;
    private bool _hover;
    private int _selectedIndex;
    public event Action? SelectionChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set { int index = Math.Clamp(value, 0, _values.Length - 1); if (_selectedIndex == index) return; _selectedIndex = index; Invalidate(); SelectionChanged?.Invoke(); }
    }
    public string SelectedText => _values[_selectedIndex];
    public bool Expanded => _popup.Visible;

    public ShadeDropdown(string[] values, int selected = 0)
    {
        if (values.Length == 0) throw new ArgumentException("A dropdown needs at least one choice.", nameof(values));
        _values = values; _selectedIndex = Math.Clamp(selected, 0, values.Length - 1);
        TabStop = true; Cursor = Cursors.Hand; Height = 32; Dock = DockStyle.Fill;
        AccessibleRole = AccessibleRole.ComboBox;
        BackColor = Theme.Panel; ForeColor = Theme.Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        _list = new ChoiceList(values, index => { SelectedIndex = index; _popup.Close(); Focus(); });
        var host = new ToolStripControlHost(_list) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
        _popup.Padding = Padding.Empty; _popup.Margin = Padding.Empty;
        _popup.Items.Add(host); Theme.StyleMenu(_popup);
        // The hosted list owns the whole border. ToolStrip's border otherwise
        // adds another outline and shifts the attached edges by a pixel.
        _popup.Renderer = new PopupRenderer();
        _popup.Closing += (_, e) =>
        {
            // The owner handles its own press. ToolStrip must not close first
            // and turn the same press into an unintended reopen.
            if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && RectangleToScreen(ClientRectangle).Contains(NativeMethods.CursorPosition)) e.Cancel = true;
        };
        _popup.Closed += (_, _) => Invalidate();
    }

    public void OpenChoices()
    {
        if (!Enabled) return;
        Focus();
        if (_popup.Visible) { _popup.Close(); return; }
        _list.SetSelection(SelectedIndex);
        _list.Font = Font;
        var size = new Size(Width, Math.Min(_values.Length, 9) * (int)Math.Round(30 * DeviceDpi / 96d) + (int)Math.Round(8 * DeviceDpi / 96d));
        _popup.ClientSize = size;
        ((ToolStripControlHost)_popup.Items[0]).Size = size;
        _list.Size = size;
        _popup.Show(this, new Point(0, Height));
        // Reapply physical bounds after ToolStrip's first DPI/layout pass.
        _popup.ClientSize = size;
        ((ToolStripControlHost)_popup.Items[0]).Size = size;
        _list.Bounds = new Rectangle(Point.Empty, size);
        _list.Focus(); Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) OpenChoices(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        float scale = DeviceDpi / 96f;
        Color edge = Expanded || _hover ? Theme.Outline : Focused && ShowFocusCues ? Theme.Sky : Theme.Border;
        Theme.Surface(e.Graphics, ClientSize, Theme.Panel, edge, scale, Expanded && _popup.Top >= PointToScreen(Point.Empty).Y + Height);
        var textBounds = new Rectangle((int)(11 * scale), 0, Width - (int)(42 * scale), Height);
        TextRenderer.DrawText(e.Graphics, SelectedText, Font, textBounds, Enabled ? Theme.Text : Theme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        using var arrow = new Pen(Enabled ? Theme.Muted : Theme.Border, 1.4f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float x = Width - 17 * scale, y = Height / 2f;
        float direction = Expanded ? -1 : 1;
        e.Graphics.DrawLines(arrow, [new PointF(x - 4 * scale, y - direction * 2 * scale), new PointF(x, y + direction * 2 * scale), new PointF(x + 4 * scale, y - direction * 2 * scale)]);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter || (e.Alt && e.KeyCode == Keys.Down)) { OpenChoices(); e.Handled = true; }
        else if (e.KeyCode == Keys.Down) { SelectedIndex++; e.Handled = true; }
        else if (e.KeyCode == Keys.Up) { SelectedIndex--; e.Handled = true; }
        else if (e.KeyCode == Keys.Home) { SelectedIndex = 0; e.Handled = true; }
        else if (e.KeyCode == Keys.End) { SelectedIndex = _values.Length - 1; e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) _popup.Close(); Invalidate(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) _popup.Close(); }
    protected override void Dispose(bool disposing) { if (disposing) _popup.Dispose(); base.Dispose(disposing); }

    internal Rectangle PopupBounds => _popup.Bounds;
    internal Rectangle ChoiceBounds => _list.RectangleToScreen(_list.ClientRectangle);
    internal nint PopupHandle => _popup.Handle;
    internal void CloseChoices() => _popup.Close();

    internal void SaveExpandedPreview(string path)
    {
        using var root = new Bitmap(Width, Height);
        DrawToBitmap(root, new Rectangle(Point.Empty, root.Size));
        using var choices = new Bitmap(_list.Width, _list.Height);
        _list.DrawToBitmap(choices, new Rectangle(Point.Empty, choices.Size));
        using var combined = new Bitmap(Width, Height + choices.Height);
        using (var graphics = Graphics.FromImage(combined)) { graphics.DrawImageUnscaled(root, 0, 0); graphics.DrawImageUnscaled(choices, 0, Height); }
        combined.Save(path);
        _popup.Close();
    }

    private sealed class ChoiceList : Control
    {
        private readonly string[] _values;
        private readonly Action<int> _choose;
        private int _highlight, _selected, _offset;
        private int RowHeight => (int)Math.Round(30 * DeviceDpi / 96d);
        private int Inset => (int)Math.Round(4 * DeviceDpi / 96d);
        private int VisibleRows => Math.Min(_values.Length, 9);
        public ChoiceList(string[] values, Action<int> choose)
        {
            _values = values; _choose = choose; TabStop = true; BackColor = Theme.Panel;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        }
        public void SetSelection(int index) { _highlight = _selected = index; EnsureVisible(); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Panel);
            int edgeWidth = Math.Max(1, (int)Math.Round(DeviceDpi / 96d));
            using var edge = new SolidBrush(Theme.Outline);
            e.Graphics.FillRectangle(edge, 0, 0, edgeWidth, Height);
            e.Graphics.FillRectangle(edge, Width - edgeWidth, 0, edgeWidth, Height);
            e.Graphics.FillRectangle(edge, 0, Height - edgeWidth, Width, edgeWidth);
            using var separator = new Pen(Theme.Border);
            e.Graphics.DrawLine(separator, edgeWidth, 0, Width - edgeWidth - 1, 0);
            for (int row = 0; row < VisibleRows && _offset + row < _values.Length; row++)
            {
                int index = _offset + row;
                var bounds = new Rectangle(edgeWidth, Inset + row * RowHeight, Width - 2 * edgeWidth, RowHeight);
                if (index == _highlight) { using var fill = new SolidBrush(Theme.Fill); e.Graphics.FillRectangle(fill, bounds); }
                if (index == _selected) { using var fill = new SolidBrush(Theme.Sky); e.Graphics.FillRectangle(fill, edgeWidth + 1, bounds.Y + Inset, edgeWidth, bounds.Height - 2 * Inset); }
                bounds.X += 3 * Inset; bounds.Width -= 6 * Inset;
                TextRenderer.DrawText(e.Graphics, _values[index], Font, bounds, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int index = _offset + (e.Y - Inset) / RowHeight; if (e.Y >= Inset && index >= 0 && index < _values.Length) { _highlight = index; Invalidate(); } }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); int row = (e.Y - Inset) / RowHeight; if (e.Button == MouseButtons.Left && e.Y >= Inset && row < VisibleRows && row + _offset < _values.Length) _choose(row + _offset); }
        protected override bool IsInputKey(Keys keyData) => true;
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) _highlight = Math.Max(0, _highlight - 1);
            if (e.KeyCode == Keys.Down) _highlight = Math.Min(_values.Length - 1, _highlight + 1);
            if (e.KeyCode == Keys.Home) _highlight = 0;
            if (e.KeyCode == Keys.End) _highlight = _values.Length - 1;
            if (e.KeyCode is Keys.Enter or Keys.Space) _choose(_highlight);
            if (e.KeyCode is Keys.Escape or Keys.Tab) { (Parent as ToolStripDropDown)?.Close(); e.SuppressKeyPress = true; }
            EnsureVisible(); Invalidate(); e.Handled = true; base.OnKeyDown(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _offset = Math.Clamp(_offset - Math.Sign(e.Delta), 0, Math.Max(0, _values.Length - VisibleRows)); Invalidate(); }
        private void EnsureVisible() { if (_highlight < _offset) _offset = _highlight; if (_highlight >= _offset + VisibleRows) _offset = _highlight - VisibleRows + 1; }
    }

    private sealed class PopupRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Theme.Panel);
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }
}
