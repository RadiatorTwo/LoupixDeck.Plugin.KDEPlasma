using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Builds the command picker tree. The host merges a contributed menu into an existing category
/// only when the root node name matches the command group exactly, so the whole tree hangs below a
/// single root named <see cref="KdeCommands.Group"/> instead of adding sibling categories.
/// Sections are only emitted for commands that were actually registered, so a capability that this
/// Plasma session lacks leaves no dead menu entry behind. The two folder commands stay out of the
/// tree because they are the only ones left visible in the flat list, which keeps the category card
/// itself (and its description) in place.
/// </summary>
internal static class KdeMenuTree
{
    public static IReadOnlyList<MenuNode> Build(
        IEnumerable<IPluginCommand> commands,
        VirtualDesktopClient? desktops,
        ActivityManagerClient? activities,
        KWinBridgeClient? bridge)
    {
        HashSet<string> available = new(StringComparer.Ordinal);
        foreach (IPluginCommand command in commands)
        {
            available.Add(command.Descriptor.CommandName);
        }

        List<MenuNode> sections = [];

        AddSection(sections, "Virtual Desktops", BuildDesktopSection(available, desktops));
        AddSection(sections, "Active Window", BuildWindowSection(available, desktops, bridge));
        AddSection(sections, "Activities", BuildActivitySection(available, activities));
        AddSection(sections, "Overview and Desktop", BuildOverviewSection(available));
        AddSection(sections, "Night Color", BuildNightColorSection(available));
        AddSection(sections, "Session", BuildSessionSection(available));
        AddSection(sections, "Status", BuildStatusSection(available));

        return sections.Count == 0 ? [] : [new MenuNode { Name = KdeCommands.Group, Children = sections }];
    }

    private static List<MenuNode> BuildDesktopSection(IReadOnlySet<string> available, VirtualDesktopClient? desktops)
    {
        List<MenuNode> nodes = [];

        Add(nodes, available, "DesktopNext", "Next Desktop");
        Add(nodes, available, "DesktopPrevious", "Previous Desktop");
        Add(nodes, available, "DesktopSelectByName", "Switch by Name");

        List<MenuNode> fixedSlots = [];
        for (int number = 1; number <= VirtualDesktopCommands.FixedDesktopCommandCount; number++)
        {
            string name = VirtualDesktopCommands.SwitchCommandName(number);
            if (available.Contains(name))
            {
                fixedSlots.Add(new MenuNode
                {
                    Name = $"Desktop {number.ToString(CultureInfo.InvariantCulture)}",
                    CommandName = name
                });
            }
        }

        AddSection(nodes, "Switch by Number", fixedSlots);

        List<MenuNode> manage = [];
        Add(manage, available, "DesktopAdd", "Add Desktop");
        Add(manage, available, "DesktopRemove", "Remove Current Desktop");
        Add(manage, available, "DesktopRemoveLast", "Remove Last Desktop");
        AddSection(nodes, "Manage Desktops", manage);

        // The live list binds the id-based command, so a renamed desktop keeps working.
        if (desktops is not null && desktops.HasState && available.Contains(KdeCommands.Prefix + "DesktopSelect"))
        {
            List<MenuNode> live = [];
            foreach (VirtualDesktop desktop in desktops.Desktops)
            {
                live.Add(new MenuNode
                {
                    Name = desktop.Name.Length > 0
                        ? desktop.Name
                        : $"Desktop {desktop.Number.ToString(CultureInfo.InvariantCulture)}",
                    CommandName = KdeCommands.Prefix + "DesktopSelect",
                    Parameters = new Dictionary<string, string> { ["desktopId"] = desktop.Id }
                });
            }

            AddSection(nodes, "Switch to Desktop", live);
        }

        return nodes;
    }

    private static List<MenuNode> BuildWindowSection(
        IReadOnlySet<string> available,
        VirtualDesktopClient? desktops,
        KWinBridgeClient? bridge)
    {
        List<MenuNode> nodes = [];

        Add(nodes, available, "WindowMinimize", "Minimize");
        Add(nodes, available, "WindowMaximize", "Maximize / Restore");
        Add(nodes, available, "WindowClose", "Close");
        Add(nodes, available, "WindowFullscreen", "Toggle Fullscreen");
        Add(nodes, available, "WindowKeepAbove", "Toggle Keep Above");

        List<MenuNode> toDesktop = [];
        Add(toDesktop, available, "WindowToDesktopNext", "Next Desktop");
        Add(toDesktop, available, "WindowToDesktopPrevious", "Previous Desktop");
        Add(toDesktop, available, "WindowToDesktopNumber", "Desktop by Number");

        // The live list binds the id-based bridge command, so a renamed desktop keeps working.
        if (desktops is not null && desktops.HasState && available.Contains(WindowBridgeCommands.MoveToDesktopName))
        {
            foreach (VirtualDesktop desktop in desktops.Desktops)
            {
                toDesktop.Add(new MenuNode
                {
                    Name = desktop.Name,
                    CommandName = WindowBridgeCommands.MoveToDesktopName,
                    Parameters = new Dictionary<string, string> { ["desktopId"] = desktop.Id }
                });
            }
        }

        AddSection(nodes, "Move to Desktop", toDesktop);

        List<MenuNode> toScreen = [];
        Add(toScreen, available, "WindowToScreenNext", "Next Screen");
        Add(toScreen, available, "WindowToScreenPrevious", "Previous Screen");
        Add(toScreen, available, "WindowToScreenNumber", "Screen by Index");

        // The monitor names come from the bridge, because KWin reports them to the script only.
        if (bridge is not null && available.Contains(WindowBridgeCommands.MoveToOutputName))
        {
            foreach (BridgeOutput output in bridge.Outputs)
            {
                toScreen.Add(new MenuNode
                {
                    Name = output.DisplayName,
                    CommandName = WindowBridgeCommands.MoveToOutputName,
                    Parameters = new Dictionary<string, string> { ["output"] = output.Name }
                });
            }
        }

        AddSection(nodes, "Move to Screen", toScreen);

        return nodes;
    }

    private static List<MenuNode> BuildActivitySection(IReadOnlySet<string> available, ActivityManagerClient? activities)
    {
        List<MenuNode> nodes = [];

        Add(nodes, available, "ActivityNext", "Next Activity");
        Add(nodes, available, "ActivityPrevious", "Previous Activity");
        Add(nodes, available, "ActivitySelectByName", "Switch by Name");

        // The live list binds the id-based command, so a renamed Activity keeps working.
        if (activities is not null && activities.HasState && available.Contains(KdeCommands.Prefix + "ActivitySelect"))
        {
            List<MenuNode> live = [];
            foreach (KdeActivity activity in activities.Activities)
            {
                live.Add(new MenuNode
                {
                    Name = activity.Name,
                    CommandName = KdeCommands.Prefix + "ActivitySelect",
                    Parameters = new Dictionary<string, string> { ["activityId"] = activity.Id }
                });
            }

            AddSection(nodes, "Switch to Activity", live);
        }

        return nodes;
    }

    private static List<MenuNode> BuildOverviewSection(IReadOnlySet<string> available)
    {
        List<MenuNode> nodes = [];

        Add(nodes, available, "ShowDesktop", "Show Desktop");
        Add(nodes, available, "Overview", "Overview");
        Add(nodes, available, "OverviewCycle", "Cycle Overview");
        Add(nodes, available, "GridView", "Grid View");
        Add(nodes, available, "PresentWindows", "Present Windows (Current Desktop)");
        Add(nodes, available, "PresentWindowsAll", "Present Windows (All Desktops)");
        Add(nodes, available, "PresentWindowsClass", "Present Windows (Same Application)");

        return nodes;
    }

    private static List<MenuNode> BuildNightColorSection(IReadOnlySet<string> available)
    {
        List<MenuNode> nodes = [];

        Add(nodes, available, "NightColorToggle", "Toggle Night Color");
        Add(nodes, available, "NightColorStatus", "Night Color Status");

        return nodes;
    }

    private static List<MenuNode> BuildSessionSection(IReadOnlySet<string> available)
    {
        List<MenuNode> nodes = [];

        Add(nodes, available, "LockScreen", "Lock Screen");
        Add(nodes, available, "KRunner", "Open KRunner");
        Add(nodes, available, "KRunnerQuery", "KRunner Query");

        return nodes;
    }

    private static List<MenuNode> BuildStatusSection(IReadOnlySet<string> available)
    {
        List<MenuNode> nodes = [];

        Add(nodes, available, "CurrentDesktop", "Current Desktop");
        Add(nodes, available, "CurrentDesktopName", "Desktop Name");
        Add(nodes, available, "DesktopCount", "Desktop Count");
        Add(nodes, available, "CurrentActivity", "Current Activity");
        Add(nodes, available, "PlasmaVersion", "Plasma Version");
        Add(nodes, available, "ActiveWindowTitle", "Active Window Title");
        Add(nodes, available, "ActiveWindowAppId", "Active Window Application");
        Add(nodes, available, "ActiveWindowState", "Active Window State");

        return nodes;
    }

    private static void Add(List<MenuNode> nodes, IReadOnlySet<string> available, string command, string label)
    {
        string commandName = KdeCommands.Prefix + command;
        if (!available.Contains(commandName))
        {
            return;
        }

        nodes.Add(new MenuNode { Name = label, CommandName = commandName });
    }

    private static void AddSection(List<MenuNode> target, string name, List<MenuNode> children)
    {
        if (children.Count == 0)
        {
            return;
        }

        target.Add(new MenuNode { Name = name, Children = children });
    }
}
