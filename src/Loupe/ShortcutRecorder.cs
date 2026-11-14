namespace Loupe;

internal sealed record RecordedShortcut(KeyBinding? Binding = null, bool Canceled = false, string? Error = null);

internal sealed class RecordingSession(bool allowModifierOnly)
{
    private readonly KeyboardState _state = new();
    private readonly HashSet<Keys> _pressed = [];
    private KeyBinding? _candidate;
    private KeyModifiers _peak;
    private bool _canceled;

    public RecordedShortcut? Process(Keys key, bool down)
    {
        _state.Set(key, down);
        if (down)
        {
            _pressed.Add(key);
            _peak |= _state.Modifiers;
            if (key == Keys.Escape) _canceled = true;
            else if (!KeyBinding.IsModifier(key)) _candidate = new KeyBinding(key, _state.Modifiers);
        }
        else if (!_pressed.Remove(key)) return null; // Ignore the release of the key used to focus/click Record.
        if (down || _pressed.Count != 0) return null;
        if (_canceled) return new(Canceled: true);
        KeyBinding binding = _candidate ?? new(Keys.None, _peak);
        string? error = binding.Error(allowModifierOnly);
        return new(error is null ? binding : null, Error: error);
    }
}

internal sealed class ShortcutRecorder : ShadeButton
{
    private readonly bool _allowModifiers;
    private KeyBinding _binding;
    private KeyboardHook? _hook;
    private RecordingSession? _session;
    private bool _recording;
    public event Action<string?>? StatusChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public KeyBinding Binding { get => _binding; set { Stop(); _binding = value; Text = value.ToString(); } }
    public bool Recording => _recording;

    public ShortcutRecorder(KeyBinding binding, bool allowModifiers = false)
    {
        _binding = binding; _allowModifiers = allowModifiers;
        Text = binding.ToString(); Height = 32; Dock = DockStyle.Fill;
        AccessibleName = "Record shortcut";
        Click += (_, _) => { if (_recording) Stop(); else Start(); };
    }

    private void Start()
    {
        Focus(); _recording = true; Active = true; AccentPink = true;
        Text = "Press keys…";
        _session = new RecordingSession(_allowModifiers);
        StatusChanged?.Invoke(_allowModifiers ? "Press and release a shortcut or modifiers. Esc cancels." : "Press and release your shortcut. Esc cancels.");
        try
        {
            _hook = new KeyboardHook();
            _hook.KeyChanged = (key, down) =>
            {
                RecordedShortcut? result = _session?.Process(key, down);
                if (result is not null) BeginInvoke(() => Complete(result));
                return true;
            };
        }
        catch { Stop(); throw; }
    }

    private void Complete(RecordedShortcut result)
    {
        if (!_recording || IsDisposed) return;
        if (result.Error is not null)
        {
            _session = new RecordingSession(_allowModifiers);
            Text = "Try another shortcut";
            StatusChanged?.Invoke(result.Error);
            return;
        }
        if (result.Binding is not null) _binding = result.Binding;
        Stop();
    }

    private void Stop()
    {
        _recording = false; _session = null;
        _hook?.Dispose(); _hook = null;
        Active = false; AccentPink = false; Text = _binding.ToString();
        StatusChanged?.Invoke(null);
    }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (_recording) Stop(); }
    protected override void Dispose(bool disposing) { if (disposing) Stop(); base.Dispose(disposing); }
}
