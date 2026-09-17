using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Builds the commands that act on the active window. All of them run through KGlobalAccel and
/// are only offered when the running KWin actually provides the matching action.
/// </summary>
internal static class WindowCommands
{
    /// <summary>The highest desktop and screen slot KWin offers as a global shortcut.</summary>
    private const int MaximumDesktopSlot = 20;
    private const int MaximumScreenSlot = 7;

    public static IEnumerable<IPluginCommand> Create(KGlobalAccelClient accel, KdeCapabilities capabilities)
    {
        List<IPluginCommand> commands = [];

        AddFixed(commands, accel, capabilities, "WindowMinimize", "KDE: Minimize Window", "Window Minimize");
        AddFixed(commands, accel, capabilities, "WindowMaximize", "KDE: Maximize / Restore Window", "Window Maximize");
        AddFixed(commands, accel, capabilities, "WindowClose", "KDE: Close Window", "Window Close");
        AddFixed(commands, accel, capabilities, "WindowFullscreen", "KDE: Toggle Fullscreen", "Window Fullscreen");
        AddFixed(commands, accel, capabilities, "WindowKeepAbove", "KDE: Toggle Keep Above", "Window Above Other Windows");

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
        string actionName)
    {
        if (!capabilities.HasShortcut(actionName))
        {
            return;
        }

        CommandDescriptor descriptor = new()
        {
            CommandName = KdeCommands.Prefix + name,
            DisplayName = displayName,
            Group = KdeCommands.Group
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
            Parameters = [new CommandParameter(parameterName, typeof(int)) { DefaultValue = defaultValue }]
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
