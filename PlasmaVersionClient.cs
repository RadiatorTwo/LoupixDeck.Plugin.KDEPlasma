using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Reads the Plasma version from plasmashell. The value only changes when plasmashell restarts,
/// so it is read once and cached for the session.
/// </summary>
internal sealed class PlasmaVersionClient(KdeSession session)
{
    private const string Path = "/MainApplication";
    private const string Interface = "org.qtproject.Qt.QCoreApplication";

    private readonly Lock _gate = new();

    private string _version = string.Empty;

    /// <summary>The cached version, or an empty string when it is unknown.</summary>
    public string Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public async Task SeedAsync()
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            Set(string.Empty);
            return;
        }

        VariantValue value = await client
            .GetPropertyAsync(KdeServices.PlasmaShell, Path, Interface, "applicationVersion")
            .ConfigureAwait(false);

        Set(value.Type == VariantValueType.String ? value.GetString() : string.Empty);
    }

    private void Set(string version)
    {
        lock (_gate)
        {
            _version = version;
        }
    }
}
