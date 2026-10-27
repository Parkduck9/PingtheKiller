using System.Buffers.Binary;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness;

public static class Check
{
    public static void Require(bool condition, string evidence)
    {
        if (!condition) throw new CheckFailure(evidence);
    }
}

public sealed class CheckFailure(string message) : Exception(message);

public static class Measurements
{
    public static double P99(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        Check.Require(values.Length > 0, "No timing samples received.");
        return values[(int)Math.Ceiling(values.Length * 0.99) - 1];
    }

    public static (double ArrivalP99Ms, double TickP99Ms) Sync(IReadOnlyList<ReceivedFrame> frames)
    {
        Check.Require(frames.Count >= 3, $"Only {frames.Count} WorldStates received; need at least 3.");
        var arrivals = new List<double>();
        var ticks = new List<double>();
        for (int i = 1; i < frames.Count; i++)
        {
            var before = (S2C_WorldState)frames[i - 1].Packet;
            var after = (S2C_WorldState)frames[i].Packet;
            Check.Require(after.Tick > before.Tick && after.Tick - before.Tick <= 2,
                $"Tick sequence invalid: {before.Tick} -> {after.Tick}.");
            double ms = ReceivedFrame.SecondsBetween(frames[i - 1].ReceivedAt, frames[i].ReceivedAt) * 1000;
            arrivals.Add(ms);
            // TCP exposes no server execution duration. This is the observed tick-period
            // estimate, normalized by the wire Tick delta (not server CPU timing).
            ticks.Add(ms / (after.Tick - before.Tick));
        }
        double arrivalP99 = P99(arrivals);
        Check.Require(arrivalP99 <= 120, $"WorldState inter-arrival p99={arrivalP99:F3} ms > 120 ms.");
        return (arrivalP99, P99(ticks));
    }

    public static string FloodTiming(int count, double seconds)
    {
        string evidence = $"{count} inputs in {seconds:F3} s, achieved rate={count / seconds:F1}/s";
        Check.Require(seconds <= 1.15, $"Harness flood exceeded 1.15 s ({evidence}).");
        return evidence;
    }

    public static PlayerState Self(ReceivedFrame frame, int id)
    {
        var world = (S2C_WorldState)frame.Packet;
        var matches = world.Players.Where(p => p.Id == id).ToArray();
        Check.Require(matches.Length == 1, $"WorldState tick={world.Tick} contains {matches.Length} entries for own player {id}.");
        var player = matches[0];
        Check.Require(float.IsFinite(player.X) && float.IsFinite(player.Z), $"Non-finite position for player {id}.");
        return player;
    }

    public static byte[] FloatBytes(float value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(value));
        return bytes;
    }

    public static void Masked(ReceivedFrame frame, int survivorId, PlayerState stopped)
    {
        if (frame.Packet is S2C_WorldState world)
            Check.Require(world.Players.All(p => p.Id != survivorId),
                $"Silent survivor {survivorId} leaked as PlayerState at tick={world.Tick}.");
        // Search each coordinate independently across the entire captured frame,
        // including headers and non-WorldState packets, without re-encoding floats.
        Check.Require(frame.Raw.AsSpan().IndexOf(FloatBytes(stopped.X)) < 0,
            $"Silent survivor X={stopped.X:R} bytes leaked in {frame.Packet.Id}.");
        Check.Require(frame.Raw.AsSpan().IndexOf(FloatBytes(stopped.Z)) < 0,
            $"Silent survivor Z={stopped.Z:R} bytes leaked in {frame.Packet.Id}.");
    }

    public static void KillerHidden(ReceivedFrame frame, int killerId)
    {
        if (frame.Packet is S2C_WorldState world)
            Check.Require(world.Players.All(p => p.Id != killerId && p.Role != (byte)Role.Killer),
                $"Killer {killerId} leaked to survivor at tick={world.Tick}.");
    }

    public static double MaxSpeed(IReadOnlyList<ReceivedFrame> frames, int playerId, byte role, bool requireStationary)
    {
        Check.Require(frames.Count >= 3, "Not enough WorldStates to measure abusive movement.");
        double maximum = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            var beforeWorld = (S2C_WorldState)frames[i - 1].Packet;
            var afterWorld = (S2C_WorldState)frames[i].Packet;
            Check.Require(afterWorld.Tick > beforeWorld.Tick, "Non-increasing Tick while measuring speed.");
            var before = Self(frames[i - 1], playerId);
            var after = Self(frames[i], playerId);
            Check.Require(before.Role == role && after.Role == role, "Mover role changed during abuse check.");
            double dx = (double)after.X - before.X, dz = (double)after.Z - before.Z;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            if (requireStationary) Check.Require(distance <= 0.001, $"NaN input moved player by {distance:F6} m.");
            double speed = distance * GameConstants.TickRate / (afterWorld.Tick - beforeWorld.Tick);
            maximum = Math.Max(maximum, speed);
        }
        double limit = (role == (byte)Role.Killer ? GameConstants.KillerSpeed : GameConstants.SurvivorSpeed) * 1.05;
        Check.Require(maximum <= limit, $"Observed speed {maximum:F4} m/s exceeds {limit:F4} m/s.");
        return maximum;
    }
}
