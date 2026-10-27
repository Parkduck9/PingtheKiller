using PingTheKiller.Shared.Net;
using Xunit;

namespace PingTheKiller.Tests;

public sealed class PrimitiveTests
{
    [Fact]
    public void AllPrimitivesMatchLiteralLittleEndianBytes()
    {
        byte[] bytes =
        {
            0xAB, 0x34, 0x12,
            0xFE, 0xFF, 0xFF, 0xFF,
            0xEF, 0xCD, 0xAB, 0x89,
            0xFD, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            8, 7, 6, 5, 4, 3, 2, 1,
            0, 0, 0x80, 0x3F,
            0, 0, 0, 0x80,
            0xFF, 0xFF, 0x7F, 0x7F,
            0, 0
        };
        var w = new PacketWriter(1);
        w.WriteByte(0xAB);
        w.WriteUInt16(0x1234);
        w.WriteInt32(-2);
        w.WriteUInt32(0x89ABCDEFU);
        w.WriteInt64(-3);
        w.WriteUInt64(0x0102030405060708UL);
        w.WriteSingle(1f);
        w.WriteSingle(-0f);
        w.WriteSingle(float.MaxValue);
        w.WriteString("", 48);
        Assert.Equal(bytes, w.WrittenSpan.ToArray());

        var r = new PacketReader(bytes);
        Assert.Equal(0xAB, r.ReadByte());
        Assert.Equal(0x1234, r.ReadUInt16());
        Assert.Equal(-2, r.ReadInt32());
        Assert.Equal(0x89ABCDEFU, r.ReadUInt32());
        Assert.Equal(-3, r.ReadInt64());
        Assert.Equal(0x0102030405060708UL, r.ReadUInt64());
        Assert.Equal(1f, r.ReadSingle());
        Assert.Equal(unchecked((int)0x80000000), BitConverter.SingleToInt32Bits(r.ReadSingle()));
        Assert.Equal(float.MaxValue, r.ReadSingle());
        Assert.Equal("", r.ReadString(48));
        r.EnsureEnd();
    }
}
