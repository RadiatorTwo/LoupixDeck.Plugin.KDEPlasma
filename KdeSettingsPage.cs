using System.Globalization;
using System.Text;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>Builds the declarative settings page and its capability test.</summary>
internal static class KdeSettingsPage
{
    public static IReadOnlyList<PluginSettingDescriptor> BuildSchema() =>
    [
        new PluginSettingDescriptor
        {
            Key = "__heading_display",
            Label = "Display",
            Kind = PluginSettingKind.Heading
        },
        new PluginSettingDescriptor
        {
            Key = KdeSettingsStore.DesktopNamesKey,
            Label = "Show desktop names instead of numbers",
            Kind = PluginSettingKind.Toggle,
            Description = "Applies to the desktop buttons and the virtual desktops folder",
            DefaultValue = KdeSettingsStore.DefaultDesktopNames
        },
        new PluginSettingDescriptor
        {
            Key = "__heading_behavior",
            Label = "Behaviour",
            Kind = PluginSettingKind.Heading
        },
        new PluginSettingDescriptor
        {
            Key = KdeSettingsStore.TimeoutKey,
            Label = "D-Bus timeout (ms)",
            Kind = PluginSettingKind.Number,
            Description = "How long a KDE call may take before it is given up (250 to 10000)",
            DefaultValue = KdeSettingsStore.DefaultTimeoutMilliseconds
        }
    ];

    /// <summary>Re-runs the capability detection and reports what this session offers.</summary>
    public static async Task<string> TestCapabilitiesAsync(
        KdeSession? session,
        VirtualDesktopClient? desktops,
        ActivityManagerClient? activities,
        NightLightClient? nightLight)
    {
        try
        {
            if (session is null || !session.IsSupported)
            {
                return "Not a KDE session, or org.kde.KWin is not on the session bus.";
            }

            KdeCapabilities capabilities = await new KdeCapabilityDetector(session).DetectAsync().ConfigureAwait(false);

            StringBuilder status = new();
            status.Append("Plasma ");
            status.Append(capabilities.PlasmaVersion.Length > 0 ? capabilities.PlasmaVersion : "unknown");
            status.Append(" · KWin ").Append(capabilities.HasKWin ? "yes" : "no");

            if (desktops is not null && desktops.HasState)
            {
                status.Append(" · ").Append(desktops.Count.ToString(CultureInfo.InvariantCulture)).Append(" desktops");

                VirtualDesktop? current = desktops.Current;
                if (current is not null)
                {
                    status.Append(" (current \"").Append(current.Value.Name).Append("\")");
                }
            }

            if (activities is not null && activities.HasState)
            {
                status.Append(" · ").Append(activities.Activities.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(" activities");
            }

            status.Append(" · Night Color ");
            if (!capabilities.NightLightAvailable)
            {
                status.Append("unavailable");
            }
            else
            {
                status.Append("available (").Append(nightLight?.Enabled == true ? "on" : "off").Append(')');
            }

            status.Append(" · effects: ");
            status.Append(capabilities.SupportedEffects.Count > 0
                ? string.Join(", ", capabilities.SupportedEffects)
                : "none");

            status.Append(" · ").Append(capabilities.KWinShortcuts.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" KWin shortcuts");

            return status.ToString();
        }
        catch (Exception ex)
        {
            return $"Capability test failed: {ex.Message}";
        }
    }
}
