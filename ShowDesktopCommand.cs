using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Toggles Show Desktop. KWin exposes both the action and its state, so the button reflects the
/// real desktop state even when the user triggered Show Desktop from the keyboard.
/// </summary>
internal static class ShowDesktopCommand
{
    public const string Name = KdeCommands.Prefix + "ShowDesktop";
    public const string OnState = "On";
    public const string OffState = "Off";

    public static IPluginCommand Create(KWinClient kwin)
    {
        CommandDescriptor descriptor = new()
        {
            CommandName = Name,
            DisplayName = "KDE: Show Desktop",
            Group = KdeCommands.Group,
            Description = "Show the desktop and hide all windows",
            HiddenFromMenu = true,
            States =
            [
                new ButtonStateDescriptor { Name = OffState, Description = "Windows are visible" },
                new ButtonStateDescriptor { Name = OnState, Description = "The desktop is shown" }
            ]
        };

        return new KdeActionCommand(descriptor, _ => kwin.ToggleShowDesktopAsync());
    }
}
