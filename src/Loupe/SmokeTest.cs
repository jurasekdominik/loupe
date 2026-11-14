using System.Text.Json;

namespace Loupe;

// Runs locally against a synthetic chart. No network, game process, or input injection.
internal static class SmokeTest
{
    public static void Run(string[] args)
    {
        int outputIndex = Array.IndexOf(args, "--output");
        string output = outputIndex >= 0 && outputIndex + 1 < args.Length ? Path.GetFullPath(args[outputIndex + 1]) : Path.GetFullPath("artifacts/smoke");
        Directory.CreateDirectory(output);
        using var harness = new Harness(output);
        Application.Run(harness);
    }

    private sealed class Harness : Form
    {
        private readonly string _output;
        private readonly Bitmap _pattern = new(256, 256);
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 350 };
        private readonly List<string> _passed = [];
        private LensForm? _lens;
        private SettingsForm? _settingsPreview;
        private AppSettings _settings = new() { ShowGrid = false, Zoom = 8, Size = 448 };
        private int _stage;
        private bool _inverted;
        private Exception? _failure;
        private readonly Color _a = Color.FromArgb(63, 214, 180), _b = Color.FromArgb(208, 155, 72);
        protected override bool ShowWithoutActivation => true;

        public Harness(string output)
        {
            _output = output;
            Text = "Loupe local rendering check";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background;
            ClientSize = new Size(770, 680);
            StartPosition = FormStartPosition.Manual;
            var work = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(work.Left + 30, work.Top + 30);
            UpdatePattern();
            _timer.Tick += Step;
            Shown += (_, _) => _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var title = new Font("Segoe UI", 19, FontStyle.Bold);
            using var small = new Font("Segoe UI", 10);
            using var text = new SolidBrush(Theme.Text);
            using var muted = new SolidBrush(Theme.Muted);
            e.Graphics.DrawString("LOUPE / LOCAL PIXEL CHECK", title, text, 28, 24);
            e.Graphics.DrawString("Each checkerboard cell below is exactly one physical pixel.", small, muted, 28, 65);
            e.Graphics.DrawImageUnscaled(_pattern, 100, 100);
            using var edge = new Pen(Theme.Sky);
            e.Graphics.DrawRectangle(edge, 100, 390, 255, 35);
            e.Graphics.DrawRectangle(edge, 100, 430, 255, 35);
            e.Graphics.DrawString("Local GPU capture → nearest-neighbor enlargement", small, muted, 28, 510);
        }

        private void Step(object? sender, EventArgs args)
        {
            try
            {
                switch (_stage++)
                {
                    case 0:
                        NativeMethods.ShowWindow(Handle, 4);
                        NativeMethods.SetWindowPos(Handle, -1, 0, 0, 0, 0, 0x53);
                        Refresh();
                        _lens = new LensForm(_settings);
                        _lens.CaptureFailed += e => _failure = e;
                        _lens.VisibleChanged += (_, _) =>
                        {
                            if (_lens.Visible && !_lens.HasPresentedFrame)
                                _failure = new InvalidOperationException("The lens became visible before its first captured frame was presented.");
                        };
                        _lens.Open(Screen.FromControl(this), PointToScreen(new Point(228, 228)), pinned: true);
                        _lens.CloseInspection();
                        if (_lens.IsOpen || _lens.Visible) throw new InvalidOperationException("An opening lens could not be canceled.");
                        _passed.Add("Opening capture can be canceled before the first frame");
                        nint foreground = NativeMethods.GetForegroundWindow();
                        _lens.Open(Screen.FromControl(this), PointToScreen(new Point(228, 228)), pinned: true);
                        nint after = NativeMethods.GetForegroundWindow();
                        NativeMethods.GetWindowThreadProcessId(after, out uint foregroundProcess);
                        if (foreground != after && foregroundProcess == Environment.ProcessId)
                            throw new InvalidOperationException("Showing the loupe took keyboard focus.");
                        _passed.Add("Showing the overlay preserves keyboard focus");
                        _lens.Location = new Point(_lens.Viewport.Source.Left - 40, _lens.Viewport.Source.Top - 40);
                        break;
                    case 1:
                        if (!_lens!.Visible || !_lens.HasPresentedFrame) throw new InvalidOperationException("The lens did not reveal its first frame.");
                        _passed.Add("The lens becomes visible only after presenting a captured frame");
                        CheckPixels("square-8x.png");
                        _inverted = true;
                        UpdatePattern();
                        Invalidate(new Rectangle(100, 100, 256, 256));
                        Update();
                        _lens!.ChangeZoom(1);
                        break;
                    case 2:
                        CheckPixels("square-12x.png");
                        _passed.Add("Pinned capture stays live when source pixels change");
                        _lens!.ToggleMeasure();
                        Point start = new(_lens.Viewport.Destination.Left + 5 * _settings.Zoom, _lens.Viewport.Destination.Top + 6 * _settings.Zoom);
                        Point end = new(start.X + 17 * _settings.Zoom, start.Y + 9 * _settings.Zoom);
                        MouseMessage(0x200, start); _lens.AddGuide(true); _lens.AddGuide(false);
                        MouseMessage(0x201, start); MouseMessage(0x200, end, 1); MouseMessage(0x202, end);
                        _settings.ShowGrid = true;
                        break;
                    case 3:
                        if (_lens!.CurrentMeasurement is not { Width: 17, Height: 9 }) throw new InvalidOperationException("Dragging the ruler did not measure 17 × 9 pixels.");
                        _lens!.SaveDiagnosticFrame(Path.Combine(_output, "measurement-square.png"));
                        _lens.SaveControlsDiagnostic(Path.Combine(_output, "controls.png"));
                        _settingsPreview = new SettingsForm(_settings);
                        _ = _settingsPreview.Handle;
                        NativeMethods.SetWindowLongW(_settingsPreview.Handle, NativeMethods.GwlExStyle,
                            NativeMethods.GetWindowLongW(_settingsPreview.Handle, NativeMethods.GwlExStyle) | NativeMethods.WsExNoActivate);
                        _settingsPreview.Show();
                        _settingsPreview.Refresh();
                        _passed.Add("Live measurement rendering with pinned guides and pixel grid");
                        CheckCaptures();
                        _lens.ClearGuides();
                        var rulerViewport = _lens.Viewport;
                        var rulerA = new Point(rulerViewport.Destination.Left + 3 * _settings.Zoom, rulerViewport.Destination.Top + 4 * _settings.Zoom);
                        var rulerB = new Point(rulerA.X + 17 * _settings.Zoom, rulerA.Y + 9 * _settings.Zoom);
                        MouseMessage(0x200, rulerA); _lens.PlaceRulerPoint();
                        MouseMessage(0x200, rulerB); _lens.PlaceRulerPoint();
                        if (_lens.CurrentMeasurement is not { Width: 17, Height: 9 }) throw new InvalidOperationException("Keyboard ruler points did not measure 17 × 9 pixels.");
                        _passed.Add("Two keyboard ruler points measure the same 17 × 9 pixels as dragging");
                        break;
                    case 4:
                        using (var bitmap = new Bitmap(_settingsPreview!.Width, _settingsPreview.Height))
                        {
                            _settingsPreview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(_output, "settings.png"));
                        }
                        var dropdown = FindDropdown(_settingsPreview);
                        dropdown.OpenChoices();
                        dropdown.SaveExpandedPreview(Path.Combine(_output, "dropdown-expanded.png"));
                        _settingsPreview.SelectPage("Shortcuts");
                        break;
                    case 5:
                        SaveSettingsPreview("shortcuts.png");
                        _settingsPreview!.SelectPage("General");
                        break;
                    case 6:
                        SaveSettingsPreview("general.png");
                        _settingsPreview!.Dispose(); _settingsPreview = null;
                        _lens!.CloseInspection();
                        _lens.Open(Screen.FromControl(this), PointToScreen(new Point(228, 228)), pinned: true);
                        break;
                    case 7:
                        _settings.ShowGrid = false;
                        CheckPixels("reopened.png");
                        _passed.Add("Capture resources can be released and reopened");
                        Complete();
                        break;
                }
                if (_failure is not null) throw _failure;
            }
            catch (Exception exception) { _failure = exception; Complete(); }
        }

        private void MouseMessage(uint message, Point point, nint buttons = 0) =>
            NativeMethods.SendMessageW(_lens!.Handle, message, buttons, (nint)((point.Y << 16) | (point.X & 0xffff)));

        private void CheckCaptures()
        {
            var area = _lens!.SaveCapture(false, Path.Combine(_output, "captures"), false);
            using var raw = new Bitmap(area.Path);
            var viewport = _lens.Viewport;
            if (raw.Size != viewport.Source.Size) throw new InvalidOperationException("Original capture was rescaled.");
            var origin = PointToScreen(new Point(100, 100));
            for (int y = 0; y < raw.Height; y++)
                for (int x = 0; x < raw.Width; x++)
                {
                    bool a = (((viewport.Source.X - origin.X + x + viewport.Source.Y - origin.Y + y) & 1) == 0) != _inverted;
                    if (raw.GetPixel(x, y).ToArgb() != (a ? _a : _b).ToArgb()) throw new InvalidOperationException("Original capture did not preserve source pixels.");
                }
            var view = _lens.SaveCapture(true, Path.Combine(_output, "captures"), false);
            if (view.Size != _lens.ClientSize) throw new InvalidOperationException("Magnified capture has the wrong size.");
            _passed.Add("Original PNG capture preserves every source pixel; magnified capture preserves view dimensions");
        }

        private void SaveSettingsPreview(string name)
        {
            using var bitmap = new Bitmap(_settingsPreview!.Width, _settingsPreview.Height);
            _settingsPreview.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(_output, name));
        }

        private static ShadeDropdown FindDropdown(Control control)
        {
            if (control is ShadeDropdown dropdown) return dropdown;
            foreach (Control child in control.Controls)
            {
                try { return FindDropdown(child); } catch (InvalidOperationException) { }
            }
            throw new InvalidOperationException("No dropdown was found.");
        }

        private void CheckPixels(string fileName)
        {
            string path = Path.Combine(_output, fileName);
            _lens!.SaveDiagnosticFrame(path);
            using var rendered = new Bitmap(path);
            var viewport = _lens.Viewport;
            Point origin = PointToScreen(new Point(100, 100));
            int checkedPixels = 0;
            for (int y = 2; y < viewport.Source.Height - 2; y += 3)
            {
                for (int x = 2; x < viewport.Source.Width - 2; x += 3)
                {
                    int sourceX = viewport.Source.Left + x - origin.X, sourceY = viewport.Source.Top + y - origin.Y;
                    Color expected = (((sourceX + sourceY) & 1) == 0) != _inverted ? _a : _b;
                    for (int dy = 0; dy < _settings.Zoom; dy++)
                    {
                        for (int dx = 0; dx < _settings.Zoom; dx++)
                        {
                            int px = viewport.Destination.Left + x * _settings.Zoom + dx;
                            int py = viewport.Destination.Top + y * _settings.Zoom + dy;
                            Color actual = rendered.GetPixel(px, py);
                            if (actual.ToArgb() != expected.ToArgb())
                                throw new InvalidOperationException($"{fileName}: pixel ({px},{py}) was {actual}, expected {expected}. DPI={_lens.DeviceDpi}, source={viewport.Source}");
                            checkedPixels++;
                        }
                    }
                }
            }
            _passed.Add($"{fileName}: {checkedPixels} exact pixels, {_settings.Zoom}× integer scaling, no self-capture");
        }

        private void UpdatePattern()
        {
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++) _pattern.SetPixel(x, y, (((x + y) & 1) == 0) != _inverted ? _a : _b);
        }

        private void Complete()
        {
            _timer.Stop();
            _settingsPreview?.Dispose();
            _settingsPreview = null;
            NativeMethods.GetWindowDisplayAffinity(Handle, out uint affinity);
            string windowInfo = $"visible={NativeMethods.IsWindowVisible(Handle)}, managedVisible={Visible}, affinity={affinity}, topMost={TopMost}, bounds={Bounds}, prev={NativeMethods.GetWindow(Handle, 3)}, lens={_lens?.Handle}";
            _lens?.Dispose();
            _lens = null;
            File.WriteAllText(Path.Combine(_output, "result.json"), JsonSerializer.Serialize(new { success = _failure is null, passed = _passed, error = _failure?.ToString(), dpi = DeviceDpi, windowInfo }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = _failure is null ? 0 : 1;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _lens?.Dispose(); _settingsPreview?.Dispose(); _pattern.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
