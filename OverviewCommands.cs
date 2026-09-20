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

    public static IEnumerable<IPluginCommand> Create(
        KGlobalAccelClient accel,
        KdeCapabilities capabilities,
        Func<string> preferredEffect)
    {
        List<IPluginCommand> commands = [];

        AddOverview(commands, accel, capabilities, preferredEffect);
        Add(commands, accel, capabilities, OverviewEffect, "OverviewCycle", "KDE: Cycle Overview", "Cycle Overview");
        Add(commands, accel, capabilities, OverviewEffect, "GridView", "KDE: Grid View", "Grid View");
        Add(commands, accel, capabilities, WindowViewEffect, "PresentWindows", "KDE: Present Windows (Current Desktop)", "Expose");
        Add(commands, accel, capabilities, WindowViewEffect, "PresentWindowsAll", "KDE: Present Windows (All Desktops)", "ExposeAll");
        Add(commands, accel, capabilities, WindowViewEffect, "PresentWindowsClass", "KDE: Present Windows (Same Application)", "ExposeClass");

        return commands;
    }

    /// <summary>
    /// The Overview button. Which of the three Overview actions it triggers is a setting, so one
    /// button can open the Grid View without the user rebinding it. The action is resolved on every
    /// press, and an action this session does not have falls back to the plain Overview.
    /// </summary>
    private static void AddOverview(
        List<IPluginCommand> commands,
        KGlobalAccelClient accel,
        KdeCapabilities capabilities,
        Func<string> preferredEffect)
    {
        if (!capabilities.HasEffect(OverviewEffect) || !capabilities.HasShortcut(OverviewEffects.OverviewAction))
        {
            return;
        }

        CommandDescriptor descriptor = new()
        {
            CommandName = KdeCommands.Prefix + "Overview",
            DisplayName = "KDE: Overview",
            Group = KdeCommands.Group,
            Description = "Opens the Overview effect selected in the plugin settings",
            HiddenFromMenu = true
        };

        commands.Add(new KdeShortcutCommand(descriptor, accel, _ =>
        {
            string action = OverviewEffects.ActionFor(preferredEffect());
            return capabilities.HasShortcut(action) ? action : OverviewEffects.OverviewAction;
        }));
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
