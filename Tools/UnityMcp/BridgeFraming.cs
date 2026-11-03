using System.Buffers.Binary;

namespace UnityMcp;

/// <summary>
/// Frame format between this server and the Unity bridge:
/// <c>[int32 Length, little-endian][UTF-8 JSON payload]</c>, where Length is the payload byte count.
/// Frames with Length &lt; 1 or Length &gt; <see cref="MaxPayloadBytes"/> are protocol errors and the
/// connection is closed. Requests are <c>{"id":n,"cmd":"name","args":{...}}</c>, responses are
/// <c>{"id":n,"ok":true,"result":...}</c> or <c>{"id":n,"ok":false,"error":"..."}</c>.
/// The Unity side (Client/Assets/Editor/McpBridge/McpBridge.cs) must stay in sync with this.
/// </summary>
public static class BridgeFraming
{
    public const int HeaderBytes = 4;
    public const int MaxPayloadBytes = 8 * 1024 * 1024;

    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1 || payload.Length > MaxPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payload), $"Payload must be 1..{MaxPayloadBytes} bytes, got {payload.Length}.");
        var frame = new byte[HeaderBytes + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderBytes));
        return frame;
    }

    /// <summary>Reads one frame. Returns null on a clean end of stream before a header starts.</summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[HeaderBytes];
        if (!await ReadExactAsync(stream, header, allowCleanEof: true, ct).ConfigureAwait(false))
            return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 1 || length > MaxPayloadBytes)
            throw new InvalidDataException($"Invalid frame length {length} (allowed 1..{MaxPayloadBytes}).");
        var payload = new byte[length];
        await ReadExactAsync(stream, payload, allowCleanEof: false, ct).ConfigureAwait(false);
        return payload;
    }

    private static async Task<bool> ReadExactAsync(Stream stream, Memory<byte> buffer, bool allowCleanEof, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer[read..], ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0 && allowCleanEof) return false;
                throw new EndOfStreamException($"Stream ended after {read} of {buffer.Length} bytes.");
            }
            read += n;
        }
        return true;
    }
}
