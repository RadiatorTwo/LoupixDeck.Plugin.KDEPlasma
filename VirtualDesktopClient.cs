using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// A KWin virtual desktop. The id is a UUID and stays stable across renames.
/// <paramref name="Position"/> is the zero-based position KWin reports.
/// </summary>
internal readonly record struct VirtualDesktop(int Position, string Id, string Name)
{
    /// <summary>The one-based desktop number as shown to the user.</summary>
    public int Number => Position + 1;
}

/// <summary>
/// Caches the virtual desktop layout of KWin and keeps it up to date from D-Bus signals.
/// Desktops are addressed by their UUID so renaming or reordering never mis-targets a button.
/// </summary>
internal sealed class VirtualDesktopClient(KdeSession session) : IDisposable
{
    private const string Path = "/VirtualDesktopManager";
    private const string Interface = "org.kde.KWin.VirtualDesktopManager";

    private static readonly string[] WatchedSignals =
    [
        "currentChanged",
        "countChanged",
        "desktopCreated",
        "desktopRemoved",
        "desktopDataChanged"
    ];

    private readonly Lock _gate = new();
    private readonly List<IDisposable> _watches = [];

    private IReadOnlyList<VirtualDesktop> _desktops = [];
    private string _currentId = string.Empty;
    private bool _hasState;

    /// <summary>Raised when the cached desktop list or the current desktop changed.</summary>
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

    public IReadOnlyList<VirtualDesktop> Desktops
    {
        get
        {
            lock (_gate)
            {
                return _desktops;
            }
        }
    }

    public string CurrentId
    {
        get
        {
            lock (_gate)
            {
                return _currentId;
            }
        }
    }

    public VirtualDesktop? Current
    {
        get
        {
            lock (_gate)
            {
                foreach (VirtualDesktop desktop in _desktops)
                {
                    if (string.Equals(desktop.Id, _currentId, StringComparison.Ordinal))
                    {
                        return desktop;
                    }
                }

                return null;
            }
        }
    }

    public int Count => Desktops.Count;

    public async Task StartAsync()
    {
        await SeedAsync().ConfigureAwait(false);

        DBusClient? client = session.Client;
        if (client is null)
        {
            return;
        }

        foreach (string signal in WatchedSignals)
        {
            IDisposable? watch = await client.WatchSignalAsync(
                KdeServices.KWin,
                Path,
                Interface,
                signal,
                IgnoreBody,
                _ => OnSignal()).ConfigureAwait(false);

            if (watch is not null)
            {
                _watches.Add(watch);
            }
        }
    }

    /// <summary>Re-reads the whole desktop layout, for example after KWin restarted.</summary>
    public async Task SeedAsync()
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            Invalidate();
            return;
        }

        VariantValue desktops = await client.GetPropertyAsync(KdeServices.KWin, Path, Interface, "desktops")
            .ConfigureAwait(false);
        VariantValue current = await client.GetPropertyAsync(KdeServices.KWin, Path, Interface, "current")
            .ConfigureAwait(false);

        if (desktops.Type != VariantValueType.Array || current.Type != VariantValueType.String)
        {
            Invalidate();
            return;
        }

        List<VirtualDesktop> parsed = new(desktops.Count);
        try
        {
            for (int i = 0; i < desktops.Count; i++)
            {
                VariantValue entry = desktops.GetItem(i);
                if (entry.Type != VariantValueType.Struct || entry.Count < 3)
                {
                    continue;
                }

                parsed.Add(new VirtualDesktop(
                    ReadPosition(entry.GetItem(0)),
                    entry.GetItem(1).GetString(),
                    entry.GetItem(2).GetString()));
            }
        }
        catch (Exception)
        {
            // An unexpected payload shape must not take the plugin down; the cache stays stale instead.
            Invalidate();
            return;
        }

        parsed.Sort(static (left, right) => left.Position.CompareTo(right.Position));

        lock (_gate)
        {
            _desktops = parsed;
            _currentId = current.GetString();
            _hasState = true;
        }

        Changed?.Invoke();
    }

    /// <summary>Switches to a desktop by its stable UUID.</summary>
    public Task<bool> SetCurrentAsync(string desktopId)
    {
        DBusClient? client = session.Client;
        if (client is null || string.IsNullOrEmpty(desktopId))
        {
            return Task.FromResult(false);
        }

        return client.SetPropertyAsync(
            KdeServices.KWin,
            Path,
            Interface,
            "current",
            (ref MessageWriter writer) => writer.WriteVariantString(desktopId));
    }

    /// <summary>
    /// Appends a new virtual desktop at the end of the layout. KWin answers this call, so the
    /// reply is awaited; the cache updates itself from the desktopCreated signal afterwards.
    /// </summary>
    public Task<bool> CreateAsync(string name) => CreateAsync((uint)Count, name);

    /// <summary>Creates a new virtual desktop at a zero-based position.</summary>
    public Task<bool> CreateAsync(uint position, string name)
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            return Task.FromResult(false);
        }

        return client.CallAsync(
            KdeServices.KWin,
            Path,
            Interface,
            "createDesktop",
            "us",
            (ref MessageWriter writer) =>
            {
                writer.WriteUInt32(position);
                writer.WriteString(name);
            });
    }

    /// <summary>
    /// Removes the desktop with the given UUID. KWin refuses to remove the last remaining desktop,
    /// so the caller checks <see cref="Count"/> first.
    /// </summary>
    public Task<bool> RemoveAsync(string desktopId)
    {
        DBusClient? client = session.Client;
        if (client is null || string.IsNullOrEmpty(desktopId))
        {
            return Task.FromResult(false);
        }

        return client.CallAsync(
            KdeServices.KWin,
            Path,
            Interface,
            "removeDesktop",
            "s",
            (ref MessageWriter writer) => writer.WriteString(desktopId));
    }

    /// <summary>The desktop at the end of the layout, or null when the cache holds no state.</summary>
    public VirtualDesktop? Last
    {
        get
        {
            IReadOnlyList<VirtualDesktop> desktops = Desktops;
            return desktops.Count == 0 ? null : desktops[^1];
        }
    }

    /// <summary>Finds a desktop by its one-based number as shown to the user.</summary>
    public VirtualDesktop? FindByNumber(int number)
    {
        foreach (VirtualDesktop desktop in Desktops)
        {
            if (desktop.Number == number)
            {
                return desktop;
            }
        }

        return null;
    }

    /// <summary>Finds a desktop by its display name, ignoring case.</summary>
    public VirtualDesktop? FindByName(string name)
    {
        foreach (VirtualDesktop desktop in Desktops)
        {
            if (string.Equals(desktop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return desktop;
            }
        }

        return null;
    }

    public void Dispose()
    {
        foreach (IDisposable watch in _watches)
        {
            watch.Dispose();
        }

        _watches.Clear();
    }

    private void OnSignal()
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

    /// <summary>
    /// KWin documents the desktop position as a signed integer but sends an unsigned one,
    /// so both are accepted here.
    /// </summary>
    private static int ReadPosition(VariantValue value) => value.Type switch
    {
        VariantValueType.Int32 => value.GetInt32(),
        VariantValueType.UInt32 => (int)value.GetUInt32(),
        _ => 0
    };
}
