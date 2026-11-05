using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests.Core;

public sealed class RttProtocolTests
{
    [Theory]
    [InlineData(false, (ushort)0x0014)]
    [InlineData(true, (ushort)0x0015)]
    public void GoldenBytesRoundTripAndExactPayloadLength(bool echo, ushort id)
    {
        IPacket packet = echo ? new C2S_RttEcho { Nonce = 0xFEDCBA98 } : new S2C_RttProbe { Nonce = 0xFEDCBA98 };
        byte[] golden = { 6, 0, 0, 0, (byte)id, 0, 0x98, 0xBA, 0xDC, 0xFE };
        Assert.Equal(id, (ushort)packet.Id);
        Assert.Equal(golden, PacketCodec.Encode(packet));
        var decoded = PacketCodec.Decode(id, golden.AsSpan(6));
        Assert.Equivalent(packet, decoded, strict: true);
        Assert.Equal(golden, PacketCodec.Encode(decoded));
        for (int length = 0; length < 4; length++)
        {
            int shortLength = length;
            Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(id, golden.AsSpan(6, shortLength)));
        }
        Assert.Throws<PacketFormatException>(() => PacketCodec.Decode(id, new byte[5]));
        foreach (uint nonce in new[] { 0U, 1U, uint.MaxValue })
        {
            IPacket edge = echo ? new C2S_RttEcho { Nonce = nonce } : new S2C_RttProbe { Nonce = nonce };
            Assert.Equivalent(edge, PacketCodec.Decode(id, PacketCodec.Encode(edge).AsSpan(6)), strict: true);
        }
    }
}
