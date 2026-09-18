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
                ctx => SwitchToNameAsync(ctx, desktops)),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "DesktopAdd",
                    DisplayName = "KDE: Add Desktop",
                    Group = KdeCommands.Group,
                    Description = "Adds a virtual desktop at the end; an empty name falls back to \"Desktop N\"",
                    ParameterTemplate = "({name})",
                    Parameters = [new CommandParameter("name", typeof(string))],
                    HiddenFromMenu = true
                },
                ctx => AddAsync(ctx, desktops)),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "DesktopRemove",
                    DisplayName = "KDE: Remove Current Desktop",
                    Group = KdeCommands.Group,
                    Description = "Removes the desktop that is currently active",
                    HiddenFromMenu = true
                },
                ctx => RemoveAsync(ctx, desktops, desktops.Current)),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "DesktopRemoveLast",
                    DisplayName = "KDE: Remove Last Desktop",
                    Group = KdeCommands.Group,
                    Description = "Removes the last desktop of the layout",
                    HiddenFromMenu = true
                },
                ctx => RemoveAsync(ctx, desktops, desktops.Last))
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

    private static async Task AddAsync(CommandContext ctx, VirtualDesktopClient desktops)
    {
        string name = ctx.Parameters.Length > 0 ? ctx.Parameters[0].Trim() : string.Empty;
        if (name.Length == 0)
        {
            // KWin accepts an empty name but then shows an unnamed desktop, so the usual
            // "Desktop N" naming is filled in here.
            name = $"Desktop {(desktops.Count + 1).ToString(CultureInfo.InvariantCulture)}";
        }

        await desktops.CreateAsync(name).ConfigureAwait(false);
    }

    private static async Task RemoveAsync(CommandContext ctx, VirtualDesktopClient desktops, VirtualDesktop? target)
    {
        if (target is null)
        {
            ctx.Host.Logger.Warn("KdePlasma: no desktop to remove; the KWin state is unknown.");
            return;
        }

        // KWin refuses to remove the last remaining desktop, so the button stays a no-op there.
        if (desktops.Count <= 1)
        {
            ctx.Host.Logger.Warn("KdePlasma: the last virtual desktop cannot be removed.");
            return;
        }

        await desktops.RemoveAsync(target.Value.Id).ConfigureAwait(false);
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
