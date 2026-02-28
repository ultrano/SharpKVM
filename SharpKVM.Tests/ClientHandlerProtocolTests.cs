using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpKVM;
using Xunit;

namespace SharpKVM.Tests;

public class ClientHandlerProtocolTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    [Fact]
    public void ClientHandler_Constructors_DoNotDependOnMainWindow()
    {
        var constructors = typeof(ClientHandler).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(constructors);
        foreach (var constructor in constructors)
        {
            foreach (var parameter in constructor.GetParameters())
            {
                Assert.NotEqual(typeof(MainWindow), parameter.ParameterType);
            }
        }
    }

    [Fact]
    public async Task HandshakeAsync_HelloPacketWithPositiveResolution_SetsWidthAndHeight()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        await harness.SendPacketAsync(new InputPacket
        {
            Type = PacketType.Hello,
            X = 2560,
            Y = 1440
        });

        var ok = await harness.Handler.HandshakeAsync();

        Assert.True(ok);
        Assert.Equal(2560, harness.Handler.Width);
        Assert.Equal(1440, harness.Handler.Height);
    }

    [Fact]
    public async Task HandshakeAsync_NonHelloPacket_ReturnsFalse()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        await harness.SendPacketAsync(new InputPacket
        {
            Type = PacketType.PlatformInfo,
            KeyCode = 1
        });

        var ok = await harness.Handler.HandshakeAsync();

        Assert.False(ok);
        Assert.Equal(1920, harness.Handler.Width);
        Assert.Equal(1080, harness.Handler.Height);
    }

    [Fact]
    public async Task HandshakeAsync_HelloPacketWithInvalidResolution_ReturnsFalse()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        await harness.SendPacketAsync(new InputPacket
        {
            Type = PacketType.Hello,
            X = 0,
            Y = 1080
        });

        var ok = await harness.Handler.HandshakeAsync();

        Assert.False(ok);
        Assert.Equal(1920, harness.Handler.Width);
        Assert.Equal(1080, harness.Handler.Height);
    }

    [Fact]
    public async Task HandshakeAsync_TruncatedHeader_ReturnsFalse()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var fullHeader = InputPacketSerializer.Serialize(new InputPacket
        {
            Type = PacketType.Hello,
            X = 1920,
            Y = 1080
        });
        var truncatedHeader = fullHeader[..^1];

        await harness.SendRawBytesAsync(truncatedHeader);
        harness.ClosePeer();

        var ok = await harness.Handler.HandshakeAsync();

        Assert.False(ok);
        Assert.Equal(1920, harness.Handler.Width);
        Assert.Equal(1080, harness.Handler.Height);
    }

    [Fact]
    public async Task HandshakeAsync_MalformedHeaderWithUnknownPacketType_ReturnsFalse()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        await harness.SendPacketAsync(new InputPacket
        {
            Type = (PacketType)255,
            X = 1920,
            Y = 1080
        });

        var ok = await harness.Handler.HandshakeAsync();

        Assert.False(ok);
        Assert.Equal(1920, harness.Handler.Width);
        Assert.Equal(1080, harness.Handler.Height);
    }

    [Fact]
    public async Task StartReading_PlatformInfoAfterHandshake_SetsIsMac()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        await harness.SendPacketAsync(new InputPacket { Type = PacketType.Hello, X = 1920, Y = 1080 });
        await harness.SendPacketAsync(PlatformInfoPacket(1));
        Assert.True(await harness.Handler.HandshakeAsync());

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        await WaitUntilAsync(() => harness.Handler.IsMac, DefaultTimeout);
        Assert.True(harness.Handler.IsMac);

        harness.ClosePeer();
        Assert.True(await disconnected);
    }

    [Theory]
    [MemberData(nameof(PlatformInfoTransitionCases))]
    public async Task StartReading_PlatformInfoSequence_TracksEachStateTransition(int[] keyCodes, bool[] expectedStates)
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        Assert.Equal(keyCodes.Length, expectedStates.Length);

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        for (var i = 0; i < keyCodes.Length; i++)
        {
            await AssertPlatformInfoKeyCodeSetsExpectedStateAsync(harness, keyCodes[i], expectedStates[i]);
        }

        harness.ClosePeer();
        Assert.True(await disconnected);
    }

    [Theory]
    [MemberData(nameof(PlatformInfoKeyCodeMappingCases))]
    public async Task StartReading_PlatformInfoKeyCode_MapsToExpectedIsMacState(int keyCode, bool expectedIsMac)
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var initialIsMac = !expectedIsMac;
        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        await AssertPlatformInfoKeyCodeSetsExpectedStateAsync(harness, initialIsMac ? 1 : 0, initialIsMac);
        await AssertPlatformInfoKeyCodeSetsExpectedStateAsync(harness, keyCode, expectedIsMac);

        harness.ClosePeer();
        Assert.True(await disconnected);
    }

    [Fact]
    public async Task StartReading_ValidClipboardPayload_ForwardsTextToSink()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        var text = "characterization payload";
        var payload = Encoding.UTF8.GetBytes(text);
        await harness.SendPacketAsync(
            new InputPacket { Type = PacketType.Clipboard, X = payload.Length },
            payload);

        await WaitUntilAsync(() => harness.Sink.ClipboardTexts.Count == 1, DefaultTimeout);
        Assert.Equal(text, harness.Sink.ClipboardTexts[0]);

        harness.ClosePeer();
        Assert.True(await disconnected);
    }

    [Fact]
    public async Task StartReading_ValidClipboardFilePayload_ForwardsFileToSink()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        var payload = new byte[] { 11, 22, 33, 44, 55 };
        await harness.SendPacketAsync(
            new InputPacket { Type = PacketType.ClipboardFile, X = payload.Length },
            payload);

        await WaitUntilAsync(() => harness.Sink.FilePayloads.Count == 1, DefaultTimeout);
        Assert.Equal(payload, harness.Sink.FilePayloads[0]);
        Assert.Empty(harness.Sink.ClipboardTexts);
        Assert.Empty(harness.Sink.ImagePayloads);

        harness.ClosePeer();
        Assert.True(await disconnected);
    }

    [Fact]
    public async Task StartReading_ValidClipboardImagePayload_ForwardsImageToSink()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        var payload = new byte[] { 101, 102, 103, 104 };
        await harness.SendPacketAsync(
            new InputPacket { Type = PacketType.ClipboardImage, X = payload.Length },
            payload);

        await WaitUntilAsync(() => harness.Sink.ImagePayloads.Count == 1, DefaultTimeout);
        Assert.Equal(payload, harness.Sink.ImagePayloads[0]);
        Assert.Empty(harness.Sink.ClipboardTexts);
        Assert.Empty(harness.Sink.FilePayloads);

        harness.ClosePeer();
        Assert.True(await disconnected);
    }

    [Fact]
    public async Task StartReading_MalformedHeaderWithUnknownPacketType_IgnoresPacketAndContinues()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        await harness.SendPacketAsync(new InputPacket
        {
            Type = (PacketType)255,
            X = 0,
            Y = 0,
            KeyCode = 0,
            ClickCount = 0
        });
        await harness.SendPacketAsync(PlatformInfoPacket(1));

        await WaitUntilAsync(() => harness.Handler.IsMac, DefaultTimeout);
        Assert.True(harness.Handler.IsMac);
        Assert.Empty(harness.Sink.ClipboardTexts);
        Assert.Empty(harness.Sink.FilePayloads);
        Assert.Empty(harness.Sink.ImagePayloads);

        harness.ClosePeer();
        Assert.True(await disconnected);
    }

    [Fact]
    public async Task StartReading_UnhandledKnownPacketType_IgnoresPacketAndContinues()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        await harness.SendPacketAsync(new InputPacket
        {
            Type = PacketType.MouseMove,
            X = 10,
            Y = 20
        });
        await harness.SendPacketAsync(PlatformInfoPacket(1));

        await WaitUntilAsync(() => harness.Handler.IsMac, DefaultTimeout);
        Assert.True(harness.Handler.IsMac);
        Assert.Empty(harness.Sink.ClipboardTexts);
        Assert.Empty(harness.Sink.FilePayloads);
        Assert.Empty(harness.Sink.ImagePayloads);

        harness.ClosePeer();
        Assert.True(await disconnected);
    }

    [Fact]
    public async Task StartReading_TruncatedHeader_DisconnectsWithoutForwarding()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        var fullHeader = InputPacketSerializer.Serialize(new InputPacket
        {
            Type = PacketType.PlatformInfo,
            KeyCode = 1
        });
        var truncatedHeader = fullHeader[..^1];

        await harness.SendRawBytesAsync(truncatedHeader);
        harness.ClosePeer();

        Assert.True(await disconnected);
        Assert.False(harness.Handler.IsMac);
        Assert.Empty(harness.Sink.ClipboardTexts);
        Assert.Empty(harness.Sink.FilePayloads);
        Assert.Empty(harness.Sink.ImagePayloads);
    }

    [Fact]
    public async Task StartReading_TruncatedHeaderAfterValidPacket_DisconnectsOnNextLoopBoundary()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        await harness.SendPacketAsync(PlatformInfoPacket(1));
        await WaitUntilAsync(() => harness.Handler.IsMac, DefaultTimeout);

        var fullHeader = InputPacketSerializer.Serialize(new InputPacket
        {
            Type = PacketType.PlatformInfo,
            KeyCode = 0
        });
        var truncatedHeader = fullHeader[..^1];
        await harness.SendRawBytesAsync(truncatedHeader);
        harness.ClosePeer();

        Assert.True(await disconnected);
        Assert.True(harness.Handler.IsMac);
        Assert.Empty(harness.Sink.ClipboardTexts);
        Assert.Empty(harness.Sink.FilePayloads);
        Assert.Empty(harness.Sink.ImagePayloads);
    }

    [Theory]
    [InlineData(PacketType.Clipboard, ProtocolPayloadLimits.MaxClipboardTextBytes + 1)]
    [InlineData(PacketType.ClipboardFile, ProtocolPayloadLimits.MaxClipboardFileBytes + 1)]
    [InlineData(PacketType.ClipboardImage, ProtocolPayloadLimits.MaxClipboardImageBytes + 1)]
    public async Task StartReading_PayloadLengthAboveLimit_DisconnectsWithoutForwarding(PacketType type, int invalidLength)
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        await harness.SendPacketAsync(new InputPacket
        {
            Type = type,
            X = invalidLength
        });

        Assert.True(await disconnected);
        Assert.Empty(harness.Sink.ClipboardTexts);
        Assert.Empty(harness.Sink.FilePayloads);
        Assert.Empty(harness.Sink.ImagePayloads);
    }

    [Theory]
    [InlineData(PacketType.Clipboard, 0)]
    [InlineData(PacketType.Clipboard, -1)]
    [InlineData(PacketType.ClipboardFile, 0)]
    [InlineData(PacketType.ClipboardFile, -1)]
    [InlineData(PacketType.ClipboardImage, 0)]
    [InlineData(PacketType.ClipboardImage, -1)]
    public async Task StartReading_NonPositivePayloadLength_DisconnectsWithoutForwarding(PacketType type, int invalidLength)
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        await harness.SendPacketAsync(new InputPacket
        {
            Type = type,
            X = invalidLength
        });

        Assert.True(await disconnected);
        Assert.Empty(harness.Sink.ClipboardTexts);
        Assert.Empty(harness.Sink.FilePayloads);
        Assert.Empty(harness.Sink.ImagePayloads);
    }

    [Theory]
    [InlineData(PacketType.Clipboard, 8, 3)]
    [InlineData(PacketType.ClipboardFile, 8, 3)]
    [InlineData(PacketType.ClipboardImage, 8, 3)]
    public async Task StartReading_TruncatedPayload_DisconnectsWithoutForwarding(PacketType type, int declaredLength, int actualLength)
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var disconnected = harness.WaitForDisconnectedAsync(DefaultTimeout);
        harness.Handler.StartReading();

        var partialPayload = new byte[actualLength];
        for (var i = 0; i < partialPayload.Length; i++)
        {
            partialPayload[i] = (byte)(i + 1);
        }

        await harness.SendPacketAsync(
            new InputPacket { Type = type, X = declaredLength },
            partialPayload);
        harness.ClosePeer();

        Assert.True(await disconnected);
        Assert.Empty(harness.Sink.ClipboardTexts);
        Assert.Empty(harness.Sink.FilePayloads);
        Assert.Empty(harness.Sink.ImagePayloads);
    }

    [Fact]
    public async Task SendClipboardPacket_ValidText_SendsHeaderThenUtf8Payload()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        const string text = "send-path-text";
        var expectedPayload = Encoding.UTF8.GetBytes(text);

        harness.Handler.SendClipboardPacket(text);

        var (packet, payload) = await harness.ReceivePacketWithPayloadAsync(DefaultTimeout);

        Assert.Equal(PacketType.Clipboard, packet.Type);
        Assert.Equal(expectedPayload.Length, packet.X);
        Assert.Equal(0, packet.Y);
        Assert.Equal(0, packet.KeyCode);
        Assert.Equal(0, packet.ClickCount);
        Assert.Equal(expectedPayload, payload);
    }

    [Fact]
    public async Task SendFilePacket_ValidPayload_SendsHeaderThenPayload()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var data = new byte[] { 1, 2, 3, 4, 5, 6 };

        harness.Handler.SendFilePacket(data);

        var (packet, payload) = await harness.ReceivePacketWithPayloadAsync(DefaultTimeout);

        Assert.Equal(PacketType.ClipboardFile, packet.Type);
        Assert.Equal(data.Length, packet.X);
        Assert.Equal(0, packet.Y);
        Assert.Equal(0, packet.KeyCode);
        Assert.Equal(0, packet.ClickCount);
        Assert.Equal(data, payload);
    }

    [Fact]
    public async Task SendImagePacket_ValidPayload_SendsHeaderThenPayload()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var data = new byte[] { 10, 20, 30, 40 };

        harness.Handler.SendImagePacket(data);

        var (packet, payload) = await harness.ReceivePacketWithPayloadAsync(DefaultTimeout);

        Assert.Equal(PacketType.ClipboardImage, packet.Type);
        Assert.Equal(data.Length, packet.X);
        Assert.Equal(0, packet.Y);
        Assert.Equal(0, packet.KeyCode);
        Assert.Equal(0, packet.ClickCount);
        Assert.Equal(data, payload);
    }

    [Fact]
    public async Task SendClipboardPacket_PayloadAboveLimit_DoesNotWriteToStream()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        var oversized = new string('a', ProtocolPayloadLimits.MaxClipboardTextBytes + 1);

        harness.Handler.SendClipboardPacket(oversized);

        Assert.False(await harness.TryReceiveAnyByteAsync(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public async Task SendFilePacket_ZeroLengthPayload_DoesNotWriteToStream()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        harness.Handler.SendFilePacket(Array.Empty<byte>());

        Assert.False(await harness.TryReceiveAnyByteAsync(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public async Task SendImagePacket_ZeroLengthPayload_DoesNotWriteToStream()
    {
        using var harness = await ClientHandlerHarness.CreateAsync();

        harness.Handler.SendImagePacket(Array.Empty<byte>());

        Assert.False(await harness.TryReceiveAnyByteAsync(TimeSpan.FromMilliseconds(250)));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = Stopwatch.StartNew();
        while (start.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(15);
        }

        throw new TimeoutException("Condition was not met within the timeout.");
    }

    private static InputPacket PlatformInfoPacket(int keyCode) => new InputPacket
    {
        Type = PacketType.PlatformInfo,
        KeyCode = keyCode
    };

    public static IEnumerable<object[]> PlatformInfoTransitionCases =>
    [
        new object[] { new[] { 1, 0 }, new[] { true, false } },
        new object[] { new[] { 1, 0, 1 }, new[] { true, false, true } }
    ];

    public static IEnumerable<object[]> PlatformInfoKeyCodeMappingCases =>
    [
        new object[] { 1, true },
        new object[] { 0, false },
        new object[] { -1, false },
        new object[] { 2, false },
        new object[] { 7, false },
        new object[] { int.MinValue, false },
        new object[] { int.MaxValue, false }
    ];

    private static async Task AssertPlatformInfoKeyCodeSetsExpectedStateAsync(
        ClientHandlerHarness harness,
        int keyCode,
        bool expectedIsMac)
    {
        await harness.SendPacketAsync(PlatformInfoPacket(keyCode));
        await WaitUntilAsync(() => harness.Handler.IsMac == expectedIsMac, DefaultTimeout);
        Assert.Equal(expectedIsMac, harness.Handler.IsMac);
    }

    private sealed class RecordingSink : IClientHandlerMessageSink
    {
        public List<string> ClipboardTexts { get; } = new();
        public List<byte[]> FilePayloads { get; } = new();
        public List<byte[]> ImagePayloads { get; } = new();

        public void SetRemoteClipboard(string text) => ClipboardTexts.Add(text);
        public void ProcessReceivedFiles(byte[] zipData) => FilePayloads.Add(zipData);
        public void ProcessReceivedImage(byte[] imgData) => ImagePayloads.Add(imgData);
    }

    private sealed class ClientHandlerHarness : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly TcpClient _peerClient;
        private readonly NetworkStream _peerStream;

        public RecordingSink Sink { get; }
        public ClientHandler Handler { get; }

        private ClientHandlerHarness(TcpListener listener, TcpClient peerClient, TcpClient serverClient)
        {
            _listener = listener;
            _peerClient = peerClient;
            _peerStream = peerClient.GetStream();
            Sink = new RecordingSink();
            Handler = new ClientHandler(serverClient, Sink);
        }

        public static async Task<ClientHandlerHarness> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var peerClient = new TcpClient { NoDelay = true };
            var connectTask = peerClient.ConnectAsync(IPAddress.Loopback, port);
            var serverClient = await listener.AcceptTcpClientAsync();
            await connectTask;
            serverClient.NoDelay = true;

            return new ClientHandlerHarness(listener, peerClient, serverClient);
        }

        public async Task SendPacketAsync(InputPacket packet, byte[]? payload = null)
        {
            var header = InputPacketSerializer.Serialize(packet);
            await _peerStream.WriteAsync(header, 0, header.Length);

            if (payload is { Length: > 0 })
            {
                await _peerStream.WriteAsync(payload, 0, payload.Length);
            }

            await _peerStream.FlushAsync();
        }

        public async Task SendRawBytesAsync(byte[] data)
        {
            if (data.Length > 0)
            {
                await _peerStream.WriteAsync(data, 0, data.Length);
            }

            await _peerStream.FlushAsync();
        }

        public async Task<(InputPacket Packet, byte[] Payload)> ReceivePacketWithPayloadAsync(TimeSpan timeout)
        {
            var headerBytes = await ReadExactAsync(Marshal.SizeOf<InputPacket>(), timeout);
            if (!InputPacketSerializer.TryDeserialize(headerBytes, out var packet))
            {
                throw new InvalidOperationException("Received header could not be deserialized as InputPacket.");
            }

            var payload = packet.X > 0
                ? await ReadExactAsync(packet.X, timeout)
                : Array.Empty<byte>();

            return (packet, payload);
        }

        public async Task<bool> TryReceiveAnyByteAsync(TimeSpan timeout)
        {
            byte[] buffer = new byte[1];
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                var read = await _peerStream.ReadAsync(buffer.AsMemory(0, 1), cts.Token);
                return read > 0;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private async Task<byte[]> ReadExactAsync(int size, TimeSpan timeout)
        {
            byte[] buffer = new byte[size];
            int totalRead = 0;
            using var cts = new CancellationTokenSource(timeout);

            while (totalRead < size)
            {
                var bytesRead = await _peerStream.ReadAsync(
                    buffer.AsMemory(totalRead, size - totalRead),
                    cts.Token);

                if (bytesRead == 0)
                {
                    throw new InvalidOperationException("Peer closed connection before expected bytes were received.");
                }

                totalRead += bytesRead;
            }

            return buffer;
        }

        public Task<bool> WaitForDisconnectedAsync(TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnDisconnected(object _)
            {
                tcs.TrySetResult(true);
            }

            Handler.Disconnected += OnDisconnected;
            return WaitInternalAsync();

            async Task<bool> WaitInternalAsync()
            {
                try
                {
                    var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
                    return completed == tcs.Task;
                }
                finally
                {
                    Handler.Disconnected -= OnDisconnected;
                }
            }
        }

        public void ClosePeer() => _peerClient.Close();

        public void Dispose()
        {
            Handler.Dispose();
            try { _peerClient.Close(); } catch { }
            try { _listener.Stop(); } catch { }
        }
    }
}
