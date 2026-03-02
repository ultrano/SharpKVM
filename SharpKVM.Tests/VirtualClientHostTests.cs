using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Threading;
using SharpKVM;
using Xunit;

namespace SharpKVM.Tests;

public class VirtualClientHostTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public void CreateHandshakePackets_HasHelloThenPlatformInfo()
    {
        var packets = VirtualClientHost.CreateHandshakePackets(1920, 1080, false);

        Assert.Equal(2, packets.Length);
        Assert.Equal(PacketType.Hello, packets[0].Type);
        Assert.Equal(1920, packets[0].X);
        Assert.Equal(1080, packets[0].Y);

        Assert.Equal(PacketType.PlatformInfo, packets[1].Type);
        Assert.Equal(0, packets[1].KeyCode);
    }

    [Fact]
    public async Task TryStart_WhileAlreadyRunning_ReturnsFalse_UntilStopped()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var host = new VirtualClientHostHarness();
        var acceptedClientTask = listener.AcceptTcpClientAsync();

        Assert.True(host.Instance.TryStart("127.0.0.1", port, 1920, 1080, isMac: false));
        using var acceptedClient = await acceptedClientTask.WaitAsync(DefaultTimeout);
        Assert.True(host.Instance.IsRunning);

        Assert.False(host.Instance.TryStart("127.0.0.1", port, 1920, 1080, isMac: false));

        host.Instance.Stop();
        await host.WaitForStoppedAsync(DefaultTimeout);
        Assert.False(host.Instance.IsRunning);

        // Give RunAsync finally a brief chance to run; Stop should still emit at most one Stopped event.
        await Task.Delay(100);
        Assert.Equal(1, host.StoppedCount);
    }

    [Fact]
    public async Task TryStart_WhenConnectFails_RaisesStoppedAndResetsIsRunning()
    {
        var unusedPort = GetUnusedLoopbackPort();
        using var host = new VirtualClientHostHarness();

        Assert.True(host.Instance.TryStart("127.0.0.1", unusedPort, 1920, 1080, isMac: false));

        await host.WaitForStoppedAsync(DefaultTimeout);
        Assert.False(host.Instance.IsRunning);
        Assert.Equal(1, host.StoppedCount);
    }

    [Fact]
    public void CreateHandshakePackets_UsesRequestedResolution()
    {
        var packets = VirtualClientHost.CreateHandshakePackets(2560, 1440, false);

        Assert.Equal(PacketType.Hello, packets[0].Type);
        Assert.Equal(2560, packets[0].X);
        Assert.Equal(1440, packets[0].Y);
    }

    [Fact]
    public async Task DrainIncomingPacketsAsync_ClipboardPayloads_AreDrainedUntilEndOfStream()
    {
        var bytes = Concatenate(
            InputPacketSerializer.Serialize(new InputPacket { Type = PacketType.MouseMove, X = 1, Y = 2 }),
            InputPacketSerializer.Serialize(new InputPacket { Type = PacketType.Clipboard, X = 4 }),
            new byte[] { 10, 20, 30, 40 },
            InputPacketSerializer.Serialize(new InputPacket { Type = PacketType.PlatformInfo, KeyCode = 1 }));
        using var stream = new ChunkedMemoryStream(bytes, maxChunkSize: 2);

        await VirtualClientHost.DrainIncomingPacketsAsync(stream);

        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public async Task DrainIncomingPacketsAsync_ClipboardZeroLength_DoesNotRequirePayload()
    {
        var bytes = Concatenate(
            InputPacketSerializer.Serialize(new InputPacket { Type = PacketType.Clipboard, X = 0 }),
            InputPacketSerializer.Serialize(new InputPacket { Type = PacketType.MouseUp, X = 3, Y = 4 }));
        using var stream = new ChunkedMemoryStream(bytes, maxChunkSize: 3);

        await VirtualClientHost.DrainIncomingPacketsAsync(stream);

        Assert.Equal(stream.Length, stream.Position);
    }

    [Theory]
    [InlineData(PacketType.Clipboard)]
    [InlineData(PacketType.ClipboardFile)]
    [InlineData(PacketType.ClipboardImage)]
    public async Task DrainIncomingPacketsAsync_PayloadLengthAboveLimit_StopsWithoutDrainingTrailingBytes(PacketType type)
    {
        var invalidLength = type switch
        {
            PacketType.Clipboard => ProtocolPayloadLimits.MaxClipboardTextBytes + 1,
            PacketType.ClipboardFile => ProtocolPayloadLimits.MaxClipboardFileBytes + 1,
            PacketType.ClipboardImage => ProtocolPayloadLimits.MaxClipboardImageBytes + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported clipboard payload type.")
        };

        var header = InputPacketSerializer.Serialize(new InputPacket { Type = type, X = invalidLength });
        var trailing = new byte[] { 1, 2, 3, 4, 5 };
        var bytes = Concatenate(header, trailing);
        using var stream = new ChunkedMemoryStream(bytes, maxChunkSize: 2);

        await VirtualClientHost.DrainIncomingPacketsAsync(stream);

        Assert.Equal(header.Length, stream.Position);
    }

    [Fact]
    public async Task DrainIncomingPacketsAsync_TruncatedPayload_StopsGracefully()
    {
        var bytes = Concatenate(
            InputPacketSerializer.Serialize(new InputPacket { Type = PacketType.ClipboardImage, X = 5 }),
            new byte[] { 7, 8 });
        using var stream = new ChunkedMemoryStream(bytes, maxChunkSize: 1);

        await VirtualClientHost.DrainIncomingPacketsAsync(stream);

        Assert.Equal(stream.Length, stream.Position);
    }

    private static byte[] Concatenate(params byte[][] chunks)
    {
        int totalLength = 0;
        foreach (var chunk in chunks)
        {
            totalLength += chunk.Length;
        }

        var result = new byte[totalLength];
        int offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        return result;
    }

    private static int GetUnusedLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
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

    private sealed class VirtualClientHostHarness : IDisposable
    {
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public VirtualClientHost Instance { get; } = new();
        public int StoppedCount { get; private set; }

        public VirtualClientHostHarness()
        {
            Instance.Stopped += OnStopped;
        }

        public async Task WaitForStoppedAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_stopped.Task, Task.Delay(timeout));
            if (completed != _stopped.Task)
            {
                throw new TimeoutException("VirtualClientHost did not raise Stopped within timeout.");
            }
        }

        public void Dispose()
        {
            Instance.Stopped -= OnStopped;
            Instance.Stop();
        }

        private void OnStopped()
        {
            StoppedCount++;
            _stopped.TrySetResult();
        }
    }
}
