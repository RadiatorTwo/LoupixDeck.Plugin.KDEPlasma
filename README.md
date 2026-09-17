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
- **D-Bus timeout (ms)** — how long a KDE call may take before it is given up.
- **Test detected capabilities** — re-runs the detection and reports what this session offers.

## Not included yet

These need the optional KWin script (Level 3 of the design in issue #257) and are planned for a
later round:

- Active window title, application id and window state as dynamic values.
- Button states for "window maximized" and "keep above".
- Moving a window to a specific desktop by UUID or to a monitor identified by name.
- Activity icons in the Activities folder — KDE reports icon theme names, which the SDK cannot
  render yet, so the folder is text only.

## Build and local test

The SDK package is resolved from the sibling SDK repository, so clone `LoupixDeck.PluginSdk` next to
this repository and build it once.

```bash
dotnet build LoupixDeck.Plugin.KDEPlasma.csproj -c Release
```

The output lands in `bin/Release/` (no target framework suffix). For a local test, copy the DLLs
together with `plugin.json` into `~/.config/LoupixDeck/plugins/kdeplasma/` and restart LoupixDeck.
`release.ps1` (PowerShell) and `release.sh` (bash) package the same files into `dist/kdeplasma/`;
both take an optional output root as their only argument.
