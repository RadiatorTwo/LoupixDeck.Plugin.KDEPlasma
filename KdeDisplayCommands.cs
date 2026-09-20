using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Builds the touch buttons that show live KDE values. They read the client caches only, which are
/// kept current by D-Bus signals, so the poll interval is just a safety net.
/// </summary>
internal static class KdeDisplayCommands
{
    public const string CurrentDesktopName = KdeCommands.Prefix + "CurrentDesktop";
    public const string CurrentDesktopNameName = KdeCommands.Prefix + "CurrentDesktopName";
    public const string DesktopCountName = KdeCommands.Prefix + "DesktopCount";
    public const string CurrentActivityName = KdeCommands.Prefix + "CurrentActivity";
    public const string PlasmaVersionName = KdeCommands.Prefix + "PlasmaVersion";
    public const string ActiveWindowTitleName = KdeCommands.Prefix + "ActiveWindowTitle";
    public const string ActiveWindowAppIdName = KdeCommands.Prefix + "ActiveWindowAppId";
    public const string ActiveWindowStateName = KdeCommands.Prefix + "ActiveWindowState";
    public const string DesktopFolderName = KdeCommands.Prefix + "DesktopFolder";
    public const string ActivityFolderName = KdeCommands.Prefix + "ActivityFolder";

    private static readonly TimeSpan DesktopInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VersionInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan WindowInterval = TimeSpan.FromSeconds(1);

    public static IEnumerable<IPluginCommand> CreateDesktopDisplays(
        VirtualDesktopClient desktops,
        Func<bool> showDesktopNames)
    {
        return
        [
            new KdeTextDisplayCommand(
                new CommandDescriptor
                {
                    CommandName = CurrentDesktopName,
                    DisplayName = "KDE: Current Desktop",
                    Group = KdeCommands.Group,
                    Description = "Shows the current virtual desktop",
                    HiddenFromMenu = true
                },
                DesktopInterval,
                () => DescribeCurrentDesktop(desktops, showDesktopNames())),

            new KdeTextDisplayCommand(
                new CommandDescriptor
                {
                    CommandName = CurrentDesktopNameName,
                    DisplayName = "KDE: Desktop Name",
                    Group = KdeCommands.Group,
                    Description = "Shows the name of the current virtual desktop",
                    HiddenFromMenu = true
                },
                DesktopInterval,
                () => desktops.Current?.Name ?? KdeTextDisplayCommand.UnknownText),

            new KdeTextDisplayCommand(
                new CommandDescriptor
                {
                    CommandName = DesktopCountName,
                    DisplayName = "KDE: Desktop Count",
                    Group = KdeCommands.Group,
                    Description = "Shows how many virtual desktops exist",
                    HiddenFromMenu = true
                },
                SlowInterval,
                () => desktops.HasState
                    ? desktops.Count.ToString(CultureInfo.InvariantCulture)
                    : KdeTextDisplayCommand.UnknownText)
        ];
    }

    /// <summary>
    /// The active window displays. They need the KWin bridge, because KWin exposes no per-window
    /// D-Bus interface, so they show the unknown text while no script is connected.
    /// </summary>
    public static IEnumerable<IPluginCommand> CreateActiveWindowDisplays(KWinBridgeClient bridge)
    {
        return
        [
            new KdeTextDisplayCommand(
                new CommandDescriptor
                {
                    CommandName = ActiveWindowTitleName,
                    DisplayName = "KDE: Active Window Title",
                    Group = KdeCommands.Group,
                    Description = "Shows the title of the active window",
                    HiddenFromMenu = true
                },
                WindowInterval,
                () => DescribeWindow(bridge, static window => window.Title)),

            new KdeTextDisplayCommand(
                new CommandDescriptor
                {
                    CommandName = ActiveWindowAppIdName,
                    DisplayName = "KDE: Active Window Application",
                    Group = KdeCommands.Group,
                    Description = "Shows the application id of the active window",
                    HiddenFromMenu = true
                },
                WindowInterval,
                () => DescribeWindow(bridge, static window => window.DisplayAppId)),

            new KdeTextDisplayCommand(
                new CommandDescriptor
                {
                    CommandName = ActiveWindowStateName,
                    DisplayName = "KDE: Active Window State",
                    Group = KdeCommands.Group,
                    Description = "Shows whether the active window is maximized, kept above or fullscreen",
                    HiddenFromMenu = true
                },
                WindowInterval,
                () => DescribeWindow(bridge, DescribeWindowState))
        ];
    }

    /// <summary>Reads one value of the active window, or the unknown text while there is none.</summary>
    private static string DescribeWindow(KWinBridgeClient bridge, Func<ActiveWindowInfo, string> read)
    {
        ActiveWindowInfo window = bridge.ActiveWindow;
        if (!bridge.Connected || !window.Present)
        {
            return KdeTextDisplayCommand.UnknownText;
        }

        string text = read(window);
        return text.Length > 0 ? text : KdeTextDisplayCommand.UnknownText;
    }

    private static string DescribeWindowState(ActiveWindowInfo window)
    {
        List<string> parts = [];

        if (window.Maximized)
        {
            parts.Add("Maximized");
        }

        if (window.KeepAbove)
        {
            parts.Add("Above");
        }

        if (window.FullScreen)
        {
            parts.Add("Fullscreen");
        }

        if (window.Minimized)
        {
            parts.Add("Minimized");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "Normal";
    }

    public static IPluginCommand CreateActivityDisplay(ActivityManagerClient activities)
    {
        return new KdeTextDisplayCommand(
            new CommandDescriptor
            {
                CommandName = CurrentActivityName,
                DisplayName = "KDE: Current Activity",
                Group = KdeCommands.Group,
                Description = "Shows the current Activity",
                HiddenFromMenu = true
            },
            SlowInterval,
            () => activities.Current?.Name ?? KdeTextDisplayCommand.UnknownText);
    }

    public static IPluginCommand CreateVersionDisplay(PlasmaVersionClient version)
    {
        return new KdeTextDisplayCommand(
            new CommandDescriptor
            {
                CommandName = PlasmaVersionName,
                DisplayName = "KDE: Plasma Version",
                Group = KdeCommands.Group,
                Description = "Shows the running Plasma version",
                HiddenFromMenu = true
            },
            VersionInterval,
            () => version.Version.Length > 0 ? version.Version : KdeTextDisplayCommand.UnknownText);
    }

    /// <summary>The button that shows the current desktop and opens the virtual desktop folder.</summary>
    public static IPluginCommand CreateDesktopFolder(
        VirtualDesktopClient desktops,
        KdeFolderGrid grid,
        Func<bool> showDesktopNames)
    {
        return new KdeTextDisplayCommand(
            new CommandDescriptor
            {
                CommandName = DesktopFolderName,
                DisplayName = "KDE: Virtual Desktops",
                Group = KdeCommands.Group,
                Description = "Opens a folder with all virtual desktops"
            },
            DesktopInterval,
            () => DescribeCurrentDesktop(desktops, showDesktopNames()),
            ctx =>
            {
                ctx.Host.OpenFolder(new VirtualDesktopFolderProvider(desktops, grid, showDesktopNames));
                return Task.CompletedTask;
            });
    }

    /// <summary>The button that shows the current Activity and opens the Activities folder.</summary>
    public static IPluginCommand CreateActivityFolder(ActivityManagerClient activities, KdeFolderGrid grid)
    {
        return new KdeTextDisplayCommand(
            new CommandDescriptor
            {
                CommandName = ActivityFolderName,
                DisplayName = "KDE: Activities",
                Group = KdeCommands.Group,
                Description = "Opens a folder with all Activities"
            },
            SlowInterval,
            () => activities.Current?.Name ?? KdeTextDisplayCommand.UnknownText,
            ctx =>
            {
                ctx.Host.OpenFolder(new ActivityFolderProvider(activities, grid));
                return Task.CompletedTask;
            });
    }

    private static string DescribeCurrentDesktop(VirtualDesktopClient desktops, bool useNames)
    {
        VirtualDesktop? current = desktops.Current;
        if (current is null)
        {
            return KdeTextDisplayCommand.UnknownText;
        }

        return useNames
            ? current.Value.Name
            : current.Value.Number.ToString(CultureInfo.InvariantCulture);
    }
}
