using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LoupixDeck.PluginSdk;
using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// The session bus object the KWin bridge script talks to. It accepts the handshake, receives the
/// state events and hands out the command that is waiting for the script.
/// </summary>
/// <remarks>
/// <see cref="HandleMethodAsync"/> runs on the connection read loop, so it only parses the payload,
/// stores it and raises an event. Everything that touches the host happens elsewhere.
/// </remarks>
internal sealed class KWinBridgeEndpoint(IPluginLogger logger) : IPathMethodHandler
{
    private static readonly TimeSpan WarnInterval = TimeSpan.FromMinutes(1);

    private static readonly ReadOnlyMemory<byte> InterfaceXml = Encoding.UTF8.GetBytes(
        $"""
         <interface name="{KWinBridgeProtocol.Interface}">
           <method name="{KWinBridgeProtocol.HelloMember}">
             <arg type="s" direction="in"/>
             <arg type="i" direction="out"/>
           </method>
           <method name="{KWinBridgeProtocol.NotifyMember}">
             <arg type="s" direction="in"/>
           </method>
           <method name="{KWinBridgeProtocol.TakeCommandMember}">
             <arg type="s" direction="out"/>
           </method>
         </interface>
         """);

    /// <summary>The command the script picks up on its next pull. Only the newest one survives.</summary>
    private string? _pendingCommand;

    private long _lastWarnTimestamp;

    /// <summary>The connection this object is served on, so a repeated attach is a no-op.</summary>
    private DBusConnection? _attachedConnection;

    public string Path => KWinBridgeProtocol.ObjectPath;

    public bool HandlesChildPaths => false;

    /// <summary>Raised with the script version when a script with a matching protocol announced itself.</summary>
    public event Action<string>? HelloReceived;

    /// <summary>Raised for every accepted event, with the event type and its data member.</summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>Queues a command for the script. A command that was never pulled is replaced.</summary>
    public void PostCommand(string commandJson) => Interlocked.Exchange(ref _pendingCommand, commandJson);

    /// <summary>True once the object is served and the well-known name is owned.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>
    /// Serves this object on <paramref name="connection"/> and asks for the well-known name. A name
    /// that somebody else already owns is left alone, so a second LoupixDeck never steals the bridge
    /// from the first one.
    /// </summary>
    public async Task<bool> AttachAsync(DBusConnection connection)
    {
        if (ReferenceEquals(_attachedConnection, connection))
        {
            return IsAttached;
        }

        try
        {
            connection.AddMethodHandler(this);
            _attachedConnection = connection;

            if (!await connection.TryRequestNameAsync(KWinBridgeProtocol.BusName, RequestNameOptions.None).ConfigureAwait(false))
            {
                connection.RemoveMethodHandler(Path);
                _attachedConnection = null;
                logger.Info($"KDE Plasma: {KWinBridgeProtocol.BusName} is already owned, the KWin bridge stays inactive.");
                IsAttached = false;
                return false;
            }

            IsAttached = true;
            return true;
        }
        catch (Exception ex)
        {
            logger.Warn($"KDE Plasma: the KWin bridge endpoint could not be published: {ex.Message}");
            _attachedConnection = null;
            IsAttached = false;
            return false;
        }
    }

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        try
        {
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([InterfaceXml]);
                return ValueTask.CompletedTask;
            }

            if (context.Request.InterfaceAsString != KWinBridgeProtocol.Interface)
            {
                context.ReplyUnknownMethodError();
                return ValueTask.CompletedTask;
            }

            switch (context.Request.MemberAsString)
            {
                case KWinBridgeProtocol.HelloMember:
                    HandleHello(context);
                    break;

                case KWinBridgeProtocol.NotifyMember:
                    HandleNotify(context);
                    break;

                case KWinBridgeProtocol.TakeCommandMember:
                    HandleTakeCommand(context);
                    break;

                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
        }
        catch (Exception ex)
        {
            Warn($"handling {context.Request.MemberAsString} failed: {ex.Message}");
            context.HandleException(ex, shouldDisconnect: false);
        }

        return ValueTask.CompletedTask;
    }

    private void HandleHello(MethodContext context)
    {
        int accepted = -1;
        string version = string.Empty;

        if (TryReadPayload(context, out JsonDocument? payload) && payload is not null)
        {
            using (payload)
            {
                if (ReadProtocol(payload.RootElement) == KWinBridgeProtocol.Version)
                {
                    accepted = KWinBridgeProtocol.Version;
                    version = payload.RootElement.TryGetProperty("script", out JsonElement scriptVersion)
                        && scriptVersion.ValueKind == JsonValueKind.String
                        ? scriptVersion.GetString() ?? string.Empty
                        : string.Empty;
                }
            }
        }

        Reply(context, "i", accepted);

        if (accepted > 0)
        {
            HelloReceived?.Invoke(version);
        }
        else
        {
            Warn("a KWin script announced an unsupported protocol and was rejected.");
        }
    }

    private void HandleNotify(MethodContext context)
    {
        if (!TryReadPayload(context, out JsonDocument? payload) || payload is null)
        {
            return;
        }

        using (payload)
        {
            if (ReadProtocol(payload.RootElement) != KWinBridgeProtocol.Version)
            {
                Warn("an event with an unsupported protocol was dropped.");
                return;
            }

            if (!payload.RootElement.TryGetProperty("t", out JsonElement type) || type.ValueKind != JsonValueKind.String)
            {
                return;
            }

            string? name = type.GetString();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            payload.RootElement.TryGetProperty("d", out JsonElement data);

            // The data element does not outlive the document, so the handler has to read it now.
            EventReceived?.Invoke(name, data);
        }
    }

    private void HandleTakeCommand(MethodContext context)
    {
        string command = Interlocked.Exchange(ref _pendingCommand, null) ?? string.Empty;
        Reply(context, "s", command);
    }

    /// <summary>Reads the single string argument of a call and parses it as JSON.</summary>
    private bool TryReadPayload(MethodContext context, out JsonDocument? payload)
    {
        payload = null;

        if (context.Request.SignatureAsString != "s")
        {
            context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", "One string argument is expected.");
            return false;
        }

        Reader reader = context.Request.GetBodyReader();
        string json = reader.ReadString();

        try
        {
            payload = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException ex)
        {
            Warn($"a bridge payload was not valid JSON: {ex.Message}");
            return false;
        }
    }

    private static int ReadProtocol(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("v", out JsonElement version)
            && version.TryGetInt32(out int value)
            ? value
            : 0;
    }

    private static void Reply(MethodContext context, string signature, int value)
    {
        MessageWriter writer = context.CreateReplyWriter(signature);
        try
        {
            writer.WriteInt32(value);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void Reply(MethodContext context, string signature, string value)
    {
        MessageWriter writer = context.CreateReplyWriter(signature);
        try
        {
            writer.WriteString(value);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>Logs at most one warning per minute so a misbehaving script cannot flood the log.</summary>
    private void Warn(string message)
    {
        long now = Stopwatch.GetTimestamp();
        long last = Interlocked.Read(ref _lastWarnTimestamp);

        if (last != 0 && Stopwatch.GetElapsedTime(last, now) < WarnInterval)
        {
            return;
        }

        Interlocked.Exchange(ref _lastWarnTimestamp, now);
        logger.Warn($"KDE Plasma: {message}");
    }
}
