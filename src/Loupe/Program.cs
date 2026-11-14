namespace Loupe;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            MessageBox.Show("Loupe requires Windows 10 version 2004 or newer, or Windows 11.", "Loupe");
            return;
        }
        if (args.Contains("--smoke-test"))
        {
            SmokeTest.Run(args);
            return;
        }
        if (args.Contains("--ui-test"))
        {
            UiDiagnostics.Run(args);
            return;
        }
        using var instance = new Mutex(true, "Local\\Loupe.PixelInspector", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Loupe is already running. Look for the blue lens in your system tray.", "Loupe");
            return;
        }

        Application.ThreadException += (_, e) => ReportError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write(e.ExceptionObject.ToString() ?? "Unexpected error");

        using var context = new TrayApplicationContext();
        Application.Run(context);
    }

    private static void ReportError(Exception exception)
    {
        AppLog.Write(exception.ToString());
        MessageBox.Show($"Loupe encountered an error.\n\n{exception.Message}\n\nDetails: {AppLog.LogPath}", "Loupe", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

internal static class AppLog
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loupe");
    public static string LogPath => Path.Combine(DirectoryPath, "loupe.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                File.Move(LogPath, LogPath + ".old", true);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
