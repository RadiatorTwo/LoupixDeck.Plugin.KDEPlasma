// loupixdeck-bridge protocol=1 version=1.0.0
//
// The LoupixDeck KWin bridge. KWin is the only process that can read window properties and move a
// window to a named output on Wayland, so this script runs inside KWin and reports the state to the
// plugin over the session bus.
//
// Constraints of the KWin script engine this file has to live with:
//   - no timers and no setTimeout, so every update is signal driven,
//   - callDBus turns a JavaScript number into a D-Bus double, so every message is one JSON string,
//   - the script has no D-Bus server of its own, so commands are pulled, not pushed.

var SERVICE = "org.loupixdeck.KWinBridge";
var PATH = "/KWinBridge";
var INTERFACE = "org.loupixdeck.KWinBridge1";

var PROTOCOL = 1;
var VERSION = "1.0.0";

var SHORTCUT_NAME = "LoupixDeck Bridge Command";
var SHORTCUT_LABEL = "LoupixDeck: run bridge command";

// Set once the plugin accepted the handshake. Until then nothing is reported, so a plugin that
// speaks a different protocol never sees messages it cannot read.
var accepted = false;

// The window whose signals are currently connected, plus the handlers used for it. The handlers are
// kept so they can be disconnected again when the active window changes.
var trackedWindow = null;
var trackedHandlers = [];

function send(type, data) {
    if (!accepted) {
        return;
    }

    callDBus(SERVICE, PATH, INTERFACE, "Notify", JSON.stringify({ v: PROTOCOL, t: type, d: data }));
}

function windowState(window) {
    if (!window) {
        return { present: false };
    }

    return {
        present: true,
        title: window.caption ? String(window.caption) : "",
        appId: window.desktopFileName ? String(window.desktopFileName) : "",
        resourceClass: window.resourceClass ? String(window.resourceClass) : "",
        // MaximizeRestore = 0, MaximizeVertical = 1, MaximizeHorizontal = 2, MaximizeFull = 3.
        maximized: window.maximizeMode === 3,
        maximizeMode: window.maximizeMode,
        keepAbove: window.keepAbove === true,
        fullScreen: window.fullScreen === true,
        minimized: window.minimized === true
    };
}

function sendWindowState() {
    send("WindowStateChanged", windowState(trackedWindow));
}

function untrackWindow() {
    if (!trackedWindow) {
        trackedHandlers = [];
        return;
    }

    for (var i = 0; i < trackedHandlers.length; i++) {
        var entry = trackedHandlers[i];
        try {
            entry.signal.disconnect(entry.handler);
        } catch (error) {
            // A window that is already gone drops its connections by itself.
        }
    }

    trackedHandlers = [];
    trackedWindow = null;
}

function connectWindowSignal(window, signal) {
    if (!signal) {
        return;
    }

    try {
        signal.connect(sendWindowState);
        trackedHandlers.push({ signal: signal, handler: sendWindowState });
    } catch (error) {
        console.log("loupixdeck-bridge: connecting a window signal failed: " + error);
    }
}

function trackWindow(window) {
    untrackWindow();
    trackedWindow = window;

    if (!window) {
        return;
    }

    connectWindowSignal(window, window.captionChanged);
    connectWindowSignal(window, window.windowClassChanged);
    connectWindowSignal(window, window.desktopFileNameChanged);
    connectWindowSignal(window, window.maximizedChanged);
    connectWindowSignal(window, window.keepAboveChanged);
    connectWindowSignal(window, window.fullScreenChanged);
    connectWindowSignal(window, window.minimizedChanged);
}

function describeDesktop(desktop) {
    if (!desktop) {
        return null;
    }

    return {
        id: String(desktop.id),
        name: String(desktop.name),
        number: desktop.x11DesktopNumber
    };
}

function sendActiveWindow() {
    trackWindow(workspace.activeWindow);
    send("ActiveWindowChanged", windowState(trackedWindow));
}

function sendCurrentDesktop() {
    send("CurrentDesktopChanged", { desktop: describeDesktop(workspace.currentDesktop) });
}

function sendDesktopList() {
    var desktops = [];
    var all = workspace.desktops;
    for (var i = 0; i < all.length; i++) {
        desktops.push(describeDesktop(all[i]));
    }

    send("DesktopListChanged", { desktops: desktops });
}

function sendOutputList() {
    var outputs = [];
    var screens = workspace.screens;
    for (var i = 0; i < screens.length; i++) {
        var screen = screens[i];
        outputs.push({
            name: String(screen.name),
            manufacturer: screen.manufacturer ? String(screen.manufacturer) : "",
            model: screen.model ? String(screen.model) : ""
        });
    }

    send("OutputListChanged", { outputs: outputs });
}

function sendAll() {
    sendActiveWindow();
    sendCurrentDesktop();
    sendDesktopList();
    sendOutputList();
}

function findDesktop(id) {
    var all = workspace.desktops;
    for (var i = 0; i < all.length; i++) {
        if (String(all[i].id) === id) {
            return all[i];
        }
    }

    return null;
}

function findOutput(name) {
    var screens = workspace.screens;
    for (var i = 0; i < screens.length; i++) {
        if (String(screens[i].name) === name) {
            return screens[i];
        }
    }

    return null;
}

function runCommand(json) {
    if (!json) {
        return;
    }

    var command = null;
    try {
        command = JSON.parse(json);
    } catch (error) {
        console.log("loupixdeck-bridge: the pending command is not valid JSON: " + error);
        return;
    }

    if (!command || command.v !== PROTOCOL) {
        return;
    }

    var window = workspace.activeWindow;
    if (!window) {
        return;
    }

    if (command.c === "MoveToDesktop") {
        var desktop = findDesktop(String(command.desktopId));
        if (desktop) {
            window.desktops = [desktop];
        }

        return;
    }

    if (command.c === "MoveToOutput") {
        var output = findOutput(String(command.output));
        if (output) {
            workspace.sendClientToScreen(window, output);
        }
    }
}

function takeCommand() {
    if (!accepted) {
        return;
    }

    callDBus(SERVICE, PATH, INTERFACE, "TakeCommand", function (json) {
        try {
            runCommand(json);
        } catch (error) {
            console.log("loupixdeck-bridge: running the command failed: " + error);
        }
    });
}

function onHelloReply(acceptedProtocol) {
    if (acceptedProtocol !== PROTOCOL) {
        console.log("loupixdeck-bridge: the plugin does not accept protocol " + PROTOCOL + ", staying silent.");
        return;
    }

    accepted = true;
    sendAll();
}

function connectWorkspace() {
    workspace.windowActivated.connect(sendActiveWindow);
    // KWin 6 passes (previous, current, output) here, so the handler reads the workspace instead.
    workspace.currentDesktopChanged.connect(sendCurrentDesktop);
    workspace.desktopsChanged.connect(sendDesktopList);
    workspace.screensChanged.connect(sendOutputList);

    if (workspace.screenOrderChanged) {
        workspace.screenOrderChanged.connect(sendOutputList);
    }
}

function start() {
    connectWorkspace();
    registerShortcut(SHORTCUT_NAME, SHORTCUT_LABEL, "", takeCommand);

    callDBus(
        SERVICE,
        PATH,
        INTERFACE,
        "Hello",
        JSON.stringify({ v: PROTOCOL, script: VERSION }),
        onHelloReply);
}

start();
