using LoupixDeck.PluginSdk;
using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>Well-known bus names of the KDE services this plugin talks to.</summary>
internal static class KdeServices
{
    public const string KWin = "org.kde.KWin";
    public const string KGlobalAccel = "org.kde.kglobalaccel";
    public const string ActivityManager = "org.kde.ActivityManager";
    public const string NightLight = "org.kde.KWin.NightLight";
    public const string PlasmaShell = "org.kde.plasmashell";
    public const string KRunner = "org.kde.krunner";
    public const string ScreenSaver = "org.freedesktop.ScreenSaver";

    /// <summary>Services whose restart has to be reflected in cached state.</summary>
    public static readonly string[] Watched =
    [
        KWin,
        KGlobalAccel,
        ActivityManager,
        NightLight,
        PlasmaShell
    ];
}

/// <summary>
/// Owns the session bus connection, decides whether this is a KDE session at all and reports
/// when one of the watched services changes its owner (a KWin or Plasma restart).
/// </summary>
internal sealed class KdeSession(IPluginLogger logger) : IDisposable
{
    private const string BusService = "org.freedesktop.DBus";
    private const string BusPath = "/org/freedesktop/DBus";
    private const string BusInterface = "org.freedesktop.DBus";

    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30)
    ];

    private readonly CancellationTokenSource _shutdown = new();

    private DBusConnection? _connection;
    private IDisposable? _nameOwnerWatch;

    /// <summary>The D-Bus helper, or null while the session is not connected.</summary>
    public DBusClient? Client { get; private set; }

    /// <summary>True when this is a KDE session with a reachable session bus.</summary>
    public bool IsSupported { get; private set; }

    /// <summary>The services that own a bus name right now.</summary>
    public IReadOnlySet<string> AvailableServices { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The raw connection, or null while the session is not connected.</summary>
    public DBusConnection? Connection => _connection;

    /// <summary>Raised when a watched service gained (true) or lost (false) its owner.</summary>
    public event Action<string, bool>? ServiceOwnerChanged;

    /// <summary>
    /// Raised once a connection is usable, including after a reconnect. Exported objects and owned
    /// bus names do not survive a reconnect, so everything that serves on the bus re-registers here.
    /// </summary>
    public event Action<DBusConnection>? ConnectionReady;

    /// <summary>Connects to the session bus and probes for KDE. Safe to call once, from Initialize.</summary>
    public async Task<bool> ConnectAsync()
    {
        string? address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address))
        {
            logger.Info("KDE Plasma: no session bus address, the plugin stays inactive.");
            return false;
        }

        try
        {
            DBusConnection connection = new(address);
            await connection.ConnectAsync().ConfigureAwait(false);

            _connection = connection;
            Client = new DBusClient(connection, logger);

            await RefreshServicesAsync().ConfigureAwait(false);

            if (!LooksLikeKde())
            {
                logger.Info("KDE Plasma: not a KDE session, the plugin stays inactive.");
                Disconnect();
                return false;
            }

            _nameOwnerWatch = await WatchNameOwnerChangesAsync().ConfigureAwait(false);
            IsSupported = true;
            _ = Task.Run(() => MonitorConnectionAsync(connection), _shutdown.Token);
            RaiseConnectionReady(connection);
            return true;
        }
        catch (Exception ex)
        {
            logger.Info($"KDE Plasma: cannot use the session bus ({ex.Message}), the plugin stays inactive.");
            Disconnect();
            return false;
        }
    }

    /// <summary>Re-reads which services currently own a bus name.</summary>
    public async Task RefreshServicesAsync()
    {
        DBusConnection? connection = _connection;
        if (connection is null)
        {
            return;
        }

        try
        {
            string[] names = await connection.ListServicesAsync().ConfigureAwait(false);
            AvailableServices = new HashSet<string>(names, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.Warn($"KDE Plasma: cannot list the session bus services: {ex.Message}");
        }
    }

    public bool HasService(string service) => AvailableServices.Contains(service);

    public void Dispose()
    {
        _shutdown.Cancel();
        _nameOwnerWatch?.Dispose();
        _nameOwnerWatch = null;
        Disconnect();
        _shutdown.Dispose();
    }

    /// <summary>Tells the subscribers about a usable connection without letting one of them break the setup.</summary>
    private void RaiseConnectionReady(DBusConnection connection)
    {
        try
        {
            ConnectionReady?.Invoke(connection);
        }
        catch (Exception ex)
        {
            logger.Warn($"KDE Plasma: a connection handler failed: {ex.Message}");
        }
    }

    private void Disconnect()
    {
        IsSupported = false;
        Client = null;
        _connection?.Dispose();
        _connection = null;
    }

    private bool LooksLikeKde()
    {
        if (AvailableServices.Contains(KdeServices.KWin))
        {
            return true;
        }

        string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
        return desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase);
    }

    private Task<IDisposable?> WatchNameOwnerChangesAsync()
    {
        DBusClient client = Client!;
        return client.WatchSignalAsync(
            BusService,
            BusPath,
            BusInterface,
            "NameOwnerChanged",
            ReadNameOwnerChanged,
            OnNameOwnerChanged);
    }

    private void OnNameOwnerChanged(NameOwnerChange change)
    {
        if (Array.IndexOf(KdeServices.Watched, change.Name) < 0)
        {
            return;
        }

        bool hasOwner = !string.IsNullOrEmpty(change.NewOwner);
        HashSet<string> services = new(AvailableServices, StringComparer.Ordinal);

        if (hasOwner)
        {
            services.Add(change.Name);
        }
        else
        {
            services.Remove(change.Name);
        }

        AvailableServices = services;
        logger.Info($"KDE Plasma: {change.Name} {(hasOwner ? "appeared" : "disappeared")}.");
        ServiceOwnerChanged?.Invoke(change.Name, hasOwner);
    }

    private async Task MonitorConnectionAsync(DBusConnection connection)
    {
        int attempt = 0;

        while (!_shutdown.IsCancellationRequested)
        {
            Exception? error;
            try
            {
                error = await connection.DisconnectedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            logger.Warn($"KDE Plasma: the session bus connection was lost ({error?.Message ?? "unknown reason"}), reconnecting.");
            IsSupported = false;

            TimeSpan delay = ReconnectDelays[Math.Min(attempt, ReconnectDelays.Length - 1)];
            attempt++;

            try
            {
                await Task.Delay(delay, _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (await ReconnectAsync().ConfigureAwait(false))
            {
                attempt = 0;
                return;
            }
        }
    }

    private async Task<bool> ReconnectAsync()
    {
        _nameOwnerWatch?.Dispose();
        _nameOwnerWatch = null;
        _connection?.Dispose();
        _connection = null;
        Client = null;

        if (!await ConnectAsync().ConfigureAwait(false))
        {
            return false;
        }

        foreach (string service in KdeServices.Watched)
        {
            ServiceOwnerChanged?.Invoke(service, HasService(service));
        }

        return true;
    }

    private static NameOwnerChange ReadNameOwnerChanged(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        string name = reader.ReadString();
        string oldOwner = reader.ReadString();
        string newOwner = reader.ReadString();
        return new NameOwnerChange(name, oldOwner, newOwner);
    }
}

internal readonly record struct NameOwnerChange(string Name, string OldOwner, string NewOwner);
