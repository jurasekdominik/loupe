using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Loupe;

internal static partial class NativeMethods
{
    public const int WmHotkey = 0x0312, WmDisplayChange = 0x007E, WmMouseActivate = 0x0021;
    public const int WsExToolWindow = 0x80, WsExNoActivate = 0x08000000, WsExLayered = 0x80000, WsExTransparent = 0x20;
    public const int GwlExStyle = -20;
    public const uint ModAlt = 1, ModControl = 2, ModNoRepeat = 0x4000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint window, int id);
    [LibraryImport("user32.dll")]
    public static partial int GetWindowLongW(nint window, int index);
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int SetWindowLongW(nint window, int index, int value);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(nint window, uint affinity);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorInfo(ref CursorInfo info);
    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int key);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();
    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint window, uint command);
    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PrintWindow(nint window, nint deviceContext, uint flags);
    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public uint Size, Flags; public nint Cursor; public NativePoint Position; }
    public static bool CursorVisible
    {
        get { var info = new CursorInfo { Size = (uint)Marshal.SizeOf<CursorInfo>() }; return GetCursorInfo(ref info) && (info.Flags & 1) != 0; }
    }

    public static Point CursorPosition
    {
        get { GetCursorPos(out var point); return new Point(point.X, point.Y); }
    }

    public static void ExcludeFromCapture(nint window)
    {
        // Only the separate --ui-test process permits printing its own windows.
        // GPU smoke checks still exercise normal capture exclusion.
        if (UiDiagnostics.Running) return;
        if (!SetWindowDisplayAffinity(window, 0x11))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not exclude the loupe from capture. Loupe requires Windows 10 version 2004 or newer.");
    }

    public static void SetClickThrough(nint window, bool enabled)
    {
        int style = GetWindowLongW(window, GwlExStyle);
        SetWindowLongW(window, GwlExStyle, enabled ? style | WsExTransparent : style & ~WsExTransparent);
    }
}
