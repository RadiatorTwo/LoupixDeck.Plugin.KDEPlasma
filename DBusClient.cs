using System.Collections.Concurrent;
using System.Diagnostics;
using LoupixDeck.PluginSdk;
using Tmds.DBus.Protocol;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Writes the arguments of a D-Bus method call into the message body.
/// A dedicated delegate is required because <see cref="MessageWriter"/> is a ref struct
/// and therefore cannot be used as a generic type argument.
/// </summary>
internal delegate void DBusArgumentWriter(ref MessageWriter writer);

/// <summary>
/// Thin helper around the raw D-Bus protocol API. This is the only type in the plugin that
/// touches Tmds.DBus.Protocol directly. It applies the call timeout, swallows transport errors
/// and rate-limits the resulting log output so an unresponsive service cannot flood the log.
/// </summary>
internal sealed class DBusClient(DBusConnection connection, IPluginLogger logger)
{
    public const string PropertiesInterface = "org.freedesktop.DBus.Properties";

    private const int MinimumTimeoutMilliseconds = 250;
    private const int MaximumTimeoutMilliseconds = 10000;

    private static readonly TimeSpan WarnInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, long> _lastWarnTimestamps = new(StringComparer.Ordinal);

    private int _timeoutMilliseconds = 2000;

    public DBusConnection Connection { get; } = connection;

    /// <summary>Per-call timeout. Values outside the supported range are clamped.</summary>
    public int TimeoutMilliseconds
    {
        get => _timeoutMilliseconds;
        set => _timeoutMilliseconds = Math.Clamp(value, MinimumTimeoutMilliseconds, MaximumTimeoutMilliseconds);
    }

    /// <summary>Calls a method and ignores the reply. Returns false when the call failed.</summary>
    public async Task<bool> CallAsync(
        string destination,
        string path,
        string @interface,
        string member,
        string? signature = null,
        DBusArgumentWriter? writeArguments = null)
    {
        try
        {
            MessageBuffer message = CreateCall(destination, path, @interface, member, signature, writeArguments);
            await Connection.CallMethodAsync(message).WaitAsync(Timeout).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Warn(destination, path, member, ex);
            return false;
        }
    }

    /// <summary>Calls a method and reads its reply. Returns <paramref name="fallback"/> when the call failed.</summary>
    public async Task<T> CallAsync<T>(
        string destination,
        string path,
        string @interface,
        string member,
        MessageValueReader<T> reader,
        T fallback,
        string? signature = null,
        DBusArgumentWriter? writeArguments = null)
    {
        try
        {
            MessageBuffer message = CreateCall(destination, path, @interface, member, signature, writeArguments);
            return await Connection.CallMethodAsync(message, reader, null).WaitAsync(Timeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Warn(destination, path, member, ex);
            return fallback;
        }
    }

    /// <summary>Reads a property through org.freedesktop.DBus.Properties.Get.</summary>
    public Task<VariantValue> GetPropertyAsync(string destination, string path, string @interface, string property)
    {
        return CallAsync(
            destination,
            path,
            PropertiesInterface,
            "Get",
            ReadVariant,
            default,
            "ss",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(@interface);
                writer.WriteString(property);
            });
    }

    /// <summary>Writes a property through org.freedesktop.DBus.Properties.Set.</summary>
    public Task<bool> SetPropertyAsync(
        string destination,
        string path,
        string @interface,
        string property,
        DBusArgumentWriter writeVariant)
    {
        return CallAsync(
            destination,
            path,
            PropertiesInterface,
            "Set",
            "ssv",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(@interface);
                writer.WriteString(property);
                writeVariant(ref writer);
            });
    }

    /// <summary>
    /// Subscribes to a signal. The handler runs on the connection read loop, so it must return
    /// quickly and must never block. Returns null when the subscription could not be established.
    /// </summary>
    public async Task<IDisposable?> WatchSignalAsync<T>(
        string sender,
        string path,
        string @interface,
        string signal,
        MessageValueReader<T> reader,
        Action<T> handler)
    {
        try
        {
            return await Connection.WatchSignalAsync(
                sender,
                path,
                @interface,
                signal,
                reader,
                (Notification<T> notification) =>
                {
                    if (notification.Exception is not null)
                    {
                        Warn(sender, path, signal, notification.Exception);
                        return;
                    }

                    if (!notification.HasValue)
                    {
                        return;
                    }

                    try
                    {
                        handler(notification.Value);
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"KDE Plasma: signal handler for {@interface}.{signal} failed.", ex);
                    }
                },
                flags: ObserverFlags.None,
                emitOnCapturedContext: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Warn(sender, path, signal, ex);
            return null;
        }
    }

    public static VariantValue ReadVariant(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        return reader.ReadVariantValue();
    }

    public static string ReadString(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        return reader.ReadString();
    }

    public static bool ReadBool(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        return reader.ReadBool();
    }

    public static int ReadInt32(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        return reader.ReadInt32();
    }

    public static string[] ReadStringArray(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();
        return reader.ReadArrayOfString();
    }

    private TimeSpan Timeout => TimeSpan.FromMilliseconds(_timeoutMilliseconds);

    private MessageBuffer CreateCall(
        string destination,
        string path,
        string @interface,
        string member,
        string? signature,
        DBusArgumentWriter? writeArguments)
    {
        MessageWriter writer = Connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination, path, @interface, member, signature);
            writeArguments?.Invoke(ref writer);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private void Warn(string destination, string path, string member, Exception exception)
    {
        string key = string.Concat(destination, path, member);
        long now = Stopwatch.GetTimestamp();

        if (_lastWarnTimestamps.TryGetValue(key, out long last) && Stopwatch.GetElapsedTime(last, now) < WarnInterval)
        {
            return;
        }

        _lastWarnTimestamps[key] = now;
        logger.Warn($"KDE Plasma: D-Bus call {destination}{path} {member} failed: {exception.Message}");
    }
}
