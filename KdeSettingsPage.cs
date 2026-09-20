using System.Globalization;
using System.Text;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>Builds the declarative settings page and its capability test.</summary>
internal static class KdeSettingsPage
{
    /// <summary>Text for a state the plugin cannot report on, because it never started.</summary>
    private const string NotActiveText = "The plugin is not active on this session.";

    /// <summary>Separates the parts of a status line.</summary>
    private const string Separator = " · ";

    /// <summary>The accepted values of the Overview effect setting, for its description.</summary>
    private static readonly string OverviewEffectValues = string.Join(", ", OverviewEffects.All);

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
            Key = KdeSettingsStore.OverviewEffectKey,
            Label = "Overview effect",
            Kind = PluginSettingKind.Text,
            Description = "What the Overview button opens: " + OverviewEffectValues,
            DefaultValue = KdeSettingsStore.DefaultOverviewEffect
        },
        new PluginSettingDescriptor
        {
            Key = KdeSettingsStore.HiddenActivitiesKey,
            Label = "Hidden Activities",
            Kind = PluginSettingKind.Text,
            Description = "Activities to leave out of the Activities folder and the command picker, "
                          + "by name or id, separated by commas",
            DefaultValue = string.Empty
        },
        new PluginSettingDescriptor
        {
            Key = KdeSettingsStore.MonitorOrderKey,
            Label = "Monitor order",
            Kind = PluginSettingKind.Text,
            Description = "Connector names in the order they should appear in the picker, separated "
                          + "by commas, for example DP-1, HDMI-A-1. Needs the KWin bridge",
            DefaultValue = string.Empty
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
        },
        new PluginSettingDescriptor
        {
            Key = "__heading_bridge",
            Label = "KWin bridge",
            Kind = PluginSettingKind.Heading,
            Description = "An optional KWin script that reports the active window and moves it to a "
                          + "named desktop or monitor. Everything else works without it."
        }
    ];

    /// <summary>The buttons that manage the KWin bridge script.</summary>
    public static IReadOnlyList<PluginSettingAction> BuildBridgeActions(
        IPluginHost? host,
        KWinBridgeInstaller? installer,
        KWinBridgeClient? bridge,
        KdeSettingsStore? settings)
    {
        return
        [
            new PluginSettingAction
            {
                Label = "Bridge status",
                Invoke = () => Task.FromResult(DescribeBridge(host, installer, bridge))
            },
            new PluginSettingAction
            {
                Label = "Install or update bridge",
                Invoke = () => InstallBridgeAsync(host, installer, bridge, settings)
            },
            new PluginSettingAction
            {
                Label = "Remove bridge",
                Invoke = () => RemoveBridgeAsync(host, installer, bridge, settings)
            }
        ];
    }

    /// <summary>
    /// Looks text up in the plugin's own translation files. An action result is built while
    /// running, so unlike a descriptor it is never translated by the host on its own.
    /// </summary>
    private static string Tr(IPluginHost? host, string english) => host?.Tr(english) ?? english;

    private static string DescribeBridge(IPluginHost? host, KWinBridgeInstaller? installer, KWinBridgeClient? bridge)
    {
        if (installer is null)
        {
            return Tr(host, NotActiveText);
        }

        StringBuilder status = new();
        status.Append(installer.Inspect().Describe(english => Tr(host, english)));

        if (bridge is not null)
        {
            status.Append(Separator).Append(Tr(host, bridge.Connected ? "connected" : "not connected"));

            if (bridge.Connected)
            {
                status.Append(Separator).Append(string.Format(
                    Tr(host, "{0} monitors"),
                    bridge.Outputs.Count.ToString(CultureInfo.InvariantCulture)));
            }
        }

        status.Append(Separator).Append(KWinBridgeInstaller.ScriptPath);
        return status.ToString();
    }

    private static async Task<string> InstallBridgeAsync(
        IPluginHost? host,
        KWinBridgeInstaller? installer,
        KWinBridgeClient? bridge,
        KdeSettingsStore? settings)
    {
        if (installer is null)
        {
            return Tr(host, NotActiveText);
        }

        if (!await installer.InstallAsync().ConfigureAwait(false))
        {
            return Tr(host, "The bridge could not be written, see the log.");
        }

        BridgeInstallState state = installer.Inspect();
        settings?.SetBridgeInstalled(state.IsUsable, state.InstalledVersion);

        if (bridge is not null)
        {
            await bridge.SeedAsync().ConfigureAwait(false);
        }

        return string.Format(
            Tr(host, "{0}. Restart LoupixDeck to get the bridge commands."),
            state.Describe(english => Tr(host, english)));
    }

    private static async Task<string> RemoveBridgeAsync(
        IPluginHost? host,
        KWinBridgeInstaller? installer,
        KWinBridgeClient? bridge,
        KdeSettingsStore? settings)
    {
        if (installer is null)
        {
            return Tr(host, NotActiveText);
        }

        if (bridge is not null)
        {
            await bridge.UnloadScriptAsync().ConfigureAwait(false);
        }

        if (!installer.Remove())
        {
            return Tr(host, "The bridge could not be removed, see the log.");
        }

        settings?.SetBridgeInstalled(false, string.Empty);
        return Tr(host, "The bridge was removed. Restart LoupixDeck to drop its commands.");
    }

    /// <summary>Re-runs the capability detection and reports what this session offers.</summary>
    public static async Task<string> TestCapabilitiesAsync(
        IPluginHost? host,
        KdeSession? session,
        KWinBridgeInstaller? bridgeInstaller,
        VirtualDesktopClient? desktops,
        ActivityManagerClient? activities,
        NightLightClient? nightLight)
    {
        try
        {
            if (session is null || bridgeInstaller is null || !session.IsSupported)
            {
                return Tr(host, "Not a KDE session, or org.kde.KWin is not on the session bus.");
            }

            KdeCapabilities capabilities = await new KdeCapabilityDetector(session, bridgeInstaller).DetectAsync().ConfigureAwait(false);

            StringBuilder status = new();
            status.Append(string.Format(
                Tr(host, "Plasma {0}"),
                capabilities.PlasmaVersion.Length > 0 ? capabilities.PlasmaVersion : Tr(host, "unknown")));
            status.Append(Separator).Append(string.Format(
                Tr(host, "KWin: {0}"),
                Tr(host, capabilities.HasKWin ? "yes" : "no")));

            if (desktops is not null && desktops.HasState)
            {
                string count = desktops.Count.ToString(CultureInfo.InvariantCulture);
                VirtualDesktop? current = desktops.Current;

                status.Append(Separator).Append(current is null
                    ? string.Format(Tr(host, "{0} desktops"), count)
                    : string.Format(Tr(host, "{0} desktops, current \"{1}\""), count, current.Value.Name));
            }

            if (activities is not null && activities.HasState)
            {
                status.Append(Separator).Append(string.Format(
                    Tr(host, "{0} Activities"),
                    activities.Activities.Count.ToString(CultureInfo.InvariantCulture)));
            }

            status.Append(Separator).Append(string.Format(
                Tr(host, "Night Color: {0}"),
                capabilities.NightLightAvailable
                    ? Tr(host, nightLight?.Enabled == true ? "on" : "off")
                    : Tr(host, "unavailable")));

            status.Append(Separator).Append(string.Format(
                Tr(host, "Effects: {0}"),
                capabilities.SupportedEffects.Count > 0
                    ? string.Join(", ", capabilities.SupportedEffects)
                    : Tr(host, "none")));

            status.Append(Separator).Append(string.Format(
                Tr(host, "{0} KWin shortcuts"),
                capabilities.KWinShortcuts.Count.ToString(CultureInfo.InvariantCulture)));

            return status.ToString();
        }
        catch (Exception ex)
        {
            return string.Format(Tr(host, "Capability test failed: {0}"), ex.Message);
        }
    }
}
