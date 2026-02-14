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
}
