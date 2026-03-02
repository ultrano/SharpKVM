using SharpKVM;
using Xunit;

namespace SharpKVM.Tests;

public class ProtocolPayloadLimitsTests
{
    [Fact]
    public void TryGetMaxPayload_KnownClipboardTypes_ReturnsConfiguredLimits()
    {
        Assert.True(ProtocolPayloadLimits.TryGetMaxPayload(PacketType.Clipboard, out var textLimit));
        Assert.Equal(ProtocolPayloadLimits.MaxClipboardTextBytes, textLimit);

        Assert.True(ProtocolPayloadLimits.TryGetMaxPayload(PacketType.ClipboardFile, out var fileLimit));
        Assert.Equal(ProtocolPayloadLimits.MaxClipboardFileBytes, fileLimit);

        Assert.True(ProtocolPayloadLimits.TryGetMaxPayload(PacketType.ClipboardImage, out var imageLimit));
        Assert.Equal(ProtocolPayloadLimits.MaxClipboardImageBytes, imageLimit);
    }

    [Fact]
    public void TryGetMaxPayload_NonPayloadType_ReturnsFalse()
    {
        Assert.False(ProtocolPayloadLimits.TryGetMaxPayload(PacketType.MouseMove, out _));
    }

    [Theory]
    [InlineData(PacketType.Clipboard, ProtocolPayloadLimits.MaxClipboardTextBytes)]
    [InlineData(PacketType.ClipboardFile, ProtocolPayloadLimits.MaxClipboardFileBytes)]
    [InlineData(PacketType.ClipboardImage, ProtocolPayloadLimits.MaxClipboardImageBytes)]
    public void IsValidPayloadLength_PayloadTypes_ApplyLowerAndUpperBounds(PacketType type, int maxBytes)
    {
        Assert.False(ProtocolPayloadLimits.IsValidPayloadLength(type, -1));
        Assert.False(ProtocolPayloadLimits.IsValidPayloadLength(type, 0));
        Assert.True(ProtocolPayloadLimits.IsValidPayloadLength(type, 1));
        Assert.True(ProtocolPayloadLimits.IsValidPayloadLength(type, maxBytes));
        Assert.False(ProtocolPayloadLimits.IsValidPayloadLength(type, maxBytes + 1));
    }

    [Theory]
    [InlineData(PacketType.MouseMove)]
    [InlineData(PacketType.MouseDown)]
    [InlineData(PacketType.Hello)]
    [InlineData(PacketType.PlatformInfo)]
    public void IsValidPayloadLength_NonPayloadType_ReturnsFalse(PacketType type)
    {
        Assert.False(ProtocolPayloadLimits.IsValidPayloadLength(type, 1));
        Assert.False(ProtocolPayloadLimits.IsValidPayloadLength(type, 1024));
    }
}
