using SharpKVM;

namespace SharpKVM.Tests;

public class MacInputSourceHotkeyProviderCapsLockOptionTests
{
    [Fact]
    public void TryReadCapsLockOptionFromHitoolboxJson_PrefersExplicitKnownKeyOverRecursiveMatch()
    {
        const string json = """
                            {
                              "AppleGlobalTextInputProperties": {
                                "UseCapsLockSwitchToAndFromABC": 0
                              },
                              "Nested": {
                                "CapsLockInputSourceSwitchEnabled": true
                              }
                            }
                            """;

        var result = MacInputSourceHotkeyProvider.TryReadCapsLockOptionFromHitoolboxJson(json);

        Assert.True(result.HasValue);
        Assert.False(result.Value);
        Assert.Equal("hitoolbox_explicit", result.Source);
        Assert.Equal("AppleGlobalTextInputProperties.UseCapsLockSwitchToAndFromABC", result.RawKey);
        Assert.Equal("0", result.RawValue);
    }

    [Fact]
    public void TryReadCapsLockOptionFromHitoolboxJson_ParsesStringBooleanValues()
    {
        const string json = """
                            {
                              "AppleCapsLockSwitchToAndFromABC": " On "
                            }
                            """;

        var result = MacInputSourceHotkeyProvider.TryReadCapsLockOptionFromHitoolboxJson(json);

        Assert.True(result.HasValue);
        Assert.True(result.Value);
        Assert.Equal("hitoolbox_explicit", result.Source);
        Assert.Equal("AppleCapsLockSwitchToAndFromABC", result.RawKey);
        Assert.Equal(" On ", result.RawValue);
    }

    [Fact]
    public void TryReadCapsLockOptionFromHitoolboxJson_FindsRecursiveCapsLockOptionInsideArrays()
    {
        const string json = """
                            {
                              "Profiles": [
                                { "Name": "Default" },
                                {
                                  "Input": {
                                    "CapsLockInputSourceSwitchEnabled": "yes"
                                  }
                                }
                              ]
                            }
                            """;

        var result = MacInputSourceHotkeyProvider.TryReadCapsLockOptionFromHitoolboxJson(json);

        Assert.True(result.HasValue);
        Assert.True(result.Value);
        Assert.Equal("hitoolbox_recursive", result.Source);
        Assert.Equal("Profiles[1].Input.CapsLockInputSourceSwitchEnabled", result.RawKey);
        Assert.Equal("yes", result.RawValue);
    }

    [Fact]
    public void TryReadCapsLockOptionFromHitoolboxJson_ReturnsUnavailableForMalformedJson()
    {
        const string json = "{ malformed";

        var result = MacInputSourceHotkeyProvider.TryReadCapsLockOptionFromHitoolboxJson(json);

        Assert.False(result.HasValue);
        Assert.Equal("unavailable", result.Source);
        Assert.Equal("n/a", result.RawKey);
        Assert.Equal("n/a", result.RawValue);
    }
}
