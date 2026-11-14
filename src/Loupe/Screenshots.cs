using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.Mathematics;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace Loupe;

internal static class GpuReadback
{
    // Readback is used only for an explicit capture or diagnostic, never the render loop.
    public static unsafe Bitmap Read(ID3D11Device device, ID3D11DeviceContext context, ID3D11Texture2D texture, Rectangle area)
    {
        var description = texture.Description;
        description.Width = (uint)area.Width; description.Height = (uint)area.Height;
        description.Usage = ResourceUsage.Staging; description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read; description.MiscFlags = ResourceOptionFlags.None;
        using var staging = device.CreateTexture2D(description);
        var box = new Box(area.Left, area.Top, 0, area.Right, area.Bottom, 1);
        context.CopySubresourceRegion(staging, 0, 0, 0, 0, texture, 0, box);
        var mapped = context.Map(staging, 0, MapMode.Read, MapFlags.None);
        try
        {
            var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            try
            {
                var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, bitmap.PixelFormat);
                try
                {
                    for (int y = 0; y < area.Height; y++)
                        Buffer.MemoryCopy((byte*)mapped.DataPointer + y * mapped.RowPitch, (byte*)data.Scan0 + y * data.Stride, data.Stride, area.Width * 4);
                }
                finally { bitmap.UnlockBits(data); }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
        finally { context.Unmap(staging, 0); }
    }

}

internal sealed record CaptureResult(string Path, System.Drawing.Size Size, bool CopiedToClipboard);

internal static class Screenshots
{
    public static CaptureResult Save(Bitmap bitmap, string folder, string kind, bool copy)
    {
        Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, $"Loupe_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{kind}_{Guid.NewGuid().ToString("N")[..6]}.png");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) bitmap.Save(file, ImageFormat.Png);
        bool copied = false;
        if (copy)
        {
            try { Clipboard.SetImage(bitmap); copied = true; }
            catch (ExternalException exception) { AppLog.Write($"Capture saved, clipboard unavailable: {exception.Message}"); }
        }
        return new(path, bitmap.Size, copied);
    }
}
