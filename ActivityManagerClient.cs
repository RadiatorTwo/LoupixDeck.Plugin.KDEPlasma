using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>A KDE Activity. The id is a UUID and stays stable across renames.</summary>
internal readonly record struct KdeActivity(string Id, string Name, string Icon);

/// <summary>Caches the Activities of the session and keeps them up to date from D-Bus signals.</summary>
internal sealed class ActivityManagerClient(KdeSession session) : IDisposable
{
    private const string Path = "/ActivityManager/Activities";
    private const string Interface = "org.kde.ActivityManager.Activities";

    private static readonly string[] WatchedSignals =
    [
        "CurrentActivityChanged",
        "ActivityAdded",
        "ActivityRemoved",
        "ActivityChanged",
        "ActivityNameChanged",
        "ActivityIconChanged"
    ];

    private readonly Lock _gate = new();
    private readonly List<IDisposable> _watches = [];

    private IReadOnlyList<KdeActivity> _activities = [];
    private string _currentId = string.Empty;
    private bool _hasState;

    /// <summary>Raised when the cached Activity list or the current Activity changed.</summary>
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

    public IReadOnlyList<KdeActivity> Activities
    {
        get
        {
            lock (_gate)
            {
                return _activities;
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

    public KdeActivity? Current
    {
        get
        {
            lock (_gate)
            {
                foreach (KdeActivity activity in _activities)
                {
                    if (string.Equals(activity.Id, _currentId, StringComparison.Ordinal))
                    {
                        return activity;
                    }
                }

                return null;
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

        foreach (string signal in WatchedSignals)
        {
            IDisposable? watch = await client.WatchSignalAsync(
                KdeServices.ActivityManager,
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

    /// <summary>Re-reads the Activity list and the current Activity.</summary>
    public async Task SeedAsync()
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            Invalidate();
            return;
        }

        List<KdeActivity> activities = await client.CallAsync(
            KdeServices.ActivityManager,
            Path,
            Interface,
            "ListActivitiesWithInformation",
            ReadActivities,
            []).ConfigureAwait(false);

        string current = await client.CallAsync(
            KdeServices.ActivityManager,
            Path,
            Interface,
            "CurrentActivity",
            DBusClient.ReadString,
            string.Empty).ConfigureAwait(false);

        if (activities.Count == 0 && current.Length == 0)
        {
            Invalidate();
            return;
        }

        lock (_gate)
        {
            _activities = activities;
            _currentId = current;
            _hasState = true;
        }

        Changed?.Invoke();
    }

    /// <summary>Switches to an Activity by its stable UUID.</summary>
    public Task<bool> SetCurrentAsync(string activityId)
    {
        DBusClient? client = session.Client;
        if (client is null || string.IsNullOrEmpty(activityId))
        {
            return Task.FromResult(false);
        }

        return client.CallAsync(
            KdeServices.ActivityManager,
            Path,
            Interface,
            "SetCurrentActivity",
            DBusClient.ReadBool,
            false,
            "s",
            (ref MessageWriter writer) => writer.WriteString(activityId));
    }

    public Task<bool> NextAsync() => Call("NextActivity");

    public Task<bool> PreviousAsync() => Call("PreviousActivity");

    public void Dispose()
    {
        foreach (IDisposable watch in _watches)
        {
            watch.Dispose();
        }

        _watches.Clear();
    }

    private Task<bool> Call(string member)
    {
        DBusClient? client = session.Client;
        return client is null
            ? Task.FromResult(false)
            : client.CallAsync(KdeServices.ActivityManager, Path, Interface, member);
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

    private static List<KdeActivity> ReadActivities(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        List<KdeActivity> activities = [];

        ArrayEnd end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            // The struct is (id, name, description, icon, state).
            string id = reader.ReadString();
            string name = reader.ReadString();
            reader.ReadString();
            string icon = reader.ReadString();
            reader.ReadInt32();

            activities.Add(new KdeActivity(id, name, icon));
        }

        return activities;
    }

    private static bool IgnoreBody(Message message, object? state) => true;
}
