using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>Probes the running KDE session and builds a <see cref="KdeCapabilities"/> snapshot.</summary>
internal sealed class KdeCapabilityDetector(KdeSession session, KWinBridgeInstaller bridgeInstaller)
{
    /// <summary>Effects the plugin exposes commands for.</summary>
    public static readonly string[] ProbedEffects = ["overview", "windowview"];

    private const string EffectsPath = "/Effects";
    private const string EffectsInterface = "org.kde.kwin.Effects";
    private const string KGlobalAccelComponentPath = "/component/kwin";
    private const string KGlobalAccelComponentInterface = "org.kde.kglobalaccel.Component";
    private const string NightLightPath = "/org/kde/KWin/NightLight";
    private const string NightLightInterface = "org.kde.KWin.NightLight";
    private const string PlasmaShellApplicationPath = "/MainApplication";
    private const string QtCoreApplicationInterface = "org.qtproject.Qt.QCoreApplication";

    public async Task<KdeCapabilities> DetectAsync()
    {
        DBusClient? client = session.Client;
        if (client is null)
        {
            return KdeCapabilities.Empty;
        }

        await session.RefreshServicesAsync().ConfigureAwait(false);

        bool hasKWin = session.HasService(KdeServices.KWin);
        bool hasKGlobalAccel = session.HasService(KdeServices.KGlobalAccel);

        HashSet<string> shortcuts = hasKGlobalAccel
            ? await ReadShortcutNamesAsync(client).ConfigureAwait(false)
            : [];

        HashSet<string> effects = hasKWin
            ? await ReadSupportedEffectsAsync(client).ConfigureAwait(false)
            : [];

        bool nightLight = session.HasService(KdeServices.NightLight)
                          && await ReadNightLightAvailableAsync(client).ConfigureAwait(false);

        string version = session.HasService(KdeServices.PlasmaShell)
            ? await ReadPlasmaVersionAsync(client).ConfigureAwait(false)
            : string.Empty;

        // Reading the installed script is plain file I/O, so the command set can be gated on it
        // while the commands are built, long before the script answers over the bus.
        BridgeInstallState bridge = bridgeInstaller.Inspect();

        return new KdeCapabilities
        {
            HasKWin = hasKWin,
            Bridge = bridge,
            HasWindowBridge = hasKWin && bridge.IsUsable,
            HasKGlobalAccel = hasKGlobalAccel,
            HasActivities = session.HasService(KdeServices.ActivityManager),
            HasKRunner = session.HasService(KdeServices.KRunner),
            HasScreenSaver = session.HasService(KdeServices.ScreenSaver),
            NightLightAvailable = nightLight,
            KWinShortcuts = shortcuts,
            SupportedEffects = effects,
            PlasmaVersion = version
        };
    }

    private static async Task<HashSet<string>> ReadShortcutNamesAsync(DBusClient client)
    {
        string[] names = await client.CallAsync(
            KdeServices.KGlobalAccel,
            KGlobalAccelComponentPath,
            KGlobalAccelComponentInterface,
            "shortcutNames",
            DBusClient.ReadStringArray,
            []).ConfigureAwait(false);

        return new HashSet<string>(names, StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> ReadSupportedEffectsAsync(DBusClient client)
    {
        bool[] supported = await client.CallAsync(
            KdeServices.KWin,
            EffectsPath,
            EffectsInterface,
            "areEffectsSupported",
            ReadBoolArray,
            [],
            "as",
            (ref MessageWriter writer) => writer.WriteArray(ProbedEffects)).ConfigureAwait(false);

        HashSet<string> effects = new(StringComparer.Ordinal);
        for (int i = 0; i < ProbedEffects.Length && i < supported.Length; i++)
        {
            if (supported[i])
            {
                effects.Add(ProbedEffects[i]);
            }
        }

        return effects;
    }

    private static async Task<bool> ReadNightLightAvailableAsync(DBusClient client)
    {
        VariantValue value = await client.GetPropertyAsync(
            KdeServices.NightLight,
            NightLightPath,
            NightLightInterface,
            "available").ConfigureAwait(false);

        return value.Type == VariantValueType.Bool && value.GetBool();
    }

    private static async Task<string> ReadPlasmaVersionAsync(DBusClient client)
    {
        VariantValue value = await client.GetPropertyAsync(
            KdeServices.PlasmaShell,
            PlasmaShellApplicationPath,
            QtCoreApplicationInterface,
            "applicationVersion").ConfigureAwait(false);

        return value.Type == VariantValueType.String ? value.GetString() : string.Empty;
    }

    private static bool[] ReadBoolArray(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        return reader.ReadArrayOfBool();
    }
}
