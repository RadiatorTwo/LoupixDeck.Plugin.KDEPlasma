using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>Locks the session through the freedesktop screensaver interface.</summary>
internal sealed class ScreenSaverClient(KdeSession session)
{
    private const string Path = "/ScreenSaver";
    private const string Interface = "org.freedesktop.ScreenSaver";

    public Task<bool> LockAsync()
    {
        DBusClient? client = session.Client;
        return client is null
            ? Task.FromResult(false)
            : client.CallAsync(KdeServices.ScreenSaver, Path, Interface, "Lock");
    }
}
