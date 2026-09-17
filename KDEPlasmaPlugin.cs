using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

public sealed class KDEPlasmaPlugin : LoupixPlugin
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(2);

    private KdeSession? _session;
    private KdeCapabilities _capabilities = KdeCapabilities.Empty;

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
        KdeSession session = new(host.Logger);
        _session = session;

        try
        {
            if (!session.ConnectAsync().WaitAsync(StartupTimeout).GetAwaiter().GetResult())
            {
                return;
            }

            _capabilities = new KdeCapabilityDetector(session).DetectAsync().WaitAsync(StartupTimeout).GetAwaiter().GetResult();
            host.Logger.Info(
                $"KDE Plasma: Plasma {(_capabilities.PlasmaVersion.Length > 0 ? _capabilities.PlasmaVersion : "unknown")}, " +
                $"{_capabilities.KWinShortcuts.Count} KWin shortcuts, effects: {string.Join(", ", _capabilities.SupportedEffects)}.");
        }
        catch (Exception ex)
        {
            host.Logger.Info($"KDE Plasma: initialization failed ({ex.Message}), the plugin stays inactive.");
            session.Dispose();
            _session = null;
        }
    }

    public override IEnumerable<IPluginCommand> GetCommands() => [];

    public override void Shutdown()
    {
        _session?.Dispose();
        _session = null;
        base.Shutdown();
    }
}
