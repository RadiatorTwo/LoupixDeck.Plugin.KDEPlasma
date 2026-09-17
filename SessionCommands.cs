using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>Builds the session commands: locking the screen and opening KRunner.</summary>
internal static class SessionCommands
{
    public static IEnumerable<IPluginCommand> Create(
        ScreenSaverClient screenSaver,
        KRunnerClient krunner,
        KdeCapabilities capabilities)
    {
        List<IPluginCommand> commands = [];

        if (capabilities.HasScreenSaver)
        {
            commands.Add(new KdeActionCommand(
                new CommandDescriptor
                {
                    CommandName = KdeCommands.Prefix + "LockScreen",
                    DisplayName = "KDE: Lock Screen",
                    Group = KdeCommands.Group
                },
                _ => screenSaver.LockAsync()));
        }

        if (!capabilities.HasKRunner)
        {
            return commands;
        }

        commands.Add(new KdeActionCommand(
            new CommandDescriptor
            {
                CommandName = KdeCommands.Prefix + "KRunner",
                DisplayName = "KDE: Open KRunner",
                Group = KdeCommands.Group
            },
            _ => krunner.DisplayAsync()));

        commands.Add(new KdeActionCommand(
            new CommandDescriptor
            {
                CommandName = KdeCommands.Prefix + "KRunnerQuery",
                DisplayName = "KDE: KRunner Query",
                Group = KdeCommands.Group,
                Description = "Open KRunner pre-filled with the given query",
                ParameterTemplate = "({query})",
                Parameters = [new CommandParameter("query", typeof(string))]
            },
            ctx =>
            {
                string query = ctx.Parameters.Length > 0 ? ctx.Parameters[0] : string.Empty;
                return string.IsNullOrWhiteSpace(query) ? krunner.DisplayAsync() : krunner.QueryAsync(query);
            }));

        return commands;
    }
}
