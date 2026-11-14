namespace Loupe;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly Screen[] _screens = Screen.AllScreens;
    private readonly ShadeDropdown _monitor, _zoom, _size, _fps;
    private readonly ShadeToggle _grid, _enabled, _startup, _clipboard;
    private readonly ShadeSlider _opacity;
    private readonly Dictionary<AppCommand, ShortcutRecorder> _recorders = [];
    private readonly Dictionary<string, ShadeScrollPanel> _pages = [];
    private readonly Dictionary<string, Button> _tabs = [];
    private readonly Dictionary<string, TableLayoutPanel[]> _pageLayouts = [];
    private string? _currentPage;
    private readonly Label _status = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private string _folder;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<IReadOnlyDictionary<AppCommand, KeyBinding>, string?>? ValidateBindings { get; set; }

    public SettingsForm(AppSettings settings, bool chooseMonitor = false)
    {
        _settings = settings; _folder = settings.ScreenshotFolder;
        SuspendLayout();
        Theme.Apply(this);
        Text = chooseMonitor ? "Loupe · Choose monitor" : "Loupe · Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 480); Padding = new Padding(18);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 36, 36 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.Controls.Add(new Label { Text = "Settings", Font = new Font("Segoe UI", 15, FontStyle.Bold), Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        foreach (string name in new[] { "Lens", "Shortcuts", "General" })
        {
            var page = new ShadeScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4), Visible = false };
            _pages.Add(name, page); body.Controls.Add(page);
            var tab = Theme.Button(name, (_, _) => SelectPage(name), 92, 28);
            ((ShadeButton)tab).Ghost = true;
            _tabs.Add(name, tab); navigation.Controls.Add(tab);
        }
        root.Controls.Add(navigation, 0, 1); root.Controls.Add(body, 0, 2);

        var lens = FieldTable(_pages["Lens"]);
        _monitor = new ShadeDropdown(_screens.Select(s => $"{s.DeviceName.Replace("\\\\.\\", "")} · {s.Bounds.Width} × {s.Bounds.Height}").ToArray(), Math.Max(0, Array.FindIndex(_screens, s => s.DeviceName == settings.MonitorDevice)));
        _zoom = new ShadeDropdown(AppSettings.ZoomLevels.Select(x => $"{x}×").ToArray(), Array.IndexOf(AppSettings.ZoomLevels, settings.Zoom));
        _size = new ShadeDropdown(AppSettings.Sizes.Select(x => $"{x} × {x}").ToArray(), Array.IndexOf(AppSettings.Sizes, settings.Size));
        _fps = new ShadeDropdown(AppSettings.FrameRates.Select(x => $"{x} fps").ToArray(), Array.IndexOf(AppSettings.FrameRates, settings.FramesPerSecond));
        _grid = new ShadeToggle("Visible at 4× and above", settings.ShowGrid);
        _opacity = new ShadeSlider(settings.GridOpacity);
        AddField(lens, "Monitor", null, _monitor);
        AddField(lens, "Magnification", null, _zoom);
        AddField(lens, "Lens size", null, _size);
        AddField(lens, "Refresh rate", "While the lens is visible", _fps);
        AddField(lens, "Pixel grid", null, _grid);
        AddField(lens, "Grid opacity", null, _opacity);

        var shortcuts = FieldTable(_pages["Shortcuts"]);
        AddGroup(shortcuts, null, CommandCatalog.Main.ToArray());
        var additional = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 0, Margin = Padding.Empty, Dock = DockStyle.Top, Visible = false };
        additional.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46)); additional.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        AddGroup(additional, "App", [AppCommand.ToggleEnabled, AppCommand.Peek, AppCommand.Hide, AppCommand.OpenSettings, AppCommand.OpenCaptureFolder, AppCommand.Exit]);
        AddGroup(additional, "Lens", [AppCommand.ToggleFreeze, AppCommand.Clear, AppCommand.ToggleGrid, AppCommand.IncreaseSize, AppCommand.DecreaseSize]);
        AddGroup(additional, "Guides & ruler", [AppCommand.GuideHorizontal, AppCommand.GuideVertical, AppCommand.GuideCrosshair, AppCommand.RulerPoint]);
        AddGroup(additional, "Pan", [AppCommand.PanLeft, AppCommand.PanRight, AppCommand.PanUp, AppCommand.PanDown]);
        AddGroup(additional, "Screenshots", [AppCommand.CaptureArea, AppCommand.CaptureView]);
        var more = Theme.Button("Show additional", null, 180, 28);
        more.AccessibleName = "Additional shortcuts";
        more.Click += (_, _) =>
        {
            additional.Visible = !additional.Visible;
            more.Text = additional.Visible ? "Hide additional" : "Show additional";
            ((ShadeButton)more).Active = additional.Visible;
        };
        AddField(shortcuts, "Additional shortcuts", null, more, 40);
        int extraRow = shortcuts.RowCount++;
        shortcuts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shortcuts.Controls.Add(additional, 0, extraRow); shortcuts.SetColumnSpan(additional, 2);
        var reset = Theme.Button("Restore defaults", (_, _) => { foreach (var definition in CommandCatalog.All) _recorders[definition.Command].Binding = CommandCatalog.DefaultBinding(definition.Command); }, 180, 28);
        reset.AccessibleName = "Restore default shortcuts";
        AddField(shortcuts, "Default shortcuts", null, reset, 40);

        var general = FieldTable(_pages["General"]);
        _enabled = new ShadeToggle("Enabled", settings.Enabled);
        _startup = new ShadeToggle("Start in the tray", settings.StartWithWindows);
        _clipboard = new ShadeToggle("Copy screenshots", settings.CopyCapturesToClipboard);
        AddField(general, "Global shortcuts", null, _enabled);
        AddField(general, "Start with Windows", null, _startup);
        var folder = Theme.Button(_folder, null, 0, 28); folder.AutoSize = false; folder.Click += ChooseFolder;
        AddField(general, "Screenshot folder", "Click to choose a folder", folder);
        AddField(general, "Clipboard", "PNG files are always saved", _clipboard);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _status.Font = new Font("Segoe UI", 8.5f); _status.Margin = new Padding(0, 8, 12, 0);
        var cancel = Theme.Button("Cancel", (_, _) => { DialogResult = DialogResult.Cancel; Close(); }, 88, 28);
        var save = Theme.Button(chooseMonitor ? "Use monitor" : "Save settings", Save, 132, 28); ((ShadeButton)save).Primary = true;
        foreach (var button in new[] { cancel, save }) { button.Dock = DockStyle.Fill; button.Margin = new Padding(8, 16, 0, 8); }
        footer.Controls.Add(_status, 0, 0); footer.Controls.Add(cancel, 1, 0); footer.Controls.Add(save, 2, 0);
        root.Controls.Add(footer, 0, 3); Controls.Add(root); CancelButton = cancel;
        foreach (var (name, page) in _pages) _pageLayouts.Add(name, Descendants(page).OfType<TableLayoutPanel>().ToArray());
        SelectPage("Lens"); ResumeLayout(true);

        void ChooseFolder(object? sender, EventArgs args)
        {
            using var dialog = new FolderBrowserDialog { Description = "Screenshot folder", SelectedPath = _folder, UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) { _folder = dialog.SelectedPath; folder.Text = _folder; }
        }
    }

    private void AddGroup(TableLayoutPanel table, string? title, AppCommand[] commands)
    {
        if (title is not null)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            var heading = new Label { Text = title, ForeColor = Theme.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
            table.Controls.Add(heading, 0, row); table.SetColumnSpan(heading, 2);
        }
        foreach (var command in commands)
        {
            var definition = CommandCatalog.Get(command);
            var recorder = new ShortcutRecorder(_settings.Binding(command), command == AppCommand.Peek) { Margin = Padding.Empty, AccessibleName = definition.Label };
            recorder.StatusChanged += message => { _status.Text = message ?? ""; _status.ForeColor = Theme.Muted; };
            _recorders.Add(command, recorder);
            var controls = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
            controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            controls.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); controls.Controls.Add(recorder, 0, 0);
            var clear = Theme.Button("×", (_, _) => recorder.Binding = new(), 28, 28);
            ((ShadeButton)clear).Ghost = true;
            clear.Dock = DockStyle.Fill; clear.Margin = new Padding(8, 0, 0, 0); clear.AccessibleName = $"Unassign {definition.Label}";
            controls.Controls.Add(clear, 1, 0);
            AddField(table, definition.Label, null, controls, 40);
        }
    }

    private static TableLayoutPanel FieldTable(ShadeScrollPanel page)
    {
        var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 0, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        page.SetContent(table); return table;
    }

    private static void AddField(TableLayoutPanel table, string title, string? description, Control control, int height = 48)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var labels = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = description is null ? 1 : 2, ColumnCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 4, 12, 4) };
        labels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        labels.RowStyles.Add(new RowStyle(SizeType.Percent, description is null ? 100 : 50));
        labels.Controls.Add(new Label { Text = title, ForeColor = Theme.Text, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        if (description is not null)
        {
            labels.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            labels.Controls.Add(new Label { Text = description, ForeColor = Theme.Muted, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = new Font("Segoe UI", 8.5f), TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        }
        control.Margin = new Padding(8, (height - 28) / 2, 0, (height - 28) / 2); control.Dock = DockStyle.Fill; control.AccessibleName ??= title;
        table.Controls.Add(labels, 0, row); table.Controls.Add(control, 1, row);
    }

    internal void SelectPage(string name)
    {
        if (_currentPage == name) return;
        foreach (var dropdown in new[] { _monitor, _zoom, _size, _fps })
            if (dropdown.Expanded) dropdown.CloseChoices();
        // Parent visibility propagates to every row. Their geometry is unchanged;
        // batch those notifications instead of re-laying out each table in turn.
        if (_currentPage is { } previous) SetPageVisible(previous, false);
        SetPageVisible(name, true);
        foreach (var (key, button) in _tabs) Theme.SetActive(button, key == name);
        _pages[name].BringToFront();
        _currentPage = name;
        _status.Text = name == "Shortcuts" ? "Click a binding, then press and release your keys." : "";
        _status.ForeColor = Theme.Muted;
    }

    private void SetPageVisible(string name, bool visible)
    {
        var layouts = _pageLayouts[name];
        foreach (var table in layouts) table.SuspendLayout();
        try { _pages[name].Visible = visible; }
        finally { foreach (var table in layouts.Reverse()) table.ResumeLayout(false); }
        if (visible) _pages[name].CompleteContentLayout();
    }

    internal static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private void Save(object? sender, EventArgs args)
    {
        if (_recorders.Values.Any(x => x.Recording)) { _status.Text = "Finish or cancel recording first."; return; }
        var bindings = _recorders.ToDictionary(x => x.Key, x => x.Value.Binding);
        string? error = CommandCatalog.Validate(bindings) ?? ValidateBindings?.Invoke(bindings);
        if (error is not null) { SelectPage("Shortcuts"); _status.Text = error; _status.ForeColor = Theme.Pink; return; }
        try { StartupRegistration.Set(_startup.Checked); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { _status.Text = $"Could not update startup: {e.Message}"; _status.ForeColor = Theme.Pink; return; }
        _settings.MonitorDevice = _screens[_monitor.SelectedIndex].DeviceName;
        _settings.Zoom = AppSettings.ZoomLevels[_zoom.SelectedIndex]; _settings.Size = AppSettings.Sizes[_size.SelectedIndex];
        _settings.FramesPerSecond = AppSettings.FrameRates[_fps.SelectedIndex]; _settings.ShowGrid = _grid.Checked;
        _settings.Enabled = _enabled.Checked; _settings.StartWithWindows = _startup.Checked; _settings.GridOpacity = _opacity.Value;
        _settings.ScreenshotFolder = _folder; _settings.CopyCapturesToClipboard = _clipboard.Checked;
        _settings.Bindings = bindings; _settings.Save(); DialogResult = DialogResult.OK; Close();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Create the controls once while the dialog is opening. The first visit
        // to Shortcuts should not have to create a window for every recorder.
        foreach (var page in _pages.Values)
        {
            page.Bounds = page.Parent!.ClientRectangle;
            page.CompleteContentLayout();
            _ = page.Handle;
            foreach (var control in Descendants(page)) _ = control.Handle;
        }
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); NativeMethods.ExcludeFromCapture(Handle); Theme.WindowChrome(Handle); }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var work = Screen.FromControl(this).WorkingArea;
        Size = new Size(Math.Min(Width, work.Width - 32), Math.Min(Height, work.Height - 32));
        Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
        Theme.WindowChrome(Handle);
    }
}

internal static class StartupRegistration
{
    public static void Set(bool enabled)
    {
        using var registry = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) registry.SetValue("Loupe", $"\"{Environment.ProcessPath}\"");
        else registry.DeleteValue("Loupe", false);
    }
}
