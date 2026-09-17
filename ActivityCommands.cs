using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Builds the Activity commands. Activities are addressed by their UUID, so renaming an Activity
/// never breaks a button.
/// </summary>
internal static class ActivityCommands
{
    public static IEnumerable<IPluginCommand> Create(ActivityManagerClient activities)
    {
        return
        [
            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "ActivityNext",
                    DisplayName = "KDE: Next Activity",
                    Group = KdeCommands.Group,
                    HiddenFromMenu = true
                },
                _ => activities.NextAsync()),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "ActivityPrevious",
                    DisplayName = "KDE: Previous Activity",
                    Group = KdeCommands.Group,
                    HiddenFromMenu = true
                },
                _ => activities.PreviousAsync()),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "ActivitySelect",
                    DisplayName = "KDE: Switch to Activity",
                    Group = KdeCommands.Group,
                    Description = "Switch to the Activity with the given id",
                    ParameterTemplate = "({activityId})",
                    Parameters = [new CommandParameter("activityId", typeof(string))],
                    HiddenFromMenu = true
                },
                ctx => SwitchAsync(ctx, activities)),

            new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "ActivitySelectByName",
                    DisplayName = "KDE: Switch to Activity by Name",
                    Group = KdeCommands.Group,
                    Description = "Switch to the Activity with the given name",
                    ParameterTemplate = "({name})",
                    Parameters = [new CommandParameter("name", typeof(string))],
                    HiddenFromMenu = true
                },
                ctx => SwitchByNameAsync(ctx, activities))
        ];
    }

    private static async Task SwitchAsync(CommandContext ctx, ActivityManagerClient activities)
    {
        if (ctx.Parameters.Length == 0 || string.IsNullOrWhiteSpace(ctx.Parameters[0]))
        {
            ctx.Host.Logger.Warn("KdePlasma.ActivitySelect: no Activity id given.");
            return;
        }

        await activities.SetCurrentAsync(ctx.Parameters[0].Trim()).ConfigureAwait(false);
    }

    private static async Task SwitchByNameAsync(CommandContext ctx, ActivityManagerClient activities)
    {
        if (ctx.Parameters.Length == 0 || string.IsNullOrWhiteSpace(ctx.Parameters[0]))
        {
            ctx.Host.Logger.Warn("KdePlasma.ActivitySelectByName: no Activity name given.");
            return;
        }

        string name = ctx.Parameters[0].Trim();
        foreach (KdeActivity activity in activities.Activities)
        {
            if (string.Equals(activity.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                await activities.SetCurrentAsync(activity.Id).ConfigureAwait(false);
                return;
            }
        }

        ctx.Host.Logger.Warn($"KdePlasma.ActivitySelectByName: no Activity named '{name}'.");
    }
}
