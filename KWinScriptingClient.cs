using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Loads and unloads KWin scripts. KWin only constructs a script in <c>loadScript</c> and runs it in
/// <c>start</c>, and <c>unloadScript</c> deletes the script asynchronously, so both steps are always
/// paired here and an unload is awaited by polling.
/// </summary>
internal sealed class KWinScriptingClient(KdeSession session)
{
    private const string Path = "/Scripting";
    private const string Interface = "org.kde.kwin.Scripting";

    private static readonly TimeSpan UnloadPollInterval = TimeSpan.FromMilliseconds(100);
    private const int UnloadPollAttempts = 5;

    /// <summary>Whether KWin currently holds a script with that plugin name.</summary>
    public async Task<bool> IsLoadedAsync(string pluginName)
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            return false;
        }

        return await client.CallAsync(
            KdeServices.KWin,
            Path,
            Interface,
            "isScriptLoaded",
            DBusClient.ReadBool,
            false,
            "s",
            (ref MessageWriter writer) => writer.WriteString(pluginName)).ConfigureAwait(false);
    }

    /// <summary>Loads the script and runs it. Returns false when KWin refused the file.</summary>
    public async Task<bool> LoadAsync(string scriptPath, string pluginName)
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            return false;
        }

        // A negative id means KWin already holds a script under that plugin name and did not load
        // the file, which start() below would silently turn into "nothing happened".
        int id = await client.CallAsync(
            KdeServices.KWin,
            Path,
            Interface,
            "loadScript",
            DBusClient.ReadInt32,
            -1,
            "ss",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(scriptPath);
                writer.WriteString(pluginName);
            }).ConfigureAwait(false);

        if (id < 0)
        {
            return false;
        }

        return await StartAsync().ConfigureAwait(false);
    }

    /// <summary>Runs every loaded script that is not running yet. KWin itself calls this on a config change.</summary>
    public Task<bool> StartAsync()
    {
        DBusClient? client = session.Client;
        return client is null
            ? Task.FromResult(false)
            : client.CallAsync(KdeServices.KWin, Path, Interface, "start");
    }

    /// <summary>Unloads the script and waits until KWin really dropped it.</summary>
    public async Task<bool> UnloadAsync(string pluginName)
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            return false;
        }

        await client.CallAsync(
            KdeServices.KWin,
            Path,
            Interface,
            "unloadScript",
            DBusClient.ReadBool,
            false,
            "s",
            (ref MessageWriter writer) => writer.WriteString(pluginName)).ConfigureAwait(false);

        // KWin deletes the script later, so an immediate reload would be refused as "already loaded".
        for (int attempt = 0; attempt < UnloadPollAttempts; attempt++)
        {
            if (!await IsLoadedAsync(pluginName).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(UnloadPollInterval).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Replaces a running script with the file on disk, whether or not one is loaded.</summary>
    public async Task<bool> ReloadAsync(string scriptPath, string pluginName)
    {
        if (await IsLoadedAsync(pluginName).ConfigureAwait(false))
        {
            await UnloadAsync(pluginName).ConfigureAwait(false);
        }

        return await LoadAsync(scriptPath, pluginName).ConfigureAwait(false);
    }
}
