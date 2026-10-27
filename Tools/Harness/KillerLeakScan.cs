using System.Buffers.Binary;
using System.Text;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;

namespace PingTheKiller.Harness;

// H6 structural scan of one survivor-bound frame:
// 1. the decoded packet must re-encode to exactly the raw bytes (no extra or unexplained bytes);
// 2. every decoded 4-byte field (floats, and ints reinterpreted; 8-byte fields as two halves; string bytes at any
//    alignment inside the string) is compared with the killer X/Z bit patterns observed by the killer. A match in
//    a legitimate survivor field is a tolerated coincidence; any other match is a leak.
// Windows straddling field boundaries are never formed, so round killer floats cannot false-positive.
public static class KillerLeakScan
{
    public static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    private readonly record struct Field(int Bits, bool Legitimate);

    // Returns failure evidence, or null. coincidences (optional) receives the count of each explained match.
    public static string? Excess(ReceivedFrame frame, IReadOnlySet<int> killerBits, IReadOnlyCollection<int> survivorIds,
        Action<int>? coincidences = null)
    {
        byte[] encoded;
        try { encoded = PacketCodec.Encode(frame.Packet); }
        catch (PacketFormatException) { encoded = Array.Empty<byte>(); }
        if (!encoded.AsSpan().SequenceEqual(frame.Raw))
            return $"unexplained bytes in survivor-bound frame {frame.Packet.Id}: {frame.Raw.Length} raw bytes do not re-encode " +
                $"from the decoded packet ({encoded.Length} bytes); possible killer coordinate leak.";
        Dictionary<int, (int Count, int Allowed)>? found = null;
        foreach (var field in Fields(frame.Packet, survivorIds))
        {
            if (!killerBits.Contains(field.Bits)) continue;
            found ??= new Dictionary<int, (int, int)>();
            var (count, allowed) = found.GetValueOrDefault(field.Bits);
            found[field.Bits] = (count + 1, allowed + (field.Legitimate ? 1 : 0));
        }
        if (found == null) return null;
        foreach (var (bits, (count, allowed)) in found)
        {
            if (count > allowed)
                return $"killer coordinate {BitConverter.Int32BitsToSingle(bits):R} bytes leaked: {count} occurrence(s) in survivor-bound " +
                    $"{frame.Packet.Id} ({frame.Raw.Length} bytes) but only {allowed} legitimate field(s) hold that value.";
            coincidences?.Invoke(count);
        }
        return null;
    }

    private static IEnumerable<Field> Fields(IPacket packet, IReadOnlyCollection<int> survivorIds)
    {
        Field F(float value, bool legitimate = false) => new(Bits(value), legitimate);
        Field I(int value) => new(value, false);
        IEnumerable<Field> L(long value) => new[] { I((int)value), I((int)(value >> 32)) };
        IEnumerable<Field> S(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            for (int i = 0; i + 4 <= bytes.Length; i++) yield return I(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i)));
        }
        var fields = new List<Field>();
        switch (packet)
        {
            case S2C_WorldState w:
                fields.Add(I((int)w.Tick));
                fields.Add(F(w.TimeLeft, true));
                foreach (var t in w.Terminals) { fields.Add(F(t.X, true)); fields.Add(F(t.Z, true)); }
                foreach (var p in w.Players)
                {
                    bool survivor = survivorIds.Contains(p.Id);
                    fields.Add(I(p.Id));
                    fields.Add(F(p.X, survivor));
                    fields.Add(F(p.Z, survivor));
                    fields.Add(F(p.Yaw, survivor));
                    fields.Add(F(p.StatusTimer, survivor));
                }
                break;
            case S2C_SkillEffect e:
                bool survivorCast = survivorIds.Contains(e.CasterId);
                fields.Add(I(e.CasterId));
                fields.Add(F(e.X, survivorCast));
                fields.Add(F(e.Z, survivorCast));
                fields.Add(F(e.Duration));
                break;
            case S2C_GameResult r:
                fields.AddRange(r.EscapedIds.Select(I));
                break;
            case S2C_JoinResult j:
                fields.Add(I(j.PlayerId));
                fields.AddRange(L((long)j.ReconnectToken));
                break;
            case S2C_Pong pong:
                fields.AddRange(L(pong.Timestamp));
                break;
            case S2C_LobbyState lobby:
                foreach (var slot in lobby.Slots) { fields.Add(I(slot.PlayerId)); fields.AddRange(S(slot.Nickname)); }
                break;
            case S2C_MatchStart start:
                fields.Add(I(start.Seed));
                break;
            default:
                // Any other packet (not expected toward a client): conservatively every 4-byte window of the frame.
                byte[] raw = PacketCodec.Encode(packet);
                for (int i = 0; i + 4 <= raw.Length; i++) fields.Add(I(BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(i))));
                break;
        }
        return fields;
    }
}
