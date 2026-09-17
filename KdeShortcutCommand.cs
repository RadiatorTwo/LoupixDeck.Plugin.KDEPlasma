using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>Shared constants of all KDE Plasma commands.</summary>
internal static class KdeCommands
{
    public const string Group = "KDE Plasma";
    public const string Prefix = "KdePlasma.";
}

/// <summary>
/// Invokes a KWin action through KGlobalAccel. This backs every command that has no direct
/// D-Bus equivalent, so the user's own KDE shortcut bindings stay authoritative.
/// </summary>
internal sealed class KdeShortcutCommand : IPluginCommand
{
    private readonly KGlobalAccelClient _accel;
    private readonly Func<string[], string?> _resolveAction;

    public KdeShortcutCommand(CommandDescriptor descriptor, KGlobalAccelClient accel, string actionName)
        : this(descriptor, accel, _ => actionName)
    {
    }

    public KdeShortcutCommand(CommandDescriptor descriptor, KGlobalAccelClient accel, Func<string[], string?> resolveAction)
    {
        Descriptor = descriptor;
        _accel = accel;
        _resolveAction = resolveAction;
    }

    public CommandDescriptor Descriptor { get; }

    public ButtonTargets SupportedTargets => ButtonTargets.SimpleButton | ButtonTargets.TouchButton;

    public async Task Execute(CommandContext ctx)
    {
        try
        {
            string? action = _resolveAction(ctx.Parameters);
            if (string.IsNullOrEmpty(action))
            {
                ctx.Host.Logger.Warn($"{Descriptor.CommandName}: no KWin action for the given parameters.");
                return;
            }

            await _accel.InvokeShortcutAsync(action).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ctx.Host.Logger.Warn($"{Descriptor.CommandName} failed: {ex.Message}");
        }
    }
}
