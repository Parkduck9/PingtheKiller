using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityMcp;

namespace UnityMcp.Tests;

/// <summary>
/// Stand-in for the Unity bridge: same framing, a scripted handler per request. The handler returns the
/// <c>result</c> node, throws to send <c>ok:false</c>, or returns <see cref="NoReply"/> to stay silent.
/// </summary>
internal sealed class FakeBridge : IAsyncDisposable
{
    public static readonly JsonNode NoReply = JsonValue.Create("__no_reply__");

    private readonly Func<string, JsonElement, JsonNode?> _handler;
    private readonly List<TcpClient> _clients = [];
    private TcpListener? _listener;
    private Task? _acceptLoop;

    public FakeBridge(Func<string, JsonElement, JsonNode?> handler, int port = 0)
    {
        _handler = handler;
        Port = port;
    }

    public int Port { get; private set; }
    public int Accepted;

    public FakeBridge Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, Port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_listener);
        return this;
    }

    /// <summary>Closes the listener and every connection, like a Unity domain reload does.</summary>
    public async Task StopAsync()
    {
        _listener?.Stop();
        lock (_clients)
        {
            foreach (var c in _clients) c.Dispose();
            _clients.Clear();
        }
        if (_acceptLoop != null) await _acceptLoop;
        _listener = null;
    }

    public void DropConnections()
    {
        lock (_clients)
        {
            foreach (var c in _clients) c.Dispose();
            _clients.Clear();
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch (Exception) { return; }
            Interlocked.Increment(ref Accepted);
            lock (_clients) _clients.Add(client);
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                var payload = await BridgeFraming.ReadFrameAsync(stream, CancellationToken.None);
                if (payload == null) return;
                using var doc = JsonDocument.Parse(payload);
                long id = doc.RootElement.GetProperty("id").GetInt64();
                string cmd = doc.RootElement.GetProperty("cmd").GetString()!;
                var args = doc.RootElement.GetProperty("args").Clone();

                JsonObject response;
                try
                {
                    var result = _handler(cmd, args);
                    if (ReferenceEquals(result, NoReply)) continue;
                    response = new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result };
                }
                catch (Exception ex)
                {
                    response = new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = ex.Message };
                }
                await stream.WriteAsync(BridgeFraming.Encode(JsonSerializer.SerializeToUtf8Bytes(response)));
            }
        }
        catch (Exception)
        {
            // Connection closed by the test.
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
