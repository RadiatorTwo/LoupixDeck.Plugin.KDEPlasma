using System.Reflection;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>What the KWin script directory currently holds.</summary>
internal enum BridgeState
{
    /// <summary>No script is installed.</summary>
    NotInstalled,

    /// <summary>The installed script matches the one this plugin ships.</summary>
    Installed,

    /// <summary>A bridge script is installed, but it is a different protocol or version.</summary>
    Outdated,

    /// <summary>A file is there, but it carries no readable bridge header.</summary>
    Foreign
}

/// <summary>The installed script compared against the one this plugin ships.</summary>
internal sealed record BridgeInstallState(
    BridgeState State,
    int InstalledProtocol,
    string InstalledVersion,
    int ShippedProtocol,
    string ShippedVersion,
    string ScriptPath)
{
    /// <summary>Whether the bridge commands may be offered at all.</summary>
    public bool IsUsable => State == BridgeState.Installed;

    /// <summary>
    /// One line for the settings page. The text is built while running, so it is looked up through
    /// <paramref name="translate"/> instead of being translated by the host from a descriptor.
    /// </summary>
    public string Describe(Func<string, string> translate) => State switch
    {
        BridgeState.Installed => string.Format(
            translate("Installed {0} (protocol {1})"),
            InstalledVersion,
            InstalledProtocol),
        BridgeState.Outdated => string.Format(
            translate("Outdated {0} (protocol {1}), this plugin ships {2} (protocol {3})"),
            InstalledVersion,
            InstalledProtocol,
            ShippedVersion,
            ShippedProtocol),
        BridgeState.Foreign => translate("A foreign script occupies the bridge directory"),
        _ => translate("Not installed")
    };
}

/// <summary>
/// Owns the KWin script directory of the bridge. This type only touches the file system, so the
/// state can be read synchronously while the commands are built, before any D-Bus call is made.
/// </summary>
internal sealed class KWinBridgeInstaller(IPluginLogger logger)
{
    private const string ResourceName = "LoupixDeck.Plugin.KDEPlasma.KWinBridgeScript.js";

    /// <summary>
    /// Where the script is written. KWin loads it by path, so it deliberately does not live in the
    /// KWin script package directory: a package there would show up in the KWin script registry,
    /// and KWin unloads a script whose name belongs to a package that is switched off.
    /// </summary>
    public static string ScriptDirectory { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dataHome
            ? dataHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "loupixdeck",
        "kwin-bridge");

    /// <summary>The file KWin is asked to load.</summary>
    public static string ScriptPath { get; } = Path.Combine(ScriptDirectory, "main.js");

    /// <summary>Compares the installed script against the shipped one. Never throws.</summary>
    public BridgeInstallState Inspect()
    {
        (int shippedProtocol, string shippedVersion) = ReadShippedHeader();

        try
        {
            if (!File.Exists(ScriptPath))
            {
                return new BridgeInstallState(BridgeState.NotInstalled, 0, string.Empty, shippedProtocol, shippedVersion, ScriptPath);
            }

            string firstLine = ReadFirstLine(ScriptPath);
            if (!KWinBridgeProtocol.TryParseHeader(firstLine, out int protocol, out string version))
            {
                return new BridgeInstallState(BridgeState.Foreign, 0, string.Empty, shippedProtocol, shippedVersion, ScriptPath);
            }

            BridgeState state = protocol == shippedProtocol && string.Equals(version, shippedVersion, StringComparison.Ordinal)
                ? BridgeState.Installed
                : BridgeState.Outdated;

            return new BridgeInstallState(state, protocol, version, shippedProtocol, shippedVersion, ScriptPath);
        }
        catch (Exception ex)
        {
            logger.Warn($"KDE Plasma: the KWin bridge directory could not be read: {ex.Message}");
            return new BridgeInstallState(BridgeState.Foreign, 0, string.Empty, shippedProtocol, shippedVersion, ScriptPath);
        }
    }

    /// <summary>Writes the shipped script and its package metadata, replacing whatever was there.</summary>
    public async Task<bool> InstallAsync()
    {
        try
        {
            Directory.CreateDirectory(ScriptDirectory);
            await File.WriteAllTextAsync(ScriptPath, ReadShippedScript()).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.Warn($"KDE Plasma: the KWin bridge could not be installed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Removes the whole script directory again.</summary>
    public bool Remove()
    {
        try
        {
            if (Directory.Exists(ScriptDirectory))
            {
                Directory.Delete(ScriptDirectory, recursive: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.Warn($"KDE Plasma: the KWin bridge could not be removed: {ex.Message}");
            return false;
        }
    }

    /// <summary>The script that is embedded in this assembly.</summary>
    public static string ReadShippedScript()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static (int Protocol, string Version) ReadShippedHeader()
    {
        string script = ReadShippedScript();
        int end = script.IndexOf('\n');
        string firstLine = end < 0 ? script : script[..end];

        return KWinBridgeProtocol.TryParseHeader(firstLine, out int protocol, out string version)
            ? (protocol, version)
            : (0, string.Empty);
    }

    private static string ReadFirstLine(string path)
    {
        using StreamReader reader = new(path);
        return reader.ReadLine() ?? string.Empty;
    }
}
