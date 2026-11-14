using System.Numerics;

namespace Loupe;

// All coordinates here are physical desktop pixels. Only the surrounding UI uses DIPs.
public readonly record struct InspectionViewport(Rectangle Source, Rectangle Destination, int Zoom)
{
    public static InspectionViewport Create(Rectangle monitor, Point center, Size canvas, int zoom)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0 || zoom < 1) throw new ArgumentOutOfRangeException(nameof(zoom));
        int width = Math.Clamp((canvas.Width - 8) / zoom, 1, monitor.Width);
        int height = Math.Clamp((canvas.Height - 8) / zoom, 1, monitor.Height);
        int x = Math.Clamp(center.X - width / 2, monitor.Left, monitor.Right - width);
        int y = Math.Clamp(center.Y - height / 2, monitor.Top, monitor.Bottom - height);
        var destination = new Rectangle((canvas.Width - width * zoom) / 2, (canvas.Height - height * zoom) / 2, width * zoom, height * zoom);
        return new(new Rectangle(x, y, width, height), destination, zoom);
    }

    // Measurements select pixel boundaries, so a three-pixel gap measures 3, not 2 or 4.
    public Point NearestBoundary(Point lensPoint) => new(
        Source.Left + Math.Clamp(RoundBoundary(lensPoint.X - Destination.Left), 0, Source.Width),
        Source.Top + Math.Clamp(RoundBoundary(lensPoint.Y - Destination.Top), 0, Source.Height));

    private int RoundBoundary(int coordinate) => (int)Math.Round(coordinate / (double)Zoom, MidpointRounding.AwayFromZero);

    public PointF ToLens(Point desktopPoint) => new(
        Destination.Left + (desktopPoint.X - Source.Left) * Zoom,
        Destination.Top + (desktopPoint.Y - Source.Top) * Zoom);

    public Matrix3x2 DesktopToLens(Point monitorOrigin) =>
        Matrix3x2.CreateTranslation(monitorOrigin.X - Source.X, monitorOrigin.Y - Source.Y) *
        Matrix3x2.CreateScale(Zoom) * Matrix3x2.CreateTranslation(Destination.Left, Destination.Top);
}

public readonly record struct PixelMeasurement(Point Start, Point End)
{
    public int Width => Math.Abs(End.X - Start.X);
    public int Height => Math.Abs(End.Y - Start.Y);
    public string Label => $"W {Width} px   H {Height} px";
}

public readonly record struct PixelGuide(bool Vertical, int Coordinate);
