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
// U3d: the X/Z fields of a killer entry that KillerSight judged legal (line of sight) are legitimate in that frame only.
public static class KillerLeakScan
{
    public static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    private readonly record struct Field(int Bits, bool Legitimate);

    // SEAM (U2c protocol v0.3): S2C_SelfState carries only the recipient's own timers, so its float fields are tolerated
    // coincidences, like F4. Resolved by name at run time so this compiles before Shared has the type; while it is absent,
    // its frames (0x10) are unknown Ids and get the conservative raw scan in UnknownLeak instead.
    public static readonly Type? SelfStateType = typeof(IPacket).Assembly.GetType("PingTheKiller.Shared.Packets.S2C_SelfState");

    // An unknown-Id frame cannot be checked structurally: every 4-byte window at any alignment (header included) is
    // compared with the killer X/Z bits and any match fails. Conservative on purpose.
    public static string? UnknownLeak(UnknownFrame frame, IReadOnlySet<int> killerBits)
    {
        for (int i = 0; i + 4 <= frame.Raw.Length; i++)
        {
            int bits = BinaryPrimitives.ReadInt32LittleEndian(frame.Raw.AsSpan(i));
            if (killerBits.Contains(bits))
                return $"killer coordinate bytes in unknown packet 0x{frame.Id:X2} (update Shared): {BitConverter.Int32BitsToSingle(bits):R} " +
                    $"at byte {i} of {frame.Raw.Length}.";
        }
        return null;
    }

    // Returns failure evidence, or null. coincidences (optional) receives the count of each explained match.
    // selfStateType overrides SelfStateType (tests only). legalKillerEntry (U3d): index in a WorldState's Players of a killer
    // entry that KillerSight judged legal (line of sight); only its own X/Z fields may hold killer X/Z. Every other
    // occurrence in the frame is still counted against the legitimate fields.
    public static string? Excess(ReceivedFrame frame, IReadOnlySet<int> killerBits, IReadOnlyCollection<int> survivorIds,
        Action<int>? coincidences = null, Type? selfStateType = null, int legalKillerEntry = -1)
    {
        byte[] encoded;
        try { encoded = PacketCodec.Encode(frame.Packet); }
        catch (PacketFormatException) { encoded = Array.Empty<byte>(); }
        if (!encoded.AsSpan().SequenceEqual(frame.Raw))
            return $"unexplained bytes in survivor-bound frame {frame.Packet.Id}: {frame.Raw.Length} raw bytes do not re-encode " +
                $"from the decoded packet ({encoded.Length} bytes); possible killer coordinate leak.";
        Dictionary<int, (int Count, int Allowed)>? found = null;
        foreach (var field in Fields(frame.Packet, survivorIds, selfStateType ?? SelfStateType, legalKillerEntry))
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

    // Own-timer packet fields by reflection: floats are legitimate, 4/8-byte integers are compared as usual, 1/2-byte
    // fields cannot hold a 4-byte pattern. Null (raw-window fallback) for any other field type.
    private static List<Field>? OwnTimerFields(IPacket packet)
    {
        var fields = new List<Field>();
        foreach (var info in packet.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            object? value = info.GetValue(packet);
            switch (value)
            {
                case float f: fields.Add(new(Bits(f), true)); break;
                case int n: fields.Add(new(n, false)); break;
                case uint n: fields.Add(new((int)n, false)); break;
                case long n: fields.Add(new((int)n, false)); fields.Add(new((int)(n >> 32), false)); break;
                case ulong n: fields.Add(new((int)n, false)); fields.Add(new((int)(n >> 32), false)); break;
                case byte or sbyte or bool or short or ushort: break;
                default: return null;
            }
        }
        return fields;
    }

    private static IEnumerable<Field> Fields(IPacket packet, IReadOnlyCollection<int> survivorIds, Type? selfStateType, int legalKillerEntry)
    {
        if (selfStateType != null && packet.GetType() == selfStateType && OwnTimerFields(packet) is { } own) return own;
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
            case S2C_RttProbe:
                // Opaque server nonce, no coordinate fields; exact re-encoding is still checked above.
                break;
            case S2C_WorldState w:
                fields.Add(I((int)w.Tick));
                fields.Add(F(w.TimeLeft, true));
                foreach (var t in w.Terminals) { fields.Add(F(t.X, true)); fields.Add(F(t.Z, true)); }
                for (int i = 0; i < w.Players.Count; i++)
                {
                    var p = w.Players[i];
                    bool survivor = survivorIds.Contains(p.Id), legalKiller = i == legalKillerEntry;
                    fields.Add(I(p.Id));
                    fields.Add(F(p.X, survivor || legalKiller));
                    fields.Add(F(p.Z, survivor || legalKiller));
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
