using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>Opens KRunner, optionally pre-filled with a query.</summary>
internal sealed class KRunnerClient(KdeSession session)
{
    private const string Path = "/App";
    private const string Interface = "org.kde.krunner.App";

    public Task<bool> DisplayAsync()
    {
        DBusClient? client = session.Client;
        return client is null
            ? Task.FromResult(false)
            : client.CallAsync(KdeServices.KRunner, Path, Interface, "display");
    }

    public Task<bool> QueryAsync(string query)
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            return Task.FromResult(false);
        }

        return client.CallAsync(
            KdeServices.KRunner,
            Path,
            Interface,
            "query",
            "s",
            (ref MessageWriter writer) => writer.WriteString(query));
    }
}
