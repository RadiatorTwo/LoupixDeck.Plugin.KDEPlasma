using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Invokes KWin actions through KGlobalAccel. The plugin triggers the semantic KDE action,
/// so a user who rebinds the keyboard shortcut keeps the LoupixDeck button working.
/// </summary>
internal sealed class KGlobalAccelClient(KdeSession session)
{
    public const string KWinComponent = "kwin";

    private const string ComponentPath = "/component/kwin";
    private const string ComponentInterface = "org.kde.kglobalaccel.Component";

    /// <summary>Invokes a KWin action by its stable action name.</summary>
    public Task<bool> InvokeShortcutAsync(string actionName)
    {
        DBusClient? client = session.Client;
        if (client is null || string.IsNullOrEmpty(actionName))
        {
            return Task.FromResult(false);
        }

        return client.CallAsync(
            KdeServices.KGlobalAccel,
            ComponentPath,
            ComponentInterface,
            "invokeShortcut",
            "s",
            (ref MessageWriter writer) => writer.WriteString(actionName));
    }

    /// <summary>Reads the action names the kwin component currently offers.</summary>
    public async Task<IReadOnlySet<string>> GetShortcutNamesAsync()
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        string[] names = await client.CallAsync(
            KdeServices.KGlobalAccel,
            ComponentPath,
            ComponentInterface,
            "shortcutNames",
            DBusClient.ReadStringArray,
            []).ConfigureAwait(false);

        return new HashSet<string>(names, StringComparer.Ordinal);
    }
}
