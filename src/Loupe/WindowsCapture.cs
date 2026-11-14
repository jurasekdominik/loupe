using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Loupe;

// Small native WinRT bridge. No projection package or worker callback is needed:
// the UI's existing refresh timer drains the free-threaded GPU frame pool.
// ABI: Windows.Graphics.Capture and windows.graphics.capture.interop.h.
internal sealed unsafe partial class WindowsCapture : IDisposable
{
    private nint _device, _item, _pool, _session, _frame;
    private bool _initialized;
    private readonly SizeInt32 _size;
    private static readonly Guid Closable = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");
    private static readonly Guid SurfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    public WindowsCapture(ID3D11Device device, Rectangle monitor, int fps)
    {
        _size = new(monitor.Width, monitor.Height);
        try
        {
            Check(RoInitialize(0)); _initialized = true;
            using var dxgi = device.QueryInterface<IDXGIDevice>();
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out nint inspectable));
            try { _device = Query(inspectable, new Guid("A37624AB-8D5F-4650-9D3E-9EAE3D9BC670")); }
            finally { Release(ref inspectable); }

            nint factory = Factory("Windows.Graphics.Capture.GraphicsCaptureItem", new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"));
            try
            {
                Guid itemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                var rect = new NativeRect(monitor.Left, monitor.Top, monitor.Right, monitor.Bottom);
                nint hmonitor = MonitorFromRect(ref rect, 2);
                nint item;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)Method(factory, 4))(factory, hmonitor, &itemId, &item));
                _item = item;
            }
            finally { Release(ref factory); }

            factory = Factory("Windows.Graphics.Capture.Direct3D11CaptureFramePool", new Guid("589B103F-6BBC-5DF5-A991-02E28B3B66D5"));
            try
            {
                nint pool;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, SizeInt32, nint*, int>)Method(factory, 6))(factory, _device, 87, 2, _size, &pool));
                _pool = pool;
            }
            finally { Release(ref factory); }

            nint session;
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Method(_pool, 10))(_pool, _item, &session));
            _session = session;
            nint options = Query(_session, new Guid("2C39AE40-7D2E-5044-804E-8B6799D4CF9E"));
            try
            {
                // Disable the actual pointer in the captured pixels. This does
                // not hide or change the user's pointer in the game/desktop.
                Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)Method(options, 7))(options, 0));
                byte included;
                Check(((delegate* unmanaged[Stdcall]<nint, byte*, int>)Method(options, 6))(options, &included));
                if (included != 0) throw new InvalidOperationException("Windows did not disable cursor capture.");
            }
            finally { Release(ref options); }

            options = TryQuery(_session, new Guid("F2CDD966-22AE-5EA1-9596-3A289344C3BE"));
            if (options != 0)
            {
                try { Check(((delegate* unmanaged[Stdcall]<nint, byte, int>)Method(options, 7))(options, 0)); }
                finally { Release(ref options); }
            }
            options = TryQuery(_session, new Guid("67C0EA62-1F85-5061-925A-239BE0AC09CB"));
            if (options != 0)
            {
                try { Check(((delegate* unmanaged[Stdcall]<nint, long, int>)Method(options, 7))(options, TimeSpan.TicksPerSecond / fps)); }
                finally { Release(ref options); }
            }
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Method(_session, 6))(_session));
        }
        catch { Dispose(); throw; }
    }

    // Hands out the newest frame's own texture and keeps that frame open until the
    // next one arrives, so the monitor is never copied. The pool's second buffer
    // receives new frames meanwhile.
    public bool AcquireLatest(ref ID3D11Texture2D? texture)
    {
        nint latest = 0;
        try
        {
            // Drain at most the two queued frames, retaining the newest one.
            for (int i = 0; i < 2; i++)
            {
                nint frame;
                Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Method(_pool, 7))(_pool, &frame));
                if (frame == 0) break;
                Close(ref latest); latest = frame;
            }
            if (latest == 0) return false;
            SizeInt32 size;
            Check(((delegate* unmanaged[Stdcall]<nint, SizeInt32*, int>)Method(latest, 8))(latest, &size));
            if (size != _size) throw new InvalidOperationException("The captured monitor size changed. Reopen Loupe to reconnect.");
            nint surface;
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Method(latest, 6))(latest, &surface));
            try
            {
                nint access = Query(surface, SurfaceAccess);
                try
                {
                    Guid textureId = typeof(ID3D11Texture2D).GUID;
                    nint pointer;
                    Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Method(access, 3))(access, &textureId, &pointer));
                    texture?.Dispose();
                    texture = new ID3D11Texture2D(pointer);
                }
                finally { Release(ref access); }
            }
            finally { Release(ref surface); }
            Close(ref _frame); _frame = latest; latest = 0;
            return true;
        }
        finally { Close(ref latest); }
    }

    private static nint Method(nint obj, int slot) => (*(nint**)obj)[slot];
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    private static nint Query(nint obj, Guid id) { Check(Marshal.QueryInterface(obj, in id, out nint value)); return value; }
    private static nint TryQuery(nint obj, Guid id) => Marshal.QueryInterface(obj, in id, out nint value) >= 0 ? value : 0;
    private static void Release(ref nint obj) { if (obj != 0) { Marshal.Release(obj); obj = 0; } }
    private static void Close(ref nint obj)
    {
        if (obj == 0) return;
        nint closable = TryQuery(obj, Closable);
        try { if (closable != 0) _ = ((delegate* unmanaged[Stdcall]<nint, int>)Method(closable, 6))(closable); }
        finally { Release(ref closable); Release(ref obj); }
    }
    private static nint Factory(string name, Guid id)
    {
        Check(WindowsCreateString(name, name.Length, out nint text));
        try { Check(RoGetActivationFactory(text, ref id, out nint factory)); return factory; }
        finally { WindowsDeleteString(text); }
    }
    public void Dispose()
    {
        Close(ref _frame); Close(ref _session); Close(ref _pool); Release(ref _item); Release(ref _device);
        if (_initialized) { RoUninitialize(); _initialized = false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct SizeInt32(int Width, int Height);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);
    [LibraryImport("combase.dll")] private static partial int RoInitialize(uint type);
    [LibraryImport("combase.dll")] private static partial void RoUninitialize();
    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int WindowsCreateString(string text, int length, out nint value);
    [LibraryImport("combase.dll")] private static partial int WindowsDeleteString(nint value);
    [LibraryImport("combase.dll")] private static partial int RoGetActivationFactory(nint name, ref Guid id, out nint factory);
    [LibraryImport("d3d11.dll")] private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint inspectable);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromRect(ref NativeRect rect, uint flags);
}
