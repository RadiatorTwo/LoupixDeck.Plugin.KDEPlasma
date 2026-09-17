using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

public sealed class KDEPlasmaPlugin : LoupixPlugin
{
    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "kdeplasma",
        Name = "KDEPlasma",
        Version = new Version(1, 0, 0),
        SdkVersion = SdkInfo.Version,
        Author = "",
        Description = ""
    };

    public override void Initialize(IPluginHost host)
    {
    }

    public override IEnumerable<IPluginCommand> GetCommands() => [];
}
