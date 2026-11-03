using System.Text;
using UnityMcp;
using Xunit;

namespace UnityMcp.Tests;

public class BridgeFramingTests
{
    [Fact]
    public void Encode_WritesLittleEndianPayloadLength()
    {
        var frame = BridgeFraming.Encode(Encoding.UTF8.GetBytes("{\"a\":1}"));

        Assert.Equal(4 + 7, frame.Length);
        Assert.Equal(new byte[] { 7, 0, 0, 0 }, frame[..4]);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(frame, 4, 7));
    }

    [Fact]
    public async Task RoundTrip_MultipleFramesFromOneStream()
    {
        var stream = new MemoryStream();
        stream.Write(BridgeFraming.Encode("first"u8));
        stream.Write(BridgeFraming.Encode("second"u8));
        stream.Position = 0;

        Assert.Equal("first", Encoding.UTF8.GetString((await BridgeFraming.ReadFrameAsync(stream, default))!));
        Assert.Equal("second", Encoding.UTF8.GetString((await BridgeFraming.ReadFrameAsync(stream, default))!));
        Assert.Null(await BridgeFraming.ReadFrameAsync(stream, default));
    }

    [Fact]
    public async Task Read_ReassemblesFrameDeliveredOneByteAtATime()
    {
        var payload = Encoding.UTF8.GetBytes(new string('x', 5000));
        var stream = new TrickleStream(BridgeFraming.Encode(payload));

        var read = await BridgeFraming.ReadFrameAsync(stream, default);

        Assert.Equal(payload, read);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(BridgeFraming.MaxPayloadBytes + 1)]
    public async Task Read_RejectsInvalidLength(int length)
    {
        var stream = new MemoryStream(BitConverter.GetBytes(length));

        await Assert.ThrowsAsync<InvalidDataException>(() => BridgeFraming.ReadFrameAsync(stream, default));
    }

    [Fact]
    public async Task Read_ThrowsWhenStreamEndsMidFrame()
    {
        var frame = BridgeFraming.Encode("truncated"u8);
        var stream = new MemoryStream(frame[..6]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => BridgeFraming.ReadFrameAsync(stream, default));
    }

    [Fact]
    public async Task Read_ThrowsWhenStreamEndsMidHeader()
    {
        var stream = new MemoryStream([5, 0]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => BridgeFraming.ReadFrameAsync(stream, default));
    }

    [Fact]
    public void Encode_RejectsEmptyAndOversizedPayloads()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeFraming.Encode(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => BridgeFraming.Encode(new byte[BridgeFraming.MaxPayloadBytes + 1]));
    }

    /// <summary>Returns at most one byte per read, like a congested socket.</summary>
    private sealed class TrickleStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
