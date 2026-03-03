using SharpHook.Native;
using SharpKVM;

namespace SharpKVM.Tests;

public class MacInputSourceTogglePolicyTests
{
    [Theory]
    [InlineData(KeyCode.VcCapsLock)]
    [InlineData(KeyCode.VcHangul)]
    public void IsCapsInputSourceToggleKey_RecognizesCapsAndHangul(KeyCode keyCode)
    {
        Assert.True(MainWindow.IsCapsInputSourceToggleKey(keyCode));
    }

    [Theory]
    [InlineData(KeyCode.VcSpace)]
    [InlineData(KeyCode.VcA)]
    [InlineData(KeyCode.VcLeftShift)]
    public void IsCapsInputSourceToggleKey_RejectsNonCapsInputSourceKeys(KeyCode keyCode)
    {
        Assert.False(MainWindow.IsCapsInputSourceToggleKey(keyCode));
    }

    [Fact]
    public void GetEffectiveCapsLikeTriggerKey_NormalizesHangulToCapsLock()
    {
        Assert.Equal(KeyCode.VcCapsLock, MainWindow.GetEffectiveCapsLikeTriggerKey(KeyCode.VcHangul));
    }

    [Fact]
    public void GetEffectiveCapsLikeTriggerKey_LeavesNonHangulUnchanged()
    {
        Assert.Equal(KeyCode.VcSpace, MainWindow.GetEffectiveCapsLikeTriggerKey(KeyCode.VcSpace));
    }

    [Fact]
    public void HotkeyMatching_CanUseNormalizedTriggerForHangul()
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

        var pressed = new HashSet<KeyCode> { KeyCode.VcHangul };
        var effectiveTrigger = MainWindow.GetEffectiveCapsLikeTriggerKey(KeyCode.VcHangul);

        Assert.True(hotkey.Matches(pressed, effectiveTrigger));
    }

    [Fact]
    public void DirectToggleModifierCalculation_RespectsShiftWhenTriggerIsHangul()
    {
        var pressed = new HashSet<KeyCode> { KeyCode.VcHangul, KeyCode.VcLeftShift };
        var effectiveTrigger = MainWindow.GetEffectiveCapsLikeTriggerKey(KeyCode.VcHangul);

        Assert.Equal(MacModifierMask.Shift, MacInputSourceHotkeyMapper.ToModifierMask(pressed, effectiveTrigger));
    }
}
