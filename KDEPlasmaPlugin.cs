using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

public sealed class KDEPlasmaPlugin : LoupixPlugin
{
    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "kdeplasma",
        Name = "KDE Plasma",
        Version = new Version(1, 0, 0),
        SdkVersion = SdkInfo.Version,
        Author = "RadiatorTwo",
        Description = "KDE Plasma desktop control: virtual desktops, window actions, Activities, Overview, Night Color and session commands."
    };

    public override void Initialize(IPluginHost host)
    {
    }

    public override IEnumerable<IPluginCommand> GetCommands() => [];
}
