using System;
using System.IO;
using System.Threading.Tasks;
using SharpKVM;
using Xunit;

namespace SharpKVM.Tests;

public class ProtocolStreamReaderGuardTests
{
    [Fact]
    public async Task ReadInputPacketHeaderAsync_NullStream_ThrowsArgumentNullException()
    {
        var headerBuffer = ProtocolStreamReader.CreateInputPacketHeaderBuffer();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await ProtocolStreamReader.ReadInputPacketHeaderAsync(null!, headerBuffer));
    }

    [Fact]
    public async Task ReadInputPacketHeaderAsync_NullHeaderBuffer_ThrowsArgumentNullException()
    {
        using var stream = new MemoryStream(new byte[ProtocolStreamReader.InputPacketHeaderSize]);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await ProtocolStreamReader.ReadInputPacketHeaderAsync(stream, null!));
    }

    [Fact]
    public async Task ReadPayloadAsync_NonPayloadType_DoesNotReadStreamAndReturnsNull()
    {
        using var stream = new ReadTrackingMemoryStream(new byte[] { 1, 2, 3, 4 });

        var payload = await ProtocolStreamReader.ReadPayloadAsync(stream, PacketType.MouseMove, 4);

        Assert.Null(payload);
        Assert.Equal(0, stream.ReadCalls);
        Assert.Equal(0, stream.Position);
    }

    private sealed class ReadTrackingMemoryStream : MemoryStream
    {
        public int ReadCalls { get; private set; }

        public ReadTrackingMemoryStream(byte[] buffer)
            : base(buffer)
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCalls++;
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
