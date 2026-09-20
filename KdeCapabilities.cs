namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Immutable snapshot of what this KDE session can actually do. Commands that have no backing
/// interface are never offered, so the user never sees a command that only appears to work.
/// </summary>
internal sealed class KdeCapabilities
{
    public static readonly KdeCapabilities Empty = new();

    public bool HasKWin { get; init; }

    public bool HasKGlobalAccel { get; init; }

    public bool HasActivities { get; init; }

    public bool HasKRunner { get; init; }

    public bool HasScreenSaver { get; init; }

    public bool NightLightAvailable { get; init; }

    /// <summary>What the KWin bridge directory currently holds.</summary>
    public BridgeInstallState? Bridge { get; init; }

    /// <summary>True when a usable bridge script is installed, so its commands may be offered.</summary>
    public bool HasWindowBridge { get; init; }

    /// <summary>Global shortcut action names of the kwin component.</summary>
    public IReadOnlySet<string> KWinShortcuts { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>KWin effects that are supported on this system.</summary>
    public IReadOnlySet<string> SupportedEffects { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public string PlasmaVersion { get; init; } = string.Empty;

    public bool HasShortcut(string actionName) => KWinShortcuts.Contains(actionName);

    public bool HasEffect(string effectName) => SupportedEffects.Contains(effectName);
}
