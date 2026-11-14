namespace Loupe;

// A small managed scrollbar keeps the settings theme independent of Windows' scrollbar theme.
internal sealed class ShadeScrollPanel : Panel
{
    private readonly Panel _viewport = new();
    private readonly ScrollTrack _track;
    private Control? _content;
    private int _offset;
    private bool _arranging;
    private bool _contentLayoutPending;
    private readonly HashSet<Control> _wheelControls = [];
    private int Maximum => Math.Max(0, (_content?.Height ?? 0) - _viewport.Height);

    public ShadeScrollPanel()
    {
        AutoScroll = false;
        _track = new ScrollTrack(this) { Visible = false };
        Controls.Add(_viewport); Controls.Add(_track);
        AttachWheel(_viewport);
    }

    public void SetContent(Control content)
    {
        _content = content; content.Dock = DockStyle.None;
        _viewport.Controls.Add(content);
        content.SizeChanged += (_, _) => Arrange();
        Arrange();
    }

    private void AttachWheel(Control control)
    {
        if (!_wheelControls.Add(control)) return;
        control.MouseWheel += (_, e) =>
        {
            if (e is HandledMouseEventArgs { Handled: true }) return;
            ScrollTo(_offset - Math.Sign(e.Delta) * (int)(60 * DeviceDpi / 96d));
            if (e is HandledMouseEventArgs handled) handled.Handled = true;
        };
        control.Enter += (_, _) =>
        {
            if (_content is null || !control.IsHandleCreated) return;
            if (control == _content || !control.CanSelect) return;
            int top = _viewport.PointToClient(control.PointToScreen(Point.Empty)).Y;
            if (top < 0) ScrollTo(_offset + top - 4);
            else if (top + control.Height > _viewport.Height) ScrollTo(_offset + top + control.Height - _viewport.Height + 4);
        };
        control.ControlAdded += (_, e) => { if (e.Control is not null) AttachWheel(e.Control); };
        foreach (Control child in control.Controls) AttachWheel(child);
    }

    private void ScrollTo(int value)
    {
        _offset = Math.Clamp(value, 0, Maximum);
        if (_content is not null) _content.Top = -_offset;
        _track.Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Arrange(); }
    private void Arrange()
    {
        if (_arranging || _track is null) return;
        _arranging = true;
        try
        {
            int trackWidth = (int)Math.Round(12 * DeviceDpi / 96d);
            // Reserve the gutter consistently. Toggling it changed the content
            // width during AutoSize layout and could leave controls clipped.
            _viewport.Bounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, ClientSize.Width - Padding.Horizontal - trackWidth), Math.Max(1, ClientSize.Height - Padding.Vertical));
            _track.Bounds = new Rectangle(ClientSize.Width - Padding.Right - trackWidth, Padding.Top, trackWidth, _viewport.Height);
            if (_content is not null)
            {
                var width = new Size(_viewport.Width, 0);
                if (_content.MinimumSize != width || _content.MaximumSize != width)
                {
                    // Visibility changes do not change the available width.
                    // Releasing/reapplying these constraints on every layout
                    // repeatedly measured every shortcut row during tab changes.
                    _content.SuspendLayout();
                    _content.MinimumSize = Size.Empty;
                    _content.MaximumSize = width;
                    _content.MinimumSize = width;
                    _content.Width = width.Width;
                    _content.ResumeLayout(true);
                    _contentLayoutPending = true;
                }
                _offset = Math.Clamp(_offset, 0, Maximum); _content.Location = new Point(0, -_offset);
            }
            _track.Visible = Maximum > 0;
            _track.Invalidate();
        }
        finally { _arranging = false; }
    }

    private sealed class ScrollTrack : Control
    {
        private readonly ShadeScrollPanel owner;
        public ScrollTrack(ShadeScrollPanel owner)
        {
            this.owner = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        private int? _drag;
        private Rectangle Thumb
        {
            get
            {
                int inset = Math.Max(2, (int)Math.Round(3 * DeviceDpi / 96d));
                int height = Math.Min(Height, Math.Max((int)Math.Round(28 * DeviceDpi / 96d), (int)(Height * owner._viewport.Height / (double)Math.Max(1, owner._content?.Height ?? 1))));
                int top = owner.Maximum == 0 ? 0 : (int)((Height - height) * owner._offset / (double)owner.Maximum);
                return new Rectangle(inset, top, Math.Max(2, Width - 2 * inset), Math.Max(1, height));
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Background);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var thumb = Thumb;
            if (thumb.Height <= 0) return;
            using var path = Theme.RoundRect(thumb, 3);
            using var brush = new SolidBrush(Capture ? Theme.Sky : Theme.Outline);
            e.Graphics.FillPath(brush, path);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (Thumb.Contains(e.Location)) { _drag = e.Y - Thumb.Top; Capture = true; }
            else owner.ScrollTo(owner._offset + (e.Y < Thumb.Top ? -owner._viewport.Height : owner._viewport.Height));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag is { } start) owner.ScrollTo((int)((e.Y - start) / (double)Math.Max(1, Height - Thumb.Height) * owner.Maximum));
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = null; Capture = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) _drag = null; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); owner.ScrollTo(owner._offset - Math.Sign(e.Delta) * 60); }
    }

    internal void ScrollToEnd() => ScrollTo(Maximum);
    internal int ScrollOffset => _offset;
    internal void CompleteContentLayout()
    {
        if (!_contentLayoutPending || _content is null) return;
        _contentLayoutPending = false;
        _content.PerformLayout();
    }
}
