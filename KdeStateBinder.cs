using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Translates KDE state changes into host calls. This is the only place that touches
/// <see cref="IPluginHost"/> from a background thread, which keeps the commands free of
/// host callback logic and the D-Bus read loop free of rendering work.
/// </summary>
internal sealed class KdeStateBinder(
    IPluginHost host,
    KWinClient kwin,
    VirtualDesktopClient desktops,
    ActivityManagerClient activities,
    NightLightClient nightLight,
    KWinBridgeClient bridge) : IDisposable
{
    private bool _started;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        kwin.ShowingDesktopChanged += OnShowingDesktopChanged;
        desktops.Changed += OnDesktopsChanged;
        activities.Changed += OnActivitiesChanged;
        nightLight.Changed += OnNightLightChanged;
        bridge.Changed += OnBridgeChanged;
        _started = true;
    }

    /// <summary>Pushes every state and refresh again, for example after KWin restarted.</summary>
    public void ReplayAll()
    {
        OnShowingDesktopChanged();
        OnDesktopsChanged();
        OnActivitiesChanged();
        OnNightLightChanged();
        OnBridgeChanged();
    }

    public void Dispose()
    {
        if (!_started)
        {
            return;
        }

        kwin.ShowingDesktopChanged -= OnShowingDesktopChanged;
        desktops.Changed -= OnDesktopsChanged;
        activities.Changed -= OnActivitiesChanged;
        nightLight.Changed -= OnNightLightChanged;
        bridge.Changed -= OnBridgeChanged;
        _started = false;
    }

    private void OnShowingDesktopChanged()
    {
        bool? showing = kwin.ShowingDesktop;
        if (showing is null)
        {
            return;
        }

        Dispatch(() => host.SetActiveButtonState(
            ShowDesktopCommand.Name,
            showing.Value ? ShowDesktopCommand.OnState : ShowDesktopCommand.OffState));
    }

    private void OnDesktopsChanged()
    {
        Dispatch(() =>
        {
            host.RequestButtonRefresh(KdeDisplayCommands.CurrentDesktopName);
            host.RequestButtonRefresh(KdeDisplayCommands.CurrentDesktopNameName);
            host.RequestButtonRefresh(KdeDisplayCommands.DesktopCountName);
            host.RequestButtonRefresh(KdeDisplayCommands.DesktopFolderName);

            if (!desktops.HasState)
            {
                return;
            }

            VirtualDesktop? current = desktops.Current;
            for (int number = 1; number <= VirtualDesktopCommands.FixedDesktopCommandCount; number++)
            {
                bool isCurrent = current is not null && current.Value.Number == number;
                host.SetActiveButtonState(
                    VirtualDesktopCommands.SwitchCommandName(number),
                    isCurrent ? VirtualDesktopCommands.ActiveState : VirtualDesktopCommands.InactiveState);
            }
        });
    }

    private void OnActivitiesChanged()
    {
        Dispatch(() =>
        {
            host.RequestButtonRefresh(KdeDisplayCommands.CurrentActivityName);
            host.RequestButtonRefresh(KdeDisplayCommands.ActivityFolderName);
        });
    }

    private void OnNightLightChanged()
    {
        if (!nightLight.HasState)
        {
            return;
        }

        bool enabled = nightLight.Enabled;
        Dispatch(() =>
        {
            host.SetActiveButtonState(
                NightColorCommands.ToggleName,
                enabled ? NightColorCommands.OnState : NightColorCommands.OffState);
            host.RequestButtonRefresh(NightColorCommands.StatusName);
        });
    }

    /// <summary>Pushes the window state the KWin bridge reported, which nothing else can supply.</summary>
    private void OnBridgeChanged()
    {
        if (!bridge.Connected)
        {
            return;
        }

        ActiveWindowInfo window = bridge.ActiveWindow;

        Dispatch(() =>
        {
            host.RequestButtonRefresh(KdeDisplayCommands.ActiveWindowTitleName);
            host.RequestButtonRefresh(KdeDisplayCommands.ActiveWindowAppIdName);
            host.RequestButtonRefresh(KdeDisplayCommands.ActiveWindowStateName);

            SetWindowState(WindowCommands.MaximizeName, window.Present && window.Maximized);
            SetWindowState(WindowCommands.KeepAboveName, window.Present && window.KeepAbove);
            SetWindowState(WindowCommands.FullscreenName, window.Present && window.FullScreen);
        });
    }

    private void SetWindowState(string commandName, bool on)
    {
        host.SetActiveButtonState(commandName, on ? WindowCommands.OnState : WindowCommands.OffState);
    }

    private void Dispatch(Action action)
    {
        // Some events arrive on the D-Bus read loop, which must never be blocked by host work.
        _ = Task.Run(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                host.Logger.Warn($"KDE Plasma: updating the button state failed: {ex.Message}");
            }
        });
    }
}
