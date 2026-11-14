using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace Loupe;

internal sealed class DesktopCapture : IDisposable
{
    public ID3D11Device Device { get; private set; } = null!;
    public ID3D11DeviceContext Context { get; private set; } = null!;
    public IDXGIFactory2 Factory { get; private set; } = null!;
    public ID3D11Texture2D? Texture => _texture;
    public Rectangle Bounds { get; private set; }
    private WindowsCapture? _source;
    private ID3D11Texture2D? _texture;

    public DesktopCapture(string monitorDevice, int fps = 60)
    {
        try
        {
            Factory = CreateDXGIFactory2<IDXGIFactory2>(false);
            for (uint adapterIndex = 0; Factory.EnumAdapters1(adapterIndex, out var adapter).Success; adapterIndex++)
            {
                using (adapter)
                {
                    for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Success; outputIndex++)
                    {
                        using (output)
                        {
                            var description = output.Description;
                            if (!string.Equals(description.DeviceName, monitorDevice, StringComparison.OrdinalIgnoreCase)) continue;
                            D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out var device, out ID3D11DeviceContext context).CheckError();
                            Device = device;
                            Context = context;
                            var rect = description.DesktopCoordinates;
                            Bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                            _source = new WindowsCapture(Device, Bounds, fps);
                            return;
                        }
                    }
                }
            }
            throw new InvalidOperationException("The selected monitor is no longer available. Choose a monitor in Settings.");
        }
        catch { Dispose(); throw; }
    }

    // A nonblocking acquire: only the visible loupe calls this, at the selected refresh cap.
    // Frames stay on the GPU; there is no screen-sized CPU readback in normal operation.
    public bool Update()
    {
        return _source!.CopyLatest(Context, Device, ref _texture);
    }

    public Bitmap CaptureArea(Rectangle desktopArea)
    {
        Update();
        if (Texture is null) throw new InvalidOperationException("Waiting for the first desktop frame. Try capturing again in a moment.");
        if (!Bounds.Contains(desktopArea)) throw new ArgumentOutOfRangeException(nameof(desktopArea));
        // Windows Graphics Capture already supplies upright monitor pixels.
        Rectangle localArea = desktopArea;
        localArea.Offset(-Bounds.Left, -Bounds.Top);
        return GpuReadback.Read(Device, Context, Texture, localArea);
    }

    public void Dispose()
    {
        _source?.Dispose();
        Texture?.Dispose();
        Context?.ClearState();
        Context?.Flush();
        Context?.Dispose();
        Device?.Dispose();
        Factory?.Dispose();
    }
}
