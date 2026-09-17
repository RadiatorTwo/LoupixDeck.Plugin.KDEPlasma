using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

public sealed class KDEPlasmaPlugin : LoupixPlugin
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(2);

    private readonly List<IPluginCommand> _commands = [];

    private KdeSession? _session;
    private KdeCapabilities _capabilities = KdeCapabilities.Empty;
    private KWinClient? _kwin;
    private VirtualDesktopClient? _desktops;
    private ActivityManagerClient? _activities;
    private NightLightClient? _nightLight;
    private KGlobalAccelClient? _accel;
    private ScreenSaverClient? _screenSaver;
    private KRunnerClient? _krunner;
    private PlasmaVersionClient? _plasmaVersion;
    private KdeStateBinder? _binder;

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

            CreateClients(session);
            BuildCommands();

            _binder = new KdeStateBinder(host, _kwin!, _desktops!, _activities!, _nightLight!);
            _binder.Start();
            session.ServiceOwnerChanged += OnServiceOwnerChanged;

            // Seeding the caches is I/O, so it must not hold up the host startup.
            _ = Task.Run(StartClientsAsync);
        }
        catch (Exception ex)
        {
            host.Logger.Info($"KDE Plasma: initialization failed ({ex.Message}), the plugin stays inactive.");
            Shutdown();
        }
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

    /// <summary>Whether desktop buttons show names instead of numbers. Becomes a setting later.</summary>
    private bool ShowDesktopNames => true;

    public override void Shutdown()
    {
        if (_session is not null)
        {
            _session.ServiceOwnerChanged -= OnServiceOwnerChanged;
        }

        _binder?.Dispose();
        _binder = null;
        _desktops?.Dispose();
        _activities?.Dispose();
        _nightLight?.Dispose();
        _kwin?.Dispose();
        _session?.Dispose();

        _desktops = null;
        _activities = null;
        _nightLight = null;
        _kwin = null;
        _accel = null;
        _screenSaver = null;
        _krunner = null;
        _plasmaVersion = null;
        _session = null;

        _commands.Clear();
        base.Shutdown();
    }

    private void CreateClients(KdeSession session)
    {
        _accel = new KGlobalAccelClient(session);
        _kwin = new KWinClient(session);
        _desktops = new VirtualDesktopClient(session);
        _activities = new ActivityManagerClient(session);
        _nightLight = new NightLightClient(session);
        _screenSaver = new ScreenSaverClient(session);
        _krunner = new KRunnerClient(session);
        _plasmaVersion = new PlasmaVersionClient(session);
    }

    private void BuildCommands()
    {
        _commands.Clear();

        if (_capabilities.HasKGlobalAccel && _accel is not null)
        {
            _commands.AddRange(WindowCommands.Create(_accel, _capabilities));
            _commands.AddRange(OverviewCommands.Create(_accel, _capabilities));
        }

        if (_capabilities.HasKWin && _kwin is not null && _desktops is not null)
        {
            _commands.AddRange(VirtualDesktopCommands.Create(_kwin, _desktops));
            _commands.Add(ShowDesktopCommand.Create(_kwin));
            _commands.AddRange(KdeDisplayCommands.CreateDesktopDisplays(_desktops, () => ShowDesktopNames));
        }

        if (_capabilities.HasActivities && _activities is not null)
        {
            _commands.AddRange(ActivityCommands.Create(_activities));
            _commands.Add(KdeDisplayCommands.CreateActivityDisplay(_activities));
        }

        if (_plasmaVersion is not null)
        {
            _commands.Add(KdeDisplayCommands.CreateVersionDisplay(_plasmaVersion));
        }

        if (_screenSaver is not null && _krunner is not null)
        {
            _commands.AddRange(SessionCommands.Create(_screenSaver, _krunner, _capabilities));
        }

        if (_accel is not null && _nightLight is not null)
        {
            _commands.AddRange(NightColorCommands.Create(_accel, _nightLight, _capabilities));
        }
    }

    private async Task StartClientsAsync()
    {
        if (_capabilities.HasKWin)
        {
            await _kwin!.StartAsync().ConfigureAwait(false);
            await _desktops!.StartAsync().ConfigureAwait(false);
        }

        if (_capabilities.HasActivities)
        {
            await _activities!.StartAsync().ConfigureAwait(false);
        }

        if (_capabilities.NightLightAvailable)
        {
            await _nightLight!.StartAsync().ConfigureAwait(false);
        }

        await _plasmaVersion!.SeedAsync().ConfigureAwait(false);
        _binder?.ReplayAll();
    }

    private void OnServiceOwnerChanged(string service, bool hasOwner)
    {
        if (!hasOwner)
        {
            return;
        }

        // The service came back, so every cache has to be read again before the states are replayed.
        _ = Task.Run(StartClientsAsync);
    }
}
