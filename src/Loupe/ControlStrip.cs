namespace Loupe;

internal sealed class ControlStrip : OverlayForm
{
    public const int LogicalWidth = 600, LogicalHeight = 140;
    private readonly Button _pin, _measure, _grid;
    private readonly Label _zoom, _status, _hint, _live;
    private readonly ToolTip _toolTip = new();
    private readonly ContextMenuStrip _captureMenu = new();
    private readonly Dictionary<AppCommand, Button> _buttons = [];
    private (bool Pinned, bool Measuring, bool Grid, int Zoom)? _lastState;
    private string _lastStatus = "", _notice = "";
    private DateTime _noticeUntil;
    public bool HasOpenMenu => _captureMenu.Visible;
    public event Action<AppCommand>? CommandRequested;

    public ControlStrip()
    {
        Text = "Loupe controls"; ClientSize = new Size(LogicalWidth, LogicalHeight); Padding = new Padding(12, 9, 12, 9);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _live = new Label { Text = "●  LOUPE  /  LIVE", ForeColor = Theme.Sky, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.SizeAll };
        _live.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { NativeMethods.ReleaseCapture(); NativeMethods.SendMessageW(Handle, 0xA1, 2, 0); } };
        layout.Controls.Add(_live, 0, 0);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _pin = ActionButton("Pin", AppCommand.TogglePin, 58);
        _measure = ActionButton("Measure", AppCommand.ToggleMeasure, 74);
        _grid = ActionButton("Grid", AppCommand.ToggleGrid, 46);
        var minus = ActionButton("−", AppCommand.ZoomOut, 28);
        _zoom = new Label { Text = "8×", Width = 36, Height = 30, TextAlign = ContentAlignment.MiddleCenter, Margin = Padding.Empty };
        var plus = ActionButton("+", AppCommand.ZoomIn, 28);
        var clear = ActionButton("Clear", AppCommand.Clear, 50);
        var capture = Theme.Button("Capture ▾", (sender, _) => { if (sender is Control button) _captureMenu.Show(button, new Point(0, button.Height)); }, 90);
        capture.TabStop = false;
        var close = ActionButton("×", AppCommand.Hide, 28);
        ((ShadeButton)close).Ghost = true;
        controls.Controls.AddRange([_pin, _measure, _grid, minus, _zoom, plus, clear, capture, close]);
        Theme.StyleMenu(_captureMenu);
        foreach (var command in new[] { AppCommand.CaptureArea, AppCommand.CaptureView })
        {
            var item = new ToolStripMenuItem(CommandCatalog.Get(command).Label) { Tag = command };
            item.Click += (_, _) => CommandRequested?.Invoke(command); _captureMenu.Items.Add(item);
        }
        layout.Controls.Add(controls, 0, 1);
        _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text, Margin = Padding.Empty, Font = new Font("Consolas", 9) };
        _hint = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Margin = Padding.Empty, Font = new Font("Segoe UI", 8.5f) };
        layout.Controls.Add(_status, 0, 2); layout.Controls.Add(_hint, 0, 3); Controls.Add(layout);
    }

    private Button ActionButton(string label, AppCommand command, int width)
    {
        var button = Theme.Button(label, (_, _) => CommandRequested?.Invoke(command), width);
        button.TabStop = false; _buttons.Add(command, button); return button;
    }

    public void RefreshShortcuts(AppSettings settings)
    {
        _lastState = null;
        foreach (var (command, button) in _buttons)
        {
            var binding = settings.Binding(command);
            _toolTip.SetToolTip(button, CommandCatalog.Get(command).Label + (binding.IsEmpty ? "" : $" · {binding}"));
        }
        foreach (ToolStripMenuItem item in _captureMenu.Items)
        {
            var binding = settings.Binding((AppCommand)item.Tag!);
            item.ShortcutKeyDisplayString = binding.IsEmpty ? "" : binding.ToString();
        }
    }

    public void ShowNotice(string text)
    {
        _notice = text; _noticeUntil = DateTime.UtcNow.AddSeconds(4);
    }

    public void UpdateState(AppSettings settings, bool pinned, bool measuring, string status)
    {
        var state = (pinned, measuring, settings.ShowGrid, settings.Zoom);
        if (_lastState != state)
        {
            _pin.Text = pinned ? "Pinned" : "Pin"; _measure.Text = measuring ? "Inspect" : "Measure"; _zoom.Text = $"{settings.Zoom}×";
            Theme.SetActive(_pin, pinned); Theme.SetActive(_measure, measuring); Theme.SetActive(_grid, settings.ShowGrid);
            _live.Text = pinned ? "●  LOUPE  /  LIVE · PINNED" : "●  LOUPE  /  LIVE · FOLLOWING CURSOR";
            _live.ForeColor = measuring ? Theme.Pink : Theme.Sky;
            _hint.Text = measuring
                ? "Drag: ruler · Right-click: guides · Middle-drag: pan"
                : "Clicks pass through · Measure to place guides · Drag the header to move";
            _lastState = state;
            _toolTip.SetToolTip(_hint, _hint.Text);
        }
        if (DateTime.UtcNow < _noticeUntil) status = _notice;
        if (status != _lastStatus) { _status.Text = status; _lastStatus = status; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); using var pen = new Pen(Theme.Border); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
    protected override void Dispose(bool disposing) { if (disposing) { _toolTip.Dispose(); _captureMenu.Dispose(); } base.Dispose(disposing); }
}
