using System.Buffers.Binary;
using System.Runtime.InteropServices;
using PingTheKiller.Shared;
using PingTheKiller.Shared.Net;
using PingTheKiller.Shared.Packets;
using Xunit;

namespace PingTheKiller.Tests;

public sealed class PacketAssemblerTests
{
    private static readonly byte[] A = { 10, 0, 0, 0, 8, 0, 8, 7, 6, 5, 4, 3, 2, 1 };
    private static readonly byte[] B = { 2, 0, 0, 0, 11, 0 };

    private static void ReadExpected(PacketAssembler assembler, ushort expectedId, byte[] expectedPayload)
    {
        Assert.Equal(AssembleResult.Packet, assembler.TryRead(out ushort id, out var payload));
        Assert.Equal(expectedId, id);
        Assert.Equal(expectedPayload, payload.ToArray());
    }

    private static void NeedMore(PacketAssembler assembler)
    {
        Assert.Equal(AssembleResult.NeedMoreData, assembler.TryRead(out ushort id, out var payload));
        Assert.Equal(0, id);
        Assert.True(payload.IsEmpty);
        Assert.False(assembler.IsFaulted);
    }

    [Fact]
    public void WholePacketAndTwoPacketsInOneChunk()
    {
        var assembler = new PacketAssembler();
        NeedMore(assembler);
        assembler.Append(A);
        ReadExpected(assembler, 8, new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 });
        NeedMore(assembler);
        assembler.Append(A.Concat(B).ToArray());
        ReadExpected(assembler, 8, new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 });
        ReadExpected(assembler, 11, Array.Empty<byte>());
        NeedMore(assembler);
    }

    [Fact]
    public void HeadThenTailPlusNextPacketAtEverySplit()
    {
        for (int split = 0; split <= A.Length; split++)
        {
            var assembler = new PacketAssembler(initialCapacity: 1);
            assembler.Append(A.AsSpan(0, split));
            if (split < A.Length)
                NeedMore(assembler);
            else
                ReadExpected(assembler, 8, new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 });

            assembler.Append(A[split..].Concat(B).ToArray());
            if (split < A.Length)
                ReadExpected(assembler, 8, new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 });
            ReadExpected(assembler, 11, Array.Empty<byte>());
            NeedMore(assembler);
        }
    }

    [Fact]
    public void SinglePacketSplitAtEveryByteOffset()
    {
        byte[] frame = PacketCodec.Encode(new C2S_Join { Nickname = "살인마", ReconnectToken = ulong.MaxValue });
        for (int split = 0; split <= frame.Length; split++)
        {
            var assembler = new PacketAssembler(initialCapacity: 2);
            assembler.Append(frame.AsSpan(0, split));
            if (split < frame.Length)
                NeedMore(assembler);
            assembler.Append(frame.AsSpan(split));
            ReadExpected(assembler, 1, frame[6..]);
            NeedMore(assembler);
        }
    }

    [Fact]
    public void OneByteDripFeedOfOneHundredPackets()
    {
        var assembler = new PacketAssembler(initialCapacity: 1);
        int received = 0;
        for (int packet = 0; packet < 100; packet++)
        {
            byte[] frame = PacketCodec.Encode(new C2S_Ping { Timestamp = packet });
            for (int offset = 0; offset < frame.Length; offset++)
            {
                assembler.Append(frame.AsSpan(offset, 1));
                if (offset + 1 < frame.Length)
                    NeedMore(assembler);
                else
                {
                    Assert.Equal(AssembleResult.Packet, assembler.TryRead(out ushort id, out var payload));
                    Assert.Equal(packet, Assert.IsType<C2S_Ping>(PacketCodec.Decode(id, payload.Span)).Timestamp);
                    received++;
                }
            }
        }
        Assert.Equal(100, received);
        NeedMore(assembler);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(Protocol.MaxBodyLength + 1)]
    public void InvalidLengthPermanentlyFaultsEvenBeforeBodyArrives(int length)
    {
        var assembler = new PacketAssembler(initialCapacity: 1);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        assembler.Append(header.AsSpan(0, 3));
        NeedMore(assembler);
        assembler.Append(header.AsSpan(3));
        Assert.Equal(AssembleResult.Malformed, assembler.TryRead(out ushort id, out var payload));
        Assert.Equal(0, id);
        Assert.True(payload.IsEmpty);
        Assert.True(assembler.IsFaulted);
        assembler.Append(A);
        assembler.Append(ReadOnlySpan<byte>.Empty);
        Assert.Equal(AssembleResult.Malformed, assembler.TryRead(out _, out _));
        Assert.Equal(AssembleResult.Malformed, assembler.TryRead(out _, out _));
    }

    [Fact]
    public void MaximumLengthAcceptedAndFollowingFramePreserved()
    {
        var frame = new byte[4 + Protocol.MaxBodyLength];
        BinaryPrimitives.WriteInt32LittleEndian(frame, Protocol.MaxBodyLength);
        frame[4] = 0x34;
        frame[5] = 0x12;
        frame.AsSpan(6).Fill(0xAB);
        var assembler = new PacketAssembler(initialCapacity: 1);
        assembler.Append(frame.AsSpan(0, frame.Length - 1));
        NeedMore(assembler);
        assembler.Append(frame[^1..].Concat(B).ToArray());
        Assert.Equal(AssembleResult.Packet, assembler.TryRead(out ushort id, out var payload));
        Assert.Equal(0x1234, id);
        Assert.Equal(16382, payload.Length);
        Assert.Equal(frame[6..], payload.ToArray());
        ReadExpected(assembler, 11, Array.Empty<byte>());
        NeedMore(assembler);
    }

    [Fact]
    public void ConfiguredLengthLimitIsEnforced()
    {
        var assembler = new PacketAssembler(maxBodyLength: 2, initialCapacity: 1);
        assembler.Append(B);
        ReadExpected(assembler, 11, Array.Empty<byte>());
        assembler.Append(A);
        Assert.Equal(AssembleResult.Malformed, assembler.TryRead(out _, out _));
    }

    [Fact]
    public void PayloadViewsRemainValidAcrossReadsIncludingFaultAndNeedMore()
    {
        var assembler = new PacketAssembler(initialCapacity: 32);
        assembler.Append(A.Concat(A).Concat(new byte[4]).ToArray());
        Assert.Equal(AssembleResult.Packet, assembler.TryRead(out _, out var first));
        Assert.Equal(AssembleResult.Packet, assembler.TryRead(out _, out var second));
        Assert.Equal(AssembleResult.Malformed, assembler.TryRead(out _, out _));
        Assert.Equal(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }, first.ToArray());
        Assert.Equal(first.ToArray(), second.ToArray());

        var clean = new PacketAssembler();
        clean.Append(A.Concat(B).ToArray());
        Assert.Equal(AssembleResult.Packet, clean.TryRead(out _, out var saved));
        ReadExpected(clean, 11, Array.Empty<byte>());
        NeedMore(clean);
        Assert.Equal(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }, saved.ToArray());
    }

    [Fact]
    public void CompactionReusesBufferAndKeepsPartialNextFrame()
    {
        var assembler = new PacketAssembler(initialCapacity: 32);
        assembler.Append(A.Concat(A).Concat(A[..3]).ToArray());
        Assert.Equal(AssembleResult.Packet, assembler.TryRead(out _, out var first));
        Assert.True(MemoryMarshal.TryGetArray(first, out var original));
        ReadExpected(assembler, 8, A[6..]);
        NeedMore(assembler);
        assembler.Append(A.AsSpan(3));
        Assert.Equal(AssembleResult.Packet, assembler.TryRead(out _, out var next));
        Assert.Equal(A[6..], next.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(next, out var reused));
        Assert.Same(original.Array, reused.Array);
        NeedMore(assembler);
    }

    [Fact]
    public void GrowthIsBoundedForAnIncomingBatchLargerThanOneFrame()
    {
        byte[] batch = Enumerable.Range(0, 5000).SelectMany(_ => A).ToArray();
        var assembler = new PacketAssembler(initialCapacity: 1);
        assembler.Append(batch);
        Assert.Equal(AssembleResult.Packet, assembler.TryRead(out _, out var payload));
        Assert.True(MemoryMarshal.TryGetArray(payload, out var segment));
        Assert.InRange(segment.Array!.Length, batch.Length, 4 + Protocol.MaxBodyLength + batch.Length);
        int count = 1;
        while (assembler.TryRead(out _, out _) == AssembleResult.Packet)
            count++;
        Assert.Equal(5000, count);
    }

    [Fact]
    public void UndrainedBatchesCannotGrowBeyondFramePlusIncomingChunk()
    {
        byte[] batch = Enumerable.Range(0, 5000).SelectMany(_ => A).ToArray();
        var assembler = new PacketAssembler(initialCapacity: 1);
        assembler.Append(batch);
        Assert.Throws<InvalidOperationException>(() => assembler.Append(batch));
        Assert.False(assembler.IsFaulted);
        int count = 0;
        while (assembler.TryRead(out _, out _) == AssembleResult.Packet)
            count++;
        Assert.Equal(5000, count);
        assembler.Append(A);
        ReadExpected(assembler, 8, A[6..]);
        NeedMore(assembler);
    }

    [Fact]
    public void ReadsAllocateNoPerPacketStorage()
    {
        var assembler = new PacketAssembler();
        assembler.Append(Enumerable.Range(0, 100).SelectMany(_ => A).ToArray());
        Assert.Equal(AssembleResult.Packet, assembler.TryRead(out _, out _));
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;
        while (assembler.TryRead(out _, out _) == AssembleResult.Packet)
            count++;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(99, count);
        Assert.Equal(0, allocated);
    }
}
