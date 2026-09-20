using System.Globalization;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// The contract between the plugin and the KWin bridge script. Everything the two sides have to
/// agree on lives here, including the header line that carries the script version, so a script
/// written by another version of the plugin can be recognised instead of being trusted blindly.
/// </summary>
internal static class KWinBridgeProtocol
{
    /// <summary>The protocol this plugin speaks. A script with another value is not used.</summary>
    public const int Version = 1;

    /// <summary>Well-known bus name the plugin owns while the bridge is available.</summary>
    public const string BusName = "org.loupixdeck.KWinBridge";

    public const string ObjectPath = "/KWinBridge";

    public const string Interface = "org.loupixdeck.KWinBridge1";

    /// <summary>The script announces itself and learns which protocol the plugin accepts.</summary>
    public const string HelloMember = "Hello";

    /// <summary>The script reports one state change.</summary>
    public const string NotifyMember = "Notify";

    /// <summary>The script pulls the command that is waiting for it.</summary>
    public const string TakeCommandMember = "TakeCommand";

    /// <summary>
    /// The name KWin knows the loaded script under. It must not match the id of an installed KWin
    /// script package: <c>start()</c> re-reads the enabled packages and unloads every script whose
    /// name belongs to a package that is switched off, which would drop the bridge again.
    /// </summary>
    public const string ScriptPluginName = "loupixdeck-bridge";

    /// <summary>Global shortcut the plugin triggers to make the script pull a command.</summary>
    public const string CommandShortcut = "LoupixDeck Bridge Command";

    // Event names used in the "t" member of a Notify payload.
    public const string ActiveWindowChanged = "ActiveWindowChanged";
    public const string WindowStateChanged = "WindowStateChanged";
    public const string CurrentDesktopChanged = "CurrentDesktopChanged";
    public const string DesktopListChanged = "DesktopListChanged";
    public const string OutputListChanged = "OutputListChanged";

    // Command names used in the "c" member of a TakeCommand payload.
    public const string MoveToDesktopCommand = "MoveToDesktop";
    public const string MoveToOutputCommand = "MoveToOutput";

    private const string HeaderMarker = "loupixdeck-bridge";
    private const string ProtocolMarker = "protocol=";
    private const string VersionMarker = "version=";

    /// <summary>
    /// Reads the machine-readable header line of a bridge script, for example
    /// <c>// loupixdeck-bridge protocol=1 version=1.0.0</c>. The header is the single source of
    /// truth for what an installed file actually is, because the file may have been written by an
    /// older or newer plugin, or by something else entirely.
    /// </summary>
    public static bool TryParseHeader(string? firstLine, out int protocol, out string version)
    {
        protocol = 0;
        version = string.Empty;

        if (string.IsNullOrWhiteSpace(firstLine) || !firstLine.Contains(HeaderMarker, StringComparison.Ordinal))
        {
            return false;
        }

        string? protocolText = ReadValue(firstLine, ProtocolMarker);
        string? versionText = ReadValue(firstLine, VersionMarker);

        if (protocolText is null
            || versionText is null
            || !int.TryParse(protocolText, NumberStyles.None, CultureInfo.InvariantCulture, out protocol))
        {
            protocol = 0;
            return false;
        }

        version = versionText;
        return true;
    }

    /// <summary>Reads the whitespace-terminated value that follows <paramref name="marker"/>.</summary>
    private static string? ReadValue(string line, string marker)
    {
        int start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;

        int end = start;
        while (end < line.Length && !char.IsWhiteSpace(line[end]))
        {
            end++;
        }

        return end > start ? line[start..end] : null;
    }
}
