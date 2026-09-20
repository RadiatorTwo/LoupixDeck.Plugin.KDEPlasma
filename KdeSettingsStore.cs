using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Typed access to the plugin settings. The keys are public API towards the user's settings file
/// and must stay stable, so a settings file written by an older version keeps loading unchanged.
/// </summary>
internal sealed class KdeSettingsStore(IPluginSettings settings)
{
    public const string DesktopNamesKey = "display:desktopNames";
    public const string TimeoutKey = "behavior:dbusTimeoutMs";
    public const string BridgeInstalledKey = "bridge:installed";
    public const string BridgeVersionKey = "bridge:version";

    // Reserved for later rounds so their keys cannot collide:
    // "display:hiddenActivities", "display:overviewEffect", "display:monitorOrder".

    public const bool DefaultDesktopNames = true;
    public const int DefaultTimeoutMilliseconds = 2000;

    private const int MinimumTimeoutMilliseconds = 250;
    private const int MaximumTimeoutMilliseconds = 10000;

    /// <summary>Whether desktop buttons show the desktop name instead of its number.</summary>
    public bool ShowDesktopNames => settings.Get(DesktopNamesKey, DefaultDesktopNames);

    /// <summary>The per-call D-Bus timeout, clamped to a usable range.</summary>
    public int TimeoutMilliseconds
    {
        get
        {
            long stored = settings.Get(TimeoutKey, (long)DefaultTimeoutMilliseconds);
            return (int)Math.Clamp(stored, MinimumTimeoutMilliseconds, MaximumTimeoutMilliseconds);
        }
    }

    /// <summary>Whether the user installed the KWin bridge script. Absent in an older file, which
    /// reads as false and matches a profile that never had a bridge.</summary>
    public bool BridgeInstalled => settings.Get(BridgeInstalledKey, false);

    /// <summary>The script version that was written last, empty when none was.</summary>
    public string BridgeVersion => settings.Get(BridgeVersionKey, string.Empty) ?? string.Empty;

    /// <summary>Records what the bridge installation did, so the settings page can report it.</summary>
    public void SetBridgeInstalled(bool installed, string version)
    {
        settings.Set(BridgeInstalledKey, installed);
        settings.Set(BridgeVersionKey, installed ? version : string.Empty);
        settings.Save();
    }
}
