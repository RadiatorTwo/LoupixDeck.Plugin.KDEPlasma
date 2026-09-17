using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// A touch button that renders a cached KDE value. <see cref="GetText"/> runs on the host polling
/// timer, so it only reads the client caches and never issues a D-Bus call.
/// </summary>
internal sealed class KdeTextDisplayCommand(
    CommandDescriptor descriptor,
    TimeSpan updateInterval,
    Func<string> getText,
    Func<CommandContext, Task>? action = null) : IDisplayCommand
{
    /// <summary>Shown while a cache was never filled successfully.</summary>
    public const string UnknownText = "—";

    public CommandDescriptor Descriptor { get; } = descriptor;

    public ButtonTargets SupportedTargets => ButtonTargets.TouchButton;

    public TimeSpan UpdateInterval { get; } = updateInterval;

    public string GetText(CommandContext ctx)
    {
        try
        {
            return getText();
        }
        catch (Exception)
        {
            return UnknownText;
        }
    }

    public async Task Execute(CommandContext ctx)
    {
        if (action is null)
        {
            return;
        }

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
