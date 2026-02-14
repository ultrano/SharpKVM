using SharpKVM;
using Xunit;

namespace SharpKVM.Tests;

public class InputPacketSerializerTests
{
    [Fact]
    public void Serialize_And_TryDeserialize_RoundTripsPacket()
    {
        var packet = new InputPacket
        {
            Type = PacketType.MouseDown,
            X = 123,
            Y = 456,
            KeyCode = 2,
            ClickCount = 3
        };

        var bytes = InputPacketSerializer.Serialize(packet);
        var ok = InputPacketSerializer.TryDeserialize(bytes, out var parsed);

        Assert.True(ok);
        Assert.Equal(packet.Type, parsed.Type);
        Assert.Equal(packet.X, parsed.X);
        Assert.Equal(packet.Y, parsed.Y);
        Assert.Equal(packet.KeyCode, parsed.KeyCode);
        Assert.Equal(packet.ClickCount, parsed.ClickCount);
    }

    [Fact]
    public void TryDeserialize_InvalidLength_ReturnsFalse()
    {
        var ok = InputPacketSerializer.TryDeserialize(new byte[3], out _);

        Assert.False(ok);
    }
}
