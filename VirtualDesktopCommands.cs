using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Builds the virtual desktop commands. They use the KWin D-Bus interfaces directly, so they keep
/// working even when the user removed the matching KDE keyboard shortcuts.
/// </summary>
internal static class VirtualDesktopCommands
{
    /// <summary>Number of fixed "switch to desktop" commands that carry their own button state.</summary>
    public const int FixedDesktopCommandCount = 10;

    public const string ActiveState = "Active";
    public const string InactiveState = "Inactive";

    /// <summary>The command name of the fixed switch command for a one-based desktop number.</summary>
    public static string SwitchCommandName(int number)
        => $"{KdeCommands.Prefix}SwitchToDesktop{number.ToString("00", CultureInfo.InvariantCulture)}";

    public static IEnumerable<IPluginCommand> Create(KWinClient kwin, VirtualDesktopClient desktops)
    {
        List<IPluginCommand> commands =
        [
            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "DesktopNext",
                    DisplayName = "KDE: Next Desktop",
                    Group = KdeCommands.Group,
                    HiddenFromMenu = true
                },
                _ => kwin.NextDesktopAsync()),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "DesktopPrevious",
                    DisplayName = "KDE: Previous Desktop",
                    Group = KdeCommands.Group,
                    HiddenFromMenu = true
                },
                _ => kwin.PreviousDesktopAsync()),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "DesktopSelect",
                    DisplayName = "KDE: Switch to Desktop",
                    Group = KdeCommands.Group,
                    Description = "Switch to the virtual desktop with the given id",
                    ParameterTemplate = "({desktopId})",
                    Parameters = [new CommandParameter("desktopId", typeof(string))],
                    HiddenFromMenu = true
                },
                ctx => SwitchToIdAsync(ctx, desktops)),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "DesktopSelectByName",
                    DisplayName = "KDE: Switch to Desktop by Name",
                    Group = KdeCommands.Group,
                    Description = "Switch to the virtual desktop with the given name",
                    ParameterTemplate = "({name})",
                    Parameters = [new CommandParameter("name", typeof(string))],
                    HiddenFromMenu = true
                },
                ctx => SwitchToNameAsync(ctx, desktops))
        ];

        for (int number = 1; number <= FixedDesktopCommandCount; number++)
        {
            commands.Add(CreateFixedSwitchCommand(kwin, desktops, number));
        }

        return commands;
    }

    private static IPluginCommand CreateFixedSwitchCommand(KWinClient kwin, VirtualDesktopClient desktops, int number)
    {
        CommandDescriptor descriptor = new()
        {
            CommandName = SwitchCommandName(number),
            DisplayName = $"KDE: Switch to Desktop {number.ToString(CultureInfo.InvariantCulture)}",
            Group = KdeCommands.Group,
            HiddenFromMenu = true,
            States =
            [
                new ButtonStateDescriptor { Name = InactiveState, Description = "The desktop is not the current one" },
                new ButtonStateDescriptor { Name = ActiveState, Description = "The desktop is the current one" }
            ]
        };

        return new KdeActionCommand(descriptor, async ctx =>
        {
            VirtualDesktop? target = desktops.FindByNumber(number);
            if (target is not null)
            {
                await desktops.SetCurrentAsync(target.Value.Id).ConfigureAwait(false);
                return;
            }

            // The cache may be stale right after a KWin restart; KWin's own numbering is the fallback.
            await kwin.SetCurrentDesktopAsync(number).ConfigureAwait(false);
        });
    }

    private static async Task SwitchToIdAsync(CommandContext ctx, VirtualDesktopClient desktops)
    {
        if (ctx.Parameters.Length == 0 || string.IsNullOrWhiteSpace(ctx.Parameters[0]))
        {
            ctx.Host.Logger.Warn("KdePlasma.DesktopSelect: no desktop id given.");
            return;
        }

        await desktops.SetCurrentAsync(ctx.Parameters[0].Trim()).ConfigureAwait(false);
    }

    private static async Task SwitchToNameAsync(CommandContext ctx, VirtualDesktopClient desktops)
    {
        if (ctx.Parameters.Length == 0 || string.IsNullOrWhiteSpace(ctx.Parameters[0]))
        {
            ctx.Host.Logger.Warn("KdePlasma.DesktopSelectByName: no desktop name given.");
            return;
        }

        string name = ctx.Parameters[0].Trim();
        VirtualDesktop? target = desktops.FindByName(name);
        if (target is null)
        {
            ctx.Host.Logger.Warn($"KdePlasma.DesktopSelectByName: no desktop named '{name}'.");
            return;
        }

        await desktops.SetCurrentAsync(target.Value.Id).ConfigureAwait(false);
    }
}
