using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Builds the commands that act on the active window. All of them run through KGlobalAccel and
/// are only offered when the running KWin actually provides the matching action.
/// </summary>
internal static class WindowCommands
{
    public const string MaximizeName = KdeCommands.Prefix + "WindowMaximize";
    public const string KeepAboveName = KdeCommands.Prefix + "WindowKeepAbove";
    public const string FullscreenName = KdeCommands.Prefix + "WindowFullscreen";

    public const string OnState = "On";
    public const string OffState = "Off";

    /// <summary>The highest desktop and screen slot KWin offers as a global shortcut.</summary>
    private const int MaximumDesktopSlot = 20;
    private const int MaximumScreenSlot = 7;

    public static IEnumerable<IPluginCommand> Create(KGlobalAccelClient accel, KdeCapabilities capabilities)
    {
        List<IPluginCommand> commands = [];

        AddFixed(commands, accel, capabilities, "WindowMinimize", "KDE: Minimize Window", "Window Minimize");
        AddFixed(commands, accel, capabilities, "WindowMaximize", "KDE: Maximize / Restore Window", "Window Maximize", "The window is maximized");
        AddFixed(commands, accel, capabilities, "WindowClose", "KDE: Close Window", "Window Close");
        AddFixed(commands, accel, capabilities, "WindowFullscreen", "KDE: Toggle Fullscreen", "Window Fullscreen", "The window is fullscreen");
        AddFixed(commands, accel, capabilities, "WindowKeepAbove", "KDE: Toggle Keep Above", "Window Above Other Windows", "The window stays above the others");

        AddFixed(commands, accel, capabilities, "WindowToDesktopNext", "KDE: Window to Next Desktop", "Window to Next Desktop");
        AddFixed(commands, accel, capabilities, "WindowToDesktopPrevious", "KDE: Window to Previous Desktop", "Window to Previous Desktop");
        AddFixed(commands, accel, capabilities, "WindowToScreenNext", "KDE: Window to Next Screen", "Window to Next Screen");
        AddFixed(commands, accel, capabilities, "WindowToScreenPrevious", "KDE: Window to Previous Screen", "Window to Previous Screen");

        AddSlotCommand(
            commands,
            accel,
            capabilities,
            "WindowToDesktopNumber",
            "KDE: Window to Desktop N",
            "Move the active window to the desktop with the given number",
            "({desktop})",
            "desktop",
            "1",
            1,
            MaximumDesktopSlot,
            static slot => $"Window to Desktop {slot.ToString(CultureInfo.InvariantCulture)}");

        AddSlotCommand(
            commands,
            accel,
            capabilities,
            "WindowToScreenNumber",
            "KDE: Window to Screen N",
            "Move the active window to the screen with the given index",
            "({screen})",
            "screen",
            "0",
            0,
            MaximumScreenSlot,
            static slot => $"Window to Screen {slot.ToString(CultureInfo.InvariantCulture)}");

        return commands;
    }

    private static void AddFixed(
        List<IPluginCommand> commands,
        KGlobalAccelClient accel,
        KdeCapabilities capabilities,
        string name,
        string displayName,
        string actionName,
        string? onStateDescription = null)
    {
        if (!capabilities.HasShortcut(actionName))
        {
            return;
        }

        // Only the KWin bridge can report a window state, so without it the button stays stateless
        // instead of showing a state that never changes.
        bool withState = onStateDescription is not null && capabilities.HasWindowBridge;

        CommandDescriptor descriptor = new()
        {
            CommandName = KdeCommands.Prefix + name,
            DisplayName = displayName,
            Group = KdeCommands.Group,
            HiddenFromMenu = true,
            States = withState
                ?
                [
                    new ButtonStateDescriptor { Name = OffState, Description = "The window is in its normal state" },
                    new ButtonStateDescriptor { Name = OnState, Description = onStateDescription! }
                ]
                : []
        };

        commands.Add(new KdeShortcutCommand(descriptor, accel, actionName));
    }

    private static void AddSlotCommand(
        List<IPluginCommand> commands,
        KGlobalAccelClient accel,
        KdeCapabilities capabilities,
        string name,
        string displayName,
        string description,
        string parameterTemplate,
        string parameterName,
        string defaultValue,
        int minimum,
        int maximum,
        Func<int, string> buildActionName)
    {
        // The command is only useful when at least the first slot exists on this Plasma version.
        if (!capabilities.HasShortcut(buildActionName(minimum)))
        {
            return;
        }

        CommandDescriptor descriptor = new()
        {
            CommandName = KdeCommands.Prefix + name,
            DisplayName = displayName,
            Group = KdeCommands.Group,
            Description = description,
            ParameterTemplate = parameterTemplate,
            Parameters = [new CommandParameter(parameterName, typeof(int)) { DefaultValue = defaultValue }],
            HiddenFromMenu = true
        };

        commands.Add(new KdeShortcutCommand(descriptor, accel, parameters =>
        {
            if (parameters.Length == 0
                || !int.TryParse(parameters[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
                || slot < minimum
                || slot > maximum)
            {
                return null;
            }

            string action = buildActionName(slot);
            return capabilities.HasShortcut(action) ? action : null;
        }));
    }
}
