using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Talks to the main KWin object. Covers desktop navigation by position and the Show Desktop
/// toggle including its state, which KWin reports through a property and a signal.
/// </summary>
internal sealed class KWinClient(KdeSession session) : IDisposable
{
    private const string Path = "/KWin";
    private const string Interface = "org.kde.KWin";

    private readonly Lock _gate = new();

    private IDisposable? _showingDesktopWatch;
    private bool _showingDesktop;
    private bool _hasState;

    /// <summary>Raised when the cached Show Desktop state changed.</summary>
    public event Action? ShowingDesktopChanged;

    /// <summary>The cached Show Desktop state, or null when it was never read successfully.</summary>
    public bool? ShowingDesktop
    {
        get
        {
            lock (_gate)
            {
                return _hasState ? _showingDesktop : null;
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

        _showingDesktopWatch = await client.WatchSignalAsync(
            KdeServices.KWin,
            Path,
            Interface,
            "showingDesktopChanged",
            DBusClient.ReadBool,
            OnShowingDesktopChanged).ConfigureAwait(false);
    }

    /// <summary>Re-reads the cached state, for example after KWin restarted.</summary>
    public async Task SeedAsync()
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            Invalidate();
            return;
        }

        VariantValue value = await client.GetPropertyAsync(KdeServices.KWin, Path, Interface, "showingDesktop")
            .ConfigureAwait(false);

        if (value.Type != VariantValueType.Bool)
        {
            Invalidate();
            return;
        }

        OnShowingDesktopChanged(value.GetBool());
    }

    public Task<bool> NextDesktopAsync() => Call("nextDesktop");

    public Task<bool> PreviousDesktopAsync() => Call("previousDesktop");

    public Task<bool> SetCurrentDesktopAsync(int position)
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
            "setCurrentDesktop",
            DBusClient.ReadBool,
            false,
            "i",
            (ref MessageWriter writer) => writer.WriteInt32(position));
    }

    public Task<bool> ShowDesktopAsync(bool show)
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
            "showDesktop",
            "b",
            (ref MessageWriter writer) => writer.WriteBool(show));
    }

    /// <summary>Toggles Show Desktop based on the cached state.</summary>
    public Task<bool> ToggleShowDesktopAsync() => ShowDesktopAsync(!(ShowingDesktop ?? false));

    public void Dispose()
    {
        _showingDesktopWatch?.Dispose();
        _showingDesktopWatch = null;
    }

    private Task<bool> Call(string member)
    {
        DBusClient? client = session.Client;
        return client is null
            ? Task.FromResult(false)
            : client.CallAsync(KdeServices.KWin, Path, Interface, member);
    }

    private void OnShowingDesktopChanged(bool showing)
    {
        lock (_gate)
        {
            if (_hasState && _showingDesktop == showing)
            {
                return;
            }

            _showingDesktop = showing;
            _hasState = true;
        }

        ShowingDesktopChanged?.Invoke();
    }

    private void Invalidate()
    {
        lock (_gate)
        {
            _hasState = false;
        }
    }
}
