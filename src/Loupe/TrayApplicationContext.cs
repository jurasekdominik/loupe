namespace Loupe;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HotkeyWindow _hotkeys = new();
    private readonly Control _dispatcher = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _enabledItem, _showItem, _inspectMenu;
    private readonly LensForm _lens;
    private KeyboardHook? _keyboard;
    private bool _latched, _peekHeld, _suppressPeek, _settingsOpen, _disposed;

    public TrayApplicationContext()
    {
        _dispatcher.CreateControl();
        _lens = new LensForm(_settings);
        _lens.CommandRequested += Execute;
        _lens.Latched += () => _latched = true;
        _lens.SettingsChanged += () => _settings.Save();
        _lens.CaptureFailed += exception => { Dismiss(); AppLog.Write(exception.ToString()); Notify("Capture stopped", $"{exception.Message}\nUse {_settings.Binding(AppCommand.ToggleLoupe)} to retry.", ToolTipIcon.Warning); };
        Theme.StyleMenu(_menu);
        _showItem = MenuAction("Show loupe", AppCommand.ToggleLoupe);
        _enabledItem = MenuAction("Enabled", AppCommand.ToggleEnabled);
        _inspectMenu = new ToolStripMenuItem("Inspection"); Theme.StyleMenu(_inspectMenu.DropDown);
        foreach (var command in new[] { AppCommand.TogglePin, AppCommand.ToggleMeasure, AppCommand.ToggleGrid, AppCommand.Clear, AppCommand.CaptureArea, AppCommand.CaptureView })
            _inspectMenu.DropDownItems.Add(MenuAction(CommandCatalog.Get(command).Label, command));
        _menu.Items.AddRange([
            _showItem, _enabledItem, _inspectMenu, new ToolStripSeparator(),
            MenuAction("Settings…", AppCommand.OpenSettings), MenuAction("Open captures folder", AppCommand.OpenCaptureFolder),
            new ToolStripSeparator(), MenuAction("Exit Loupe", AppCommand.Exit)
        ]);
        _tray = new NotifyIcon { Text = "Loupe", Icon = AppIcon.Create(SystemInformation.SmallIconSize), ContextMenuStrip = _menu, Visible = true };
        _tray.DoubleClick += (_, _) => Toggle();
        _menu.Opening += (_, _) =>
        {
            _enabledItem.Checked = _settings.Enabled; _inspectMenu.Enabled = _lens.Visible;
            _showItem.Text = _lens.IsOpen ? "Hide loupe" : "Show loupe";
            UpdateMenuBindings(_menu.Items);
        };
        _hotkeys.Pressed += id => Execute((AppCommand)id);
        _hotkeys.CloseRequested += ExitThread;
        _hotkeys.DisplaysChanged += () => { Dismiss(); Notify("Display setup changed", "Open the loupe again to reconnect or choose your monitor."); };
        ConfigureInput();
        if (!_settings.HasSeenWelcome)
        {
            Notify("Loupe is ready", $"{_settings.Binding(AppCommand.ToggleLoupe)}: show / hide\nRight-click the tray icon for settings.");
            _settings.HasSeenWelcome = true; _settings.Save();
        }
    }

    private ToolStripMenuItem MenuAction(string label, AppCommand command)
    {
        var item = new ToolStripMenuItem(label) { Tag = command };
        item.Click += (_, _) => Execute(command); return item;
    }

    private void UpdateMenuBindings(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is not ToolStripMenuItem menu) continue;
            if (menu.Tag is AppCommand command) { var binding = _settings.Binding(command); menu.ShortcutKeyDisplayString = binding.IsEmpty ? "" : binding.ToString(); }
            if (menu.HasDropDownItems) UpdateMenuBindings(menu.DropDownItems);
        }
    }

    private void ConfigureInput()
    {
        _keyboard?.Dispose(); _keyboard = null;
        RegisterCommands();
        if (_settings.Enabled && !_settingsOpen && CommandCatalog.All.Any(x =>
            (x.Command == AppCommand.Peek || x.PointerRequired) && !_settings.Binding(x.Command).IsEmpty))
        {
            _keyboard = new KeyboardHook { KeyChanged = KeyboardChanged };
            _peekHeld = _keyboard.State.IsHeld(_settings.Binding(AppCommand.Peek));
        }
        _tray.Text = _settings.Enabled ? "Loupe · " + _settings.Binding(AppCommand.ToggleLoupe) : "Loupe · disabled";
        if (_tray.Text.Length > 63) _tray.Text = "Loupe · live pixel inspection";
        _enabledItem.Checked = _settings.Enabled;
    }

    private void RegisterCommands()
    {
        _hotkeys.UnregisterAll();
        if (_settingsOpen) return;
        var missing = new List<string>();
        foreach (var definition in CommandCatalog.All)
        {
            if (definition.Command == AppCommand.Peek || definition.PointerRequired) continue;
            bool available = definition.AlwaysAvailable || (_settings.Enabled && (!definition.WhileVisible || _lens.IsOpen));
            var binding = _settings.Binding(definition.Command);
            if (!available || binding.IsEmpty) continue;
            if (!_hotkeys.Register((int)definition.Command, binding)) missing.Add(definition.Label);
        }
        if (missing.Count > 0) Notify("Some shortcuts are in use", $"Choose new bindings in Settings for: {string.Join(", ", missing)}. The tray and toolbar remain available.", ToolTipIcon.Warning);
    }

    private void Execute(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.ToggleLoupe: Toggle(); break;
            case AppCommand.ToggleEnabled: SetEnabled(!_settings.Enabled); break;
            case AppCommand.OpenSettings: ShowSettings(); break;
            case AppCommand.OpenCaptureFolder:
                try { Directory.CreateDirectory(_settings.ScreenshotFolder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_settings.ScreenshotFolder) { UseShellExecute = true }); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Notify("Folder unavailable", e.Message, ToolTipIcon.Warning); }
                break;
            case AppCommand.Exit: ExitThread(); break;
            default:
                if (!_lens.IsOpen) return;
                switch (command)
                {
                    case AppCommand.Hide: Dismiss(); break;
                    case AppCommand.TogglePin: _lens.TogglePin(); break;
                    case AppCommand.ToggleMeasure: _lens.ToggleMeasure(); break;
                    case AppCommand.ToggleGrid: _lens.ToggleGrid(); break;
                    case AppCommand.Clear: _lens.ClearGuides(); break;
                    case AppCommand.ZoomIn: _lens.ChangeZoom(1); break;
                    case AppCommand.ZoomOut: _lens.ChangeZoom(-1); break;
                    case AppCommand.GuideHorizontal: _lens.AddGuide(false); break;
                    case AppCommand.GuideVertical: _lens.AddGuide(true); break;
                    case AppCommand.GuideCrosshair: _lens.AddGuide(true); _lens.AddGuide(false); break;
                    case AppCommand.RulerPoint: _lens.PlaceRulerPoint(); break;
                    case AppCommand.PanLeft: _lens.Pan(-1, 0); break;
                    case AppCommand.PanRight: _lens.Pan(1, 0); break;
                    case AppCommand.PanUp: _lens.Pan(0, -1); break;
                    case AppCommand.PanDown: _lens.Pan(0, 1); break;
                    case AppCommand.IncreaseSize: _lens.ChangeSize(1); break;
                    case AppCommand.DecreaseSize: _lens.ChangeSize(-1); break;
                    case AppCommand.CaptureArea: case AppCommand.CaptureView: Capture(command == AppCommand.CaptureView); break;
                }
                break;
        }
    }

    private void Capture(bool magnified)
    {
        try
        {
            var result = _lens.SaveCapture(magnified);
            Notify("Capture saved", $"{Path.GetFileName(result.Path)}\n{result.Size.Width} × {result.Size.Height} pixels{(result.CopiedToClipboard ? " · copied to clipboard" : "")}");
        }
        catch (Exception e) { AppLog.Write(e.ToString()); Notify("Capture could not be saved", e.Message, ToolTipIcon.Warning); }
    }

    private bool KeyboardChanged(Keys key, bool down)
    {
        if (_keyboard is null) return false;
        var peekBinding = _settings.Binding(AppCommand.Peek);
        bool peek = _keyboard.State.IsHeld(peekBinding), wasPeek = _peekHeld;
        _peekHeld = peek;
        if (!peek) _suppressPeek = false;
        if (peek != wasPeek) Post(SyncPeek);
        if (down && _lens.Visible && _lens.Measuring && _lens.ContainsPointer)
        {
            foreach (var definition in CommandCatalog.All.Where(x => x.PointerRequired))
                if (_keyboard.State.Matches(_settings.Binding(definition.Command))) { Post(() => Execute(definition.Command)); return true; }
        }
        return peek && key == peekBinding.Key && !KeyBinding.IsModifier(key);
    }

    private void Post(Action action) { if (!_disposed) _dispatcher.BeginInvoke(action); }
    private void SyncPeek()
    {
        if (_settingsOpen || !_settings.Enabled) return;
        if (_peekHeld && !_suppressPeek) TryShow(); else if (!_latched) HideLens();
    }
    private void Toggle()
    {
        if (_lens.IsOpen) { Dismiss(); return; }
        if (!_settings.Enabled) SetEnabled(true);
        _latched = true; TryShow();
    }

    private void TryShow()
    {
        if (_settingsOpen || _lens.IsOpen || !_settings.Enabled || (_peekHeld && _suppressPeek)) return;
        Screen? screen = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == _settings.MonitorDevice);
        if (screen is null)
        {
            if (Screen.AllScreens.Length == 1) { screen = Screen.AllScreens[0]; _settings.MonitorDevice = screen.DeviceName; _settings.Save(); }
            else
            {
                bool latchOnOpen = _latched;
                if (!ShowSettings(true)) { _latched = false; _suppressPeek = _peekHeld; return; }
                _latched = latchOnOpen || !_peekHeld;
                screen = Screen.AllScreens.FirstOrDefault(x => x.DeviceName == _settings.MonitorDevice);
                if (screen is null) return;
            }
        }
        if (!_settings.Enabled) return;
        try { _lens.Open(screen, NativeMethods.CursorPosition); RegisterCommands(); }
        catch (Exception e) { _latched = false; _suppressPeek = _peekHeld; AppLog.Write(e.ToString()); Notify("Loupe could not start capture", e.Message, ToolTipIcon.Error); }
    }

    private void Dismiss() { _latched = false; _suppressPeek = _peekHeld; HideLens(); }
    private void HideLens() { _lens.CloseInspection(); RegisterCommands(); }
    private void SetEnabled(bool enabled)
    {
        Dismiss(); _peekHeld = false; _settings.Enabled = enabled; _settings.Save(); ConfigureInput();
        Notify(enabled ? "Loupe enabled" : "Loupe disabled", enabled ? $"Use {_settings.Binding(AppCommand.ToggleLoupe)} to inspect." : "Enable again using the tray menu.");
    }

    private bool ShowSettings(bool chooseMonitor = false)
    {
        if (_settingsOpen) return false;
        _settingsOpen = true; _latched = false; HideLens();
        _keyboard?.Dispose(); _keyboard = null; _hotkeys.UnregisterAll();
        try
        {
            using var form = new SettingsForm(_settings, chooseMonitor);
            form.ValidateBindings = bindings =>
            {
                foreach (var definition in CommandCatalog.All.Where(x => x.Command != AppCommand.Peek && !x.PointerRequired))
                {
                    var binding = bindings.GetValueOrDefault(definition.Command) ?? new();
                    if (binding.IsEmpty) continue;
                    bool registered = _hotkeys.Register(1000, binding); _hotkeys.Unregister(1000);
                    if (!registered) return $"{definition.Label}: {binding} is already registered by Windows or another app.";
                }
                return null;
            };
            return form.ShowDialog() == DialogResult.OK;
        }
        finally { _settingsOpen = false; _peekHeld = false; _suppressPeek = false; ConfigureInput(); }
    }

    private void Notify(string title, string message, ToolTipIcon icon = ToolTipIcon.Info) => _tray.ShowBalloonTip(3500, title, message, icon);
    protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true; _keyboard?.Dispose(); _hotkeys.Dispose(); _lens.Dispose();
            _tray.Visible = false; _tray.Icon?.Dispose(); _tray.Dispose(); _menu.Dispose(); _dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
