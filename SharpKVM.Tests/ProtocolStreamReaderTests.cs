using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpKVM;
using Xunit;

namespace SharpKVM.Tests;

public class ProtocolStreamReaderTests
{
    [Fact]
    public async Task ReadExactAsync_ReadsAcrossMultipleChunks()
    {
        var source = new byte[] { 1, 2, 3, 4, 5, 6 };
        using var stream = new ChunkedMemoryStream(source, maxChunkSize: 2);
        var buffer = new byte[source.Length];

        var ok = await ProtocolStreamReader.ReadExactAsync(stream, buffer, buffer.Length);

        Assert.True(ok);
        Assert.Equal(source, buffer);
    }

    [Fact]
    public async Task ReadExactAsync_TruncatedStream_ReturnsFalse()
    {
        using var stream = new MemoryStream(new byte[] { 9, 8, 7 });
        var buffer = new byte[5];

        var ok = await ProtocolStreamReader.ReadExactAsync(stream, buffer, buffer.Length);

        Assert.False(ok);
    }

    [Fact]
    public async Task ReadExactAsync_ZeroSize_ReturnsTrueWithoutReading()
    {
        using var stream = new MemoryStream(new byte[] { 9, 8, 7 });
        stream.Position = 1;
        var buffer = new byte[3];

        var ok = await ProtocolStreamReader.ReadExactAsync(stream, buffer, 0);

        Assert.True(ok);
        Assert.Equal(1, stream.Position);
    }

    [Fact]
    public async Task ReadExactAsync_NegativeSize_ThrowsArgumentOutOfRangeException()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var buffer = new byte[3];

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await ProtocolStreamReader.ReadExactAsync(stream, buffer, -1));
    }

    [Fact]
    public async Task ReadExactAsync_SizeGreaterThanBufferLength_ThrowsArgumentOutOfRangeException()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var buffer = new byte[3];

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await ProtocolStreamReader.ReadExactAsync(stream, buffer, 4));
    }

    [Fact]
    public async Task ReadPayloadAsync_InvalidLength_ReturnsNullWithoutReading()
    {
        using var stream = new MemoryStream(new byte[] { 10, 20, 30 });

        var payload = await ProtocolStreamReader.ReadPayloadAsync(stream, PacketType.Clipboard, 0);

        Assert.Null(payload);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public async Task ReadPayloadAsync_ValidPayload_ReturnsBytes()
    {
        var source = new byte[] { 11, 22, 33, 44 };
        using var stream = new MemoryStream(source);

        var payload = await ProtocolStreamReader.ReadPayloadAsync(stream, PacketType.Clipboard, source.Length);

        Assert.Equal(source, payload);
    }

    [Fact]
    public async Task ReadPayloadAsync_TruncatedPayload_ReturnsNull()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2 });

        var payload = await ProtocolStreamReader.ReadPayloadAsync(stream, PacketType.ClipboardImage, 3);

        Assert.Null(payload);
    }

    [Fact]
    public async Task ReadInputPacketHeaderAsync_ValidHeader_ReturnsSuccessAndPacket()
    {
        var expected = new InputPacket
        {
            Type = PacketType.MouseDown,
            X = 10,
            Y = 20,
            KeyCode = 30,
            ClickCount = 2
        };
        var header = InputPacketSerializer.Serialize(expected);
        using var stream = new MemoryStream(header);
        var headerBuffer = ProtocolStreamReader.CreateInputPacketHeaderBuffer();

        var (status, packet) = await ProtocolStreamReader.ReadInputPacketHeaderAsync(stream, headerBuffer);

        Assert.Equal(InputPacketHeaderReadStatus.Success, status);
        Assert.Equal(expected.Type, packet.Type);
        Assert.Equal(expected.X, packet.X);
        Assert.Equal(expected.Y, packet.Y);
        Assert.Equal(expected.KeyCode, packet.KeyCode);
        Assert.Equal(expected.ClickCount, packet.ClickCount);
    }

    [Fact]
    public async Task ReadInputPacketHeaderAsync_TruncatedHeader_ReturnsEndOfStream()
    {
        var header = InputPacketSerializer.Serialize(new InputPacket
        {
            Type = PacketType.MouseMove,
            X = 1,
            Y = 2
        });
        var truncated = header[..^1];
        using var stream = new MemoryStream(truncated);
        var headerBuffer = ProtocolStreamReader.CreateInputPacketHeaderBuffer();

        var (status, _) = await ProtocolStreamReader.ReadInputPacketHeaderAsync(stream, headerBuffer);

        Assert.Equal(InputPacketHeaderReadStatus.EndOfStream, status);
    }

    [Fact]
    public async Task ReadInputPacketHeaderAsync_SmallBuffer_ThrowsArgumentException()
    {
        var header = InputPacketSerializer.Serialize(new InputPacket { Type = PacketType.KeyDown, KeyCode = 65 });
        using var stream = new MemoryStream(header);
        var smallBuffer = new byte[ProtocolStreamReader.InputPacketHeaderSize - 1];

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await ProtocolStreamReader.ReadInputPacketHeaderAsync(stream, smallBuffer));
    }

    private sealed class ChunkedMemoryStream : MemoryStream
    {
        private readonly int _maxChunkSize;

        public ChunkedMemoryStream(byte[] buffer, int maxChunkSize)
            : base(buffer)
        {
            _maxChunkSize = maxChunkSize;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var chunk = buffer.Length > _maxChunkSize ? buffer[.._maxChunkSize] : buffer;
            return base.ReadAsync(chunk, cancellationToken);
        }
    }
}
