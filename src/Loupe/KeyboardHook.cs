using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Loupe;

internal sealed class KeyboardHook : IDisposable
{
    private readonly HookProcedure _procedure;
    private readonly nint _hook;
    public KeyboardState State { get; } = new();
    private readonly HashSet<Keys> _suppressed = [];
    public Func<Keys, bool, bool>? KeyChanged { get; set; }

    public KeyboardHook()
    {
        _procedure = Process;
        foreach (Keys key in new[] { Keys.LControlKey, Keys.RControlKey, Keys.LMenu, Keys.RMenu, Keys.LShiftKey, Keys.RShiftKey, Keys.LWin, Keys.RWin })
            if ((NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0) State.Set(key, true);
        _hook = SetWindowsHookExW(13, _procedure, GetModuleHandleW(null), 0);
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Loupe could not listen for the peek shortcut.");
    }

    private nint Process(int code, nint message, nint dataPointer)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KeyboardData>(dataPointer);
            var key = (Keys)data.Key;
            bool down = message == 0x100 || message == 0x104;
            bool up = message == 0x101 || message == 0x105;
            if (down || up)
            {
                bool changed = State.Set(key, down);
                bool suppress = _suppressed.Contains(key);
                if (changed && KeyChanged?.Invoke(key, down) == true) suppress = true;
                if (down && suppress) _suppressed.Add(key);
                if (up) _suppressed.Remove(key);
                if (suppress) return 1;
            }
        }
        return CallNextHookEx(_hook, code, message, dataPointer);
    }

    public void Dispose() => UnhookWindowsHookEx(_hook);

    private delegate nint HookProcedure(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData { public uint Key, ScanCode, Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int type, HookProcedure procedure, nint module, uint thread);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly HashSet<int> _registered = [];
    public event Action<int>? Pressed;
    public event Action? DisplaysChanged;
    public event Action? CloseRequested;

    public HotkeyWindow() => CreateHandle(new CreateParams { Caption = "Loupe hotkeys", ExStyle = NativeMethods.WsExToolWindow });

    public bool Register(int id, Keys key, uint modifiers = 0)
    {
        Unregister(id);
        bool success = NativeMethods.RegisterHotKey(Handle, id, modifiers | NativeMethods.ModNoRepeat, (uint)key);
        if (success) _registered.Add(id);
        return success;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) NativeMethods.UnregisterHotKey(Handle, id);
    }

    public bool Register(int id, KeyBinding binding) => !binding.IsEmpty && Register(id, binding.Key, (uint)binding.Modifiers);

    public void UnregisterAll()
    {
        foreach (int id in _registered.ToArray()) Unregister(id);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey) Pressed?.Invoke((int)message.WParam);
        else if (message.Msg == NativeMethods.WmDisplayChange) DisplaysChanged?.Invoke();
        else if (message.Msg == 0x10) { CloseRequested?.Invoke(); return; }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        foreach (int id in _registered) NativeMethods.UnregisterHotKey(Handle, id);
        _registered.Clear();
        DestroyHandle();
    }
}
