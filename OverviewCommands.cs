using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Builds the Overview and Present Windows commands. Each one needs both the KWin effect and the
/// matching global shortcut, otherwise it is not offered at all.
/// </summary>
internal static class OverviewCommands
{
    private const string OverviewEffect = "overview";
    private const string WindowViewEffect = "windowview";

    public static IEnumerable<IPluginCommand> Create(KGlobalAccelClient accel, KdeCapabilities capabilities)
    {
        List<IPluginCommand> commands = [];

        Add(commands, accel, capabilities, OverviewEffect, "Overview", "KDE: Overview", "Overview");
        Add(commands, accel, capabilities, OverviewEffect, "OverviewCycle", "KDE: Cycle Overview", "Cycle Overview");
        Add(commands, accel, capabilities, OverviewEffect, "GridView", "KDE: Grid View", "Grid View");
        Add(commands, accel, capabilities, WindowViewEffect, "PresentWindows", "KDE: Present Windows (Current Desktop)", "Expose");
        Add(commands, accel, capabilities, WindowViewEffect, "PresentWindowsAll", "KDE: Present Windows (All Desktops)", "ExposeAll");
        Add(commands, accel, capabilities, WindowViewEffect, "PresentWindowsClass", "KDE: Present Windows (Same Application)", "ExposeClass");

        return commands;
    }

    private static void Add(
        List<IPluginCommand> commands,
        KGlobalAccelClient accel,
        KdeCapabilities capabilities,
        string effectName,
        string name,
        string displayName,
        string actionName)
    {
        if (!capabilities.HasEffect(effectName) || !capabilities.HasShortcut(actionName))
        {
            return;
        }

        CommandDescriptor descriptor = new()
        {
            CommandName = KdeCommands.Prefix + name,
            DisplayName = displayName,
            Group = KdeCommands.Group,
            HiddenFromMenu = true
        };

        commands.Add(new KdeShortcutCommand(descriptor, accel, actionName));
    }
}
