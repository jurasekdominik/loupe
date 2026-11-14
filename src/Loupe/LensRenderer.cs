using System.Drawing.Imaging;
using System.Numerics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;
using static Vortice.DirectComposition.DComp;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using Color4 = Vortice.Mathematics.Color4;
using Size = System.Drawing.Size;
using PixelFormat = Vortice.DCommon.PixelFormat;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;

namespace Loupe;

internal sealed class LensRenderer : IDisposable
{
    private readonly DesktopCapture _capture;
    private ID2D1Factory1 _factory = null!;
    private ID2D1Device _device = null!;
    private ID2D1DeviceContext _context = null!;
    private IDCompositionDevice _composition = null!;
    private IDCompositionTarget _compositionTarget = null!;
    private IDCompositionVisual _visual = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID2D1Bitmap1? _target;
    private ID2D1Bitmap1? _desktop;
    private ID2D1SolidColorBrush _brush = null!;
    private Size _size;
    public bool HasPresentedFrame { get; private set; }

    private static readonly Color4 Sky = new(5 / 255f, 147 / 255f, 198 / 255f, 1);
    private static readonly Color4 Pink = new(216 / 255f, 57 / 255f, 149 / 255f, 1);
    private static readonly Color4 Shadow = new(0, 0, 0, .85f);
    private static readonly Color4 Background = new(Theme.Background.R / 255f, Theme.Background.G / 255f, Theme.Background.B / 255f, 1);

    public LensRenderer(nint window, Size size, DesktopCapture capture)
    {
        _capture = capture;
        try
        {
            using var dxgiDevice = capture.Device.QueryInterface<IDXGIDevice>();
            _factory = D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            _device = _factory.CreateDevice(dxgiDevice);
            _context = _device.CreateDeviceContext(DeviceContextOptions.None);
            _context.SetDpi(96, 96); // D2D units are deliberately physical pixels, even on a scaled monitor.
            _context.AntialiasMode = AntialiasMode.Aliased;
            _brush = _context.CreateSolidColorBrush(Sky);
            _composition = DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
            _composition.CreateTargetForHwnd(window, true, out _compositionTarget).CheckError();
            _visual = _composition.CreateVisual();

            var description = new SwapChainDescription1
            {
                Width = (uint)size.Width, Height = (uint)size.Height,
                Format = Format.B8G8R8A8_UNorm, BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput, SampleDescription = new(1, 0),
                Scaling = Scaling.Stretch, SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied
            };
            _swapChain = capture.Factory.CreateSwapChainForComposition(capture.Device, description);
            _visual.SetContent(_swapChain).CheckError();
            _compositionTarget.SetRoot(_visual).CheckError();
            _composition.Commit().CheckError();
            _size = size;
            CreateTarget();
        }
        catch { Dispose(); throw; }
    }

    private void CreateTarget()
    {
        using var surface = _swapChain.GetBuffer<IDXGISurface>(0);
        _target = _context.CreateBitmapFromDxgiSurface(surface,
            new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Premultiplied), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
        _context.Target = _target;
    }

    public void Resize(Size size)
    {
        if (size == _size) return;
        _context.Target = null;
        _target?.Dispose();
        _swapChain.ResizeBuffers(2, (uint)size.Width, (uint)size.Height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
        _size = size;
        CreateTarget();
    }

    public bool Render(InspectionViewport viewport, AppSettings settings, Point? cursor,
        IReadOnlyList<PixelGuide> guides, PixelMeasurement? measurement, bool measuring, string? savePath = null, bool force = true, Action<Bitmap>? receiveCapture = null)
    {
        bool updated = _capture.Update();
        if (_capture.Texture is null) return false;
        if (!updated && !force && savePath is null && receiveCapture is null) return HasPresentedFrame;
        if (_desktop is null && _capture.Texture is not null)
        {
            using var surface = _capture.Texture.QueryInterface<IDXGISurface>();
            _desktop = _context.CreateBitmapFromDxgiSurface(surface,
                new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, D2DAlphaMode.Ignore), 96, 96, BitmapOptions.None));
        }

        _context.BeginDraw();
        _context.Transform = Matrix3x2.Identity;
        _context.Clear(Background);
        var destination = viewport.Destination;
        _context.PushAxisAlignedClip(new RectangleF(destination.X, destination.Y, destination.Width, destination.Height), AntialiasMode.Aliased);
        if (_desktop is not null)
        {
            _context.Transform = viewport.DesktopToLens(_capture.Bounds.Location);
            _context.DrawBitmap(_desktop, 1f, BitmapInterpolationMode.NearestNeighbor);
            _context.Transform = Matrix3x2.Identity;
        }

        if (settings.ShowGrid && viewport.Zoom >= 4)
        {
            _brush.Color = new Color4(0, 0, 0, settings.GridOpacity / 100f);
            for (int x = destination.Left; x <= destination.Right; x += viewport.Zoom)
                Line(x, destination.Top, x, destination.Bottom);
            for (int y = destination.Top; y <= destination.Bottom; y += viewport.Zoom)
                Line(destination.Left, y, destination.Right, y);
        }

        foreach (var guide in guides)
        {
            PointF p = viewport.ToLens(guide.Vertical ? new Point(guide.Coordinate, viewport.Source.Top) : new Point(viewport.Source.Left, guide.Coordinate));
            GuideLine(guide.Vertical, guide.Vertical ? p.X : p.Y, destination, Pink);
        }

        if (cursor is { } position)
        {
            var p = viewport.ToLens(position);
            GuideLine(true, p.X, destination, measuring ? Sky : new Color4(1, 1, 1, .65f));
            GuideLine(false, p.Y, destination, measuring ? Sky : new Color4(1, 1, 1, .65f));
        }

        if (measurement is { } ruler)
        {
            var start = viewport.ToLens(ruler.Start);
            var end = viewport.ToLens(ruler.End);
            var rect = new Rect(Math.Min(start.X, end.X) + .5f, Math.Min(start.Y, end.Y) + .5f, Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y));
            _brush.Color = Shadow;
            _context.DrawRectangle(rect, _brush, 3);
            _brush.Color = Pink;
            _context.DrawRectangle(rect, _brush, 1);
            Line(start.X - 5, start.Y, start.X + 5, start.Y);
            Line(start.X, start.Y - 5, start.X, start.Y + 5);
            Line(end.X - 5, end.Y, end.X + 5, end.Y);
            Line(end.X, end.Y - 5, end.X, end.Y + 5);
        }
        _context.PopAxisAlignedClip();
        _brush.Color = measuring ? Pink : Sky;
        _context.DrawRectangle(new Rect(1, 1, _size.Width - 2, _size.Height - 2), _brush, 2);
        _context.EndDraw().CheckError();
        if (savePath is not null || receiveCapture is not null)
        {
            using var bitmap = ReadBackBuffer();
            if (savePath is not null) bitmap.Save(savePath, ImageFormat.Png);
            receiveCapture?.Invoke(bitmap);
        }
        _swapChain.Present(0, PresentFlags.None).CheckError();
        HasPresentedFrame = true;
        return true;
    }

    public void CompleteFirstPresentation()
    {
        _composition.Commit().CheckError();
        _composition.WaitForCommitCompletion().CheckError();
    }

    private void Line(float x1, float y1, float x2, float y2, float width = 1) =>
        _context.DrawLine(new Vector2(x1 + .5f, y1 + .5f), new Vector2(x2 + .5f, y2 + .5f), _brush, width);

    private void GuideLine(bool vertical, float position, Rectangle bounds, Color4 color)
    {
        float x1 = vertical ? position : bounds.Left, x2 = vertical ? position : bounds.Right;
        float y1 = vertical ? bounds.Top : position, y2 = vertical ? bounds.Bottom : position;
        _brush.Color = Shadow; Line(x1, y1, x2, y2, 3);
        _brush.Color = color; Line(x1, y1, x2, y2);
    }

    public Bitmap CaptureView(InspectionViewport viewport, AppSettings settings, Point? cursor,
        IReadOnlyList<PixelGuide> guides, PixelMeasurement? measurement, bool measuring)
    {
        Bitmap? result = null;
        Render(viewport, settings, cursor, guides, measurement, measuring, receiveCapture: bitmap => result = new Bitmap(bitmap));
        return result ?? throw new InvalidOperationException("The magnified view is not available yet.");
    }

    private Bitmap ReadBackBuffer()
    {
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        return GpuReadback.Read(_capture.Device, _capture.Context, backBuffer, new Rectangle(Point.Empty, _size));
    }

    public void Dispose()
    {
        if (_context is not null) _context.Target = null;
        _desktop?.Dispose(); _target?.Dispose(); _brush?.Dispose();
        _visual?.Dispose(); _compositionTarget?.Dispose(); _composition?.Dispose();
        _swapChain?.Dispose(); _context?.Dispose(); _device?.Dispose(); _factory?.Dispose();
    }
}
