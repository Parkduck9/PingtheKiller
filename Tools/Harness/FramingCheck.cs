using System.Diagnostics;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness;

public static class FramingCheck
{
    public const int PingCount = 10_000;
    public const int Seed = 0x513A;

    public static async Task<string> RunAsync(Options options, CancellationToken ct)
    {
        await using var client = await WireClient.ConnectAsync(options, ct);
        long timestampBase = Stopwatch.GetTimestamp();
        byte[] first = PacketCodec.Encode(new C2S_Ping { Timestamp = timestampBase });
        var bytes = new byte[first.Length * PingCount];
        for (int i = 0; i < PingCount; i++)
            PacketCodec.Encode(new C2S_Ping { Timestamp = timestampBase + i }).CopyTo(bytes, i * first.Length);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30) + options.Timeout);
        // Send and receive concurrently; a server's send-queue pressure must not be
        // caused by the harness waiting for all writes before reading any replies.
        var send = SendChunksAsync(client, bytes, deadline.Token);
        try
        {
            for (int i = 0; i < PingCount; i++)
            {
                ReceivedFrame frame;
                try { frame = await client.ReadAsync<S2C_Pong>(deadline.Token); }
                catch (Exception ex) when (ex is OperationCanceledException or IOException)
                {
                    throw new CheckFailure($"Pong #{i} missing after {i}/10000 replies: {ex.GetType().Name}: {ex.Message}");
                }
                long actual = ((S2C_Pong)frame.Packet).Timestamp;
                Check.Require(actual == timestampBase + i,
                    $"Pong #{i}: expected timestamp {timestampBase + i}, got {actual}.");
            }
            await send;
            // A final ordered barrier detects an extra/duplicate pong queued after #10000
            // and proves the connection is still serving requests.
            await client.SendAsync(new C2S_Ping { Timestamp = timestampBase + PingCount }, deadline.Token);
            var barrier = (S2C_Pong)(await client.ReadAsync<S2C_Pong>(deadline.Token)).Packet;
            Check.Require(barrier.Timestamp == timestampBase + PingCount, "Extra or reordered Pong after the 10000-frame stream.");
            return $"10000/10000 ordered timestamps; zero disconnects; seed={Seed}; chunks=1..1500 bytes; final Ping/Pong barrier passed.";
        }
        finally
        {
            deadline.Cancel();
            try { await send; }
            catch (Exception) when (deadline.IsCancellationRequested) { }
        }
    }

    private static async Task SendChunksAsync(WireClient client, byte[] bytes, CancellationToken ct)
    {
        var random = new Random(Seed);
        for (int offset = 0; offset < bytes.Length;)
        {
            int count = Math.Min(random.Next(1, 1501), bytes.Length - offset);
            await client.SendRawAsync(bytes.AsMemory(offset, count), ct);
            offset += count;
            if (random.Next(4) == 0) await Task.Delay(random.Next(1, 4), ct);
        }
    }
}
