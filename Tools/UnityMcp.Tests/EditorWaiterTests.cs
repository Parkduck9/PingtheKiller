using System.Text.Json;
using System.Text.Json.Nodes;
using UnityMcp;
using Xunit;

namespace UnityMcp.Tests;

public class EditorWaiterTests
{
    private static readonly BridgeClientOptions FastOptions = new()
    {
        ReconnectDelay = TimeSpan.FromMilliseconds(50),
        ConnectTimeout = TimeSpan.FromMilliseconds(500),
        ConnectGrace = TimeSpan.FromMilliseconds(300),
    };

    private static JsonObject Status(string epoch, bool compiling = false, bool playing = false, bool willPlay = false) => new()
    {
        ["epoch"] = epoch,
        ["isCompiling"] = compiling,
        ["isUpdating"] = false,
        ["isPlaying"] = playing,
        ["isPlayingOrWillChangePlaymode"] = willPlay || playing,
        ["isPaused"] = false,
    };

    [Fact]
    public async Task WaitForStable_RidesThroughCompileAndDomainReload()
    {
        // Old domain: compiling for a few polls, then the bridge disappears (reload).
        int polls = 0;
        var oldDomain = new FakeBridge((_, _) => Status("old", compiling: true)).Start();
        int port = oldDomain.Port;
        await using var client = new BridgeClient("127.0.0.1", port, FastOptions);
        var waiter = new EditorWaiter(client) { PollInterval = TimeSpan.FromMilliseconds(30) };

        var wait = waiter.WaitForStableAsync(_ => true, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(200), default);
        await Task.Delay(200);
        await oldDomain.StopAsync();
        await Task.Delay(400);
        await using var newDomain = new FakeBridge((_, _) => { polls++; return Status("new"); }, port).Start();

        var status = await wait;

        Assert.Equal("new", status.GetProperty("epoch").GetString());
        Assert.True(polls >= 2, "settle time requires several idle polls");
    }

    [Fact]
    public async Task WaitForStable_RequiresTheConditionToHoldForTheSettleTime()
    {
        int polls = 0;
        // Enter play mode: "will change" first, then playing.
        await using var bridge = new FakeBridge((_, _) => ++polls < 5 ? Status("e", willPlay: true) : Status("e", playing: true)).Start();
        await using var client = new BridgeClient("127.0.0.1", bridge.Port, FastOptions);
        var waiter = new EditorWaiter(client) { PollInterval = TimeSpan.FromMilliseconds(20) };

        var status = await waiter.WaitForStableAsync(s => EditorWaiter.GetBool(s, "isPlaying"), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100), default);

        Assert.True(status.GetProperty("isPlaying").GetBoolean());
        Assert.True(polls >= 6);
    }

    [Fact]
    public async Task WaitForStable_TimesOutWithLastState()
    {
        await using var bridge = new FakeBridge((_, _) => Status("e", compiling: true)).Start();
        await using var client = new BridgeClient("127.0.0.1", bridge.Port, FastOptions);
        var waiter = new EditorWaiter(client) { PollInterval = TimeSpan.FromMilliseconds(20) };

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            waiter.WaitForStableAsync(_ => true, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(50), default));

        Assert.Contains("compiling", ex.Message);
    }

    [Theory]
    [InlineData(false, false, false, "idle")]
    [InlineData(true, false, false, "compiling")]
    [InlineData(false, false, true, "switching play mode")]
    [InlineData(false, true, true, "playing")]
    public void Describe_MapsEditorFlags(bool compiling, bool playing, bool willChange, string expected)
    {
        var json = JsonSerializer.SerializeToElement(new
        {
            isCompiling = compiling,
            isUpdating = false,
            isPlaying = playing,
            isPlayingOrWillChangePlaymode = willChange,
            isPaused = false,
        });

        Assert.Equal(expected, EditorWaiter.Describe(json));
    }
}
