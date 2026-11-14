using System.Text.Json;
using System.Text.Json.Serialization;

namespace Loupe;

public sealed class AppSettings
{
    public int Version { get; set; } = 3;
    public bool Enabled { get; set; } = true;
    public string? MonitorDevice { get; set; }
    public int Zoom { get; set; } = 8;
    public int Size { get; set; } = 448;
    public int FramesPerSecond { get; set; } = 60;
    public bool ShowGrid { get; set; } = true;
    public int GridOpacity { get; set; } = 28;
    public bool StartWithWindows { get; set; }
    public bool HasSeenWelcome { get; set; }
    public bool CopyCapturesToClipboard { get; set; } = true;
    public string ScreenshotFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Loupe");
    public Dictionary<AppCommand, KeyBinding> Bindings { get; set; } = CommandCatalog.Defaults();

    public static int[] ZoomLevels { get; } = [2, 4, 6, 8, 12, 16, 24, 32];
    public static int[] FrameRates { get; } = [30, 60]; // shortcut: the WM_TIMER loop tops out near 64 Hz, switch to a waitable timer if 120 is wanted
    public static int[] Sizes { get; } = [320, 448, 576, 704];
    public static string SettingsPath => Path.Combine(AppLog.DirectoryPath, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public KeyBinding Binding(AppCommand command) => Bindings.GetValueOrDefault(command) ?? new();

    public void Validate()
    {
        Version = 3;
        if (!ZoomLevels.Contains(Zoom)) Zoom = 8;
        if (!Sizes.Contains(Size)) Size = 448;
        if (!FrameRates.Contains(FramesPerSecond)) FramesPerSecond = 60;
        GridOpacity = Math.Clamp(GridOpacity, 5, 70);
        if (string.IsNullOrWhiteSpace(ScreenshotFolder)) ScreenshotFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Loupe");
        Bindings ??= [];
        foreach (var command in Bindings.Keys.Where(x => !Enum.IsDefined(x)).ToArray()) Bindings.Remove(command);
        foreach (var definition in CommandCatalog.All)
            if (!Bindings.TryGetValue(definition.Command, out var binding) || binding is null || binding.Error(definition.Command == AppCommand.Peek) is not null)
                Bindings[definition.Command] = CommandCatalog.DefaultBinding(definition.Command);
    }

    public static AppSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        try
        {
            string json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("Bindings", out _) &&
                (!document.RootElement.TryGetProperty("Version", out var version) || !version.TryGetInt32(out int numberVersion) || numberVersion < 3))
                CommandCatalog.MigrateOptionalDefaults(settings.Bindings ??= []);
            if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty("Bindings", out _))
            {
                if (document.RootElement.TryGetProperty("ToggleKey", out var key) && key.ValueKind == JsonValueKind.Number && key.TryGetInt32(out int number))
                {
                    settings.Bindings[AppCommand.ToggleLoupe] = new((Keys)number);
                    settings.Bindings[AppCommand.ToggleEnabled] = new((Keys)number, KeyModifiers.Control | KeyModifiers.Alt);
                }
                if (document.RootElement.TryGetProperty("PeekShortcut", out var peek) && peek.ValueKind == JsonValueKind.Number && peek.TryGetInt32(out int shortcut))
                    settings.Bindings[AppCommand.Peek] = shortcut switch
                    {
                        1 => new(Keys.Space, KeyModifiers.Control | KeyModifiers.Shift),
                        2 => new(Keys.None, KeyModifiers.Control | KeyModifiers.Alt),
                        3 => new(Keys.RControlKey), 4 => new(), _ => settings.Binding(AppCommand.Peek)
                    };
            }
            settings.Validate();
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            if (File.Exists(path)) AppLog.Write($"Settings could not be loaded: {e.Message}");
            return new();
        }
    }

    public void Save(string? path = null)
    {
        path ??= SettingsPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"Settings could not be saved: {e.Message}");
        }
    }
}
