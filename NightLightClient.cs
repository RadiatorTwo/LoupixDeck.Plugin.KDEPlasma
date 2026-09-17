using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Caches the Night Color state of KWin. The interface offers no toggle method, so switching
/// Night Color runs through the KGlobalAccel action while the state is read from here.
/// </summary>
internal sealed class NightLightClient(KdeSession session) : IDisposable
{
    private const string Path = "/org/kde/KWin/NightLight";
    private const string Interface = "org.kde.KWin.NightLight";

    private readonly Lock _gate = new();

    private IDisposable? _propertiesWatch;
    private bool _available;
    private bool _enabled;
    private bool _running;
    private uint _temperature;
    private bool _hasState;

    /// <summary>Raised when the cached Night Color state changed.</summary>
    public event Action? Changed;

    public bool HasState
    {
        get
        {
            lock (_gate)
            {
                return _hasState;
            }
        }
    }

    public bool Available
    {
        get
        {
            lock (_gate)
            {
                return _available;
            }
        }
    }

    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    public bool Running
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public uint Temperature
    {
        get
        {
            lock (_gate)
            {
                return _temperature;
            }
        }
    }

    public async Task StartAsync()
    {
        await SeedAsync().ConfigureAwait(false);

        DBusClient? client = session.Client;
        if (client is null)
        {
            return;
        }

        _propertiesWatch = await client.WatchSignalAsync(
            KdeServices.NightLight,
            Path,
            DBusClient.PropertiesInterface,
            "PropertiesChanged",
            IgnoreBody,
            _ => OnPropertiesChanged()).ConfigureAwait(false);
    }

    /// <summary>Re-reads all cached properties.</summary>
    public async Task SeedAsync()
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            Invalidate();
            return;
        }

        VariantValue available = await Read(client, "available").ConfigureAwait(false);
        VariantValue enabled = await Read(client, "enabled").ConfigureAwait(false);
        VariantValue running = await Read(client, "running").ConfigureAwait(false);
        VariantValue temperature = await Read(client, "currentTemperature").ConfigureAwait(false);

        if (available.Type != VariantValueType.Bool)
        {
            Invalidate();
            return;
        }

        lock (_gate)
        {
            _available = available.GetBool();
            _enabled = enabled.Type == VariantValueType.Bool && enabled.GetBool();
            _running = running.Type == VariantValueType.Bool && running.GetBool();
            _temperature = temperature.Type == VariantValueType.UInt32 ? temperature.GetUInt32() : 0;
            _hasState = true;
        }

        Changed?.Invoke();
    }

    public void Dispose()
    {
        _propertiesWatch?.Dispose();
        _propertiesWatch = null;
    }

    private Task<VariantValue> Read(DBusClient client, string property)
        => client.GetPropertyAsync(KdeServices.NightLight, Path, Interface, property);

    private void OnPropertiesChanged()
    {
        // The signal handler runs on the connection read loop, so the re-seed happens off it.
        _ = Task.Run(SeedAsync);
    }

    private void Invalidate()
    {
        lock (_gate)
        {
            _hasState = false;
        }
    }

    private static bool IgnoreBody(Message message, object? state) => true;
}
