# LoupixDeck KDE Plasma Plugin

Controls a KDE Plasma desktop from LoupixDeck: virtual desktops, the active window, Overview,
Activities, Night Color and the session. The plugin is intentionally KDE-specific — it talks to the
stable KDE D-Bus interfaces and to KGlobalAccel, and it never falls back to X11 helper tools, so it
behaves the same on Wayland and X11.

Requires a KDE Plasma 6 session. The plugin declares `"platform": "Linux"`, and on a Linux session
that is not KDE it loads without registering any command.

## Commands

All commands live in the single `KDE Plasma` category. Inside it the picker shows the sections
`Virtual Desktops`, `Active Window`, `Activities`, `Overview and Desktop`, `Night Color`, `Session`
and `Status`, plus the live desktop and Activity lists. The commands themselves are marked
`HiddenFromMenu`, so each one appears exactly once — in its section — instead of as a flat list of
roughly fifty entries.

### Virtual desktops

| Command | What it does |
| --- | --- |
| `KdePlasma.DesktopNext` / `KdePlasma.DesktopPrevious` | Switch to the next or previous desktop |
| `KdePlasma.SwitchToDesktop01` … `10` | Switch to a fixed desktop, with an `Active` / `Inactive` button state |
| `KdePlasma.DesktopSelect(desktopId)` | Switch to a desktop by its stable UUID (used by the folder and the menu) |
| `KdePlasma.DesktopSelectByName(name)` | Switch to a desktop by its name |
| `KdePlasma.DesktopFolder` | Shows the current desktop and opens a folder with all desktops |
| `KdePlasma.DesktopAdd(name)` | Adds a desktop at the end; an empty name falls back to `Desktop N` |
| `KdePlasma.DesktopRemove` | Removes the desktop that is currently active |
| `KdePlasma.DesktopRemoveLast` | Removes the last desktop of the layout |

Adding and removing go through `createDesktop` / `removeDesktop` on
`org.kde.KWin.VirtualDesktopManager`. KWin refuses to remove the last remaining desktop, so that
button stays a no-op instead of failing.

### Active window

| Command | What it does |
| --- | --- |
| `KdePlasma.WindowMinimize`, `WindowMaximize`, `WindowClose`, `WindowFullscreen`, `WindowKeepAbove` | Act on the active window |
| `KdePlasma.WindowToDesktopNext` / `WindowToDesktopPrevious` / `WindowToDesktopNumber(desktop)` | Move the active window between desktops |
| `KdePlasma.WindowToScreenNext` / `WindowToScreenPrevious` / `WindowToScreenNumber(screen)` | Move the active window between screens |

### Overview, Show Desktop and Night Color

| Command | What it does |
| --- | --- |
| `KdePlasma.ShowDesktop` | Toggles Show Desktop, with an `On` / `Off` state that follows KWin |
| `KdePlasma.Overview`, `OverviewCycle`, `GridView` | Open the Overview effect |
| `KdePlasma.PresentWindows`, `PresentWindowsAll`, `PresentWindowsClass` | Open the Present Windows effect |
| `KdePlasma.NightColorToggle` | Toggles Night Color, with an `On` / `Off` state |
| `KdePlasma.NightColorStatus` | Shows the Night Color state and temperature |

### Activities and session

| Command | What it does |
| --- | --- |
| `KdePlasma.ActivityNext` / `ActivityPrevious` | Switch Activities |
| `KdePlasma.ActivitySelect(activityId)` / `ActivitySelectByName(name)` | Switch to a specific Activity |
| `KdePlasma.ActivityFolder` | Shows the current Activity and opens a folder with all Activities |
| `KdePlasma.LockScreen` | Locks the session |
| `KdePlasma.KRunner` / `KRunnerQuery(query)` | Opens KRunner, optionally pre-filled |

### Dynamic values

`KdePlasma.CurrentDesktop`, `CurrentDesktopName`, `DesktopCount`, `CurrentActivity`,
`PlasmaVersion` and `NightColorStatus` are touch buttons that show live values. They read cached
state that D-Bus signals keep current, so they update as soon as KDE reports a change.

`KdePlasma.ActiveWindowTitle`, `ActiveWindowAppId` and `ActiveWindowState` do the same for the
active window, but they need the KWin bridge below. Without it they are not offered at all.

## KWin bridge

KWin exposes no per-window D-Bus interface, so on Wayland nothing outside KWin can read the active
window or move it to a named desktop or monitor. The optional bridge closes that gap with a small
KWin script that reports changes back to the plugin over the session bus.

Install it from the plugin settings. It adds:

| Command | What it does |
| --- | --- |
| `KdePlasma.ActiveWindowTitle` / `ActiveWindowAppId` / `ActiveWindowState` | Live values of the active window |
| `KdePlasma.WindowMoveToDesktop(desktopId)` | Moves the active window to a desktop, addressed by its UUID or its name |
| `KdePlasma.WindowMoveToOutput(output)` | Moves the active window to the monitor with that connector name |

It also gives `KdePlasma.WindowMaximize`, `WindowKeepAbove` and `WindowFullscreen` an `On` / `Off`
state that follows the real window.

How it works:

- The plugin owns `org.loupixdeck.KWinBridge` on the session bus. The script announces itself with
  a protocol version and then reports the active window, the desktops and the outputs.
- The script is written to `~/.local/share/loupixdeck/kwin-bridge/main.js` and loaded into KWin over
  `org.kde.kwin.Scripting`. It is deliberately not a KWin script package: KWin unloads a script
  whose name belongs to a package that is switched off in the settings.
- A KWin script cannot be called directly, so a command is parked in the plugin and the script is
  woken through the global shortcut it registered, which then picks the command up.
- The installed file carries its protocol and version in its first line. A file from another version
  is reported as outdated and its commands stay hidden until it is updated.
- Everything else works without the bridge, and removing it from the settings takes the whole
  directory with it.

The command list is read once at startup, so installing or removing the bridge takes effect for the
commands after LoupixDeck is restarted.

## Capability detection

At startup the plugin probes the session bus once: which KDE services own a name, which KWin
effects are supported and which KWin global shortcut actions exist. A command whose backing action
or effect is missing is never registered, so the command picker never offers something that only
appears to work.

When KWin, plasmashell, kglobalaccel or the Activity manager restarts, the plugin notices the name
owner change, re-reads all cached state and pushes the button states again. Restarting KWin does
not require restarting LoupixDeck.

## Settings

- **Show desktop names instead of numbers** — applies to the desktop buttons and the folder.
- **Overview effect** — what `KdePlasma.Overview` opens: `overview`, `cycle` or `grid`. An unknown
  value falls back to the plain Overview, and so does an effect this KWin does not offer.
- **Hidden Activities** — Activities to leave out of the Activities folder and the command picker,
  by name or id, separated by commas. Switching by name or id still works if a button is bound to it.
- **Monitor order** — connector names in the order they should appear in the picker, for example
  `DP-1, HDMI-A-1`. Names that match no current monitor are ignored, the rest keep the KWin order.
  This needs the KWin bridge, because only the bridge reports the monitor names.
- **D-Bus timeout (ms)** — how long a KDE call may take before it is given up.
- **Test detected capabilities** — re-runs the detection and reports what this session offers.
- **Bridge status** — reports whether the KWin bridge is installed, current and connected.
- **Install or update bridge** — writes the shipped script and loads it into KWin.
- **Remove bridge** — unloads the script and deletes its directory.

## Not included yet

- Activity icons in the Activities folder — KDE reports icon theme names, which the SDK cannot
  render yet, so the folder is text only.

## Languages

The plugin ships `strings.de.json` and `strings.es.json` next to `plugin.json`. English is the
source language and doubles as the key, so a missing entry falls back to the English text instead of
blanking out. Everything the plugin declares — command names, descriptions, groups, setting labels —
is translated by the host at display time, and text the plugin builds while running, such as the
status of a settings action or a folder title, is looked up by the plugin itself through
`IPluginHost.Tr`. This needs SDK 1.24 and a host that implements it.

## Build and local test

The SDK package comes from nuget.org, so the repository builds on its own with nothing cloned next
to it.

```bash
dotnet build LoupixDeck.Plugin.KDEPlasma.csproj -c Release
```

The output lands in `bin/Release/` (no target framework suffix). For a local test, copy the DLLs
together with `plugin.json` into `~/.config/LoupixDeck/plugins/kdeplasma/` and restart LoupixDeck.
`release.ps1` (PowerShell) and `release.sh` (bash) package the same files into `dist/kdeplasma/`;
both take an optional output root as their only argument.
