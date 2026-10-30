using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using PingTheKiller.Shared.Net;

namespace PingTheKiller.Harness;

public static class MatchChecks
{
    private static long After(long timestamp, TimeSpan duration) => timestamp + (long)(duration.TotalSeconds * Stopwatch.Frequency);

    private static async Task<List<ReceivedFrame>> WorldsAsync(WireClient client, long start, long end, CancellationToken ct)
    {
        var frames = new List<ReceivedFrame>();
        while (true)
        {
            var frame = await client.ReadAsync<S2C_WorldState>(ct);
            if (frame.ReceivedAt < start) continue;
            frames.Add(frame);
            if (frame.ReceivedAt >= end) return frames;
        }
    }

    public static async Task<string> SyncAsync(MatchSession session, Options options, CancellationToken ct)
    {
        long start = Stopwatch.GetTimestamp(), end = After(start, options.Duration);
        var results = await Task.WhenAll(
            WorldsAsync(session.Killer, start, end, ct),
            WorldsAsync(session.Survivor, start, end, ct));
        var evidence = new List<string>();
        for (int i = 0; i < results.Length; i++)
        {
            var samples = results[i];
            var (arrival, tick) = Measurements.Sync(samples);
            evidence.Add($"client{i}: n={samples.Count}, arrival p99={arrival:F3} ms, observed tick-period p99={tick:F3} ms");
        }
        return $"2 humans + 6 bots, {options.Duration.TotalSeconds:F2} s; {string.Join("; ", evidence)}; Tick strictly increasing, gaps <=2. Tick period is inferred from TCP reception, not server CPU duration.";
    }

    public static async Task<string> MaskingAsync(MatchSession session, CancellationToken ct)
    {
        // Killer-bound survivor entries may be anonymous (U2c: Id 0, shuffled). Every one is attributed by tick and
        // position to the survivor whose true position (the survivor client's own stream holds every survivor, bots
        // included) at the same tick is nearest, within KillerView.Epsilon. The killer stream is never searched by Id.
        int killerId = session.KillerIdentity.PlayerId, survivorId = session.SurvivorIdentity.PlayerId;
        // U3d: a killer entry in a survivor world is legal only with a clear line of sight at the same tick and exact contract
        // fields; it is judged against the killer client's own entry, joined by Tick (SightJoin). Anything else is a leak.
        var sight = new SightJoin(killerId);
        // U3d a3: the judged window is defined by survivor-stream ticks (from the moving baseline on). Killer ticks from before
        // it come from the killer client's retained WorldStates (an earlier check may have consumed them); after it, the killer
        // stream is read until it covers the window's last survivor tick. Every survivor world must join; both streams must
        // keep strictly increasing ticks with gaps <= 2.
        foreach (var retained in session.Killer.RecentWorlds()) sight.SeedKiller((S2C_WorldState)retained.Packet);
        var killerTicks = new TickSequence("killer");
        var survivorTicks = new TickSequence($"survivor {survivorId}");
        void AddSurvivorWorld(S2C_WorldState world)
        {
            survivorTicks.Add(world.Tick);
            sight.AddSurvivor(survivorId, world);
        }
        void AddKillerWorld(S2C_WorldState world)
        {
            killerTicks.Add(world.Tick);
            sight.AddKiller(world);
        }
        // Establish nontrivial coordinate bytes and positive evidence that the killer
        // can see this survivor while moving, before testing omission while Silent.
        long movingStart = Stopwatch.GetTimestamp();
        var moving = SendMovingAsync(session.Survivor, session, 0.613f, 0.789f,
            TimeSpan.FromSeconds(0.65), 20, ct);
        var baseline = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var movingJoin = new TickJoin(killerId, (world, attributions, _) =>
        {
            foreach (var a in attributions)
                if (a.Nearest.Contains(survivorId) && (a.Entry.Flags & (byte)PlayerFlags.Signal) != 0)
                    baseline.TrySetResult($"tick={world.Tick} Signal entry attributed to survivor {survivorId} by tick+position ({a.Distance:F3} m)");
        });
        using (var reading = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            reading.CancelAfter(TimeSpan.FromSeconds(0.65) + session.Timeout);
            async Task FeedAsync(WireClient client, Action<ReceivedFrame> add)
            {
                try
                {
                    while (!baseline.Task.IsCompleted)
                    {
                        add(await client.ReadAsync<S2C_WorldState>(reading.Token));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    reading.Cancel(); // stop the other stream at once; this failure is the result
                    throw;
                }
            }
            var feeding = Task.WhenAll(
                FeedAsync(session.Killer, frame =>
                {
                    AddKillerWorld((S2C_WorldState)frame.Packet);
                    if (frame.ReceivedAt >= movingStart) movingJoin.AddKiller((S2C_WorldState)frame.Packet);
                }),
                FeedAsync(session.Survivor, frame =>
                {
                    if (frame.ReceivedAt < movingStart) return;
                    AddSurvivorWorld((S2C_WorldState)frame.Packet);
                    movingJoin.AddTruth((S2C_WorldState)frame.Packet);
                }));
            await Task.WhenAny(baseline.Task, feeding);
            reading.Cancel();
            try { await feeding; }
            catch (Exception) when (feeding.Exception?.InnerExceptions.FirstOrDefault(e => e is not OperationCanceledException) is { } failure)
            {
                ExceptionDispatchInfo.Throw(failure);
            }
            catch (OperationCanceledException) when (baseline.Task.IsCompleted) { }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new CheckFailure($"Moving baseline missing: no killer-view Signal entry was attributed by tick+position to moving survivor " +
                    $"{survivorId} within {(TimeSpan.FromSeconds(0.65) + session.Timeout).TotalSeconds:F2} s ({movingJoin.Joined} ticks joined).");
            }
        }
        await moving;
        string baselineEvidence = await baseline.Task;
        await session.Survivor.SendAsync(new C2S_MoveInput { Seq = session.NextSequence() }, ct);
        // The grace is measured in wall-clock time AND in ticks: the newest tick either stream had received when the zero input
        // was sent bounds the server tick from below, so a lagging survivor stream cannot hand in a frame from before the grace.
        long sendTick = Math.Max(session.Killer.LatestWorldTick, session.Survivor.LatestWorldTick);
        long settleTicks = (long)Math.Ceiling((GameConstants.SignalHoldSeconds + 0.25) * GameConstants.TickRate);
        long settled = After(Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(GameConstants.SignalHoldSeconds + 0.25));
        ReceivedFrame stoppedFrame;
        do
        {
            stoppedFrame = await session.Survivor.ReadAsync<S2C_WorldState>(ct);
            AddSurvivorWorld((S2C_WorldState)stoppedFrame.Packet);
        } while (stoppedFrame.ReceivedAt < settled || ((S2C_WorldState)stoppedFrame.Packet).Tick < sendTick + settleTicks);
        var stopped = Measurements.Self(stoppedFrame, survivorId);
        Check.Require((stopped.Flags & (byte)PlayerFlags.Signal) == 0, "Survivor remained Signal after zero input and grace period.");
        uint stoppedTick = ((S2C_WorldState)stoppedFrame.Packet).Tick;
        long start = stoppedFrame.ReceivedAt, end = After(start, TimeSpan.FromSeconds(3.1));
        // The killer's Silent window starts at its first WorldState with Tick >= the stopped tick (a lagging killer stream still
        // delivers earlier, Signal ticks after `start`; those are not part of the window).
        long killerStart = 0;
        int killerFrames = 0, killerWorlds = 0, survivorWorlds = 0, others = 0, ambiguous = 0;
        string? ambiguity = null;
        var join = new TickJoin(killerId, (world, attributions, survivors) =>
        {
            foreach (var a in attributions)
            {
                if (!a.Nearest.Contains(survivorId)) { others++; continue; }
                // Only bit-identical positions are ambiguous (a bot standing exactly on the stopped survivor): evidence, not a failure.
                Check.Require(a.ExactTie,
                    $"Silent survivor {survivorId} leaked in the killer view at tick={world.Tick}: entry ({a.Entry.X:R},{a.Entry.Z:R}) Id={a.Entry.Id} " +
                    $"is attributed to it by tick+position ({a.Distance:F4} m from its true position ({stopped.X:R},{stopped.Z:R}), " +
                    $"nearest of {survivors} survivors).");
                ambiguous++;
                ambiguity ??= $"tick={world.Tick}: survivors [{string.Join(",", a.Nearest)}] at bit-identical positions";
            }
        });
        join.AddTruth((S2C_WorldState)stoppedFrame.Packet);

        void Survivor(ReceivedFrame frame)
        {
            if (frame.Packet is not S2C_WorldState world) return;
            AddSurvivorWorld(world);
            var current = Measurements.Self(frame, survivorId);
            Check.Require(KillerView.SamePosition(current, stopped), "Survivor moved after stopping; coordinate sentinel is no longer valid.");
            survivorWorlds++;
            join.AddTruth(world);
        }

        // Every killer tick read (also those queued while the survivor settled) is line-of-sight ground truth; frames from the
        // Silent window on are byte-scanned and attributed.
        bool Killer(ReceivedFrame frame)
        {
            if (frame.Packet is S2C_WorldState own)
            {
                AddKillerWorld(own);
                if (killerStart == 0 && own.Tick >= stoppedTick) killerStart = frame.ReceivedAt;
            }
            if (killerStart == 0) return false;
            Measurements.Masked(frame, stopped);
            killerFrames++;
            if (frame.Packet is S2C_WorldState world)
            {
                killerWorlds++;
                join.AddKiller(world);
            }
            return true;
        }

        async Task ReadKillerAsync()
        {
            while (true)
            {
                var frame = await session.Killer.ReadAsync(ct);
                if (Killer(frame) && frame.ReceivedAt >= After(killerStart, TimeSpan.FromSeconds(3.1))) break;
            }
        }

        async Task ReadSurvivorAsync()
        {
            while (true)
            {
                var frame = await session.Survivor.ReadAsync(ct);
                if (frame.ReceivedAt < start) continue;
                Survivor(frame);
                if (frame.ReceivedAt >= end) break;
            }
        }

        await Task.WhenAll(ReadKillerAsync(), ReadSurvivorAsync());
        // Coverage, bounded by --timeout: read the killer stream until it covers the last survivor tick of the window (line of
        // sight), and the survivor stream until it covers killer ticks that arrived first (attribution). A survivor tick the
        // killer never receives then fails in sight.Finish(); killer ticks the survivor stream never delivers stay skipped.
        using (var tail = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            tail.CancelAfter(session.Timeout);
            try
            {
                while (true)
                {
                    if (sight.AwaitingKiller) Killer(await session.Killer.ReadAsync(tail.Token));
                    else if (join.AwaitingTruth) Survivor(await session.Survivor.ReadAsync(tail.Token));
                    else break;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        // Skipped unknown-Id frames are not delivered; their raw bytes get the same scan (from the killer's Silent window on).
        var unknown = killerStart == 0 ? new List<UnknownFrame>() : session.Killer.UnknownFramesSince(killerStart);
        foreach (var frame in unknown) Measurements.MaskedBytes(frame.Raw, $"unknown packet 0x{frame.Id:X2}", stopped);
        Check.Require(killerWorlds >= 50 && survivorWorlds >= 50 && join.Joined >= 50,
            $"Insufficient masking coverage: killer worlds={killerWorlds}, survivor worlds={survivorWorlds}, ticks joined by Tick={join.Joined} (need 50 each).");
        sight.Finish();
        TickSequence.Require(killerTicks, survivorTicks);
        return $"3.1 s Silent capture: {killerFrames} raw killer frames/{killerWorlds} worlds (+{unknown.Count} unknown-Id frames); " +
            $"{join.Joined} killer worlds joined by Tick with survivor ground truth ({join.Skipped} skipped, no survivor-stream tick); " +
            $"{join.Entries} killer-view survivor entries attributed by tick+position (epsilon {KillerView.Epsilon} m): 0 unexplained, " +
            $"0 to stopped survivor {survivorId}, {others} to other survivors, {ambiguous} ambiguous{(ambiguity != null ? $" ({ambiguity})" : "")}; " +
            $"X={stopped.X:R}/Z={stopped.Z:R} byte occurrences=0; killer {killerId} in {sight.Counts.Total} survivor worlds judged by line of sight " +
            $"(entry legal only when clear, exact fields, X/Z within {KillerSight.Epsilon} m of its own position at that tick): {sight.Counts}; " +
            $"every survivor tick joined a killer tick ({survivorTicks}; {killerTicks}; Silent from tick {stoppedTick}); " +
            $"moving baseline: {baselineEvidence}.";
    }

    public static async Task PingAsync(WireClient client, CancellationToken ct)
    {
        long timestamp = Stopwatch.GetTimestamp();
        await client.SendAsync(new C2S_Ping { Timestamp = timestamp }, ct);
        var pong = (S2C_Pong)(await client.ReadAsync<S2C_Pong>(ct)).Packet;
        Check.Require(pong.Timestamp == timestamp, "Control Ping/Pong timestamp mismatch.");
    }

    public static async Task<string> AbuseAsync(MatchSession session, Options options, CancellationToken ct)
    {
        var evidence = new List<string>();
        foreach (int length in new[] { 0, 1, -1, int.MaxValue, 16385 })
        {
            await using (var attacker = await WireClient.ConnectAsync(options, ct))
            {
                var header = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(header, length);
                await attacker.SendRawAsync(header, ct);
                await attacker.ExpectDisconnectAsync(ct);
            }
            await PingAsync(session.Killer, ct);
            evidence.Add($"Length={length}: disconnected/control pong OK");
        }
        await using (var attacker = await WireClient.ConnectAsync(options, ct))
        {
            var unknown = new byte[6];
            BinaryPrimitives.WriteInt32LittleEndian(unknown, 2);
            BinaryPrimitives.WriteUInt16LittleEndian(unknown.AsSpan(4), ushort.MaxValue);
            await attacker.SendRawAsync(unknown, ct);
            await attacker.ExpectDisconnectAsync(ct);
        }
        await PingAsync(session.Killer, ct);
        evidence.Add("unknown ID: disconnected/control pong OK");

        foreach (string mode in new[] { "NaN", "100,100", "500/s" })
        {
            if (session.Survivor.ServerDisconnected)
            {
                // Recovery is needed only after a defensive disconnect. H4 does not
                // assert H5's identity restoration contract on the replacement.
                int oldId = session.SurvivorIdentity.PlayerId;
                var replacement = await session.ReconnectSurvivorAsync(ct, requireSameIdentity: false);
                evidence.Add($"closed survivor socket replaced: PlayerId={oldId}->{replacement.PlayerId}");
            }
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var client = session.Survivor;
                await client.SendAsync(new C2S_MoveInput { Seq = session.NextSequence() }, ct);
                long settled = After(Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(0.25));
                var baseline = await client.ReadAsync<S2C_WorldState>(ct);
                while (baseline.ReceivedAt < settled) baseline = await client.ReadAsync<S2C_WorldState>(ct);
                var player = Measurements.Self(baseline, session.SurvivorIdentity.PlayerId);
                long end = After(baseline.ReceivedAt, TimeSpan.FromSeconds(1.25));
                var samples = new List<ReceivedFrame> { baseline };
                float sign = attempt == 0 ? 1 : -1;
                Task<string> send = mode == "NaN"
                    ? SendNaNAsync()
                    : SendMovingAsync(client, session, sign * (mode == "100,100" ? 100 : 1),
                        mode == "100,100" ? sign * 100 : 0, TimeSpan.FromSeconds(1), mode == "500/s" ? 500 : 20, ct);
                Task observed = ObserveAsync();
                string timing;
                bool disconnected = false;
                try
                {
                    await Task.WhenAll(send, observed);
                    timing = await send;
                    await client.SendAsync(new C2S_MoveInput { Seq = session.NextSequence() }, ct);
                }
                catch (Exception ex) when (client.ServerDisconnected && ex is IOException or System.Net.Sockets.SocketException)
                {
                    disconnected = true;
                    timing = send.IsCompletedSuccessfully ? send.Result : "send interrupted by disconnect";
                }
                await PingAsync(session.Killer, ct);
                // Even a defensive disconnect must not hide an already observed speed violation.
                double maximum = samples.Count >= 3
                    ? Measurements.MaxSpeed(samples, player.Id, player.Role, mode == "NaN") : 0;
                Check.Require(disconnected || samples.Count >= 3, "Not enough WorldStates to measure abusive movement.");
                evidence.Add($"{mode}, direction={(attempt == 0 ? "forward" : "opposite")}: {samples.Count} worlds, max={maximum:F4} m/s, role={player.Role}, {timing}, {(disconnected ? "server disconnected abusive mover (accepted)" : "socket retained")}, control pong OK");
                double roleMax = player.Role == (byte)Role.Killer ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed;
                if (disconnected || mode == "NaN" || maximum >= 0.5 * roleMax) break;
                Check.Require(attempt == 0,
                    $"{string.Join("; ", evidence)}; {mode}: no movement evidence in either direction (need max >= {0.5 * roleMax:F4} m/s).");

                async Task<string> SendNaNAsync()
                {
                    await client.SendAsync(new C2S_MoveInput { DirX = float.NaN, Seq = session.NextSequence() }, ct);
                    return "NaN input sent";
                }

                async Task ObserveAsync()
                {
                    while (true)
                    {
                        var frame = await client.ReadAsync<S2C_WorldState>(ct);
                        if (frame.ReceivedAt < baseline.ReceivedAt) continue;
                        samples.Add(frame);
                        if (frame.ReceivedAt >= end) return;
                    }
                }
            }
        }
        return string.Join("; ", evidence);
    }

    private static async Task<string> SendMovingAsync(WireClient client, MatchSession session, float x, float z,
        TimeSpan duration, int rate, CancellationToken ct)
    {
        int count = (int)(duration.TotalSeconds * rate);
        int batchSize = rate > 60 ? 20 : 1;
        var clock = Stopwatch.StartNew();
        for (int sent = 0; sent < count;)
        {
            var due = TimeSpan.FromSeconds(sent / (double)rate) - clock.Elapsed;
            if (due > TimeSpan.Zero) await Task.Delay(due, ct);
            using var batch = new MemoryStream();
            for (int i = 0; i < batchSize && sent < count; i++, sent++)
            {
                byte[] frame = PacketCodec.Encode(new C2S_MoveInput { DirX = x, DirZ = z, Seq = session.NextSequence() });
                batch.Write(frame);
            }
            await client.SendRawAsync(batch.ToArray(), ct);
        }
        return rate == 500 ? Measurements.FloodTiming(count, clock.Elapsed.TotalSeconds)
            : $"{count} inputs in {clock.Elapsed.TotalSeconds:F3} s";
    }

    public static async Task<string> ReconnectAsync(MatchSession session, CancellationToken ct)
    {
        if (session.Survivor.ServerDisconnected) await session.ReconnectSurvivorAsync(ct);
        await session.Survivor.SendAsync(new C2S_MoveInput { Seq = session.NextSequence() }, ct);
        long settled = After(Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(0.25));
        var before = await session.Survivor.ReadAsync<S2C_WorldState>(ct);
        while (before.ReceivedAt < settled) before = await session.Survivor.ReadAsync<S2C_WorldState>(ct);
        var old = Measurements.Self(before, session.SurvivorIdentity.PlayerId);
        var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var identity = await session.ReconnectSurvivorAsync(deadline.Token);
        var after = await session.Survivor.ReadAsync<S2C_WorldState>(deadline.Token);
        var restored = Measurements.Self(after, identity.PlayerId);
        Check.Require(clock.Elapsed < TimeSpan.FromSeconds(5), "Reconnect took 5 s or longer.");
        Check.Require(restored.X == old.X && restored.Z == old.Z && restored.Role == old.Role &&
            restored.Status == old.Status && restored.CaptureCount == old.CaptureCount,
            $"Reconnect changed position/role/status/capture count: ({old.X:R},{old.Z:R},{old.Status}) -> ({restored.X:R},{restored.Z:R},{restored.Status}).");
        return $"Socket dropped; token rejoined in {clock.Elapsed.TotalMilliseconds:F1} ms; same PlayerId={identity.PlayerId}, position=({restored.X:R},{restored.Z:R}), role/status/capture count preserved.";
    }
}
