namespace Loupe;

internal static class AppIcon
{
    public static Icon Create(Size? size = null)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Loupe.AppIcon.ico")
            ?? throw new InvalidOperationException("The Loupe icon resource is missing.");
        using var icon = new Icon(stream, size ?? SystemInformation.IconSize);
        return (Icon)icon.Clone();
    }
}
