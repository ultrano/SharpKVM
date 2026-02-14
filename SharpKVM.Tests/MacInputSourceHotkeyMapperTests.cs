using SharpHook.Native;
using SharpKVM;

namespace SharpKVM.Tests;

public class MacInputSourceHotkeyMapperTests
{
    [Theory]
    [InlineData(49, KeyCode.VcSpace)]
    [InlineData(57, KeyCode.VcCapsLock)]
    public void TryMapMacVirtualKeyCode_MapsKnownKeys(int macKeyCode, KeyCode expected)
    {
        var ok = MacInputSourceHotkeyMapper.TryMapMacVirtualKeyCode(macKeyCode, out var code);

        Assert.True(ok);
        Assert.Equal(expected, code);
    }

    [Fact]
    public void ToModifierMask_FromCarbonFlags_MapsCommandAndShift()
    {
        const ulong command = 0x00100000;
        const ulong shift = 0x00020000;
        var mask = MacInputSourceHotkeyMapper.ToModifierMask(command | shift);

        Assert.Equal(MacModifierMask.Command | MacModifierMask.Shift, mask);
    }

    [Fact]
    public void Matches_UsesNormalizedModifierMask()
    {
        var hotkey = new MacInputSourceHotkey
        {
            Name = "InputSourcePrimary",
            SymbolicHotkeyId = 60,
            MacVirtualKeyCode = 49,
            MacModifierFlags = 0x00100000,
            TriggerKey = KeyCode.VcSpace,
            RequiredModifiers = MacModifierMask.Command
        };

        var pressed = new HashSet<KeyCode> { KeyCode.VcRightMeta, KeyCode.VcSpace };

        Assert.True(hotkey.Matches(pressed, KeyCode.VcSpace));
    }

    [Fact]
    public void IsCapsLockPlainSwitch_True_ForCapsLockWithoutModifiers()
    {
        var hotkey = new MacInputSourceHotkey
        {
            Name = "InputSourcePrimary",
            SymbolicHotkeyId = 60,
            MacVirtualKeyCode = 57,
            MacModifierFlags = 0,
            TriggerKey = KeyCode.VcCapsLock,
            RequiredModifiers = MacModifierMask.None
        };

        Assert.True(hotkey.IsCapsLockPlainSwitch);
    }

    [Fact]
    public void IsCapsLockPlainSwitch_False_WhenModifierExists()
    {
        var hotkey = new MacInputSourceHotkey
        {
            Name = "InputSourcePrimary",
            SymbolicHotkeyId = 60,
            MacVirtualKeyCode = 57,
            MacModifierFlags = 0x00020000,
            TriggerKey = KeyCode.VcCapsLock,
            RequiredModifiers = MacModifierMask.Shift
        };

        Assert.False(hotkey.IsCapsLockPlainSwitch);
    }

    [Fact]
    public void ComputeCapsLockOptionEnabled_True_WhenAnyHotkeyIsPlainCapsLock()
    {
        var hotkey = new MacInputSourceHotkey
        {
            Name = "InputSourcePrimary",
            SymbolicHotkeyId = 60,
            MacVirtualKeyCode = 57,
            MacModifierFlags = 0,
            TriggerKey = KeyCode.VcCapsLock,
            RequiredModifiers = MacModifierMask.None
        };

        var enabled = MacInputSourceHotkeys.ComputeCapsLockOptionEnabled(hotkey, null);

        Assert.True(enabled);
    }

    [Fact]
    public void ComputeCapsLockOptionEnabled_False_WhenNoCapsLockHotkeys()
    {
        var hotkey = new MacInputSourceHotkey
        {
            Name = "InputSourcePrimary",
            SymbolicHotkeyId = 60,
            MacVirtualKeyCode = 49,
            MacModifierFlags = 0x00100000,
            TriggerKey = KeyCode.VcSpace,
            RequiredModifiers = MacModifierMask.Command
        };

        var enabled = MacInputSourceHotkeys.ComputeCapsLockOptionEnabled(hotkey, null);

        Assert.False(enabled);
    }

    [Fact]
    public void TryReadCapsLockOptionFromHitoolboxJson_ReadsExplicitKey_AsTrue()
    {
        const string json = """
                            {
                              "AppleGlobalTextInputProperties": {
                                "UseCapsLockSwitchToAndFromABC": 1
                              }
                            }
                            """;

        var result = MacInputSourceHotkeyProvider.TryReadCapsLockOptionFromHitoolboxJson(json);

        Assert.True(result.HasValue);
        Assert.True(result.Value);
        Assert.Equal("hitoolbox_explicit", result.Source);
        Assert.Equal("AppleGlobalTextInputProperties.UseCapsLockSwitchToAndFromABC", result.RawKey);
        Assert.Equal("1", result.RawValue);
    }

    [Fact]
    public void TryReadCapsLockOptionFromHitoolboxJson_ReadsRecursiveCapsLockKey_AsFalse()
    {
        const string json = """
                            {
                              "Nested": {
                                "AppleCapsLockInputSourceSwitchEnabled": false
                              }
                            }
                            """;

        var result = MacInputSourceHotkeyProvider.TryReadCapsLockOptionFromHitoolboxJson(json);

        Assert.True(result.HasValue);
        Assert.False(result.Value);
        Assert.Equal("hitoolbox_recursive", result.Source);
        Assert.Equal("Nested.AppleCapsLockInputSourceSwitchEnabled", result.RawKey);
        Assert.Equal("false", result.RawValue);
    }

    [Fact]
    public void TryReadCapsLockOptionFromHitoolboxJson_ReturnsUnavailable_WhenNoKeyFound()
    {
        const string json = """
                            {
                              "AppleEnabledInputSources": [
                                { "InputSourceKind": "Keyboard Layout" }
                              ]
                            }
                            """;

        var result = MacInputSourceHotkeyProvider.TryReadCapsLockOptionFromHitoolboxJson(json);

        Assert.False(result.HasValue);
        Assert.Equal("unavailable", result.Source);
        Assert.Equal("n/a", result.RawKey);
        Assert.Equal("n/a", result.RawValue);
    }
}
