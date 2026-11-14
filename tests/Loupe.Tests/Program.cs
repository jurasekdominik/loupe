using System.Drawing;
using System.Numerics;
using Loupe;

int count = 0;
var failures = new List<string>();
void Test(string name, Action body)
{
    try { body(); count++; Console.WriteLine($"PASS {name}"); }
    catch (Exception exception) { failures.Add(name); Console.Error.WriteLine($"FAIL {name}: {exception.Message}"); }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}");
}
void True(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

Test("Three-pixel gaps are measured between boundaries", () =>
{
    var viewport = InspectionViewport.Create(new Rectangle(0, 0, 2560, 1440), new Point(1000, 600), new Size(448, 448), 8);
    var first = viewport.NearestBoundary(new Point(viewport.Destination.Left + 80, viewport.Destination.Top + 80));
    var second = viewport.NearestBoundary(new Point(viewport.Destination.Left + 104, viewport.Destination.Top + 80));
    var measurement = new PixelMeasurement(first, second);
    Equal(3, measurement.Width);
    Equal(0, measurement.Height);
    Equal(3, new PixelMeasurement(second, first).Width);
});

Test("An adjacent pixel boundary measures one pixel", () =>
{
    foreach (int zoom in AppSettings.ZoomLevels)
    {
        var viewport = InspectionViewport.Create(new Rectangle(0, 0, 2560, 1440), new Point(1000, 600), new Size(448, 448), zoom);
        Point a = viewport.NearestBoundary(viewport.Destination.Location);
        Point b = viewport.NearestBoundary(new Point(viewport.Destination.Left + zoom, viewport.Destination.Top + zoom));
        Equal(1, new PixelMeasurement(a, b).Width);
        Equal(1, new PixelMeasurement(a, b).Height);
    }
});

Test("Zoom, DPI and monitor origin preserve exact pixel mapping", () =>
{
    foreach (double scale in new[] { 1d, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 3, 4 })
    foreach (int zoom in AppSettings.ZoomLevels)
    foreach (Rectangle monitor in new[] { new Rectangle(0, 0, 2560, 1440), new Rectangle(-3840, -200, 3840, 2160), new Rectangle(2560, -1080, 1080, 1920) })
    {
        int size = (int)Math.Round(448 * scale);
        var center = new Point(monitor.Left + monitor.Width / 2, monitor.Top + monitor.Height / 2);
        var viewport = InspectionViewport.Create(monitor, center, new Size(size, size), zoom);
        Equal(viewport.Source.Width * zoom, viewport.Destination.Width);
        Equal(viewport.Source.Height * zoom, viewport.Destination.Height);
        True(monitor.Contains(viewport.Source), "Source extended outside monitor");
        foreach (Point boundary in new[] { viewport.Source.Location, new Point(viewport.Source.Right, viewport.Source.Bottom), new Point(viewport.Source.Left + 1, viewport.Source.Top + 1) })
        {
            PointF onLens = viewport.ToLens(boundary);
            Equal(boundary, viewport.NearestBoundary(Point.Round(onLens)));
            Vector2 transformed = Vector2.Transform(new Vector2(boundary.X - monitor.Left, boundary.Y - monitor.Top), viewport.DesktopToLens(monitor.Location));
            Equal(onLens.X, transformed.X);
            Equal(onLens.Y, transformed.Y);
        }
    }
});

Test("All four monitor corners clamp without distorting pixels", () =>
{
    var monitor = new Rectangle(-2560, -1440, 2560, 1440);
    foreach (Point center in new[] { new Point(-9999, -9999), new Point(9999, -9999), new Point(-9999, 9999), new Point(9999, 9999) })
    {
        var viewport = InspectionViewport.Create(monitor, center, new Size(560, 560), 8);
        True(monitor.Contains(viewport.Source), "Source extended outside monitor");
        Equal(0, viewport.Destination.Width % 8);
        Equal(viewport.Source.Location, viewport.NearestBoundary(new Point(-1000, -1000)));
        Equal(new Point(viewport.Source.Right, viewport.Source.Bottom), viewport.NearestBoundary(new Point(10000, 10000)));
    }
});

Test("Nearest boundary snaps at the midpoint", () =>
{
    var viewport = new InspectionViewport(new Rectangle(100, 200, 20, 20), new Rectangle(4, 4, 160, 160), 8);
    Equal(new Point(100, 200), viewport.NearestBoundary(new Point(7, 7)));
    Equal(new Point(101, 201), viewport.NearestBoundary(new Point(8, 8)));
});

Test("Settings reject invalid zoom, size and key values", () =>
{
    var settings = new AppSettings { Zoom = 0, Size = -10, FramesPerSecond = 0, GridOpacity = 999 };
    settings.Bindings[AppCommand.ToggleLoupe] = new(Keys.F12);
    settings.Validate();
    Equal(8, settings.Zoom); Equal(448, settings.Size); Equal(60, settings.FramesPerSecond);
    Equal(Keys.F10, settings.Binding(AppCommand.ToggleLoupe).Key); Equal(70, settings.GridOpacity);
    var retired = new AppSettings { FramesPerSecond = 120 }; retired.Validate();
    Equal(60, retired.FramesPerSecond); // 120 was removed: the refresh timer cannot reach it.
});

Test("Settings round-trip and recover from a partial file", () =>
{
    string path = Path.Combine(Path.GetTempPath(), $"loupe-settings-test-{Guid.NewGuid():N}.json");
    try
    {
        var settings = new AppSettings { Zoom = 16, MonitorDevice = "TEST DISPLAY", Enabled = false };
        settings.Bindings[AppCommand.Peek] = new(Keys.RControlKey);
        settings.Bindings[AppCommand.Clear] = new(Keys.K, KeyModifiers.Shift | KeyModifiers.Control);
        settings.Bindings[AppCommand.CaptureView] = new();
        settings.Save(path);
        var loaded = AppSettings.Load(path);
        Equal(16, loaded.Zoom); Equal("TEST DISPLAY", loaded.MonitorDevice);
        Equal(false, loaded.Enabled); Equal(new KeyBinding(Keys.RControlKey), loaded.Binding(AppCommand.Peek));
        Equal(new KeyBinding(Keys.K, KeyModifiers.Shift | KeyModifiers.Control), loaded.Binding(AppCommand.Clear));
        True(loaded.Binding(AppCommand.CaptureView).IsEmpty, "Explicitly unassigned command was reset");
        File.WriteAllText(path, "{ partial settings");
        Equal(8, AppSettings.Load(path).Zoom);
        File.WriteAllText(path, "null");
        Equal(8, AppSettings.Load(path).Zoom);
    }
    finally { File.Delete(path); File.Delete(path + ".tmp"); }
});

Test("First-build settings migrate shortcuts and discard removed appearance choices", () =>
{
    string path = Path.Combine(Path.GetTempPath(), $"loupe-migration-{Guid.NewGuid():N}.json");
    try
    {
        File.WriteAllText(path, "{\"ToggleKey\":120,\"PeekShortcut\":2,\"Shape\":1,\"IconStyle\":\"PixelLens\",\"Zoom\":16,\"GridOpacity\":35}");
        var settings = AppSettings.Load(path);
        Equal(new KeyBinding(Keys.F9), settings.Binding(AppCommand.ToggleLoupe));
        Equal(new KeyBinding(Keys.F9, KeyModifiers.Control | KeyModifiers.Alt), settings.Binding(AppCommand.ToggleEnabled));
        Equal(new KeyBinding(Keys.None, KeyModifiers.Control | KeyModifiers.Alt), settings.Binding(AppCommand.Peek));
        Equal(16, settings.Zoom); Equal(35, settings.GridOpacity);
        settings.Save(path);
        True(!File.ReadAllText(path).Contains("Shape"), "Removed shape persisted into new settings");
        True(!File.ReadAllText(path).Contains("IconStyle"), "Removed icon choice persisted into new settings");
        File.WriteAllText(path, "{\"ToggleKey\":\"invalid\",\"PeekShortcut\":null}");
        Equal(Keys.F10, AppSettings.Load(path).Binding(AppCommand.ToggleLoupe).Key);
    }
    finally { File.Delete(path); File.Delete(path + ".tmp"); }
});

Test("Only the five main shortcuts reserve keys by default; upgrades preserve custom bindings", () =>
{
    Equal(5, CommandCatalog.Defaults().Count(x => !x.Value.IsEmpty));
    string path = Path.Combine(Path.GetTempPath(), $"loupe-shortcuts-{Guid.NewGuid():N}.json");
    try
    {
        var old = new AppSettings { Version = 2, Bindings = CommandCatalog.All.ToDictionary(x => x.Command, x => x.LegacyDefault) };
        old.Bindings[AppCommand.CaptureArea] = new(Keys.F9, KeyModifiers.Control);
        old.Bindings[AppCommand.TogglePin] = new(Keys.P, KeyModifiers.Control);
        old.Bindings[AppCommand.Clear] = new();
        old.Save(path);
        var loaded = AppSettings.Load(path);
        Equal(3, loaded.Version);
        Equal(new KeyBinding(Keys.F9, KeyModifiers.Control), loaded.Binding(AppCommand.CaptureArea));
        Equal(new KeyBinding(Keys.P, KeyModifiers.Control), loaded.Binding(AppCommand.TogglePin));
        True(loaded.Binding(AppCommand.Clear).IsEmpty, "An explicitly cleared binding was restored");
        True(loaded.Binding(AppCommand.Peek).IsEmpty && loaded.Binding(AppCommand.PanLeft).IsEmpty, "Old optional defaults still reserve keys");
        // Once upgraded, explicitly choosing a former default must keep it.
        loaded.Bindings[AppCommand.PanLeft] = new(Keys.Left);
        loaded.Save(path);
        Equal(new KeyBinding(Keys.Left), AppSettings.Load(path).Binding(AppCommand.PanLeft));
    }
    finally { File.Delete(path); File.Delete(path + ".tmp"); }
});

Test("Recorder waits for release and preserves the complete modifier chord", () =>
{
    var session = new RecordingSession(false);
    Equal<RecordedShortcut?>(null, session.Process(Keys.Enter, false));
    Equal<RecordedShortcut?>(null, session.Process(Keys.LControlKey, true));
    session.Process(Keys.LShiftKey, true); session.Process(Keys.K, true);
    Equal<RecordedShortcut?>(null, session.Process(Keys.K, false));
    Equal<RecordedShortcut?>(null, session.Process(Keys.LControlKey, false));
    var result = session.Process(Keys.LShiftKey, false);
    Equal(new KeyBinding(Keys.K, KeyModifiers.Control | KeyModifiers.Shift), result?.Binding);
});

Test("Modifier-only recording is supported for peek and rejected for other actions", () =>
{
    foreach (bool allow in new[] { true, false })
    {
        var session = new RecordingSession(allow);
        session.Process(Keys.LControlKey, true); session.Process(Keys.LMenu, true);
        session.Process(Keys.LMenu, false);
        var result = session.Process(Keys.LControlKey, false);
        if (allow) Equal(new KeyBinding(Keys.None, KeyModifiers.Control | KeyModifiers.Alt), result?.Binding);
        else True(result?.Error is not null, "Modifier-only shortcut accepted for a toggle action");
    }
});

Test("Escape cancels recording and reserved F12 is rejected", () =>
{
    var canceled = new RecordingSession(false);
    canceled.Process(Keys.Escape, true);
    True(canceled.Process(Keys.Escape, false)?.Canceled == true, "Escape did not cancel");
    var reserved = new RecordingSession(false);
    reserved.Process(Keys.F12, true);
    True(reserved.Process(Keys.F12, false)?.Error is not null, "F12 was accepted");
});

Test("Runtime matching handles either Ctrl key, exact modifiers and release", () =>
{
    var state = new KeyboardState();
    state.Set(Keys.RControlKey, true); state.Set(Keys.LMenu, true); state.Set(Keys.Space, true);
    var peek = new KeyBinding(Keys.Space, KeyModifiers.Control | KeyModifiers.Alt);
    True(state.Matches(peek), "Right Ctrl did not match generic Ctrl");
    state.Set(Keys.LShiftKey, true); True(!state.Matches(peek), "Extra modifier matched a different chord");
    True(state.IsHeld(peek), "Pressing an extra modifier dismissed a peek whose keys were still held");
    state.Set(Keys.LShiftKey, false); state.Set(Keys.Space, false); True(!state.Matches(peek), "Release left peek active");
    True(!state.IsHeld(peek), "Releasing the main key left the held peek active");
    True(state.Matches(new(Keys.None, KeyModifiers.Control | KeyModifiers.Alt)), "Modifier-only peek did not match");
    state.Set(Keys.LMenu, false); True(state.Matches(new(Keys.RControlKey)), "Side-specific legacy peek did not match");
});

Test("Duplicate shortcuts are rejected and unassigned actions stay valid", () =>
{
    var bindings = CommandCatalog.Defaults();
    True(CommandCatalog.Validate(bindings) is null, "Defaults conflict");
    bindings[AppCommand.Clear] = bindings[AppCommand.TogglePin];
    True(CommandCatalog.Validate(bindings)?.Contains("both") == true, "Duplicate not identified");
    foreach (var command in bindings.Keys.ToArray()) bindings[command] = new();
    True(CommandCatalog.Validate(bindings) is null, "Unassigned actions conflict");
});

Test("Capture saves lossless, distinct PNGs without touching the clipboard", () =>
{
    string folder = Path.Combine(Path.GetTempPath(), $"loupe-png-test-{Guid.NewGuid():N}");
    try
    {
        using var bitmap = new Bitmap(3, 2);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 5, 147, 198));
        bitmap.SetPixel(2, 1, Color.FromArgb(255, 216, 57, 149));
        var first = Screenshots.Save(bitmap, folder, "original", false);
        var second = Screenshots.Save(bitmap, folder, "original", false);
        True(first.Path != second.Path, "Capture overwrote a previous file");
        True(!first.CopiedToClipboard, "Clipboard was touched by an offline test");
        using var saved = new Bitmap(first.Path);
        Equal(new Size(3, 2), saved.Size);
        Equal(bitmap.GetPixel(0, 0).ToArgb(), saved.GetPixel(0, 0).ToArgb());
        Equal(bitmap.GetPixel(2, 1).ToArgb(), saved.GetPixel(2, 1).ToArgb());
    }
    finally { if (Directory.Exists(folder)) { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); } }
});

Test("Hotkey registration is released for reuse", () =>
{
    using var window = new HotkeyWindow();
    True(window.Register(100, Keys.F24, NativeMethods.ModControl | NativeMethods.ModAlt), "Could not register test shortcut");
    window.Unregister(100);
    True(window.Register(101, Keys.F24, NativeMethods.ModControl | NativeMethods.ModAlt), "Shortcut was not released");
});

Test("Rounded surfaces keep all four borders inside their bounds at each scale", () =>
{
    foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
    {
        int width = (int)(120 * scale), height = (int)(34 * scale);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Theme.Background);
            Theme.Surface(graphics, bitmap.Size, Theme.Panel, Theme.Sky, scale);
        }
        foreach (Point edge in new[] { new Point(width / 2, 0), new Point(width / 2, height - 1), new Point(0, height / 2), new Point(width - 1, height / 2) })
            Equal(Theme.Sky.ToArgb(), bitmap.GetPixel(edge.X, edge.Y).ToArgb());
        Equal(Theme.Panel.ToArgb(), bitmap.GetPixel(width / 2, height / 2).ToArgb());
    }
});

Test("Button symbols stay centered and the capture caption fits at each scale", () =>
{
    foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
    foreach (string text in new[] { "+", "−", "×", "Capture ▾" })
    {
        int width = (int)Math.Round((text == "Capture ▾" ? 90 : 28) * scale), height = (int)Math.Round(30 * scale);
        using var bitmap = new Bitmap(width, height);
        using var font = new Font("Segoe UI", 9.5f * scale);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            Theme.ButtonContent(graphics, text, font, new Rectangle(Point.Empty, bitmap.Size), Color.White, scale);
        }
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color pixel = bitmap.GetPixel(x, y);
            if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) < 110) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        True(right >= left, $"{text} did not paint");
        True(Math.Abs((left + right) / 2d - (width - 1) / 2d) <= 1, $"{text} is off center horizontally at {scale}×: ink={left}..{right}, width={width}");
        if (text != "Capture ▾")
            True(Math.Abs((top + bottom) / 2d - (height - 1) / 2d) <= .5, $"{text} is off center vertically at {scale}×");
        else
        {
            int expected = TextRenderer.MeasureText("Capture", font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            True(right - left + 1 >= expected + 6 * scale, $"Capture caption was truncated at {scale}×");
        }
    }
});

Console.WriteLine($"\n{count} passed, {failures.Count} failed. All checks ran locally.");
return failures.Count > 0 ? 1 : 0;
