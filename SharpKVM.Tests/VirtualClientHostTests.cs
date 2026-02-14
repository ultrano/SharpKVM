using SharpKVM;
using Xunit;

namespace SharpKVM.Tests;

public class VirtualClientHostTests
{
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
}
