using System.Diagnostics;
using System.Text.Json;
using LoupixDeck.PluginSdk;
using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>The active window as the bridge script last reported it.</summary>
internal sealed record ActiveWindowInfo(
    bool Present,
    string Title,
    string AppId,
    string ResourceClass,
    bool Maximized,
    bool KeepAbove,
    bool FullScreen,
    bool Minimized)
{
    public static readonly ActiveWindowInfo None = new(false, string.Empty, string.Empty, string.Empty, false, false, false, false);

    /// <summary>The window name to put on a button: the application id, or the class as a fallback.</summary>
    public string DisplayAppId => AppId.Length > 0 ? AppId : ResourceClass;
}

/// <summary>An output as the bridge script last reported it.</summary>
internal sealed record BridgeOutput(string Name, string Manufacturer, string Model)
{
    /// <summary>A readable label, falling back to the connector name.</summary>
    public string DisplayName => Model.Length > 0 && !string.Equals(Model, Name, StringComparison.Ordinal)
        ? $"{Name} ({Model})"
        : Name;
}

/// <summary>
/// The plugin side of the KWin bridge. It keeps the state the script reports, loads the script into
/// KWin and sends the two commands that need a script to run at all.
/// </summary>
internal sealed class KWinBridgeClient : IDisposable
{
    /// <summary>How often a missing script may be re-asserted while nothing answers.</summary>
    private static readonly TimeSpan ReassertInterval = TimeSpan.FromSeconds(10);

    private readonly KdeSession _session;
    private readonly IPluginLogger _logger;
    private readonly KGlobalAccelClient _accel;
    private readonly KWinBridgeEndpoint _endpoint;
    private readonly KWinScriptingClient _scripting;
    private readonly KWinBridgeInstaller _installer;
    private readonly Lock _gate = new();

    private ActiveWindowInfo _activeWindow = ActiveWindowInfo.None;
    private IReadOnlyList<BridgeOutput> _outputs = [];
    private string _scriptVersion = string.Empty;
    private bool _connected;
    private long _lastReassertTimestamp;
    private bool _attached;

    public KWinBridgeClient(KdeSession session, IPluginLogger logger, KGlobalAccelClient accel, KWinBridgeInstaller installer)
    {
        _session = session;
        _logger = logger;
        _accel = accel;
        _installer = installer;
        _scripting = new KWinScriptingClient(session);
        _endpoint = new KWinBridgeEndpoint(logger);

        _endpoint.HelloReceived += OnHelloReceived;
        _endpoint.EventReceived += OnEventReceived;
        session.ConnectionReady += OnConnectionReady;
    }

    /// <summary>Raised when any cached bridge state changed.</summary>
    public event Action? Changed;

    /// <summary>True while a script with a matching protocol is talking to the plugin.</summary>
    public bool Connected
    {
        get
        {
            lock (_gate)
            {
                return _connected;
            }
        }
    }

    /// <summary>The version the running script announced, empty while none did.</summary>
    public string ScriptVersion
    {
        get
        {
            lock (_gate)
            {
                return _scriptVersion;
            }
        }
    }

    /// <summary>The active window, or <see cref="ActiveWindowInfo.None"/> while nothing was reported.</summary>
    public ActiveWindowInfo ActiveWindow
    {
        get
        {
            lock (_gate)
            {
                return _activeWindow;
            }
        }
    }

    /// <summary>The outputs the script reported, in the order KWin lists them.</summary>
    public IReadOnlyList<BridgeOutput> Outputs
    {
        get
        {
            lock (_gate)
            {
                return _outputs;
            }
        }
    }

    /// <summary>Publishes the endpoint and loads the script into KWin.</summary>
    public async Task StartAsync()
    {
        await AttachAsync().ConfigureAwait(false);
        await LoadScriptAsync().ConfigureAwait(false);
    }

    /// <summary>Loads the script again, for example after KWin restarted and dropped it.</summary>
    public async Task SeedAsync()
    {
        await AttachAsync().ConfigureAwait(false);
        await LoadScriptAsync().ConfigureAwait(false);
    }

    /// <summary>Moves the active window to the desktop with that id. Needs a running script.</summary>
    public Task<bool> MoveActiveWindowToDesktopAsync(string desktopId)
    {
        return string.IsNullOrWhiteSpace(desktopId)
            ? Task.FromResult(false)
            : SendCommandAsync(JsonSerializer.Serialize(new
            {
                v = KWinBridgeProtocol.Version,
                c = KWinBridgeProtocol.MoveToDesktopCommand,
                desktopId
            }));
    }

    /// <summary>Moves the active window to the output with that name. Needs a running script.</summary>
    public Task<bool> MoveActiveWindowToOutputAsync(string outputName)
    {
        return string.IsNullOrWhiteSpace(outputName)
            ? Task.FromResult(false)
            : SendCommandAsync(JsonSerializer.Serialize(new
            {
                v = KWinBridgeProtocol.Version,
                c = KWinBridgeProtocol.MoveToOutputCommand,
                output = outputName
            }));
    }

    public void Dispose()
    {
        _session.ConnectionReady -= OnConnectionReady;
        _endpoint.HelloReceived -= OnHelloReceived;
        _endpoint.EventReceived -= OnEventReceived;
    }

    /// <summary>
    /// Hands the command to the script. The script has no way of being called directly, so it is
    /// woken through the global shortcut it registered and then pulls the command itself.
    /// </summary>
    private async Task<bool> SendCommandAsync(string commandJson)
    {
        if (!Connected)
        {
            // The script may have been dropped by a KWin restart that nothing else noticed yet.
            await ReassertScriptAsync().ConfigureAwait(false);
        }

        _endpoint.PostCommand(commandJson);
        return await _accel.InvokeShortcutAsync(KWinBridgeProtocol.CommandShortcut).ConfigureAwait(false);
    }

    private void OnConnectionReady(DBusConnection connection)
    {
        // Runs while the session is still connecting, so the publishing must not be awaited here.
        _ = Task.Run(AttachAsync);
    }

    private async Task AttachAsync()
    {
        DBusConnection? connection = _session.Connection;
        if (connection is null)
        {
            return;
        }

        bool attached = await _endpoint.AttachAsync(connection).ConfigureAwait(false);

        lock (_gate)
        {
            _attached = attached;
        }
    }

    /// <summary>Loads the installed script into KWin, replacing a copy that is still running.</summary>
    private async Task<bool> LoadScriptAsync()
    {
        lock (_gate)
        {
            if (!_attached)
            {
                return false;
            }

            _connected = false;
        }

        if (!_installer.Inspect().IsUsable)
        {
            return false;
        }

        return await _scripting
            .ReloadAsync(KWinBridgeInstaller.ScriptPath, KWinBridgeProtocol.ScriptPluginName)
            .ConfigureAwait(false);
    }

    /// <summary>Re-loads the script at most once per interval, so a broken script cannot spin.</summary>
    private async Task ReassertScriptAsync()
    {
        long now = Stopwatch.GetTimestamp();

        lock (_gate)
        {
            if (_lastReassertTimestamp != 0 && Stopwatch.GetElapsedTime(_lastReassertTimestamp, now) < ReassertInterval)
            {
                return;
            }

            _lastReassertTimestamp = now;
        }

        await LoadScriptAsync().ConfigureAwait(false);
    }

    private void OnHelloReceived(string version)
    {
        lock (_gate)
        {
            _connected = true;
            _scriptVersion = version;
        }

        _logger.Info($"KDE Plasma: the KWin bridge script {version} is connected.");
        Changed?.Invoke();
    }

    private void OnEventReceived(string type, JsonElement data)
    {
        bool changed = type switch
        {
            KWinBridgeProtocol.ActiveWindowChanged or KWinBridgeProtocol.WindowStateChanged => ApplyWindow(data),
            KWinBridgeProtocol.OutputListChanged => ApplyOutputs(data),
            // The desktop events are already covered by the KWin D-Bus interface, so they only
            // confirm that the script is alive.
            _ => false
        };

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private bool ApplyWindow(JsonElement data)
    {
        ActiveWindowInfo info = ReadWindow(data);

        lock (_gate)
        {
            if (_activeWindow == info)
            {
                return false;
            }

            _activeWindow = info;
        }

        return true;
    }

    private bool ApplyOutputs(JsonElement data)
    {
        List<BridgeOutput> outputs = [];

        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("outputs", out JsonElement array)
            && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement entry in array.EnumerateArray())
            {
                string name = ReadString(entry, "name");
                if (name.Length > 0)
                {
                    outputs.Add(new BridgeOutput(name, ReadString(entry, "manufacturer"), ReadString(entry, "model")));
                }
            }
        }

        lock (_gate)
        {
            if (_outputs.Count == outputs.Count && _outputs.SequenceEqual(outputs))
            {
                return false;
            }

            _outputs = outputs;
        }

        return true;
    }

    private static ActiveWindowInfo ReadWindow(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !ReadBool(data, "present"))
        {
            return ActiveWindowInfo.None;
        }

        return new ActiveWindowInfo(
            true,
            ReadString(data, "title"),
            ReadString(data, "appId"),
            ReadString(data, "resourceClass"),
            ReadBool(data, "maximized"),
            ReadBool(data, "keepAbove"),
            ReadBool(data, "fullScreen"),
            ReadBool(data, "minimized"));
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.True;
    }
}
