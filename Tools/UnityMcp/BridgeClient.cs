using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UnityMcp;

/// <summary>Thrown when no bridge connection exists or it dropped while a call was in flight.</summary>
public sealed class BridgeUnavailableException(string message) : Exception(message);

/// <summary>Thrown when the bridge is connected but did not answer within the call timeout.</summary>
public sealed class BridgeTimeoutException(string message) : Exception(message);

/// <summary>Thrown when the bridge answered with <c>ok:false</c>.</summary>
public sealed class BridgeCommandException(string message) : Exception(message);

public sealed class BridgeClientOptions
{
    /// <summary>Delay between connection attempts while the bridge is down.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound for a single TCP connect attempt.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a call waits for a connection before failing with <see cref="BridgeUnavailableException"/>.</summary>
    public TimeSpan ConnectGrace { get; init; } = TimeSpan.FromSeconds(3);
}

/// <summary>
/// Long-lived TCP client to the Unity bridge. A background loop keeps (re)connecting, so Unity domain
/// reloads, editor restarts and late editor starts are all absorbed here; MCP calls never hang on a
/// dead socket — they either get an answer, a timeout, or a clear "not connected" error.
/// </summary>
public sealed class BridgeClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly BridgeClientOptions _options;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<long, Pending> _pending = new();
    private readonly object _stateLock = new();
    private Connection? _connection;
    private TaskCompletionSource<Connection> _connected = NewConnectedSignal();
    private Task? _loop;
    private long _nextId;

    public BridgeClient(string host, int port, BridgeClientOptions? options = null)
    {
        _host = host;
        _port = port;
        _options = options ?? new BridgeClientOptions();
        DisconnectedSinceUtc = DateTimeOffset.UtcNow;
    }

    public string Endpoint => $"{_host}:{_port}";
    public bool IsConnected => Volatile.Read(ref _connection) != null;
    public int ConnectCount { get; private set; }
    public DateTimeOffset? ConnectedSinceUtc { get; private set; }
    public DateTimeOffset? DisconnectedSinceUtc { get; private set; }

    public void Start()
    {
        lock (_stateLock)
            _loop ??= Task.Run(() => ConnectLoopAsync(_lifetime.Token));
    }

    /// <summary>Sends one command and returns the bridge's <c>result</c> element.</summary>
    public async Task<JsonElement> CallAsync(string command, JsonObject? args, TimeSpan timeout, CancellationToken ct = default)
    {
        Start();
        var connection = await WaitForConnectionAsync(ct).ConfigureAwait(false);

        long id = Interlocked.Increment(ref _nextId);
        var pending = new Pending(connection);
        _pending[id] = pending;
        try
        {
            // OnDisconnected closes the connection before it fails pending calls, so a call registered
            // after that sweep still sees the closed flag here and cannot wait for its timeout.
            if (connection.Closed.IsCancellationRequested)
                throw new BridgeUnavailableException(LostMessage(command));

            var request = new JsonObject { ["id"] = id, ["cmd"] = command, ["args"] = args ?? new JsonObject() };
            var frame = BridgeFraming.Encode(JsonSerializer.SerializeToUtf8Bytes(request));
            try
            {
                await connection.SendAsync(frame, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                connection.Close();
                throw new BridgeUnavailableException(LostMessage(command));
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            JsonElement response;
            try
            {
                response = await pending.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new BridgeTimeoutException(
                    $"Unity did not answer '{command}' within {timeout.TotalSeconds:0.#} s. The editor main thread may be busy " +
                    "(importing, compiling, a long test) or blocked by a modal dialog.");
            }

            if (response.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return response.TryGetProperty("result", out var result) ? result.Clone() : default;
            string error = response.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()!
                : "Unity bridge returned an error without a message.";
            throw new BridgeCommandException(error);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task<Connection> WaitForConnectionAsync(CancellationToken ct)
    {
        Task<Connection> signal;
        lock (_stateLock)
        {
            if (_connection != null) return _connection;
            signal = _connected.Task;
        }
        try
        {
            return await signal.WaitAsync(_options.ConnectGrace, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new BridgeUnavailableException(
                $"Unity editor not connected on {Endpoint}: the editor is closed, the McpBridge editor script is not loaded, " +
                "or a script domain reload is in progress. Retry in a few seconds; unity_status shows the connection state.");
        }
    }

    private async Task ConnectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Connection? connection = null;
            try
            {
                var tcp = new TcpClient { NoDelay = true };
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(_options.ConnectTimeout);
                    try
                    {
                        await tcp.ConnectAsync(_host, _port, connectCts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        tcp.Dispose();
                        throw;
                    }
                }
                connection = new Connection(tcp);
                OnConnected(connection);
                await ReadLoopAsync(connection, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or InvalidDataException or JsonException or ObjectDisposedException)
            {
                // Bridge down, reloading, or sent garbage: drop the connection and retry.
            }
            finally
            {
                if (connection != null) OnDisconnected(connection);
            }

            try
            {
                await Task.Delay(_options.ReconnectDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReadLoopAsync(Connection connection, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, connection.Closed);
        while (true)
        {
            var payload = await BridgeFraming.ReadFrameAsync(connection.Stream, linked.Token).ConfigureAwait(false);
            if (payload == null) return;
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out long id)
                && _pending.TryGetValue(id, out var pending))
            {
                pending.Completion.TrySetResult(root.Clone());
            }
        }
    }

    private void OnConnected(Connection connection)
    {
        TaskCompletionSource<Connection> signal;
        lock (_stateLock)
        {
            _connection = connection;
            ConnectCount++;
            ConnectedSinceUtc = DateTimeOffset.UtcNow;
            DisconnectedSinceUtc = null;
            signal = _connected;
        }
        signal.TrySetResult(connection);
    }

    private void OnDisconnected(Connection connection)
    {
        lock (_stateLock)
        {
            if (ReferenceEquals(_connection, connection))
            {
                _connection = null;
                ConnectedSinceUtc = null;
                DisconnectedSinceUtc = DateTimeOffset.UtcNow;
                if (_connected.Task.IsCompleted) _connected = NewConnectedSignal();
            }
        }
        connection.Close();
        foreach (var pair in _pending)
        {
            if (ReferenceEquals(pair.Value.Connection, connection))
                pair.Value.Completion.TrySetException(new BridgeUnavailableException(LostMessage(null)));
        }
    }

    private static string LostMessage(string? command) =>
        (command == null ? "Connection to the Unity editor was lost" : $"Connection to the Unity editor was lost while sending '{command}'") +
        " (the editor is probably reloading scripts / entering Play mode, or it was closed). The server reconnects automatically; retry shortly.";

    private static TaskCompletionSource<Connection> NewConnectedSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        Volatile.Read(ref _connection)?.Close();
        if (_loop != null)
        {
            try { await _loop.ConfigureAwait(false); } catch { /* shutting down */ }
        }
        _lifetime.Dispose();
    }

    private sealed class Pending(Connection connection)
    {
        public Connection Connection { get; } = connection;
        public TaskCompletionSource<JsonElement> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Connection
    {
        private readonly TcpClient _tcp;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly CancellationTokenSource _closed = new();

        public Connection(TcpClient tcp)
        {
            _tcp = tcp;
            Stream = tcp.GetStream();
        }

        public NetworkStream Stream { get; }
        public CancellationToken Closed => _closed.Token;

        public async Task SendAsync(byte[] frame, CancellationToken ct)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Stream.WriteAsync(frame, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Close()
        {
            try { _closed.Cancel(); } catch (ObjectDisposedException) { }
            _tcp.Dispose();
        }
    }
}
