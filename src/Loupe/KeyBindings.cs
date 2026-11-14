using System.Text.Json.Serialization;

namespace Loupe;

[Flags]
public enum KeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

public enum AppCommand
{
    ToggleLoupe = 1, ToggleEnabled, Peek, Hide, TogglePin, ToggleMeasure, Clear,
    ToggleGrid, ZoomIn, ZoomOut, GuideHorizontal, GuideVertical, CaptureArea, CaptureView,
    PanLeft, PanRight, PanUp, PanDown, IncreaseSize, DecreaseSize, OpenSettings, OpenCaptureFolder, Exit, GuideCrosshair, RulerPoint
}

public sealed record KeyBinding(Keys Key = Keys.None, KeyModifiers Modifiers = KeyModifiers.None)
{
    [JsonIgnore] public bool IsEmpty => Key == Keys.None && Modifiers == KeyModifiers.None;
    [JsonIgnore] public bool IsModifierOnly => Key == Keys.None && Modifiers != KeyModifiers.None;

    public override string ToString()
    {
        if (IsEmpty) return "Not assigned";
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Windows)) parts.Add("Win");
        if (Key != Keys.None) parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    public static string KeyName(Keys key) => key switch
    {
        Keys.Escape => "Esc", Keys.Back => "Backspace", Keys.Oemplus => "+", Keys.OemMinus => "−",
        Keys.OemOpenBrackets => "[", Keys.OemCloseBrackets => "]", Keys.Oemtilde => "`",
        Keys.Oemcomma => ",", Keys.OemPeriod => ".", Keys.OemQuestion => "/", Keys.OemSemicolon => ";",
        Keys.RControlKey => "Right Ctrl", Keys.LControlKey => "Left Ctrl",
        Keys.RMenu => "Right Alt", Keys.LMenu => "Left Alt",
        >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(),
        _ => key.ToString()
    };

    public static bool IsModifier(Keys key) => key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;

    public string? Error(bool allowModifierOnly = false)
    {
        if (IsEmpty) return null;
        if ((Modifiers & ~(KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Windows)) != 0) return "Unknown modifier in this shortcut.";
        if (Key == Keys.None) return allowModifierOnly ? null : "Include a regular key as well as modifiers.";
        if ((int)Key < 8 || (int)Key > 254 || !Enum.IsDefined(Key)) return "That key is not supported.";
        if (IsModifier(Key) && !allowModifierOnly) return "Modifier-only shortcuts are available for hold to peek.";
        if (Key == Keys.F12) return "Windows reserves F12 for debugging. Choose another key.";
        if (Key == Keys.Delete && Modifiers.HasFlag(KeyModifiers.Control | KeyModifiers.Alt)) return "Windows reserves Ctrl + Alt + Delete.";
        if (Modifiers.HasFlag(KeyModifiers.Windows) && Key is Keys.L or Keys.U) return "Windows reserves that shortcut.";
        return null;
    }
}

public sealed record CommandDefinition(AppCommand Command, string Label, string Description, KeyBinding LegacyDefault,
    bool AlwaysAvailable = false, bool WhileVisible = false, bool PointerRequired = false)
{
    public KeyBinding Default => CommandCatalog.Main.Contains(Command) ? LegacyDefault : new();
}

public static class CommandCatalog
{
    private const KeyModifiers CA = KeyModifiers.Control | KeyModifiers.Alt;
    public static IReadOnlyList<AppCommand> Main { get; } = [AppCommand.ToggleLoupe, AppCommand.ZoomIn, AppCommand.ZoomOut, AppCommand.TogglePin, AppCommand.ToggleMeasure];
    public static IReadOnlyList<CommandDefinition> All { get; } = new CommandDefinition[]
    {
        new(AppCommand.ToggleLoupe, "Show / hide loupe", "Toggle a persistent live view", new(Keys.F10)),
        new(AppCommand.ToggleEnabled, "Enable / disable", "Keep the app in the tray", new(Keys.F10, CA), AlwaysAvailable: true),
        new(AppCommand.Peek, "Hold to peek", "Release to dismiss · modifiers alone are supported", new(Keys.Space, CA)),
        new(AppCommand.Hide, "Dismiss loupe", "Close the current inspection", new(Keys.Escape), WhileVisible: true),
        new(AppCommand.TogglePin, "Pin / follow area", "Keep the sampled area fixed and live", new(Keys.L, CA), WhileVisible: true),
        new(AppCommand.ToggleMeasure, "Measurement mode", "Interact with the enlarged pixels", new(Keys.M, CA), WhileVisible: true),
        new(AppCommand.Clear, "Clear guides & ruler", "Start a fresh measurement", new(Keys.Back, CA), WhileVisible: true),
        new(AppCommand.ToggleGrid, "Pixel grid", "Show or hide pixel boundaries", new(Keys.G, CA), WhileVisible: true),
        new(AppCommand.ZoomIn, "Zoom in", "Increase integer magnification", new(Keys.Oemplus, CA), WhileVisible: true),
        new(AppCommand.ZoomOut, "Zoom out", "Decrease integer magnification", new(Keys.OemMinus, CA), WhileVisible: true),
        new(AppCommand.GuideHorizontal, "Horizontal guide", "At the pointer inside the interactive lens", new(Keys.H), WhileVisible: true, PointerRequired: true),
        new(AppCommand.GuideVertical, "Vertical guide", "At the pointer inside the interactive lens", new(Keys.V), WhileVisible: true, PointerRequired: true),
        new(AppCommand.GuideCrosshair, "Both crosshair guides", "Pin both axes at the current pointer", new(), WhileVisible: true, PointerRequired: true),
        new(AppCommand.RulerPoint, "Place ruler point", "Press once for start, again for end", new(Keys.R), WhileVisible: true, PointerRequired: true),
        new(AppCommand.CaptureArea, "Capture original area", "A PNG at original pixel resolution", new(Keys.S, CA), WhileVisible: true),
        new(AppCommand.CaptureView, "Capture magnified view", "A PNG including the grid and guides", new(Keys.S, CA | KeyModifiers.Shift), WhileVisible: true),
        new(AppCommand.PanLeft, "Pan left", "Move the source by one physical pixel", new(Keys.Left), WhileVisible: true, PointerRequired: true),
        new(AppCommand.PanRight, "Pan right", "Move the source by one physical pixel", new(Keys.Right), WhileVisible: true, PointerRequired: true),
        new(AppCommand.PanUp, "Pan up", "Move the source by one physical pixel", new(Keys.Up), WhileVisible: true, PointerRequired: true),
        new(AppCommand.PanDown, "Pan down", "Move the source by one physical pixel", new(Keys.Down), WhileVisible: true, PointerRequired: true),
        new(AppCommand.IncreaseSize, "Larger lens", "Increase the viewing area", new(Keys.OemCloseBrackets, CA), WhileVisible: true),
        new(AppCommand.DecreaseSize, "Smaller lens", "Decrease the viewing area", new(Keys.OemOpenBrackets, CA), WhileVisible: true),
        new(AppCommand.OpenSettings, "Open settings", "Preferences and shortcuts", new(Keys.Oemcomma, CA), AlwaysAvailable: true),
        new(AppCommand.OpenCaptureFolder, "Open captures folder", "Browse saved PNG images", new(), AlwaysAvailable: true),
        new(AppCommand.Exit, "Exit Loupe", "Quit the tray app", new(), AlwaysAvailable: true)
    };
    // The other actions remain assignable, without reserving extra game keys by default.
    public static KeyBinding DefaultBinding(AppCommand command) => Get(command).Default;
    public static Dictionary<AppCommand, KeyBinding> Defaults() => All.ToDictionary(x => x.Command, x => DefaultBinding(x.Command));

    internal static void MigrateOptionalDefaults(Dictionary<AppCommand, KeyBinding> bindings)
    {
        foreach (var definition in All.Where(x => !Main.Contains(x.Command)))
            if (bindings.GetValueOrDefault(definition.Command) == definition.LegacyDefault)
                bindings[definition.Command] = new();
    }
    public static CommandDefinition Get(AppCommand command) => All.First(x => x.Command == command);

    public static string? Validate(IReadOnlyDictionary<AppCommand, KeyBinding> bindings)
    {
        var assigned = new Dictionary<KeyBinding, string>();
        foreach (var definition in All)
        {
            KeyBinding binding = bindings.GetValueOrDefault(definition.Command) ?? new();
            string? error = binding.Error(definition.Command == AppCommand.Peek);
            if (error is not null) return $"{definition.Label}: {error}";
            if (binding.IsEmpty) continue;
            if (assigned.TryGetValue(binding, out string? other)) return $"{binding} is assigned to both {other} and {definition.Label}.";
            assigned.Add(binding, definition.Label);
        }
        return null;
    }
}

internal sealed class KeyboardState
{
    private readonly HashSet<Keys> _down = [];
    public bool Set(Keys key, bool down) => down ? _down.Add(key) : _down.Remove(key);
    public KeyModifiers Modifiers =>
        ((_down.Contains(Keys.ControlKey) || _down.Contains(Keys.LControlKey) || _down.Contains(Keys.RControlKey)) ? KeyModifiers.Control : 0) |
        ((_down.Contains(Keys.Menu) || _down.Contains(Keys.LMenu) || _down.Contains(Keys.RMenu)) ? KeyModifiers.Alt : 0) |
        ((_down.Contains(Keys.ShiftKey) || _down.Contains(Keys.LShiftKey) || _down.Contains(Keys.RShiftKey)) ? KeyModifiers.Shift : 0) |
        ((_down.Contains(Keys.LWin) || _down.Contains(Keys.RWin)) ? KeyModifiers.Windows : 0);

    public bool Matches(KeyBinding binding)
    {
        if (binding.IsEmpty) return false;
        if (binding.Key == Keys.None) return Modifiers == binding.Modifiers;
        if (!_down.Contains(binding.Key)) return false;
        KeyModifiers modifiers = Modifiers;
        if (KeyBinding.IsModifier(binding.Key)) modifiers &= ~ModifierFor(binding.Key);
        return modifiers == binding.Modifiers;
    }

    public bool IsHeld(KeyBinding binding) => !binding.IsEmpty &&
        (binding.Key == Keys.None || _down.Contains(binding.Key)) &&
        (Modifiers & binding.Modifiers) == binding.Modifiers;

    public static KeyModifiers ModifierFor(Keys key) => key switch
    {
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => KeyModifiers.Control,
        Keys.Menu or Keys.LMenu or Keys.RMenu => KeyModifiers.Alt,
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => KeyModifiers.Shift,
        Keys.LWin or Keys.RWin => KeyModifiers.Windows,
        _ => KeyModifiers.None
    };
}
