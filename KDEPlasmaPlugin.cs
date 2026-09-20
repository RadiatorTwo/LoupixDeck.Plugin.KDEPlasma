using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

public sealed class KDEPlasmaPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage
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
    private KWinBridgeInstaller? _bridgeInstaller;
    private KWinBridgeClient? _bridge;
    private ScreenSaverClient? _screenSaver;
    private KRunnerClient? _krunner;
    private PlasmaVersionClient? _plasmaVersion;
    private KdeStateBinder? _binder;
    private KdeFolderGrid? _grid;
    private KdeSettingsStore? _settings;
    private IPluginHost? _host;

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
        _host = host;
        _settings = new KdeSettingsStore(host.Settings);

        KdeSession session = new(host.Logger);
        _session = session;

        try
        {
            if (!session.ConnectAsync().WaitAsync(StartupTimeout).GetAwaiter().GetResult())
            {
                return;
            }

            _bridgeInstaller = new KWinBridgeInstaller(host.Logger);
            _capabilities = new KdeCapabilityDetector(session, _bridgeInstaller).DetectAsync().WaitAsync(StartupTimeout).GetAwaiter().GetResult();
            host.Logger.Info(
                $"KDE Plasma: Plasma {(_capabilities.PlasmaVersion.Length > 0 ? _capabilities.PlasmaVersion : "unknown")}, " +
                $"{_capabilities.KWinShortcuts.Count} KWin shortcuts, effects: {string.Join(", ", _capabilities.SupportedEffects)}.");

            CreateClients(session);
            ApplySettings();
            _grid = KdeFolderGridResolver.Resolve(host);
            BuildCommands();

            _binder = new KdeStateBinder(host, _kwin!, _desktops!, _activities!, _nightLight!, _bridge!);
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

    /// <summary>mdi-monitor-dashboard - the card glyph of the KDE Plasma category.</summary>
    private const string GroupGlyph = "\U000F0A07";

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new CommandGroupDescriptor
        {
            Group = KdeCommands.Group,
            Description = "Virtual desktops, windows, Activities, Overview, Night Color and session",
            Icon = GroupGlyph
        }
    ];

    /// <summary>
    /// Contributes the whole picker tree below a single root named after the command group, so the
    /// plugin shows up as one category with sections instead of several top-level categories.
    /// </summary>
    public Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target)
    {
        return Task.FromResult(KdeMenuTree.Build(_commands, _desktops, _activities));
    }

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema => KdeSettingsPage.BuildSchema();

    public IReadOnlyList<PluginSettingAction> SettingsActions =>
    [
        new PluginSettingAction
        {
            Label = "Test detected capabilities",
            Invoke = () => KdeSettingsPage.TestCapabilitiesAsync(_session, _bridgeInstaller, _desktops, _activities, _nightLight)
        }
    ];

    public void OnSettingsSaved()
    {
        ApplySettings();

        IPluginHost? host = _host;
        if (host is null)
        {
            return;
        }

        host.RequestButtonRefresh(KdeDisplayCommands.CurrentDesktopName);
        host.RequestButtonRefresh(KdeDisplayCommands.CurrentDesktopNameName);
        host.RequestButtonRefresh(KdeDisplayCommands.DesktopFolderName);
    }

    /// <summary>Whether desktop buttons show names instead of numbers.</summary>
    private bool ShowDesktopNames => _settings?.ShowDesktopNames ?? KdeSettingsStore.DefaultDesktopNames;

    private void ApplySettings()
    {
        DBusClient? client = _session?.Client;
        if (client is not null && _settings is not null)
        {
            client.TimeoutMilliseconds = _settings.TimeoutMilliseconds;
        }
    }

    public override void Shutdown()
    {
        if (_session is not null)
        {
            _session.ServiceOwnerChanged -= OnServiceOwnerChanged;
        }

        _binder?.Dispose();
        _binder = null;
        _bridge?.Dispose();
        _desktops?.Dispose();
        _activities?.Dispose();
        _nightLight?.Dispose();
        _kwin?.Dispose();
        _session?.Dispose();

        _bridge = null;
        _bridgeInstaller = null;
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
        _bridge = new KWinBridgeClient(session, _host!.Logger, _accel, _bridgeInstaller!);
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
            _commands.Add(KdeDisplayCommands.CreateDesktopFolder(_desktops, _grid!, () => ShowDesktopNames));
        }

        if (_capabilities.HasActivities && _activities is not null)
        {
            _commands.AddRange(ActivityCommands.Create(_activities));
            _commands.Add(KdeDisplayCommands.CreateActivityDisplay(_activities));
            _commands.Add(KdeDisplayCommands.CreateActivityFolder(_activities, _grid!));
        }

        if (_capabilities.HasWindowBridge && _bridge is not null)
        {
            _commands.AddRange(KdeDisplayCommands.CreateActiveWindowDisplays(_bridge));
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

        if (_bridge is not null)
        {
            await _bridge.StartAsync().ConfigureAwait(false);
        }

        _binder?.ReplayAll();
    }

    private async Task ReseedClientsAsync()
    {
        if (_capabilities.HasKWin)
        {
            await _kwin!.SeedAsync().ConfigureAwait(false);
            await _desktops!.SeedAsync().ConfigureAwait(false);
        }

        if (_capabilities.HasActivities)
        {
            await _activities!.SeedAsync().ConfigureAwait(false);
        }

        if (_capabilities.NightLightAvailable)
        {
            await _nightLight!.SeedAsync().ConfigureAwait(false);
        }

        await _plasmaVersion!.SeedAsync().ConfigureAwait(false);

        if (_bridge is not null)
        {
            // KWin drops every loaded script when it restarts, so the bridge has to be put back.
            await _bridge.SeedAsync().ConfigureAwait(false);
        }

        _binder?.ReplayAll();
    }

    private void OnServiceOwnerChanged(string service, bool hasOwner)
    {
        if (!hasOwner)
        {
            return;
        }

        // The service came back, so every cache has to be read again before the states are replayed.
        // Only the caches are refreshed here; the signal subscriptions survive an owner change.
        _ = Task.Run(ReseedClientsAsync);
    }
}
