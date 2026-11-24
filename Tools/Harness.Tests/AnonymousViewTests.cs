using System.Diagnostics;
using PingTheKiller.Harness;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Harness.Tests;

internal static class H3
{
    public static async Task<CheckResult> RunAsync(LobbyServer server)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H3" } }, deadline.Token);
        return Assert.Single(report.Items);
    }
}

// False-fail guards (H3 is a gate): a survivor bot walking right past the stopped survivor is attributed to its own
// true position. One class per mode so they run in parallel.
public abstract class H3PassingBotTest(bool anonymous)
{
    [Fact]
    public async Task ABotWalkingRightPastTheStoppedSurvivorDoesNotFailH3()
    {
        await using var server = new LobbyServer { AnonymousKillerView = anonymous, PassingBot = true };
        var h3 = await H3.RunAsync(server);
        Assert.True(h3.Passed, h3.Evidence);
        Assert.True(server.MinBotDistanceWhileStill < 0.3f, $"bot closest pass {server.MinBotDistanceWhileStill} m");
        Assert.Contains("0 unexplained, 0 to stopped survivor 2", h3.Evidence);
        Assert.DoesNotContain("survivor 2, 0 to other survivors", h3.Evidence); // the bot was seen and attributed
    }
}

public sealed class H3PassingBotIdModeTests() : H3PassingBotTest(false);
public sealed class H3PassingBotAnonymousTests() : H3PassingBotTest(true);

public sealed class H3BorrowedIdTests
{
    // Every killer-view entry carries ANOTHER survivor's real Id: the passing bot's entry carries the stopped survivor's
    // Id 2 for the whole Silent window. Any lookup of a survivor by Id in the killer stream would fail (or pass) wrongly.
    [Fact]
    public async Task KillerViewIdsAreNeverUsedToFindSurvivors()
    {
        await using var server = new LobbyServer { RotateKillerViewIds = true, PassingBot = true };
        var h3 = await H3.RunAsync(server);
        Assert.True(h3.Passed, h3.Evidence);
        Assert.Contains("0 to stopped survivor 2", h3.Evidence);
    }
}

// Each broken fake must fail H3, in Id mode and in anonymous mode (one class per mode, so they run in parallel).
public abstract class H3BrokenAnonymousTest(bool anonymous)
{
    public static TheoryData<string> Faults => new() { "exact", "rounded", "offset", "extra", "unknown-packet" };

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task BrokenKillerViewFailsH3(string fault)
    {
        await using var server = new LobbyServer
        {
            AnonymousKillerView = anonymous, LeakSilent = fault == "exact", LeakSilentRounded = fault == "rounded",
            LeakSilentOffset = fault == "offset", ExtraEntry = fault == "extra", LeakSilentInUnknown = fault == "unknown-packet"
        };
        var h3 = await H3.RunAsync(server);
        Assert.False(h3.Passed, $"FALSE PASS ({fault}, anonymous={anonymous}): {h3.Evidence}");
        Assert.Equal(0, h3.Score);
        Assert.Contains(fault switch
        {
            "exact" => "Silent survivor",
            "rounded" or "offset" => "Silent survivor 2 leaked in the killer view",
            "extra" => "unexplained killer-view entry",
            _ => "bytes leaked in unknown packet 0x7E"
        }, h3.Evidence);
        Assert.Contains("leak", h3.Evidence);
    }
}

public sealed class H3BrokenIdModeTests() : H3BrokenAnonymousTest(false);
public sealed class H3BrokenAnonymousModeTests() : H3BrokenAnonymousTest(true);

// Unknown server packet Ids: skipped (never delivered), counted per Id in every check's evidence. A known Id that
// fails to decode, and a bad length, still fail.
public sealed class UnknownPacketTests
{
    [Fact]
    public async Task UnknownFramesInEveryStreamStillPassH1ToH5WithSkipCounts()
    {
        await using var server = new LobbyServer { UnknownFrames = true, AnonymousKillerView = true };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Duration = TimeSpan.FromSeconds(2), Only = new HashSet<string> { "H1", "H2", "H3", "H4", "H5" } }, deadline.Token);
        Assert.All(report.Items, item => Assert.True(item.Passed, $"{item.Id}: {item.Evidence}"));
        Assert.Contains("skipped unknown packets: id=0x7E ×10", report.Items.Single(i => i.Id == "H1").Evidence); // >= 10000 pongs
        foreach (var item in report.Items.Where(i => i.Id != "H1"))
        {
            Assert.Contains("skipped unknown packets: id=0x21 ×", item.Evidence);
            Assert.Contains("id=0x7E ×", item.Evidence);
        }
        Assert.DoesNotContain("(+0 unknown-Id frames)", report.Items.Single(i => i.Id == "H3").Evidence);
    }

    [Fact]
    public async Task H1PassesWithUnknownFramesBeforeEveryFragmentedPong()
    {
        await using var server = new PingServer(unknownFrames: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H1" } }, deadline.Token);
        var h1 = Assert.Single(report.Items);
        Assert.True(h1.Passed, h1.Evidence);
        Assert.EndsWith("; skipped unknown packets: id=0x21 ×10001, id=0x7E ×10001", h1.Evidence);
    }

    [Fact]
    public async Task AKnownIdWithTrailingPayloadBytesStillFailsH1()
    {
        await using var server = new PingServer(unknownFrames: true, trailingByte: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var report = await HarnessRunner.RunAsync(new Options { Port = server.Port, Timeout = TimeSpan.FromSeconds(2),
            Only = new HashSet<string> { "H1" } }, deadline.Token);
        var h1 = Assert.Single(report.Items);
        Assert.False(h1.Passed);
        Assert.Contains("invalid frame: Trailing bytes after packet payload.", h1.Evidence);
    }

    [Fact]
    public async Task WireClientSkipsUnknownIdsButFaultsOnAKnownIdDecodeFailure()
    {
        await using var server = new PingServer(unknownFrames: true, trailingByte: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var client = await WireClient.ConnectAsync(new Options { Port = server.Port }, deadline.Token);
        long before = Stopwatch.GetTimestamp();
        await client.SendAsync(new C2S_Ping { Timestamp = 1 }, deadline.Token);
        var error = await Assert.ThrowsAsync<IOException>(() => client.ReadAsync(deadline.Token));
        Assert.IsType<PacketFormatException>(error.InnerException);
        Assert.Equal(new Dictionary<ushort, int> { [0x7E] = 1, [0x21] = 1 }, client.UnknownCounts());
        var kept = client.UnknownFramesSince(before);
        Assert.Equal(new ushort[] { 0x7E, 0x21 }, kept.Select(f => f.Id));
        Assert.Equal(18, kept[0].Raw.Length); // header + Id + 12-byte payload, kept raw
    }
}
