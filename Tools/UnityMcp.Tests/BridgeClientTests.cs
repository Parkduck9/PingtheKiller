using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnityMcp;
using Xunit;

namespace UnityMcp.Tests;

public class BridgeClientTests
{
    private static readonly BridgeClientOptions FastOptions = new()
    {
        ReconnectDelay = TimeSpan.FromMilliseconds(50),
        ConnectTimeout = TimeSpan.FromMilliseconds(500),
        ConnectGrace = TimeSpan.FromMilliseconds(1500),
    };

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    private static JsonNode? Echo(string cmd, JsonElement args) =>
        new JsonObject { ["cmd"] = cmd, ["args"] = JsonNode.Parse(args.GetRawText()) };

    [Fact]
    public async Task Call_ReturnsBridgeResult()
    {
        await using var bridge = new FakeBridge(Echo).Start();
        await using var client = new BridgeClient("127.0.0.1", bridge.Port, FastOptions);

        var result = await client.CallAsync("status", new JsonObject { ["x"] = 3 }, CallTimeout);

        Assert.Equal("status", result.GetProperty("cmd").GetString());
        Assert.Equal(3, result.GetProperty("args").GetProperty("x").GetInt32());
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task Call_MatchesConcurrentResponsesById()
    {
        await using var bridge = new FakeBridge(Echo).Start();
        await using var client = new BridgeClient("127.0.0.1", bridge.Port, FastOptions);

        var calls = Enumerable.Range(0, 20)
            .Select(i => client.CallAsync("c" + i, null, CallTimeout))
            .ToArray();
        var results = await Task.WhenAll(calls);

        for (int i = 0; i < results.Length; i++)
            Assert.Equal("c" + i, results[i].GetProperty("cmd").GetString());
    }

    [Fact]
    public async Task Call_WithoutBridge_FailsWithNotConnectedInsteadOfHanging()
    {
        int port = FreePort();
        await using var client = new BridgeClient("127.0.0.1", port, FastOptions);
        var sw = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAsync<BridgeUnavailableException>(() => client.CallAsync("status", null, CallTimeout));

        Assert.Contains("not connected", ex.Message);
        Assert.InRange(sw.Elapsed.TotalSeconds, 1.0, 4.0); // ConnectGrace, not CallTimeout
    }

    [Fact]
    public async Task Call_BridgeErrorBecomesCommandException()
    {
        await using var bridge = new FakeBridge((_, _) => throw new InvalidOperationException("menu missing")).Start();
        await using var client = new BridgeClient("127.0.0.1", bridge.Port, FastOptions);

        var ex = await Assert.ThrowsAsync<BridgeCommandException>(() => client.CallAsync("execute_menu", null, CallTimeout));

        Assert.Equal("menu missing", ex.Message);
    }

    [Fact]
    public async Task Call_SilentBridge_TimesOut()
    {
        await using var bridge = new FakeBridge((_, _) => FakeBridge.NoReply).Start();
        await using var client = new BridgeClient("127.0.0.1", bridge.Port, FastOptions);

        await Assert.ThrowsAsync<BridgeTimeoutException>(() => client.CallAsync("status", null, TimeSpan.FromMilliseconds(300)));
        Assert.True(client.IsConnected); // a slow editor is not a dead editor
    }

    [Fact]
    public async Task Call_InFlightWhenConnectionDrops_FailsFastWithUnavailable()
    {
        FakeBridge? bridgeRef = null;
        await using var bridge = new FakeBridge((_, _) =>
        {
            _ = Task.Delay(100).ContinueWith(_ => bridgeRef!.DropConnections());
            return FakeBridge.NoReply;
        }).Start();
        bridgeRef = bridge;
        await using var client = new BridgeClient("127.0.0.1", bridge.Port, FastOptions);
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAsync<BridgeUnavailableException>(() => client.CallAsync("refresh", null, TimeSpan.FromSeconds(30)));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task Client_ReconnectsAfterBridgeRestartOnSamePort()
    {
        var bridge = new FakeBridge(Echo).Start();
        int port = bridge.Port;
        await using var client = new BridgeClient("127.0.0.1", port, FastOptions);
        await client.CallAsync("status", null, CallTimeout);

        // Domain reload: listener and sockets go away.
        await bridge.StopAsync();
        await WaitUntil(() => !client.IsConnected);
        await Assert.ThrowsAsync<BridgeUnavailableException>(() => client.CallAsync("status", null, CallTimeout));

        // Bridge comes back after [InitializeOnLoad].
        await using var restarted = new FakeBridge(Echo, port).Start();
        var result = await client.CallAsync("status", null, CallTimeout);

        Assert.Equal("status", result.GetProperty("cmd").GetString());
        Assert.Equal(2, client.ConnectCount);
    }

    [Fact]
    public async Task Call_WaitsWithinGraceForBridgeThatIsComingBack()
    {
        int port = FreePort();
        await using var client = new BridgeClient("127.0.0.1", port, FastOptions);
        client.Start();

        var call = client.CallAsync("status", null, CallTimeout);
        await Task.Delay(300);
        await using var bridge = new FakeBridge(Echo, port).Start();

        var result = await call;
        Assert.Equal("status", result.GetProperty("cmd").GetString());
    }

    internal static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("condition not reached");
            await Task.Delay(20);
        }
    }
}
