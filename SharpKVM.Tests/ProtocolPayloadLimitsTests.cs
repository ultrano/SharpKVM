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

    [Fact]
    public void IsValidPayloadLength_AppliesBounds()
    {
        Assert.False(ProtocolPayloadLimits.IsValidPayloadLength(PacketType.Clipboard, 0));
        Assert.True(ProtocolPayloadLimits.IsValidPayloadLength(PacketType.Clipboard, ProtocolPayloadLimits.MaxClipboardTextBytes));
        Assert.False(ProtocolPayloadLimits.IsValidPayloadLength(PacketType.Clipboard, ProtocolPayloadLimits.MaxClipboardTextBytes + 1));
    }
}
