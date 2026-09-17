using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// A command that runs a plain asynchronous action. It backs every command that talks to a
/// KDE D-Bus interface directly instead of going through a global shortcut.
/// </summary>
internal sealed class KdeActionCommand(CommandDescriptor descriptor, Func<CommandContext, Task> action) : IPluginCommand
{
    public CommandDescriptor Descriptor { get; } = descriptor;

    public ButtonTargets SupportedTargets => ButtonTargets.SimpleButton | ButtonTargets.TouchButton;

    public async Task Execute(CommandContext ctx)
    {
        try
        {
            await action(ctx).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ctx.Host.Logger.Warn($"{Descriptor.CommandName} failed: {ex.Message}");
        }
    }
}
