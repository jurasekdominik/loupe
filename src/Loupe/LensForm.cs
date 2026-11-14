using SharpGen.Runtime;

namespace Loupe;

internal sealed class LensForm : OverlayForm
{
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly ControlStrip _strip = new();
    private DesktopCapture? _capture;
    private LensRenderer? _renderer;
    private Screen _screen = null!;
    private Point _center;
    private Point? _hover;
    private Point? _rulerStart;
    private Point? _panStart;
    private Point _panCenter;
    private PixelMeasurement? _measurement;
    private readonly List<PixelGuide> _guides = [];
    private bool _placingStrip;
    private Point _stripLocation;
    private int _recoveryAttempts;
    private DateTime _retryAfter;
    private bool _dirty = true;
    public bool IsOpen { get; private set; }
    internal bool HasPresentedFrame => _renderer?.HasPresentedFrame == true;

    public bool Pinned { get; private set; }
    public bool Measuring { get; private set; }
    public bool ContainsPointer
    {
        get
        {
            Point point = NativeMethods.CursorPosition;
            return Bounds.Contains(point) && (Region is null || Region.IsVisible(PointToClient(point)));
        }
    }
    public InspectionViewport Viewport => InspectionViewport.Create(_screen.Bounds, _center, ClientSize, _settings.Zoom);
    public event Action? Latched, SettingsChanged;
    public event Action<Exception>? CaptureFailed;
    public event Action<AppCommand>? CommandRequested;

    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ExStyle |= NativeMethods.WsExLayered | NativeMethods.WsExTransparent; return parameters; }
    }

    public LensForm(AppSettings settings)
    {
        _settings = settings;
        AutoScaleMode = AutoScaleMode.None; // Image sizing is explicitly in physical pixels.
        Text = "Loupe lens";
        StartPosition = FormStartPosition.Manual;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
        _timer.Tick += (_, _) => TickFrame();
        _strip.CommandRequested += command => CommandRequested?.Invoke(command);
        _strip.LocationChanged += (_, _) =>
        {
            if (_placingStrip || !Visible) return;
            Point current = _strip.Location;
            Location = new Point(Left + current.X - _stripLocation.X, Top + current.Y - _stripLocation.Y);
            _stripLocation = current;
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!NativeMethods.SetLayeredWindowAttributes(Handle, 0, 255, 2))
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }

    public void Open(Screen screen, Point center, bool pinned = false)
    {
        if (IsOpen) CloseInspection();
        // Guides are screen coordinates, so they stay valid until cleared or the monitor changes.
        if (screen.DeviceName != _screen?.DeviceName) _guides.Clear();
        _screen = screen;
        _center = screen.Bounds.Contains(center) ? center : new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
        Pinned = pinned;
        Measuring = false;
        _hover = null;
        _measurement = null;
        _strip.RefreshShortcuts(_settings);
        _recoveryAttempts = 0;
        _retryAfter = DateTime.MinValue;
        // Moving the HWND onto its selected monitor establishes the appropriate per-monitor DPI.
        Location = new Point(screen.WorkingArea.Left + 20, screen.WorkingArea.Top + 20);
        _ = Handle;
        ApplyAppearance();
        PlaceNear(_center);
        NativeMethods.SetClickThrough(Handle, true);
        IsOpen = true;
        // WM_TIMER fires on ~15.6 ms steps; asking for slightly less than the frame time lands on 32 / 64 ticks per second.
        _timer.Interval = Math.Max(1, 1000 / _settings.FramesPerSecond - 2);
        _timer.Start();
        // Initialize capture on the next timer tick. A quick second toggle
        // can cancel opening without starting and tearing down a GPU session.
    }

    private void StartGraphics()
    {
        _capture = new DesktopCapture(_screen.DeviceName, _settings.FramesPerSecond);
        try { _renderer = new LensRenderer(Handle, ClientSize, _capture); }
        catch { _capture.Dispose(); _capture = null; throw; }
        _dirty = true;
    }

    private void StopGraphics()
    {
        _renderer?.Dispose(); _renderer = null;
        _capture?.Dispose(); _capture = null;
    }

    public void CloseInspection()
    {
        IsOpen = false;
        _timer.Stop();
        Capture = false;
        _rulerStart = null;
        _panStart = null;
        _strip.Hide();
        Hide();
        StopGraphics();
    }

    public void ApplyAppearance()
    {
        int stripHeight = (int)Math.Round(ControlStrip.LogicalHeight * DeviceDpi / 96d);
        int available = Math.Max(96, Math.Min(_screen.WorkingArea.Width, _screen.WorkingArea.Height - stripHeight - 16));
        int diameter = Math.Min((int)Math.Round(_settings.Size * DeviceDpi / 96d), available);
        ClientSize = new Size(diameter, diameter);
        _renderer?.Resize(ClientSize);
        _dirty = true;
    }

    private void PlaceNear(Point point)
    {
        Rectangle work = _screen.WorkingArea;
        // The controls can be wider than the lens; reserve their actual scaled width as well.
        int stripWidth = (int)Math.Round(ControlStrip.LogicalWidth * DeviceDpi / 96d);
        int stripHeight = (int)Math.Round(ControlStrip.LogicalHeight * DeviceDpi / 96d);
        int groupWidth = Math.Max(Width, stripWidth);
        int groupHeight = Height + stripHeight + 8;
        int x = point.X + 48;
        if (x + groupWidth > work.Right) x = point.X - groupWidth - 48;
        int y = point.Y - Height / 2;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - groupWidth));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - groupHeight));
        Location = new Point(x + (groupWidth - Width) / 2, y);
        _placingStrip = true;
        _strip.Location = new Point(x + (groupWidth - stripWidth) / 2, y + Height + 8);
        _ = _strip.Handle;
        _strip.ClientSize = new Size(stripWidth, stripHeight);
        _stripLocation = _strip.Location;
        _placingStrip = false;
    }

    private void TickFrame()
    {
        if (!IsOpen) return;
        try
        {
            if (_renderer is null)
            {
                if (DateTime.UtcNow < _retryAfter) return;
                StartGraphics();
            }
            if (_strip.NoticeExpired()) _dirty = true;
            Point pointer = NativeMethods.CursorPosition;
            if (!Pinned && !_strip.HasOpenMenu && !Bounds.Contains(pointer) && !_strip.Bounds.Contains(pointer) && _screen.Bounds.Contains(pointer))
            {
                if (_center != pointer) _dirty = true;
                _center = pointer;
            }
            var viewport = Viewport;
            Point? crosshair = Measuring ? _hover : _center;
            if (!_renderer!.Render(viewport, _settings, crosshair, _guides, _measurement, Measuring, force: _dirty)) return;
            _dirty = false;
            _recoveryAttempts = 0;
            // Following or pinned: the pixel at the crosshair. Measuring: the boundary under the pointer, or how to start.
            string status = _measurement?.Label ?? (_hover is { } p ? $"X {p.X}   Y {p.Y}"
                : Measuring ? "Drag: ruler · Right-click: guides" : $"X {_center.X}   Y {_center.Y}");
            _strip.UpdateState(_settings, Pinned, Measuring, status);
            if (!Visible)
            {
                // Present into the hidden composition target first. Neither HWND
                // becomes visible until a real captured frame is ready.
                _renderer.CompleteFirstPresentation();
                Show();
                NativeMethods.ShowWindow(Handle, 4);
                NativeMethods.SetWindowPos(Handle, -1, 0, 0, 0, 0, 0x53);
                _strip.Show(this);
                NativeMethods.ShowWindow(_strip.Handle, 4);
                NativeMethods.SetWindowPos(_strip.Handle, -1, 0, 0, 0, 0, 0x53);
            }
        }
        catch (SharpGenException exception) when (_recoveryAttempts++ < 3)
        {
            AppLog.Write($"Recreating capture after a display/device change: {exception.Message}");
            StopGraphics();
            _retryAfter = DateTime.UtcNow.AddSeconds(1);
        }
        catch (Exception exception)
        {
            CloseInspection();
            CaptureFailed?.Invoke(exception);
        }
    }

    public void TogglePin()
    {
        Pinned = !Pinned;
        if (!Pinned && Measuring) SetMeasuring(false);
        Latched?.Invoke();
        _dirty = true;
    }

    public void ToggleMeasure()
    {
        SetMeasuring(!Measuring);
        Latched?.Invoke();
    }

    private void SetMeasuring(bool enabled)
    {
        Measuring = enabled;
        if (enabled) Pinned = true;
        else { Capture = false; _rulerStart = null; _panStart = null; _hover = null; }
        NativeMethods.SetClickThrough(Handle, !enabled);
        Cursor = enabled ? Cursors.Cross : Cursors.Default;
        _dirty = true;
    }

    public void ToggleGrid() { _settings.ShowGrid = !_settings.ShowGrid; _dirty = true; SettingsChanged?.Invoke(); }

    public void ChangeZoom(int direction)
    {
        int index = Array.IndexOf(AppSettings.ZoomLevels, _settings.Zoom);
        _settings.Zoom = AppSettings.ZoomLevels[Math.Clamp(index + Math.Sign(direction), 0, AppSettings.ZoomLevels.Length - 1)];
        if (_hover is not null) _hover = Viewport.NearestBoundary(PointToClient(NativeMethods.CursorPosition));
        _dirty = true;
        SettingsChanged?.Invoke();
    }

    public void ClearGuides() { _guides.Clear(); _measurement = null; _rulerStart = null; _dirty = true; }

    public void PlaceRulerPoint()
    {
        if (!Measuring || _hover is not { } point) return;
        if (_rulerStart is { } start) { _measurement = new PixelMeasurement(start, point); _rulerStart = null; }
        else { _rulerStart = point; _measurement = new PixelMeasurement(point, point); }
        _dirty = true;
    }

    public void Pan(int x, int y)
    {
        Pinned = true; _center = new Point(Math.Clamp(_center.X + x, _screen.Bounds.Left, _screen.Bounds.Right - 1), Math.Clamp(_center.Y + y, _screen.Bounds.Top, _screen.Bounds.Bottom - 1));
        if (_hover is not null) _hover = Viewport.NearestBoundary(PointToClient(NativeMethods.CursorPosition));
        _dirty = true; Latched?.Invoke();
    }

    public void ChangeSize(int direction)
    {
        int index = Array.IndexOf(AppSettings.Sizes, _settings.Size);
        _settings.Size = AppSettings.Sizes[Math.Clamp(index + Math.Sign(direction), 0, AppSettings.Sizes.Length - 1)];
        ApplyAppearance(); PlaceNear(_center); SettingsChanged?.Invoke();
    }

    public CaptureResult SaveCapture(bool magnified, string? folder = null, bool? copy = null)
    {
        if (_capture is null || _renderer is null) throw new InvalidOperationException("Open the loupe before capturing an area.");
        using var bitmap = magnified
            ? _renderer.CaptureView(Viewport, _settings, Measuring ? _hover : null, _guides, _measurement, Measuring)
            : _capture.CaptureArea(Viewport.Source);
        var result = Screenshots.Save(bitmap, folder ?? _settings.ScreenshotFolder, magnified ? "magnified" : "original", copy ?? _settings.CopyCapturesToClipboard);
        _strip.ShowNotice($"Saved {result.Size.Width} × {result.Size.Height} PNG{(result.CopiedToClipboard ? " · copied" : "")}");
        _dirty = true; // The toolbar text refreshes with the next drawn frame.
        return result;
    }

    public void AddGuide(bool vertical)
    {
        if (!Measuring || _hover is not { } point) return;
        var guide = new PixelGuide(vertical, vertical ? point.X : point.Y);
        if (!_guides.Remove(guide))
        {
            if (_guides.Count >= 64) _guides.RemoveAt(0);
            _guides.Add(guide);
        }
        _dirty = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Measuring) return;
        if (_panStart is { } start)
        {
            _center = new Point(_panCenter.X - (int)Math.Round((e.X - start.X) / (double)_settings.Zoom), _panCenter.Y - (int)Math.Round((e.Y - start.Y) / (double)_settings.Zoom));
            _center = new Point(Math.Clamp(_center.X, _screen.Bounds.Left, _screen.Bounds.Right - 1), Math.Clamp(_center.Y, _screen.Bounds.Top, _screen.Bounds.Bottom - 1));
        }
        _hover = Viewport.NearestBoundary(e.Location);
        if (_rulerStart is { } anchor) _measurement = new PixelMeasurement(anchor, _hover.Value);
        _dirty = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Measuring || !Viewport.Destination.Contains(e.Location)) return;
        _hover = Viewport.NearestBoundary(e.Location);
        if (e.Button == MouseButtons.Left)
        {
            _rulerStart = _hover;
            _measurement = new PixelMeasurement(_hover.Value, _hover.Value);
            Capture = true;
        }
        else if (e.Button == MouseButtons.Right) { AddGuide(true); AddGuide(false); }
        else if (e.Button == MouseButtons.Middle) { _panStart = e.Location; _panCenter = _center; Capture = true; }
        _dirty = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) _rulerStart = null;
        if (e.Button == MouseButtons.Middle) _panStart = null;
        if (_rulerStart is null && _panStart is null) Capture = false;
        _dirty = true;
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (!Capture) { _hover = null; _dirty = true; } }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if (Measuring) ChangeZoom(e.Delta); }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (IsOpen && _screen is not null) { ApplyAppearance(); PlaceNear(_center); }
    }

    internal void SaveDiagnosticFrame(string path)
    {
        _renderer!.Render(Viewport, _settings, null, _guides, _measurement, Measuring, path);
    }

    internal PixelMeasurement? CurrentMeasurement => _measurement;

    internal void SaveControlsDiagnostic(string path)
    {
        using var bitmap = new Bitmap(_strip.Width, _strip.Height);
        _strip.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Stop(); _timer.Dispose(); StopGraphics(); _strip.Dispose(); }
        base.Dispose(disposing);
    }
}
