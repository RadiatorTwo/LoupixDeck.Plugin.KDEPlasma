using System.Globalization;
using System.Text;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>Builds the declarative settings page and its capability test.</summary>
internal static class KdeSettingsPage
{
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
        KWinBridgeInstaller? installer,
        KWinBridgeClient? bridge,
        KdeSettingsStore? settings)
    {
        return
        [
            new PluginSettingAction
            {
                Label = "Bridge status",
                Invoke = () => Task.FromResult(DescribeBridge(installer, bridge))
            },
            new PluginSettingAction
            {
                Label = "Install or update bridge",
                Invoke = () => InstallBridgeAsync(installer, bridge, settings)
            },
            new PluginSettingAction
            {
                Label = "Remove bridge",
                Invoke = () => RemoveBridgeAsync(installer, bridge, settings)
            }
        ];
    }

    private static string DescribeBridge(KWinBridgeInstaller? installer, KWinBridgeClient? bridge)
    {
        if (installer is null)
        {
            return "The plugin is not active on this session.";
        }

        StringBuilder status = new();
        status.Append(installer.Inspect().Describe());

        if (bridge is not null)
        {
            status.Append(bridge.Connected ? " · connected" : " · not connected");

            if (bridge.Connected)
            {
                status.Append(" · ")
                    .Append(bridge.Outputs.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(" monitors");
            }
        }

        status.Append(" · ").Append(KWinBridgeInstaller.ScriptPath);
        return status.ToString();
    }

    private static async Task<string> InstallBridgeAsync(
        KWinBridgeInstaller? installer,
        KWinBridgeClient? bridge,
        KdeSettingsStore? settings)
    {
        if (installer is null)
        {
            return "The plugin is not active on this session.";
        }

        if (!await installer.InstallAsync().ConfigureAwait(false))
        {
            return "The bridge could not be written, see the log.";
        }

        BridgeInstallState state = installer.Inspect();
        settings?.SetBridgeInstalled(state.IsUsable, state.InstalledVersion);

        if (bridge is not null)
        {
            await bridge.SeedAsync().ConfigureAwait(false);
        }

        return $"{state.Describe()}. Restart LoupixDeck to get the bridge commands.";
    }

    private static async Task<string> RemoveBridgeAsync(
        KWinBridgeInstaller? installer,
        KWinBridgeClient? bridge,
        KdeSettingsStore? settings)
    {
        if (installer is null)
        {
            return "The plugin is not active on this session.";
        }

        if (bridge is not null)
        {
            await bridge.UnloadScriptAsync().ConfigureAwait(false);
        }

        if (!installer.Remove())
        {
            return "The bridge could not be removed, see the log.";
        }

        settings?.SetBridgeInstalled(false, string.Empty);
        return "The bridge was removed. Restart LoupixDeck to drop its commands.";
    }

    /// <summary>Re-runs the capability detection and reports what this session offers.</summary>
    public static async Task<string> TestCapabilitiesAsync(
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
                return "Not a KDE session, or org.kde.KWin is not on the session bus.";
            }

            KdeCapabilities capabilities = await new KdeCapabilityDetector(session, bridgeInstaller).DetectAsync().ConfigureAwait(false);

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
