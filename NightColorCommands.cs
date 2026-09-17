using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Builds the Night Color commands. KWin offers no D-Bus toggle, so switching runs through the
/// global shortcut while the button state and the status text come from the Night Light interface.
/// </summary>
internal static class NightColorCommands
{
    public const string ToggleName = KdeCommands.Prefix + "NightColorToggle";
    public const string StatusName = KdeCommands.Prefix + "NightColorStatus";
    public const string OnState = "On";
    public const string OffState = "Off";

    private const string ToggleAction = "Toggle Night Color";

    private static readonly TimeSpan StatusUpdateInterval = TimeSpan.FromSeconds(5);

    public static IEnumerable<IPluginCommand> Create(
        KGlobalAccelClient accel,
        NightLightClient nightLight,
        KdeCapabilities capabilities)
    {
        List<IPluginCommand> commands = [];

        if (!capabilities.NightLightAvailable)
        {
            return commands;
        }

        if (capabilities.HasShortcut(ToggleAction))
        {
            CommandDescriptor toggle = new()
            {
                CommandName = ToggleName,
                DisplayName = "KDE: Toggle Night Color",
                Group = KdeCommands.Group,
                HiddenFromMenu = true,
                States =
                [
                    new ButtonStateDescriptor { Name = OffState, Description = "Night Color is off" },
                    new ButtonStateDescriptor { Name = OnState, Description = "Night Color is on" }
                ]
            };

            commands.Add(new KdeShortcutCommand(toggle, accel, ToggleAction));
        }

        CommandDescriptor status = new()
        {
            CommandName = StatusName,
            DisplayName = "KDE: Night Color Status",
            Group = KdeCommands.Group,
            Description = "Shows whether Night Color is active and at which temperature",
            HiddenFromMenu = true
        };

        commands.Add(new KdeTextDisplayCommand(status, StatusUpdateInterval, () => BuildStatusText(nightLight)));

        return commands;
    }

    private static string BuildStatusText(NightLightClient nightLight)
    {
        if (!nightLight.HasState)
        {
            return KdeTextDisplayCommand.UnknownText;
        }

        if (!nightLight.Enabled)
        {
            return "Off";
        }

        if (!nightLight.Running || nightLight.Temperature == 0)
        {
            return "On";
        }

        return $"{nightLight.Temperature.ToString(CultureInfo.InvariantCulture)}K";
    }
}
