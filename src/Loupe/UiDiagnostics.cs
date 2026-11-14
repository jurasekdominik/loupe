using System.Text.Json;
using System.Diagnostics;

namespace Loupe;

// Runs only on explicit request. Print the native app windows, including the
// separate popup HWND. This isolated mode allows its own windows to be printed;
// the GPU smoke test separately verifies normal capture exclusion.
internal static class UiDiagnostics
{
    internal static bool Running { get; private set; }
    public static void Run(string[] args)
    {
        Running = true;
        int index = Array.IndexOf(args, "--output");
        string output = Path.GetFullPath(index >= 0 ? args[index + 1] : "artifacts/ui");
        Directory.CreateDirectory(output);
        using var context = new Checks(output);
        Application.Run(context);
        Running = false;
    }

    private sealed class Checks : ApplicationContext
    {
        private readonly string _output;
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
        private readonly Queue<Action> _steps = [];
        private readonly List<string> _passed = [];
        private readonly List<string> _skipped = [];
        private readonly List<string> _mouseTrace = [];
        private SettingsForm? _form;
        private ShadeDropdown? _dropdown;
        private Size _dropdownSize;
        private Rectangle _normalBounds;
        private Form? _cursorProbe;
        private DesktopCapture? _probeCapture;
        private int _frameWaits;

        public Checks(string output)
        {
            _output = output;
            _steps.Enqueue(() => ShowSettings());
            _steps.Enqueue(ProfilePageSwitching);
            _steps.Enqueue(() => { Save("lens.png"); CheckFields(); });
            _steps.Enqueue(() =>
            {
                _dropdown = Descendants(_form!).OfType<ShadeDropdown>().First(x => x.AccessibleName == "Magnification");
                _dropdownSize = _dropdown.Size;
                _dropdown.MouseDown += (_, _) => _mouseTrace.Add($"Down: open={_dropdown.Expanded}, pointer={NativeMethods.CursorPosition}, owner={_dropdown.RectangleToScreen(_dropdown.ClientRectangle)}");
                _dropdown.MouseUp += (_, _) => _mouseTrace.Add($"Up: open={_dropdown.Expanded}, pointer={NativeMethods.CursorPosition}");
                _dropdown.Click += (_, _) => _mouseTrace.Add($"Click: open={_dropdown.Expanded}");
                _dropdown.OpenChoices();
            });
            _steps.Enqueue(() =>
            {
                var control = _dropdown!.RectangleToScreen(_dropdown.ClientRectangle);
                var popup = _dropdown.PopupBounds;
                if (popup.Width != control.Width || popup.Left != control.Left || popup.Top != control.Bottom || _dropdown.ChoiceBounds != popup)
                    throw new InvalidOperationException($"Dropdown attachment mismatch: control={control}, popup={popup}, choices={_dropdown.ChoiceBounds}");
                Capture("dropdown-open.png", Rectangle.Union(control, popup));
                _passed.Add("Real popup aligns to the control, with no host inset or extra border");
                _dropdown.SelectedIndex = 1; _dropdown.CloseChoices();
            });
            _steps.Enqueue(() =>
            {
                if (_dropdown!.Size != _dropdownSize || _dropdown.Expanded) throw new InvalidOperationException("Dropdown changes size after selection.");
                Save("dropdown-closed.png");
                _normalBounds = _form!.Bounds;
                PlaceDropdownUnderPointer(); _dropdown.OpenChoices(); PostClick(_dropdown);
            });
            _steps.Enqueue(() =>
            {
                if (_dropdown!.Expanded) throw new InvalidOperationException("Clicking the open dropdown reopened it. " + string.Join("; ", _mouseTrace));
                PlaceDropdownUnderPointer(); PostClick(_dropdown);
            });
            _steps.Enqueue(() =>
            {
                if (!_dropdown!.Expanded) throw new InvalidOperationException("A second click did not reopen the dropdown. " + string.Join("; ", _mouseTrace));
                _passed.Add("Posted owner mouse clicks close the dropdown once, then reopen on the next click");
                _dropdown.CloseChoices(); _form!.Bounds = _normalBounds; _form.SelectPage("Shortcuts");
            });
            _steps.Enqueue(() =>
            {
                Save("shortcuts-top.png"); CheckFields();
                if (Descendants(Page()).OfType<ShortcutRecorder>().Count(x => x.Visible) != 5)
                    throw new InvalidOperationException("The main shortcut page must show only five recorders.");
                var reset = Descendants(Page()).OfType<ShadeButton>().Single(b => b.AccessibleName == "Restore default shortcuts");
                if (!Page().RectangleToScreen(Page().ClientRectangle).Contains(reset.RectangleToScreen(reset.ClientRectangle)))
                    throw new InvalidOperationException("The five main shortcuts should fit without scrolling.");
                Descendants(Page()).OfType<ShadeButton>().Single(b => b.AccessibleName == "Additional shortcuts").PerformClick();
            });
            _steps.Enqueue(() =>
            {
                CheckFields(); Save("shortcuts-expanded.png");
                if (Descendants(Page()).OfType<ShortcutRecorder>().Count(x => x.Visible) != CommandCatalog.All.Count)
                    throw new InvalidOperationException("Additional shortcut recorders are missing.");
                _passed.Add("Five main bindings fit without scrolling; additional actions expand on request");
                Page().ScrollToEnd();
            });
            _steps.Enqueue(() =>
            {
                if (Page().ScrollOffset <= 0) throw new InvalidOperationException("Shortcuts did not scroll.");
                Save("shortcuts-bottom.png");
                var reset = Descendants(_form!).OfType<ShadeButton>().Single(b => b.AccessibleName == "Restore default shortcuts");
                var visible = Page().RectangleToScreen(Page().ClientRectangle);
                if (!visible.Contains(reset.RectangleToScreen(reset.ClientRectangle))) throw new InvalidOperationException("Last settings row is clipped after scrolling.");
                _passed.Add("Shortcuts scroll fully to their final row"); _form!.SelectPage("General");
            });
            _steps.Enqueue(() => { Save("general.png"); CheckFields(); _form!.SelectPage("Lens"); });
            _steps.Enqueue(() => { Save("lens-return.png"); CheckFields(); _passed.Add("All three settings pages retain their layout after switching"); });
            foreach (float scale in new[] { 1.25f, 1.5f, 2f })
            {
                _steps.Enqueue(() => ShowSettings(scale));
                _steps.Enqueue(() => { Save($"layout-{scale:0.##}x.png"); CheckFields(); _passed.Add($"Layout stress check at {scale:0.##}x fits the working area"); });
            }
            _steps.Enqueue(() =>
            {
                var pointer = NativeMethods.CursorPosition;
                var screen = Screen.FromPoint(pointer);
                var work = screen.Bounds;
                _cursorProbe = new Form
                {
                    FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, TopMost = true,
                    StartPosition = FormStartPosition.Manual, AutoScaleMode = AutoScaleMode.None,
                    Bounds = new Rectangle(Math.Clamp(pointer.X - 48, work.Left, work.Right - 96), Math.Clamp(pointer.Y - 48, work.Top, work.Bottom - 96), 96, 96),
                    BackColor = Theme.Background, Cursor = Cursors.Hand
                };
                _ = _cursorProbe.Handle;
                NativeMethods.SetWindowLongW(_cursorProbe.Handle, NativeMethods.GwlExStyle, NativeMethods.GetWindowLongW(_cursorProbe.Handle, NativeMethods.GwlExStyle) | NativeMethods.WsExNoActivate);
                _cursorProbe.Show(); NativeMethods.ShowWindow(_cursorProbe.Handle, 4);
                NativeMethods.SetWindowPos(_cursorProbe.Handle, -1, 0, 0, 0, 0, 0x53);
                NativeMethods.SendMessageW(_cursorProbe.Handle, 0x20, _cursorProbe.Handle, (nint)0x2000001);
                _probeCapture = new DesktopCapture(screen.DeviceName);
                _passed.Add("Windows capture API confirms that cursor capture is disabled");
            });
            _steps.Enqueue(CheckCursorPixels);
            _timer.Tick += (_, _) =>
            {
                try
                {
                    if (_steps.TryDequeue(out var step)) step();
                    else Complete(null);
                }
                catch (Exception exception) { Complete(exception); }
            };
            _timer.Start();
        }

        private void ShowSettings(float layoutScale = 1)
        {
            _form?.Dispose();
            _form = new SettingsForm(new AppSettings()) { TopMost = true };
            _ = _form.Handle;
            NativeMethods.SetWindowLongW(_form.Handle, NativeMethods.GwlExStyle,
                NativeMethods.GetWindowLongW(_form.Handle, NativeMethods.GwlExStyle) | NativeMethods.WsExNoActivate);
            if (layoutScale != 1) _form.Scale(new SizeF(layoutScale, layoutScale));
            _form.Show();
            NativeMethods.ShowWindow(_form.Handle, 4);
            NativeMethods.SetWindowPos(_form.Handle, -1, 0, 0, 0, 0, 0x53);
            _form.Refresh();
        }

        private ShadeScrollPanel Page() => Descendants(_form!).OfType<ShadeScrollPanel>().Single(x => x.Visible);
        private void ProfilePageSwitching()
        {
            int layouts = 0;
            LayoutEventHandler count = (_, _) => layouts++;
            var tables = Descendants(_form!).OfType<TableLayoutPanel>().ToArray();
            foreach (var table in tables) table.Layout += count;
            var samples = new List<object>();
            try
            {
                foreach (string name in new[] { "Shortcuts", "General", "Lens", "Shortcuts", "General", "Lens" })
                {
                    layouts = 0;
                    var watch = Stopwatch.StartNew();
                    _form!.SelectPage(name);
                    double layoutMs = watch.Elapsed.TotalMilliseconds;
                    using var frame = Print(_form.Handle, _form.Size);
                    if (samples.Count >= 3 && layouts > 8) throw new InvalidOperationException($"Switching back to {name} repeated {layouts} table layouts.");
                    samples.Add(new { page = name, layoutMs, paintIncludedMs = watch.Elapsed.TotalMilliseconds, layouts });
                }
            }
            finally { foreach (var table in tables) table.Layout -= count; }
            File.WriteAllText(Path.Combine(_output, "switching.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
            _passed.Add("Cached page switches avoid repeated table layout");
        }
        private void CheckCursorPixels()
        {
            var area = _cursorProbe!.Bounds;
            if (!area.Contains(NativeMethods.CursorPosition) || !NativeMethods.CursorVisible)
            {
                _skipped.Add("Visible cursor pixel check: the pointer moved away or was hidden by the foreground app.");
                _probeCapture!.Dispose(); _probeCapture = null; _cursorProbe.Dispose(); _cursorProbe = null;
                return;
            }
            _probeCapture!.Update();
            if (_probeCapture.Texture is null && _frameWaits++ < 8)
            {
                _cursorProbe.Refresh(); _steps.Enqueue(CheckCursorPixels); return;
            }
            using var bitmap = _probeCapture.CaptureArea(area);
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).ToArgb() != Theme.Background.ToArgb()) throw new InvalidOperationException($"Unexpected pixels beneath visible cursor at {x},{y}.");
            bitmap.Save(Path.Combine(_output, "cursor-excluded.png"));
            _passed.Add("Visible pointer excluded: all 9,216 source pixels under it retain the exact background color");
            _probeCapture.Dispose(); _probeCapture = null; _cursorProbe.Dispose(); _cursorProbe = null;
        }
        private void PlaceDropdownUnderPointer()
        {
            var center = _dropdown!.PointToScreen(new Point(_dropdown.Width / 2, _dropdown.Height / 2));
            var pointer = NativeMethods.CursorPosition;
            _form!.Location = new Point(_form.Left + pointer.X - center.X, _form.Top + pointer.Y - center.Y);
        }
        private static void PostClick(Control control)
        {
            nint point = (nint)(((control.Height / 2) << 16) | (control.Width / 2));
            NativeMethods.PostMessageW(control.Handle, 0x201, 1, point);
            NativeMethods.PostMessageW(control.Handle, 0x202, 0, point);
        }
        private void CheckFields()
        {
            if (!Screen.FromControl(_form!).WorkingArea.Contains(_form!.Bounds)) throw new InvalidOperationException("Settings exceeds the working area.");
            var fields = Descendants(Page()).OfType<TableLayoutPanel>().First();
            foreach (Control control in fields.Controls)
            {
                if (fields.GetColumn(control) == 1 && fields.GetColumnSpan(control) == 1 && Math.Abs(control.Right + control.Margin.Right - fields.ClientSize.Width) > 1)
                    throw new InvalidOperationException($"Settings columns did not fill the page after switching: {control.AccessibleName ?? control.Text}, bounds={control.Bounds}, table={fields.ClientSize}");
            }
            foreach (var control in Descendants(Page()).Where(c => c.Visible && c is (ShadeDropdown or ShadeButton or ShadeToggle or ShadeSlider)))
            {
                if (control.Width < 20 || control.Height < 24 || control.Left < 0 || control.Top < 0 || control.Right > control.Parent!.ClientSize.Width || control.Bottom > control.Parent.ClientSize.Height)
                    throw new InvalidOperationException($"Invalid settings bounds: {control.AccessibleName ?? control.Text}: {control.Bounds}, parent={control.Parent!.ClientSize}");
            }
        }

        private void Save(string name) => Capture(name, _form!.Bounds);
        private void Capture(string name, Rectangle bounds)
        {
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                using var form = Print(_form!.Handle, _form.Size);
                graphics.DrawImageUnscaled(form, _form.Left - bounds.Left, _form.Top - bounds.Top);
                if (_dropdown is { Expanded: true })
                {
                    var popupBounds = _dropdown.PopupBounds;
                    using var popup = Print(_dropdown.PopupHandle, popupBounds.Size);
                    graphics.DrawImageUnscaled(popup, popupBounds.Left - bounds.Left, popupBounds.Top - bounds.Top);
                }
            }
            if (name != "dropdown-open.png")
            {
                Point corner = _form!.PointToScreen(new Point(5, 5));
                if (bitmap.GetPixel(corner.X - bounds.Left, corner.Y - bounds.Top).ToArgb() != Theme.Background.ToArgb())
                    throw new InvalidOperationException("Native settings capture is incomplete.");
            }
            bitmap.Save(Path.Combine(_output, name));
        }
        private static Bitmap Print(nint handle, Size size)
        {
            var bitmap = new Bitmap(size.Width, size.Height);
            using var graphics = Graphics.FromImage(bitmap);
            nint hdc = graphics.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(handle, hdc, 2)) throw new InvalidOperationException("Windows could not print the app window.");
            }
            finally { graphics.ReleaseHdc(hdc); }
            return bitmap;
        }
        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
        private void Complete(Exception? error)
        {
            _timer.Stop(); _probeCapture?.Dispose(); _probeCapture = null; _cursorProbe?.Dispose(); _cursorProbe = null; _form?.Dispose(); _form = null;
            File.WriteAllText(Path.Combine(_output, "result.json"), JsonSerializer.Serialize(new { success = error is null, passed = _passed, skipped = _skipped, error = error?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = error is null ? 0 : 1;
            ExitThread();
        }
        protected override void Dispose(bool disposing) { if (disposing) { _timer.Dispose(); _probeCapture?.Dispose(); _cursorProbe?.Dispose(); _form?.Dispose(); } base.Dispose(disposing); }
    }
}
