using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// The window commands that only a running KWin script can carry out. KWin exposes no per-window
/// D-Bus interface, so moving a window to a named desktop or output goes through the bridge.
/// </summary>
internal static class WindowBridgeCommands
{
    public const string MoveToDesktopName = KdeCommands.Prefix + "WindowMoveToDesktop";
    public const string MoveToOutputName = KdeCommands.Prefix + "WindowMoveToOutput";

    public static IEnumerable<IPluginCommand> Create(KWinBridgeClient bridge, VirtualDesktopClient desktops)
    {
        return
        [
            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = MoveToDesktopName,
                    DisplayName = "KDE: Window to Desktop",
                    Group = KdeCommands.Group,
                    Description = "Move the active window to the virtual desktop with the given id",
                    ParameterTemplate = "({desktopId})",
                    Parameters = [new CommandParameter("desktopId", typeof(string))],
                    HiddenFromMenu = true
                },
                ctx => MoveToDesktopAsync(ctx, bridge, desktops)),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = MoveToOutputName,
                    DisplayName = "KDE: Window to Monitor",
                    Group = KdeCommands.Group,
                    Description = "Move the active window to the monitor with the given name",
                    ParameterTemplate = "({output})",
                    Parameters = [new CommandParameter("output", typeof(string))],
                    HiddenFromMenu = true
                },
                ctx => MoveToOutputAsync(ctx, bridge))
        ];
    }

    /// <summary>
    /// Accepts a desktop id, and a desktop name as a convenience, so a button that was bound from
    /// the live desktop list keeps working after the desktop was renamed.
    /// </summary>
    private static async Task MoveToDesktopAsync(CommandContext ctx, KWinBridgeClient bridge, VirtualDesktopClient desktops)
    {
        if (ctx.Parameters.Length == 0 || string.IsNullOrWhiteSpace(ctx.Parameters[0]))
        {
            ctx.Host.Logger.Warn($"{MoveToDesktopName}: no desktop id given.");
            return;
        }

        string requested = ctx.Parameters[0].Trim();
        string desktopId = ResolveDesktopId(requested, desktops);

        if (!await bridge.MoveActiveWindowToDesktopAsync(desktopId).ConfigureAwait(false))
        {
            ctx.Host.Logger.Warn($"{MoveToDesktopName}: the KWin bridge did not accept the command.");
        }
    }

    private static async Task MoveToOutputAsync(CommandContext ctx, KWinBridgeClient bridge)
    {
        if (ctx.Parameters.Length == 0 || string.IsNullOrWhiteSpace(ctx.Parameters[0]))
        {
            ctx.Host.Logger.Warn($"{MoveToOutputName}: no monitor name given.");
            return;
        }

        if (!await bridge.MoveActiveWindowToOutputAsync(ctx.Parameters[0].Trim()).ConfigureAwait(false))
        {
            ctx.Host.Logger.Warn($"{MoveToOutputName}: the KWin bridge did not accept the command.");
        }
    }

    private static string ResolveDesktopId(string requested, VirtualDesktopClient desktops)
    {
        foreach (VirtualDesktop desktop in desktops.Desktops)
        {
            if (string.Equals(desktop.Id, requested, StringComparison.Ordinal))
            {
                return requested;
            }
        }

        foreach (VirtualDesktop desktop in desktops.Desktops)
        {
            if (string.Equals(desktop.Name, requested, StringComparison.OrdinalIgnoreCase))
            {
                return desktop.Id;
            }
        }

        return requested;
    }
}
