namespace Loupe;

internal sealed class ControlStrip : OverlayForm
{
    public const int LogicalWidth = 566, LogicalHeight = 84;
    private readonly Button _pin, _freeze, _measure, _grid;
    private readonly Label _zoom, _status, _live;
    private readonly ToolTip _toolTip = new();
    private readonly ContextMenuStrip _captureMenu = new();
    private readonly Dictionary<AppCommand, Button> _buttons = [];
    private (bool Pinned, bool Measuring, bool Frozen, bool Grid, int Zoom)? _lastState;
    private string _lastStatus = "", _notice = "";
    private DateTime _noticeUntil;
    public bool HasOpenMenu => _captureMenu.Visible;
    public event Action<AppCommand>? CommandRequested;

    public ControlStrip()
    {
        Text = "Loupe controls"; ClientSize = new Size(LogicalWidth, LogicalHeight); Padding = new Padding(12, 9, 12, 9);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // One header row: the mode on the left, the position or measurement on the right. Both drag the lens.
        _live = new Label { Text = "●  LIVE", ForeColor = Theme.Sky, Dock = DockStyle.Left, Width = 170, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.SizeAll };
        _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Sky, Font = new Font("Consolas", 10, FontStyle.Bold), Cursor = Cursors.SizeAll };
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        header.Controls.Add(_status); header.Controls.Add(_live); // The filling label goes first so it is laid out last.
        foreach (var label in new[] { _live, _status })
        {
            label.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { NativeMethods.ReleaseCapture(); NativeMethods.SendMessageW(Handle, 0xA1, 2, 0); } };
            _toolTip.SetToolTip(label, "Drag to move the lens · Clicks pass through unless measuring");
        }
        layout.Controls.Add(header, 0, 0);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        _pin = ActionButton("Pin", AppCommand.TogglePin, 44);
        _freeze = ActionButton("Freeze", AppCommand.ToggleFreeze, 60);
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
        controls.Controls.AddRange([_pin, _freeze, _measure, _grid, minus, _zoom, plus, clear, capture, close]);
        Theme.StyleMenu(_captureMenu);
        foreach (var command in new[] { AppCommand.CaptureArea, AppCommand.CaptureView })
        {
            var item = new ToolStripMenuItem(CommandCatalog.Get(command).Label) { Tag = command };
            item.Click += (_, _) => CommandRequested?.Invoke(command); _captureMenu.Items.Add(item);
        }
        layout.Controls.Add(controls, 0, 1);
        Controls.Add(layout);
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

    // True once, when a shown notice has run out and the status needs a redraw.
    public bool NoticeExpired()
    {
        if (_notice.Length == 0 || DateTime.UtcNow < _noticeUntil) return false;
        _notice = ""; return true;
    }

    public void UpdateState(AppSettings settings, bool pinned, bool measuring, bool frozen, string status)
    {
        var state = (pinned, measuring, frozen, settings.ShowGrid, settings.Zoom);
        if (_lastState != state)
        {
            _zoom.Text = $"{settings.Zoom}×"; // Pin and Measure keep their names; the highlight shows the state.
            Theme.SetActive(_pin, pinned); Theme.SetActive(_freeze, frozen); Theme.SetActive(_measure, measuring); Theme.SetActive(_grid, settings.ShowGrid);
            _live.Text = frozen ? "●  FROZEN" : pinned ? "●  LIVE · PINNED" : "●  LIVE · FOLLOWING";
            _live.ForeColor = measuring ? Theme.Pink : Theme.Sky;
            _lastState = state;
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
