using SharpKVM;
using Xunit;

namespace SharpKVM.Tests;

public class LaunchArgumentParserTests
{
    [Theory]
    [InlineData(new[] { "client", "10.0.0.2" }, true, "10.0.0.2")]
    [InlineData(new[] { "--client", "10.0.0.3" }, true, "10.0.0.3")]
    [InlineData(new[] { "-c", "10.0.0.4" }, true, "10.0.0.4")]
    [InlineData(new[] { "--client=10.0.0.5" }, true, "10.0.0.5")]
    public void Parse_ClientModes_EnableAutoStart(string[] args, bool expectedAutoStart, string expectedIp)
    {
        var result = LaunchArgumentParser.Parse(args);

        Assert.Equal(expectedAutoStart, result.AutoStartClientMode);
        Assert.Equal(expectedIp, result.AutoServerIP);
    }

    [Theory]
    [InlineData(new[] { "--server", "10.0.1.1" }, false, "10.0.1.1")]
    [InlineData(new[] { "-s", "10.0.1.2" }, false, "10.0.1.2")]
    [InlineData(new[] { "--server=10.0.1.3" }, false, "10.0.1.3")]
    public void Parse_ServerModes_DoNotEnableAutoStart(string[] args, bool expectedAutoStart, string expectedIp)
    {
        var result = LaunchArgumentParser.Parse(args);

        Assert.Equal(expectedAutoStart, result.AutoStartClientMode);
        Assert.Equal(expectedIp, result.AutoServerIP);
    }

    [Fact]
    public void Parse_EmptyArgs_ReturnsDefaults()
    {
        var result = LaunchArgumentParser.Parse([]);

        Assert.False(result.AutoStartClientMode);
        Assert.Equal(string.Empty, result.AutoServerIP);
    }

    [Fact]
    public void Parse_NullArgs_ReturnsDefaults()
    {
        var result = LaunchArgumentParser.Parse(null!);

        Assert.False(result.AutoStartClientMode);
        Assert.Equal(string.Empty, result.AutoServerIP);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("--client")]
    [InlineData("-c")]
    public void Parse_ClientFlagWithoutIp_EnablesAutoStartWithEmptyIp(string flag)
    {
        var result = LaunchArgumentParser.Parse([flag]);

        Assert.True(result.AutoStartClientMode);
        Assert.Equal(string.Empty, result.AutoServerIP);
    }

    [Fact]
    public void Parse_ClientThenServer_PreservesClientModeAndUsesLatestServerIp()
    {
        var result = LaunchArgumentParser.Parse(["--client", "10.0.0.10", "--server", "10.0.0.20"]);

        Assert.True(result.AutoStartClientMode);
        Assert.Equal("10.0.0.20", result.AutoServerIP);
    }

    [Fact]
    public void Parse_WhitespaceAroundFlagAndIp_IsTrimmed()
    {
        var result = LaunchArgumentParser.Parse(["  --client  ", " 10.0.0.30 "]);

        Assert.True(result.AutoStartClientMode);
        Assert.Equal("10.0.0.30", result.AutoServerIP);
    }
}
